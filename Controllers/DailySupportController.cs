using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using ClientVisitManagement.Data;
using ClientVisitManagement.Models;
using System.Data;
using ClosedXML.Excel;

namespace ClientVisitManagement.Controllers;

public class DailySupportController : Controller
{
    private readonly DBHelper _db;
    private readonly DynamicRepository _dynamicRepo;
    private readonly ILogger<DailySupportController> _logger;
    public DailySupportController(DBHelper db, DynamicRepository dynamicRepo, ILogger<DailySupportController> logger){ _db=db; _dynamicRepo=dynamicRepo; _logger=logger; }

    // Sequential per-day ticket number: TKT-ddMMyy-001, TKT-ddMMyy-002 ...
    // (Random.Next se duplicate/skewed numbers aate the). Duplicate check ke saath
    // taaki do log ek saath banayein to bhi number clash na ho.
    private string GenerateTicketNo(){
        string prefix=$"TKT-{AppTime.Now:ddMMyy}-";
        using var con=_db.GetConnection(); con.Open();
        int seq;
        using(var cmd=new SqlCommand("SELECT COUNT(*) FROM DailySupport WITH (UPDLOCK, HOLDLOCK) WHERE SupportDate>=@DayStart AND SupportDate<@DayEnd",con)){
            cmd.Parameters.AddWithValue("@DayStart",AppTime.Today);
            cmd.Parameters.AddWithValue("@DayEnd",AppTime.Today.AddDays(1));
            seq=Convert.ToInt32(cmd.ExecuteScalar())+1;
        }
        string ticketNo;
        using(var cmd=new SqlCommand("SELECT COUNT(*) FROM DailySupport WITH (UPDLOCK, HOLDLOCK) WHERE TicketNo=@T",con)){
            cmd.Parameters.Add("@T",System.Data.SqlDbType.VarChar,40);
            // Kabhi kabhi same prefix par race condition ho sakta hai - retry with lock
            int tries=0;
            do{ ticketNo=$"{prefix}{seq:D3}"; cmd.Parameters["@T"].Value=ticketNo; seq++; tries++; if(tries>500) break; }
            while(Convert.ToInt32(cmd.ExecuteScalar())>0);
        }
        return ticketNo;
    }

    // AJAX request? (SPA navigation + grid partial dono isi header se aate hain)
    private bool IsAjaxRequest()=> string.Equals(Request.Headers["X-Requested-With"],"XMLHttpRequest",StringComparison.OrdinalIgnoreCase);
    private void AddDateParams(SqlCommand cmd, string? fromDate, string? toDate){
        // Date ko string ke roop me bhejne se SQL Server ko implicit conversion
        // karni pad sakti hai. Typed DateTime parameters query plan ko stable rakhte hain.
        if(DateTime.TryParse(fromDate, out var from)){ var p=cmd.Parameters.Add("@F",SqlDbType.DateTime); p.Value=from; if(cmd.Parameters.Contains("@Cf")) cmd.Parameters["@Cf"].Value=from; }
        if(DateTime.TryParse(toDate, out var to)){ var p=cmd.Parameters.Add("@T",SqlDbType.DateTime); p.Value=to; if(cmd.Parameters.Contains("@Ct")) cmd.Parameters["@Ct"].Value=to; }
    }
    private void AddTypedDateParameter(SqlCommand cmd,string name,string? value){
        if(DateTime.TryParse(value,out var parsed)) cmd.Parameters.Add(name,SqlDbType.DateTime).Value=parsed;
        else cmd.Parameters.AddWithValue(name,(object?)value??DBNull.Value);
    }
    // AssignedTo scope - cards ke count queries me use hota hai
    private void AddUserParam(SqlCommand cmd, string? user){
        if(int.TryParse(user,out var userId)) cmd.Parameters.Add("@U",SqlDbType.Int).Value=userId;
        else if(!string.IsNullOrWhiteSpace(user)) cmd.Parameters.AddWithValue("@U",user);
    }
    private void AddTypedIntParameter(SqlCommand cmd,string name,string? value){
        if(int.TryParse(value,out var parsed)) cmd.Parameters.Add(name,SqlDbType.Int).Value=parsed;
        else cmd.Parameters.AddWithValue(name,(object?)value??DBNull.Value);
    }
    private string BuildFullTextSearch(string? value){
        var cleaned=new string((value??"").Where(c=>char.IsLetterOrDigit(c)||char.IsWhiteSpace(c)).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned)?"":"\""+cleaned.Replace("\"","\"\"")+"*\"";
    }

    [HttpGet]
    public IActionResult Index(string? search, string? fromDate, string? toDate, string? status, string? user, int page=1, int pageSize=20, string? mywork=null){
        // ---- Pagination safety ----
        // Pehle page/pageSize validate nahi hote the. page=0 / pageSize=0 aane
        // par SQL OFFSET/FETCH galat ho jata tha aur grid KHAALI dikhta tha.
        if(pageSize!=10&&pageSize!=20&&pageSize!=50&&pageSize!=100) pageSize=20;
        if(page<1) page=1;
        // Grid refresh ke partial AJAX request me dashboard cards/dropdowns ki
        // zarurat nahi hoti. In queries ko skip karke sirf table load karo.
        bool isPartialRequest=IsAjaxRequest() && Request.Query.ContainsKey("partial");
        using var con=_db.GetConnection(); con.Open();
        var vm=new DailySupportDashboardViewModel(); vm.SearchTerm=search; vm.FromDate=fromDate; vm.ToDate=toDate; vm.FilterStatus=status; vm.FilterUser=user;

        // ---- ROLE SCOPE + "MY WORK" MODE ----------------------------------
        // Admin/Manager sab tickets dekh sakte hain. Normal (non Admin/Manager)
        // user ko HAMESHA sirf apne hi assigned tickets dikhenge - URL me
        // ?user=13 likh ke bhi server userId par force kar deta hai, isliye
        // URL hack se dusron ka data nahi mil sakta.
        bool canAssign=AccessControl.CanAssignWork(HttpContext);
        int? myUid=AccessControl.CurrentUserId(HttpContext);
        bool isMyWork=!string.IsNullOrWhiteSpace(mywork)&&(mywork=="1"||mywork.Equals("true",StringComparison.OrdinalIgnoreCase));
        if(!canAssign||isMyWork){
            // user ko apni id par force karo (userId na mile to kuch bhi na dikhe)
            user=myUid.HasValue?myUid.Value.ToString():"-1";
        }
        vm.FilterUser=user;
        vm.IsMyWork=isMyWork; vm.BaseUrl="/DailySupport"; vm.MyUserId=myUid;
        vm.MyUserName=AccessControl.CurrentUserName(HttpContext); vm.CanAssign=canAssign;

        if(!isPartialRequest){
        // Badge ka count - mere OPEN tickets (Closed ke alawa)
        if(myUid.HasValue){
            try{ using var pc=new SqlCommand("SELECT COUNT(*) FROM DailySupport WHERE AssignedTo=@M AND (Status IS NULL OR Status <> 'Closed')",con); pc.Parameters.AddWithValue("@M",myUid.Value); vm.MyPendingCount=Convert.ToInt32(pc.ExecuteScalar()); }catch{}
        }
        // User dropdown sirf Admin/Manager ke normal view me dikh raha hota hai;
        // normal user aur My Work mode me is query ki zarurat nahi.
        if(canAssign && !isMyWork){
            try{ using var ucmd=new SqlCommand("SELECT UserId, FullName FROM Users WHERE IsActive=1 ORDER BY FullName",con); using var ureader=ucmd.ExecuteReader(); while(ureader.Read()){ vm.Users.Add(new Dictionary<string,object?>{ ["UserId"]=ureader.GetInt32(0), ["FullName"]=ureader.IsDBNull(1)?ureader.GetInt32(0).ToString():ureader.GetString(1) }); } }catch{}
        }
        // ---- Stats cards ka date filter ----
        // Range di gayi hai  -> saare cards us RANGE par count karte hain.
        // Range nahi di      -> "Today/Closed Today" AAJ par, baaki total par.
        // (Pehle sirf dono dates bhare hone par filter lagta tha - half-filled
        //  filter par cards aur grid ka count mismatch ho jata tha.)
        string dateSql="";
        if(!string.IsNullOrWhiteSpace(fromDate) && !string.IsNullOrWhiteSpace(toDate)) dateSql=" AND d.SupportDate >= @F AND d.SupportDate < DATEADD(DAY,1,@T)";
        else if(!string.IsNullOrWhiteSpace(fromDate)) dateSql=" AND d.SupportDate >= @F";
        else if(!string.IsNullOrWhiteSpace(toDate)) dateSql=" AND d.SupportDate < DATEADD(DAY,1,@T)";
        bool hasRange=!string.IsNullOrEmpty(dateSql);
        string todaySql= hasRange ? dateSql : " AND CAST(d.SupportDate AS DATE)=CAST(GETDATE() AS DATE)";
        // Cards bhi user ke scope me count hone chahiye - normal user ko apne
        // hi cards dikhenge (dusron ka data leak na ho).
        string userSql= !string.IsNullOrWhiteSpace(user) ? " AND d.AssignedTo=@U " : "";
        try{
            // 5 separate COUNT queries ko ek grouped query me combine kiya.
            // Isse page load par database round-trips 5 se 1 ho jaate hain.
            var statsSql=$@"SELECT
                COALESCE(SUM(CASE WHEN 1=1 {todaySql} THEN 1 ELSE 0 END),0),
                COALESCE(SUM(CASE WHEN 1=1 {dateSql} THEN 1 ELSE 0 END),0),
                COALESCE(SUM(CASE WHEN d.Status IN ('Open','In Progress','On Hold','Pending') {dateSql} THEN 1 ELSE 0 END),0),
                COALESCE(SUM(CASE WHEN d.Status='Closed' {todaySql} THEN 1 ELSE 0 END),0),
                COALESCE(SUM(CASE WHEN d.Status IN ('In Progress','Pending','On Hold') {dateSql} THEN 1 ELSE 0 END),0)
                FROM DailySupport d WHERE 1=1 {userSql}";
            using var statsCmd=new SqlCommand(statsSql,con);
            AddDateParams(statsCmd,fromDate,toDate); AddUserParam(statsCmd,user);
            using var statsReader=statsCmd.ExecuteReader();
            if(statsReader.Read()){
                vm.TodayCalls=Convert.ToInt32(statsReader.GetValue(0));
                vm.TotalCalls=Convert.ToInt32(statsReader.GetValue(1));
                vm.OpenCalls=Convert.ToInt32(statsReader.GetValue(2));
                vm.ClosedToday=Convert.ToInt32(statsReader.GetValue(3));
                vm.PendingCalls=Convert.ToInt32(statsReader.GetValue(4));
            }
        }catch{}
        }
        string fullTextSearch=BuildFullTextSearch(search);
        string where=" WHERE 1=1 "; if(!string.IsNullOrWhiteSpace(search)) where+=string.IsNullOrWhiteSpace(fullTextSearch) ? " AND (d.TicketNo LIKE @Search OR c.ClientName LIKE @Search OR d.Mobile LIKE @Search OR d.Issue LIKE @Search) " : " AND (d.TicketNo LIKE @Search OR d.Mobile LIKE @Search OR CONTAINS(d.Issue,@FullTextSearch) OR CONTAINS(c.ClientName,@FullTextSearch)) "; if(!string.IsNullOrWhiteSpace(fromDate)) where+=" AND d.SupportDate >= @FromDate "; if(!string.IsNullOrWhiteSpace(toDate)) where+=" AND d.SupportDate < DATEADD(DAY,1,@ToDate) "; if(!string.IsNullOrWhiteSpace(status)) where+=" AND d.Status = @Status "; else where+=" AND (d.Status IS NULL OR d.Status <> 'Closed') "; /* default: Closed tickets grid me nahi - sirf Status=Closed select karne par */ if(!string.IsNullOrWhiteSpace(user)) where+=" AND d.AssignedTo = @UserId ";
        // Search na ho to Client join ki zarurat nahi; normal load me
        // count query ko DailySupport table tak hi simit rakho.
        string countFrom=string.IsNullOrWhiteSpace(search)
            ? "FROM DailySupport d"
            : "FROM DailySupport d LEFT JOIN Client c ON d.ClientId=c.ClientId";
        string searchPlanHint=string.IsNullOrWhiteSpace(search)?"":" OPTION (RECOMPILE)";
        using var countCmd=new SqlCommand($"SELECT COUNT(*) {countFrom} {where}{searchPlanHint}",con); if(!string.IsNullOrWhiteSpace(search)) countCmd.Parameters.AddWithValue("@Search","%"+search+"%"); if(!string.IsNullOrWhiteSpace(fullTextSearch)) countCmd.Parameters.AddWithValue("@FullTextSearch",fullTextSearch); if(!string.IsNullOrWhiteSpace(fromDate)) AddTypedDateParameter(countCmd,"@FromDate",fromDate); if(!string.IsNullOrWhiteSpace(toDate)) AddTypedDateParameter(countCmd,"@ToDate",toDate); if(!string.IsNullOrWhiteSpace(status)) countCmd.Parameters.AddWithValue("@Status",status); if(!string.IsNullOrWhiteSpace(user)) AddTypedIntParameter(countCmd,"@UserId",user); int total=0; try{total=(int)countCmd.ExecuteScalar();}catch{}
        // Last page se aage ka page request aaye (ya data delete ho gaya) to
        // grid khaali na dikhe - use last valid page par le aao
        int totalPages = total>0 ? (int)Math.Ceiling(total/(double)pageSize) : 1;
        if(page>totalPages) page=totalPages;
        // Grid ke liye d.* ki jagah required columns hi fetch karo.
        // Isse unused DB columns network/reader me nahi aate.
        string dataSql=$@"SELECT d.SupportId, d.TicketNo, d.Status, d.Priority, d.SupportDate,
            d.ClientId, d.Mobile, d.Channel, d.CallType, d.Category,
            d.AssignedTo, d.Issue, d.CreatedOn, c.ClientName, c.ClientName as CompanyName,
            u.FullName as AssignedName
            FROM DailySupport d LEFT JOIN Client c ON d.ClientId=c.ClientId
            LEFT JOIN Users u ON d.AssignedTo=u.UserId {where}
            ORDER BY d.SupportId DESC OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY{searchPlanHint}";
        using var dataCmd=new SqlCommand(dataSql,con); if(!string.IsNullOrWhiteSpace(search)) dataCmd.Parameters.AddWithValue("@Search","%"+search+"%"); if(!string.IsNullOrWhiteSpace(fullTextSearch)) dataCmd.Parameters.AddWithValue("@FullTextSearch",fullTextSearch); if(!string.IsNullOrWhiteSpace(fromDate)) AddTypedDateParameter(dataCmd,"@FromDate",fromDate); if(!string.IsNullOrWhiteSpace(toDate)) AddTypedDateParameter(dataCmd,"@ToDate",toDate); if(!string.IsNullOrWhiteSpace(status)) dataCmd.Parameters.AddWithValue("@Status",status); if(!string.IsNullOrWhiteSpace(user)) AddTypedIntParameter(dataCmd,"@UserId",user); dataCmd.Parameters.AddWithValue("@Skip",(page-1)*pageSize); dataCmd.Parameters.AddWithValue("@Take",pageSize);
        var table=new DailySupportGridViewModel{CurrentPage=page,PageSize=pageSize,TotalRecords=total,SearchTerm=search,FilterStatus=status,FromDate=fromDate,ToDate=toDate,FilterUser=user,BaseUrl=vm.BaseUrl,IsMyWork=isMyWork,CanAssign=canAssign};
        try{ using var reader=dataCmd.ExecuteReader(); for(int i=0;i<reader.FieldCount;i++) table.Columns.Add(reader.GetName(i)); while(reader.Read()){ var row=new Dictionary<string,object?>(); for(int i=0;i<reader.FieldCount;i++) row[reader.GetName(i)]=reader.IsDBNull(i)?null:reader.GetValue(i); table.Rows.Add(row);} }catch{}
        vm.TicketTable=table;
        
        // CSV/Excel export from Index (poora filtered data)
        if(!string.IsNullOrWhiteSpace(Request.Query["export"])){
            var exp = Request.Query["export"].ToString().ToLower();
            string exWhere=" WHERE 1=1 "; if(!string.IsNullOrWhiteSpace(search)) exWhere+=string.IsNullOrWhiteSpace(fullTextSearch) ? " AND (d.TicketNo LIKE @Search OR c.ClientName LIKE @Search OR d.Mobile LIKE @Search OR d.Issue LIKE @Search) " : " AND (d.TicketNo LIKE @Search OR d.Mobile LIKE @Search OR CONTAINS(d.Issue,@FullTextSearch) OR CONTAINS(c.ClientName,@FullTextSearch)) "; if(!string.IsNullOrWhiteSpace(fromDate)) exWhere+=" AND d.SupportDate >= @FromDate "; if(!string.IsNullOrWhiteSpace(toDate)) exWhere+=" AND d.SupportDate < DATEADD(DAY,1,@ToDate) "; if(!string.IsNullOrWhiteSpace(status)) exWhere+=" AND d.Status = @Status "; else exWhere+=" AND (d.Status IS NULL OR d.Status <> 'Closed') "; /* export bhi grid jaisa */ if(!string.IsNullOrWhiteSpace(user)) exWhere+=" AND d.AssignedTo = @UserId ";
            string exSql=$@"SELECT d.TicketNo, d.SupportDate, c.ClientName, d.Mobile, d.Channel, d.CallType, d.Category, d.Priority, d.Status, u.FullName as AssignedTo, d.Issue FROM DailySupport d LEFT JOIN Client c ON d.ClientId=c.ClientId LEFT JOIN Users u ON d.AssignedTo=u.UserId {exWhere} ORDER BY d.SupportId DESC";
            using var excmd=new SqlCommand(exSql,con); if(!string.IsNullOrWhiteSpace(search)) excmd.Parameters.AddWithValue("@Search","%"+search+"%"); if(!string.IsNullOrWhiteSpace(fullTextSearch)) excmd.Parameters.AddWithValue("@FullTextSearch",fullTextSearch); if(!string.IsNullOrWhiteSpace(fromDate)) AddTypedDateParameter(excmd,"@FromDate",fromDate); if(!string.IsNullOrWhiteSpace(toDate)) AddTypedDateParameter(excmd,"@ToDate",toDate); if(!string.IsNullOrWhiteSpace(status)) excmd.Parameters.AddWithValue("@Status",status); if(!string.IsNullOrWhiteSpace(user)) AddTypedIntParameter(excmd,"@UserId",user);
            var dt=new DataTable(); try{ using var adapter=new SqlDataAdapter(excmd); adapter.Fill(dt);}catch{}
            if(exp=="csv"){
                System.Text.StringBuilder sb=new System.Text.StringBuilder();
                for(int ccol=0;ccol<dt.Columns.Count;ccol++){ sb.Append("\"").Append(dt.Columns[ccol].ColumnName.Replace("\"","\"\"")).Append("\""); if(ccol<dt.Columns.Count-1) sb.Append(","); }
                sb.AppendLine();
                foreach(DataRow dr in dt.Rows){
                    for(int ccol=0;ccol<dt.Columns.Count;ccol++){ var v=dr[ccol]; string val=v==DBNull.Value?"":v.ToString().Replace("\"","\"\""); sb.Append("\"").Append(val).Append("\""); if(ccol<dt.Columns.Count-1) sb.Append(","); }
                    sb.AppendLine();
                }
                return File(System.Text.Encoding.UTF8.GetBytes("\uFEFF"+sb.ToString()),"text/csv",$"DailySupport_{DateTime.Now:ddMMyyyy_HHmm}.csv");
            }else{
                using var workbook=new ClosedXML.Excel.XLWorkbook(); var ws=workbook.Worksheets.Add("DailySupport"); ws.Cell(1,1).Value="Daily Support Report"; ws.Cell(1,1).Style.Font.Bold=true; ws.Cell(1,1).Style.Font.FontSize=14;
                if(dt.Rows.Count>0){ ws.Cell(3,1).InsertTable(dt,"DSS",true); var tbl=ws.Tables.FirstOrDefault(); if(tbl!=null){tbl.ShowAutoFilter=true;tbl.Theme=ClosedXML.Excel.XLTableTheme.TableStyleLight9;}}
                ws.Columns().AdjustToContents(); using var stream=new MemoryStream(); workbook.SaveAs(stream); stream.Position=0;
                return File(stream.ToArray(),"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",$"DailySupport_{DateTime.Now:ddMMyyyy_HHmm}.xlsx");
            }
        }
        
        // Grid ka partial (sirf table + pagination bar) tabhi jab client ne
        // 'partial=1' bheja. Warna poora page view jata hai - SPA navigation
        // tab poora page inject kar deti hai aur layout dobara ban jata hai.
        if(IsAjaxRequest() && Request.Query.ContainsKey("partial")) return PartialView("_CallTable",table);
        return View(vm);
    }

    // ============================================================
    // MY WORK - POPUP (koi alag page nahi).
    // Daily Support ke "My Work" button se ye partial modal me load hota hai:
    // 4 stat cards + mere saare open tickets card list me.
    // Data sirf logged-in user ka - server par force, URL se change nahi ho sakta.
    // ============================================================
    [HttpGet]
    public IActionResult MyWorkPanel()
    {
        int? myUid=AccessControl.CurrentUserId(HttpContext);
        var vm=new DailySupportDashboardViewModel{
            IsMyWork=true,
            MyUserId=myUid,
            MyUserName=AccessControl.CurrentUserName(HttpContext),
            CanAssign=AccessControl.CanAssignWork(HttpContext)
        };
        if(myUid.HasValue){
            try{
                using var con=_db.GetConnection(); con.Open();

                // OPT: pehle 6 alag COUNT queries (6 round trips) the - ab ek hi query.
                // Result bilkul same hai, sirf network trips kam.
                try{
                    using var statCmd=new SqlCommand(@"SELECT
                            ISNULL(SUM(CASE WHEN AssignedTo=@M AND (Status IS NULL OR Status<>'Closed') THEN 1 ELSE 0 END),0) AS MyPending,
                            ISNULL(SUM(CASE WHEN AssignedTo=@M AND CAST(SupportDate AS DATE)=CAST(GETDATE() AS DATE) THEN 1 ELSE 0 END),0) AS TodayCalls,
                            ISNULL(SUM(CASE WHEN AssignedTo=@M THEN 1 ELSE 0 END),0) AS TotalCalls,
                            ISNULL(SUM(CASE WHEN AssignedTo=@M AND Status IN ('Open','In Progress','On Hold','Pending') THEN 1 ELSE 0 END),0) AS OpenCalls,
                            ISNULL(SUM(CASE WHEN AssignedTo=@M AND Status='Closed' AND CAST(SupportDate AS DATE)=CAST(GETDATE() AS DATE) THEN 1 ELSE 0 END),0) AS ClosedToday,
                            ISNULL(SUM(CASE WHEN AssignedTo=@M AND Status IN ('In Progress','Pending','On Hold') THEN 1 ELSE 0 END),0) AS PendingCalls
                        FROM DailySupport",con);
                    statCmd.Parameters.AddWithValue("@M",myUid.Value);
                    using var sr=statCmd.ExecuteReader();
                    if(sr.Read()){
                        vm.MyPendingCount=Convert.ToInt32(sr["MyPending"]);
                        vm.TodayCalls=Convert.ToInt32(sr["TodayCalls"]);
                        vm.TotalCalls=Convert.ToInt32(sr["TotalCalls"]);
                        vm.OpenCalls=Convert.ToInt32(sr["OpenCalls"]);
                        vm.ClosedToday=Convert.ToInt32(sr["ClosedToday"]);
                        vm.PendingCalls=Convert.ToInt32(sr["PendingCalls"]);
                    }
                }catch{}

                // Sirf WO tickets jo mere naam par assigned hain aur abhi CLOSED
                // nahi hue. Closed tickets list me nahi aate.
                // Har task ke saath uski LATEST reassign reason bhi aati hai
                // (OUTER APPLY) - "mujhe ye kaam kyun diya gaya".
                // Order: Urgent -> High -> Medium -> Low, uske baad newest.
                string sqlWithReason=@"SELECT TOP 200 d.*, c.ClientName, u.FullName as AssignedName,
                        hr.Reason as ReassignReason, hr.ChangedByName as ReassignedBy, hr.ChangedOn as ReassignedOn
                    FROM DailySupport d LEFT JOIN Client c ON d.ClientId=c.ClientId LEFT JOIN Users u ON d.AssignedTo=u.UserId
                    OUTER APPLY (SELECT TOP 1 h.Reason, h.ChangedByName, h.ChangedOn
                                 FROM DailySupportHistory h
                                 WHERE h.SupportId=d.SupportId AND h.Action='Reassigned'
                                   AND NULLIF(LTRIM(RTRIM(ISNULL(h.Reason,''))),'') IS NOT NULL
                                 ORDER BY h.HistoryId DESC) hr
                    WHERE d.AssignedTo=@M AND (d.Status IS NULL OR d.Status <> 'Closed')
                    ORDER BY CASE LOWER(ISNULL(d.Priority,'')) WHEN 'urgent' THEN 0 WHEN 'high' THEN 1 WHEN 'medium' THEN 2 ELSE 3 END,
                             d.SupportId DESC";
                // Fallback: agar DB me DailySupportHistory / Reason column na ho
                string sqlBasic=@"SELECT TOP 200 d.*, c.ClientName, u.FullName as AssignedName
                    FROM DailySupport d LEFT JOIN Client c ON d.ClientId=c.ClientId LEFT JOIN Users u ON d.AssignedTo=u.UserId
                    WHERE d.AssignedTo=@M AND (d.Status IS NULL OR d.Status <> 'Closed')
                    ORDER BY CASE LOWER(ISNULL(d.Priority,'')) WHEN 'urgent' THEN 0 WHEN 'high' THEN 1 WHEN 'medium' THEN 2 ELSE 3 END,
                             d.SupportId DESC";

                void ReadTasks(string sqlText){
                    vm.MyTasks.Clear();
                    using var taskCmd=new SqlCommand(sqlText,con);
                    taskCmd.Parameters.AddWithValue("@M",myUid.Value);
                    using var r=taskCmd.ExecuteReader();
                    while(r.Read()){
                        var row=new Dictionary<string,object?>();
                        for(int i=0;i<r.FieldCount;i++) row[r.GetName(i)]=r.IsDBNull(i)?null:r.GetValue(i);
                        vm.MyTasks.Add(row);
                    }
                }
                try{ ReadTasks(sqlWithReason); }
                catch{ try{ ReadTasks(sqlBasic); }catch{} }

                // NEW (ticket comments): per task latest comment + total count (1 query).
                // Unread count alag query se aata hai (neeche) - isliye yahan sirf latest text.
                var taskIds=new List<int>();
                try{
                    foreach(var t in vm.MyTasks){
                        if(t.ContainsKey("SupportId") && t["SupportId"]!=null){
                            var sid=Convert.ToInt32(t["SupportId"]);
                            if(!taskIds.Contains(sid)) taskIds.Add(sid);
                        }
                    }
                    if(taskIds.Count>0){
                        var idList=string.Join(",",taskIds);
                        var cm=ReadRows(con,@"SELECT c.SupportId, c.CommentText, c.CreatedByName, c.CreatedOn, x.Cnt
                            FROM dbo.DailySupportComment c
                            INNER JOIN (SELECT SupportId, MAX(CommentId) AS LastId, COUNT(*) AS Cnt
                                        FROM dbo.DailySupportComment WHERE SupportId IN ("+idList+@") GROUP BY SupportId) x
                                ON x.LastId=c.CommentId",_=>{});
                        foreach(var row in cm){
                            var sid=Convert.ToInt32(row["SupportId"]);
                            Dictionary<string,object?>? task=null;
                            foreach(var t in vm.MyTasks){
                                if(t.ContainsKey("SupportId")&&t["SupportId"]!=null&&Convert.ToInt32(t["SupportId"])==sid){ task=t; break; }
                            }
                            if(task==null) continue;
                            task["LastCommentText"]=row["CommentText"];
                            task["LastCommentBy"]=row["CreatedByName"];
                            task["LastCommentOn"]=row["CreatedOn"];
                            task["CommentCount"]=row["Cnt"];
                        }
                    }
                }catch{}

                // NEW: mere liye unread comments (1 query) - teeno kaam karti hai:
                //   (a) upar banner ka total, (b) task row ka NEW pill, (c) closed/reassigned ka block.
                // Pehle ye 2 alag queries thi (total unread + block).
                try{
                    var unread=ReadRows(con,@"SELECT u.SupportId, u.UnreadCnt, c.CommentText, c.CreatedByName, c.CreatedOn,
                            d.TicketNo, d.Status, cl.ClientName
                        FROM (SELECT SupportId, COUNT(*) AS UnreadCnt, MAX(CommentId) AS LastId
                              FROM dbo.DailySupportComment
                              WHERE ReplyToUserId=@M AND ReadOn IS NULL
                                AND (CreatedBy IS NULL OR CreatedBy<>@M)
                              GROUP BY SupportId) u
                        INNER JOIN dbo.DailySupportComment c ON c.CommentId=u.LastId
                        INNER JOIN DailySupport d           ON d.SupportId=u.SupportId
                        LEFT JOIN Client cl                 ON d.ClientId=cl.ClientId
                        ORDER BY u.LastId DESC",
                        c=>c.Parameters.AddWithValue("@M",myUid.Value));
                    foreach(var row in unread){
                        if(row["SupportId"]==null) continue;
                        var sid=Convert.ToInt32(row["SupportId"]);
                        var cnt=row["UnreadCnt"]==null?0:Convert.ToInt32(row["UnreadCnt"]);
                        vm.MyUnreadComments+=cnt;                       // banner total (sab tickets ka)
                        if(taskIds.Contains(sid)){
                            foreach(var t in vm.MyTasks){               // mera open task -> row pe NEW pill
                                if(t.ContainsKey("SupportId")&&t["SupportId"]!=null&&Convert.ToInt32(t["SupportId"])==sid){
                                    t["UnreadCommentCount"]=cnt; break;
                                }
                            }
                        }
                        else if(vm.MyNewComments.Count<8){              // closed / reassigned -> alag block
                            vm.MyNewComments.Add(row);
                        }
                    }
                }catch{}
            }catch{}
        }
        return PartialView("_MyWork", vm);
    }

    [HttpGet] public IActionResult DSSReports(string? search,string? fromDate,string? toDate,string? status,string? priority,int page=1,int pageSize=10){
        if(pageSize!=10&&pageSize!=25&&pageSize!=50&&pageSize!=100) pageSize=10; if(page<1) page=1;
        using var con=_db.GetConnection(); con.Open(); string where=" WHERE 1=1 ";
        if(!string.IsNullOrWhiteSpace(search)) where+=" AND (d.TicketNo LIKE @Search OR c.ClientName LIKE @Search OR d.Mobile LIKE @Search OR d.Issue LIKE @Search OR d.CallerName LIKE @Search) ";
        if(!string.IsNullOrWhiteSpace(fromDate)&&!string.IsNullOrWhiteSpace(toDate)) where+=" AND d.SupportDate >= @FromDate AND d.SupportDate < DATEADD(DAY,1,@ToDate) ";
        // "All Status" (default) me Closed tickets nahi dikhte - sirf tab dikhte hain
        // jab user dropdown se "Closed" select kare.
        if(!string.IsNullOrWhiteSpace(status)) where+=" AND d.Status = @Status ";
        else where+=" AND (d.Status IS NULL OR d.Status <> 'Closed') ";
        if(!string.IsNullOrWhiteSpace(priority)) where+=" AND d.Priority = @Priority ";
        int total=0; try{ using var countCmd=new SqlCommand($"SELECT COUNT(*) FROM DailySupport d LEFT JOIN Client c ON d.ClientId=c.ClientId {where}",con); if(!string.IsNullOrWhiteSpace(search)) countCmd.Parameters.AddWithValue("@Search","%"+search+"%"); if(!string.IsNullOrWhiteSpace(fromDate)){countCmd.Parameters.AddWithValue("@FromDate",fromDate);countCmd.Parameters.AddWithValue("@ToDate",toDate);} if(!string.IsNullOrWhiteSpace(status)) countCmd.Parameters.AddWithValue("@Status",status); if(!string.IsNullOrWhiteSpace(priority)) countCmd.Parameters.AddWithValue("@Priority",priority); total=(int)countCmd.ExecuteScalar(); }catch{}
        // Last valid page par clamp (khaali grid na dikhe)
        int totalPages = total>0 ? (int)Math.Ceiling(total/(double)pageSize) : 1;
        if(page>totalPages) page=totalPages;
        string dataSql=$@"SELECT d.*, c.ClientName as CompanyName, u.FullName as AssignedToName, u.FullName as AssignedName FROM DailySupport d LEFT JOIN Client c ON d.ClientId=c.ClientId LEFT JOIN Users u ON d.AssignedTo=u.UserId {where} ORDER BY d.SupportId DESC OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY";
        using var dataCmd=new SqlCommand(dataSql,con); if(!string.IsNullOrWhiteSpace(search)) dataCmd.Parameters.AddWithValue("@Search","%"+search+"%"); if(!string.IsNullOrWhiteSpace(fromDate)){dataCmd.Parameters.AddWithValue("@FromDate",fromDate);dataCmd.Parameters.AddWithValue("@ToDate",toDate);} if(!string.IsNullOrWhiteSpace(status)) dataCmd.Parameters.AddWithValue("@Status",status); if(!string.IsNullOrWhiteSpace(priority)) dataCmd.Parameters.AddWithValue("@Priority",priority); dataCmd.Parameters.AddWithValue("@Skip",(page-1)*pageSize); dataCmd.Parameters.AddWithValue("@Take",pageSize);
        var rows=new List<Dictionary<string,object?>>(); try{ using var reader=dataCmd.ExecuteReader(); while(reader.Read()){ var row=new Dictionary<string,object?>(); for(int i=0;i<reader.FieldCount;i++) row[reader.GetName(i)]=reader.IsDBNull(i)?null:reader.GetValue(i); rows.Add(row);} }catch{}
        var vm=new DSSReportViewModel{Calls=rows,TotalCalls=total,FromDate=fromDate,ToDate=toDate,SearchTerm=search,FilterStatus=status,FilterPriority=priority,CurrentPage=page,PageSize=pageSize};
        // Sirf grid ka partial - jab client ne 'partial=1' bheja ho
        if(IsAjaxRequest() && Request.Query.ContainsKey("partial")){
            Response.Headers["X-Total-Calls"]=total.ToString();
            Response.Headers["X-Total-Pages"]=totalPages.ToString();
            Response.Headers["X-Current-Page"]=page.ToString();
            Response.Headers["X-Showing-From"]=(total==0?0:((page-1)*pageSize+1)).ToString();
            Response.Headers["X-Showing-To"]=Math.Min(page*pageSize,total).ToString();
            return PartialView("_DSSReportTable",vm);
        }
        return View(vm);
    }

    [HttpGet] public IActionResult ExportExcel(string? fromDate,string? toDate,string? status,string? priority,string? search){
        using var con=_db.GetConnection(); con.Open(); string where=" WHERE 1=1 ";
        if(!string.IsNullOrWhiteSpace(search)) where+=" AND (d.TicketNo LIKE @Search OR c.ClientName LIKE @Search OR d.Mobile LIKE @Search OR d.Issue LIKE @Search OR d.CallerName LIKE @Search) ";
        if(!string.IsNullOrWhiteSpace(fromDate)&&!string.IsNullOrWhiteSpace(toDate)) where+=" AND d.SupportDate >= @FromDate AND d.SupportDate < DATEADD(DAY,1,@ToDate) ";
        // Export bhi grid jaisa hi rahe: "All" par Closed exclude, "Closed" select par sirf Closed
        if(!string.IsNullOrWhiteSpace(status)) where+=" AND d.Status = @Status ";
        else where+=" AND (d.Status IS NULL OR d.Status <> 'Closed') ";
        if(!string.IsNullOrWhiteSpace(priority)) where+=" AND d.Priority = @Priority ";
        string dataSql=$@"SELECT d.TicketNo, d.SupportDate as [DateTime], c.ClientName as Customer, d.Mobile, d.CallerName, d.Issue, d.Priority, d.Status, u.FullName as AssignedTo, d.CreatedOn FROM DailySupport d LEFT JOIN Client c ON d.ClientId=c.ClientId LEFT JOIN Users u ON d.AssignedTo=u.UserId {where} ORDER BY d.SupportId DESC";
        using var cmd=new SqlCommand(dataSql,con); if(!string.IsNullOrWhiteSpace(search)) cmd.Parameters.AddWithValue("@Search","%"+search+"%"); if(!string.IsNullOrWhiteSpace(fromDate)){cmd.Parameters.AddWithValue("@FromDate",fromDate);cmd.Parameters.AddWithValue("@ToDate",toDate);} if(!string.IsNullOrWhiteSpace(status)) cmd.Parameters.AddWithValue("@Status",status); if(!string.IsNullOrWhiteSpace(priority)) cmd.Parameters.AddWithValue("@Priority",priority);
        var dt=new DataTable(); try{ using var adapter=new SqlDataAdapter(cmd); adapter.Fill(dt);}catch{}
        using var workbook=new XLWorkbook(); var worksheet=workbook.Worksheets.Add("DSS Reports"); worksheet.Cell(1,1).Value="DSS Detailed Report"; worksheet.Cell(1,1).Style.Font.Bold=true; worksheet.Cell(1,1).Style.Font.FontSize=14;
        worksheet.Cell(2,1).Value=$"Filters: From {fromDate??"All"} To {toDate??"All"} | Status: {(string.IsNullOrWhiteSpace(status)?"All (Closed excluded)":status)} | Priority: {priority??"All"} | Search: {search??"All"} | Exported: {DateTime.Now:dd-MM-yyyy hh:mm tt}";
        if(dt.Rows.Count>0){ worksheet.Cell(4,1).InsertTable(dt,"DSSData",true); var table=worksheet.Tables.FirstOrDefault(); if(table!=null){table.ShowAutoFilter=true;table.Theme=XLTableTheme.TableStyleLight9;}} else{ worksheet.Cell(4,1).Value="No records found"; }
        worksheet.Columns().AdjustToContents(); using var stream=new MemoryStream(); workbook.SaveAs(stream); stream.Position=0; string fileName=$"DSS_Report_{DateTime.Now:ddMMyyyy_hhmmtt}.xlsx"; return File(stream.ToArray(),"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",fileName);
    }

    // ---- Audit history (DailySupportHistory) --------------------------------
    // Har create/edit ka record: kisne, kya badla, purana->naya value, kab.
    private static string? Trunc200(string? s)=> s!=null&&s.Length>200 ? s.Substring(0,200) : s;
    private static string? Trunc300(string? s)=> s!=null&&s.Length>300 ? s.Substring(0,300) : s;
    private void LogTicketHistory(int supportId,string action,string? field,string? oldVal,string? newVal,string? reason=null){
        // Kaun kar raha hai - pehle session se, phir cookie claims se (session
        // app restart pe wipe ho jata hai to history me naam NULL padhta tha)
        int? byUserId = HttpContext.Session.GetInt32("UserId");
        string? byUserName = HttpContext.Session.GetString("FullName");
        if(byUserId==null || string.IsNullOrWhiteSpace(byUserName)){
            var claims = HttpContext.User;
            if(byUserId==null){
                var uidTxt = claims?.FindFirst("UserId")?.Value;
                if(int.TryParse(uidTxt, out var uid)) byUserId = uid;
            }
            if(string.IsNullOrWhiteSpace(byUserName)){
                byUserName = claims?.FindFirst("FullName")?.Value;
                if(string.IsNullOrWhiteSpace(byUserName)) byUserName = claims?.Identity?.Name;
            }
        }
        try{ using var con=_db.GetConnection(); con.Open();
            // Pehle Reason column ke saath try; purana DB (column nahi) to bina Reason ke insert
            try{ using var cmd=new SqlCommand(@"INSERT INTO dbo.DailySupportHistory(SupportId,Action,FieldName,OldValue,NewValue,ChangedBy,ChangedByName,Reason)
                                           VALUES(@Sid,@Act,@F,@O,@N,@By,@ByName,@Reason)",con);
                cmd.Parameters.AddWithValue("@Sid",supportId);
                cmd.Parameters.AddWithValue("@Act",action);
                cmd.Parameters.AddWithValue("@F",(object?)field??DBNull.Value);
                cmd.Parameters.AddWithValue("@O",(object?)Trunc200(oldVal)??DBNull.Value);
                cmd.Parameters.AddWithValue("@N",(object?)Trunc200(newVal)??DBNull.Value);
                cmd.Parameters.AddWithValue("@By",(object?)byUserId??DBNull.Value);
                cmd.Parameters.AddWithValue("@ByName",(object?)byUserName??DBNull.Value);
                cmd.Parameters.AddWithValue("@Reason",(object?)Trunc300(reason)??DBNull.Value);
                cmd.ExecuteNonQuery();
            }catch{ using var cmd2=new SqlCommand(@"INSERT INTO dbo.DailySupportHistory(SupportId,Action,FieldName,OldValue,NewValue,ChangedBy,ChangedByName)
                                           VALUES(@Sid,@Act,@F,@O,@N,@By,@ByName)",con);
                cmd2.Parameters.AddWithValue("@Sid",supportId);
                cmd2.Parameters.AddWithValue("@Act",action);
                cmd2.Parameters.AddWithValue("@F",(object?)field??DBNull.Value);
                cmd2.Parameters.AddWithValue("@O",(object?)Trunc200(oldVal)??DBNull.Value);
                cmd2.Parameters.AddWithValue("@N",(object?)Trunc200(newVal)??DBNull.Value);
                cmd2.Parameters.AddWithValue("@By",(object?)byUserId??DBNull.Value);
                cmd2.Parameters.AddWithValue("@ByName",(object?)byUserName??DBNull.Value);
                cmd2.ExecuteNonQuery();
            }
        }catch{ /* history fail ho to save/update na ruke */ }
    }
    private string ResolveUserName(string? id){
        if(string.IsNullOrWhiteSpace(id)) return "";
        if(!int.TryParse(id,out var i)) return id;
        try{ using var con=_db.GetConnection(); con.Open(); using var c=new SqlCommand("SELECT TOP 1 FullName FROM Users WHERE UserId=@V",con); c.Parameters.AddWithValue("@V",i); return c.ExecuteScalar()?.ToString()??id; }catch{ return id; }
    }
    private string ResolveClientName(string? id){
        if(string.IsNullOrWhiteSpace(id)) return "";
        if(!int.TryParse(id,out var i)) return id;
        try{ using var con=_db.GetConnection(); con.Open(); using var c=new SqlCommand("SELECT TOP 1 ClientName FROM Client WHERE ClientId=@V",con); c.Parameters.AddWithValue("@V",i); return c.ExecuteScalar()?.ToString()??id; }catch{ return id; }
    }

    [HttpGet] public IActionResult Create(){
        var cols=_dynamicRepo.GetColumns("DailySupport");
        ViewBag.TicketNo=GenerateTicketNo();
        ViewBag.CanAssign=AccessControl.CanAssignWork(HttpContext);
        ViewBag.MyUserId=AccessControl.CurrentUserId(HttpContext);
        ViewBag.MyUserName=AccessControl.CurrentUserName(HttpContext);
        try{ ViewBag.Customers=_dynamicRepo.GetTable("Client").Rows; }catch{ ViewBag.Customers=new List<Dictionary<string,object?>>(); }
        try{ ViewBag.Engineers=_dynamicRepo.GetTable("Users").Rows; }catch{ ViewBag.Engineers=new List<Dictionary<string,object?>>(); }
        // AJAX (loadPage) -> sirf form ka partial, warna poora layout page
        // #mainContentArea ke andar inject hokar toot jata tha.
        if(Request.Headers["X-Requested-With"]=="XMLHttpRequest") return PartialView(cols);
        return View(cols);
    }
    [HttpPost][ValidateAntiForgeryToken] public IActionResult Create(IFormCollection form){
        var values=form.Keys.Where(k=>k!="__RequestVerificationToken").ToDictionary(k=>k,k=>form[k].ToString());
        // Normal user sirf apne aap ko assign kar sakta hai - form me bhaale
        // kisi dusre ka UserId aaye to bhi server apni id hi rakhega.
        if(!AccessControl.CanAssignWork(HttpContext)){
            int? myUid=AccessControl.CurrentUserId(HttpContext);
            if(myUid.HasValue) values["AssignedTo"]=myUid.Value.ToString();
        }
        if(!values.ContainsKey("TicketNo")||string.IsNullOrWhiteSpace(values["TicketNo"])) values["TicketNo"]=GenerateTicketNo();
        else{
            // Form ka preview number kabhi-doosre-tab se already save ho chuka ho to duplicate na ban jaye
            using var con=_db.GetConnection(); con.Open(); using var chk=new SqlCommand("SELECT COUNT(*) FROM DailySupport WHERE TicketNo=@T",con); chk.Parameters.AddWithValue("@T",values["TicketNo"]);
            if(Convert.ToInt32(chk.ExecuteScalar())>0) values["TicketNo"]=GenerateTicketNo();
        }
        if(!values.ContainsKey("SupportDate")) values["SupportDate"]=AppTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        try{ int newId=_dynamicRepo.Insert("DailySupport",values);
            LogTicketHistory(newId,"Created","Ticket",null,values.GetValueOrDefault("TicketNo"));
            if(Request.Headers["X-Requested-With"]=="XMLHttpRequest") return Json(new{success=true,message="Call entry saved successfully."});
            return RedirectToAction(nameof(Index));
        }catch(Exception ex){ if(Request.Headers["X-Requested-With"]=="XMLHttpRequest") return StatusCode(StatusCodes.Status400BadRequest, new{success=false,message=ex.Message}); throw; }
    }
    [HttpGet] public IActionResult Edit(int id){
        MarkCommentsRead(id); // NEW: ticket kholte hi mere comments read mark (additive, kabhi throw nahi karta)
        var cols=_dynamicRepo.GetColumns("DailySupport");
        using var con=_db.GetConnection(); con.Open(); using var cmd=new SqlCommand("SELECT * FROM DailySupport WHERE SupportId=@Id",con); cmd.Parameters.AddWithValue("@Id",id); using var reader=cmd.ExecuteReader(); if(!reader.Read()) return NotFound();
        var row=new Dictionary<string,object?>(); for(int i=0;i<reader.FieldCount;i++) row[reader.GetName(i)]=reader.IsDBNull(i)?null:reader.GetValue(i); reader.Close();
        ViewBag.RowData=row; ViewBag.CanAssign=AccessControl.CanAssignWork(HttpContext); ViewBag.MyUserId=AccessControl.CurrentUserId(HttpContext); ViewBag.MyUserName=AccessControl.CurrentUserName(HttpContext); try{ ViewBag.Customers=_dynamicRepo.GetTable("Client").Rows; }catch{ ViewBag.Customers=new List<Dictionary<string,object?>>(); } try{ ViewBag.Engineers=_dynamicRepo.GetTable("Users").Rows; }catch{ ViewBag.Engineers=new List<Dictionary<string,object?>>(); }
        // AJAX (loadPage) -> sirf form ka partial (Add Lead wala pattern)
        if(Request.Headers["X-Requested-With"]=="XMLHttpRequest") return PartialView(cols);
        return View(cols);
    }
    [HttpPost][ValidateAntiForgeryToken] public IActionResult Edit(int id,IFormCollection form){
        var values=form.Keys.Where(k=>k!="__RequestVerificationToken"&&k!="SupportId"&&k!="TicketNo").ToDictionary(k=>k,k=>form[k].ToString());
        // Update se PEHLE purane values -> taaki kya badla (old->new) history me aa sake
        var oldVals=new Dictionary<string,object?>();
        try{ using var con=_db.GetConnection(); con.Open(); using var cmd=new SqlCommand("SELECT * FROM DailySupport WHERE SupportId=@Id",con); cmd.Parameters.AddWithValue("@Id",id); using var r=cmd.ExecuteReader(); if(r.Read()) for(int i=0;i<r.FieldCount;i++) oldVals[r.GetName(i)]=r.IsDBNull(i)?null:r.GetValue(i); }catch{}
        // Normal user reassign nahi kar sakta - AssignedTo ka purana value hi
        // rakho (form me koi bhi value aaye). History me bhi change log nahi hoga.
        if(!AccessControl.CanAssignWork(HttpContext)&&oldVals.TryGetValue("AssignedTo",out var oldAssigned)){
            values["AssignedTo"]=oldAssigned?.ToString()??"";
        }
        try{ _dynamicRepo.Update("DailySupport","SupportId",id,values);
            // Changed fields ka audit log (AssignedTo/ClientId ka naam snapshot ke saath)
            foreach(var kv in values){
                if(kv.Key is "CreatedOn" or "SupportDate") continue;
                if(!oldVals.TryGetValue(kv.Key,out var ovRaw)) continue;
                string oldV=ovRaw?.ToString()??"", newV=kv.Value??"";
                if(oldV==newV) continue;
                string fo=oldV, fn=newV;
                if(kv.Key=="AssignedTo"){ fo=ResolveUserName(oldV); fn=ResolveUserName(newV); }
                else if(kv.Key=="ClientId"){ fo=ResolveClientName(oldV); fn=ResolveClientName(newV); }
                LogTicketHistory(id,"Edited",kv.Key,fo,fn);
            }
            if(Request.Headers["X-Requested-With"]=="XMLHttpRequest") return Json(new{success=true,id=id,message="Call entry updated successfully."});
            return RedirectToAction(nameof(Index));
        }catch(Exception ex){ if(Request.Headers["X-Requested-With"]=="XMLHttpRequest") return StatusCode(StatusCodes.Status400BadRequest, new{success=false,message=ex.Message}); throw; }
    }
    // single=true -> sirf is ticket ki row history me (Index ka View modal).
    // Warna (DSS Reports) client ki last 10 calls waisi hi rehti hain.
    [HttpGet] public IActionResult GetDetails(int id, bool single=false, bool includeAudit=false, bool includeClientHistory=false){
        using var con=_db.GetConnection(); con.Open();
        Dictionary<string,object?>? ticket=null; Dictionary<string,object?>? customer=null;
        List<Dictionary<string,object?>> history=new(), audit=new(), clientHistory=new(), clientAudit=new();
        using(var cmd=new SqlCommand(@"SELECT d.*, c.ClientName as CompanyName, u.FullName as AssignedToName FROM DailySupport d LEFT JOIN Client c ON d.ClientId=c.ClientId LEFT JOIN Users u ON d.AssignedTo=u.UserId WHERE d.SupportId=@Id",con)){cmd.Parameters.AddWithValue("@Id",id); using var reader=cmd.ExecuteReader(); if(reader.Read()){ticket=new Dictionary<string,object?>(); for(int i=0;i<reader.FieldCount;i++) ticket[reader.GetName(i)]=reader.IsDBNull(i)?null:reader.GetValue(i);} reader.Close();}

        // Client resolve: ClientId na ho (walk-in / purana data) to Mobile se dhoondo
        int clientId=0;
        if(ticket!=null){
            if(ticket.ContainsKey("ClientId")&&ticket["ClientId"]!=null) int.TryParse(Convert.ToString(ticket["ClientId"]),out clientId);
            if(clientId<=0 && ticket.ContainsKey("Mobile") && ticket["Mobile"]!=null){
                var mob=Convert.ToString(ticket["Mobile"]);
                if(!string.IsNullOrWhiteSpace(mob)){
                    try{ using var mcmd=new SqlCommand("SELECT TOP 1 ClientId FROM Client WHERE MobileNo LIKE @M ORDER BY ClientId",con); mcmd.Parameters.AddWithValue("@M","%"+mob+"%"); var o=mcmd.ExecuteScalar(); if(o!=null&&o!=DBNull.Value) clientId=Convert.ToInt32(o); }catch{}
                }
            }
        }

        if(clientId>0){
            try{ using var cmd2=new SqlCommand("SELECT TOP 1 * FROM Client WHERE ClientId=@Cid",con); cmd2.Parameters.AddWithValue("@Cid",clientId); using var reader2=cmd2.ExecuteReader(); if(reader2.Read()){customer=new Dictionary<string,object?>(); for(int i=0;i<reader2.FieldCount;i++) customer[reader2.GetName(i)]=reader2.IsDBNull(i)?null:reader2.GetValue(i);} reader2.Close(); }catch{}
            try{
                string sql = single
                    ? "SELECT TicketNo, SupportDate, Issue, Status, Priority FROM DailySupport WHERE SupportId=@Id"
                    : "SELECT TOP 10 TicketNo, SupportDate, Issue, Status, Priority FROM DailySupport WHERE ClientId=@Cid ORDER BY SupportId DESC";
                using var cmd3=new SqlCommand(sql,con); cmd3.Parameters.AddWithValue("@Cid",clientId); cmd3.Parameters.AddWithValue("@Id",id);
                using var reader3=cmd3.ExecuteReader(); while(reader3.Read()){ var r=new Dictionary<string,object?>(); for(int i=0;i<reader3.FieldCount;i++) r[reader3.GetName(i)]=reader3.IsDBNull(i)?null:reader3.GetValue(i); history.Add(r);}
            }catch{}
            if(includeClientHistory){
                // Us client ki SAARI calls (koi TOP 10 limit nahi)
                clientHistory=ReadRows(con,@"SELECT d.SupportId, d.TicketNo, d.SupportDate, d.Issue, d.Status, d.Priority, d.Category, d.CreatedOn, u.FullName as AssignedToName
                    FROM DailySupport d LEFT JOIN Users u ON d.AssignedTo=u.UserId WHERE d.ClientId=@Cid ORDER BY d.SupportId DESC",c=>c.Parameters.AddWithValue("@Cid",clientId));
                // Us client ki saari tickets ka audit (kab kya hua) - Reason column na ho to bina Reason ke
                clientAudit=ReadAudit(con,@"SELECT h.HistoryId, h.SupportId, d.TicketNo, h.Action, h.FieldName, h.OldValue, h.NewValue, h.ChangedByName, h.ChangedOn, h.Reason
                    FROM DailySupportHistory h INNER JOIN DailySupport d ON d.SupportId=h.SupportId WHERE d.ClientId=@Cid ORDER BY h.ChangedOn, h.HistoryId",c=>c.Parameters.AddWithValue("@Cid",clientId));
            }
        }
        // Selected ticket ka audit trail (Index View ya report View)
        if(single || includeAudit){
            audit=ReadAudit(con,@"SELECT h.HistoryId, h.Action, h.FieldName, h.OldValue, h.NewValue, ISNULL(NULLIF(LTRIM(RTRIM(h.ChangedByName)),''), u.FullName) as ChangedByName, h.ChangedOn, h.Reason
                FROM DailySupportHistory h LEFT JOIN Users u ON h.ChangedBy = u.UserId WHERE h.SupportId=@Id ORDER BY h.HistoryId",c=>c.Parameters.AddWithValue("@Id",id));
        }
        return Json(new{ticket,customer,history,audit,clientHistory,clientAudit});
    }

    // Generic SELECT -> Dictionary list
    private static List<Dictionary<string,object?>> ReadRows(SqlConnection con,string sql,Action<SqlCommand> bind){
        var list=new List<Dictionary<string,object?>>();
        try{ using var cmd=new SqlCommand(sql,con); bind(cmd); using var r=cmd.ExecuteReader(); while(r.Read()){ var row=new Dictionary<string,object?>(); for(int i=0;i<r.FieldCount;i++) row[r.GetName(i)]=r.IsDBNull(i)?null:r.GetValue(i); list.Add(row);} }catch{}
        return list;
    }

    // Audit reader: pehle Reason ke saath; purane DB me column na ho to bina Reason ke
    private static List<Dictionary<string,object?>> ReadAudit(SqlConnection con,string sql,Action<SqlCommand> bind){
        var list=new List<Dictionary<string,object?>>();
        try{
            using var cmd=new SqlCommand(sql,con); bind(cmd);
            using var r=cmd.ExecuteReader();
            while(r.Read()){ var row=new Dictionary<string,object?>(); for(int i=0;i<r.FieldCount;i++) row[r.GetName(i)]=r.IsDBNull(i)?null:r.GetValue(i); list.Add(row);}
            return list;
        }catch{
            list.Clear();
            string sql2=sql.Replace(", h.Reason","");
            try{
                using var cmd2=new SqlCommand(sql2,con); bind(cmd2);
                using var r2=cmd2.ExecuteReader();
                while(r2.Read()){ var row=new Dictionary<string,object?>(); for(int i=0;i<r2.FieldCount;i++) row[r2.GetName(i)]=r2.IsDBNull(i)?null:r2.GetValue(i); list.Add(row);}
            }catch{}
            return list;
        }
    }
    // ---- Re-Assign: jab aap khud kaam na kar pao to ticket kisi aur user ko de do ----
    // Users ki list (modal ke dropdown ke liye)
    [HttpGet] public IActionResult GetEngineers(){
        // Sirf Admin/Manager ko hi users ki list chahiye (reassign ke liye)
        if(!AccessControl.CanAssignWork(HttpContext)) return Json(new List<object>());
        var list=new List<object>();
        try{ using var con=_db.GetConnection(); con.Open(); using var cmd=new SqlCommand("SELECT UserId, FullName FROM Users ORDER BY FullName",con); using var r=cmd.ExecuteReader();
            while(r.Read()) list.Add(new{ id=r.GetInt32(0), name=r.IsDBNull(1)||string.IsNullOrWhiteSpace(r.GetString(1)) ? "User #"+r.GetInt32(0) : r.GetString(1) });
        }catch{}
        return Json(list);
    }
    // Ticket ka AssignedTo badal + history me "Reassigned" log (reason ke saath)
    [HttpPost][ValidateAntiForgeryToken] public IActionResult Reassign(int id,int assignedTo,string? reason){
        // Reassign sirf Admin/Manager kar sakta hai - normal user ticket
        // dusron ko nahi de sakta (UI chhupa hai, par server bhi rokta hai).
        if(!AccessControl.CanAssignWork(HttpContext)) return Json(new{success=false,message="Sirf Admin/Manager ticket reassign kar sakta hai."});
        if(id<=0||assignedTo<=0) return Json(new{success=false,message="Ticket ya user sahi nahi hai."});
        int oldAssigned=0; string ticketNo="";
        try{ using var con=_db.GetConnection(); con.Open(); using var cmd=new SqlCommand("SELECT AssignedTo, TicketNo FROM DailySupport WHERE SupportId=@Id",con); cmd.Parameters.AddWithValue("@Id",id); using var r=cmd.ExecuteReader();
            if(r.Read()){ oldAssigned=r.IsDBNull(0)?0:Convert.ToInt32(r.GetValue(0)); ticketNo=r.IsDBNull(1)?"":r.GetValue(1)?.ToString()??""; }
        }catch{}
        if(string.IsNullOrWhiteSpace(ticketNo)) return Json(new{success=false,message="Ticket nahi mila."});
        if(oldAssigned==assignedTo) return Json(new{success=false,message="Ye ticket pehle se usi user ko assigned hai."});
        string newName=ResolveUserName(assignedTo.ToString());
        try{ using var con=_db.GetConnection(); con.Open(); using var cmd=new SqlCommand("UPDATE DailySupport SET AssignedTo=@A WHERE SupportId=@Id",con); cmd.Parameters.AddWithValue("@A",assignedTo); cmd.Parameters.AddWithValue("@Id",id); cmd.ExecuteNonQuery(); }
        catch(Exception ex){ return Json(new{success=false,message="Re-assign fail: "+ex.Message}); }
        // History: AssignedTo old->new naam ke saath + reason
        LogTicketHistory(id,"Reassigned","AssignedTo",ResolveUserName(oldAssigned>0?oldAssigned.ToString():null),newName,reason);
        return Json(new{success=true,message=ticketNo+" ab "+newName+" ko assign ho gaya."});
    }

    // Assigned user ya Admin/Manager ticket ka status change kar sakta hai.
    [HttpPost][ValidateAntiForgeryToken] public IActionResult UpdateStatus(int id,string? status){
        var newStatus=(status??"").Trim();
        var allowed=new[]{"Open","In Progress","On Hold","Pending","Closed"};
        if(id<=0 || !allowed.Contains(newStatus,StringComparer.OrdinalIgnoreCase))
            return Json(new{success=false,message="Valid status select karein."});
        if(!CanAccessCommentTicket(id)) return Json(new{success=false,message="Aap is ticket ka status change nahi kar sakte."});
        string oldStatus="", ticketNo="";
        try{
            using var con=_db.GetConnection(); con.Open();
            using(var cmd=new SqlCommand("SELECT TicketNo, ISNULL(Status,'Open') FROM DailySupport WHERE SupportId=@Id",con)){
                cmd.Parameters.AddWithValue("@Id",id); using var r=cmd.ExecuteReader();
                if(!r.Read()) return Json(new{success=false,message="Ticket nahi mila."});
                ticketNo=r.IsDBNull(0)?"":r.GetString(0); oldStatus=r.IsDBNull(1)?"Open":r.GetString(1);
            }
            if(string.Equals(oldStatus,newStatus,StringComparison.OrdinalIgnoreCase)) return Json(new{success=false,message="Ticket pehle se isi status me hai."});
            using var update=new SqlCommand("UPDATE DailySupport SET Status=@Status WHERE SupportId=@Id",con);
            update.Parameters.AddWithValue("@Status",newStatus); update.Parameters.AddWithValue("@Id",id); update.ExecuteNonQuery();
        }catch(Exception ex){ return Json(new{success=false,message="Status update fail: "+ex.Message}); }
        LogTicketHistory(id,"Status Changed","Status",oldStatus,newStatus,null);
        return Json(new{success=true,message=ticketNo+" ka status "+newStatus+" ho gaya."});
    }

    // ---- Ticket COMMENTS (new) ----------------------------------------------
    // Comment on a ticket / reply to the assigned user. No delete/edit - permanent record.
    // No existing function was modified; these are two new endpoints.

    // NEW: mark comments as read for the current user (only those addressed to me
    // and not written by me). Used when the ticket is opened (popup or edit page).
    private void MarkCommentsRead(int supportId){
        if(supportId<=0) return;
        var me=AccessControl.CurrentUserId(HttpContext);
        if(!me.HasValue) return;
        try{
            using var con=_db.GetConnection(); con.Open();
            using var cmd=new SqlCommand(@"UPDATE dbo.DailySupportComment SET ReadOn=GETDATE()
                WHERE SupportId=@Id AND ReplyToUserId=@M AND ReadOn IS NULL
                  AND (CreatedBy IS NULL OR CreatedBy<>@M)",con);
            cmd.Parameters.AddWithValue("@Id",supportId);
            cmd.Parameters.AddWithValue("@M",me.Value);
            cmd.ExecuteNonQuery();
        }catch(Exception ex){ _logger.LogWarning(ex,"Could not mark comments as read for ticket {SupportId}.",supportId); }
    }

    private bool CanAccessCommentTicket(int supportId){
        var userId=AccessControl.CurrentUserId(HttpContext);
        if(supportId<=0||!userId.HasValue) return false;
        using var con=_db.GetConnection(); con.Open();
        using var cmd=new SqlCommand(@"SELECT COUNT(*) FROM DailySupport
            WHERE SupportId=@Id AND (@CanAssign=1 OR AssignedTo=@UserId)",con);
        cmd.Parameters.AddWithValue("@Id",supportId);
        cmd.Parameters.AddWithValue("@CanAssign",AccessControl.CanAssignWork(HttpContext));
        cmd.Parameters.AddWithValue("@UserId",userId.Value);
        return Convert.ToInt32(cmd.ExecuteScalar())>0;
    }

    [HttpGet] public IActionResult GetComments(int id){
        if(!CanAccessCommentTicket(id)) return NotFound();
        MarkCommentsRead(id); // popup kholne par bhi read mark
        var list=new List<Dictionary<string,object?>>();
        string assignedName="";
        using(var con=_db.GetConnection()){
            con.Open();
            // LEFT JOIN keeps the assigned user's name available when no comments exist.
            using var cmd=new SqlCommand(@"SELECT d.AssignedTo, u.FullName AS AssignedName,
                    c.CommentId, c.SupportId, c.CommentText, c.ReplyToUserId, c.ReplyToName,
                    c.CreatedBy, c.CreatedByName, c.CreatedOn
                FROM DailySupport d
                LEFT JOIN Users u ON d.AssignedTo=u.UserId
                LEFT JOIN dbo.DailySupportComment c ON c.SupportId=d.SupportId
                WHERE d.SupportId=@Id
                ORDER BY c.CommentId",con);
            cmd.Parameters.AddWithValue("@Id",id);
            using var reader=cmd.ExecuteReader();
            while(reader.Read()){
                var row=new Dictionary<string,object?>();
                for(int i=0;i<reader.FieldCount;i++) row[reader.GetName(i)]=reader.IsDBNull(i)?null:reader.GetValue(i);
                if(string.IsNullOrEmpty(assignedName) && row["AssignedName"]!=null)
                    assignedName=row["AssignedName"]!.ToString()!;
                if(row["CommentId"]==null) continue;
                list.Add(row);
            }
        }
        return Json(new{success=true, assignedToName=assignedName, comments=list});
    }

    [HttpPost][ValidateAntiForgeryToken] public IActionResult AddComment(int id,string? comment){
        if(!CanAccessCommentTicket(id)) return NotFound();
        var text=(comment??"").Trim();
        if(text.Length==0) return Json(new{success=false,message="Please write a comment first."});
        if(text.Length>2000) return Json(new{success=false,message="Comment cannot exceed 2000 characters."});
        int? replyTo=null; string? replyToName=null;
        try{
            using var con=_db.GetConnection(); con.Open();
            using(var cmd=new SqlCommand(@"SELECT d.AssignedTo, u.FullName FROM DailySupport d
                    LEFT JOIN Users u ON d.AssignedTo=u.UserId WHERE d.SupportId=@Id",con)){
                cmd.Parameters.AddWithValue("@Id",id);
                using var r=cmd.ExecuteReader();
                if(!r.Read()) return Json(new{success=false,message="Ticket not found."});
                if(!r.IsDBNull(0)) replyTo=Convert.ToInt32(r.GetValue(0));
                if(!r.IsDBNull(1)) replyToName=r.GetString(1);
            }
            var uid=AccessControl.CurrentUserId(HttpContext);
            var uname=AccessControl.CurrentUserName(HttpContext);
            int newId;
            using(var cmd=new SqlCommand(@"INSERT INTO dbo.DailySupportComment(SupportId,CommentText,ReplyToUserId,ReplyToName,CreatedBy,CreatedByName)
                    VALUES(@Sid,@Txt,@Rt,@Rn,@By,@ByName); SELECT CAST(SCOPE_IDENTITY() AS INT);",con)){
                cmd.Parameters.AddWithValue("@Sid",id);
                cmd.Parameters.AddWithValue("@Txt",text);
                cmd.Parameters.AddWithValue("@Rt",(object?)replyTo??DBNull.Value);
                cmd.Parameters.AddWithValue("@Rn",(object?)replyToName??DBNull.Value);
                cmd.Parameters.AddWithValue("@By",(object?)uid??DBNull.Value);
                cmd.Parameters.AddWithValue("@ByName",(object?)uname??DBNull.Value);
                newId=Convert.ToInt32(cmd.ExecuteScalar());
            }
            return Json(new{success=true,message="Comment added.",
                comment=new{CommentId=newId,SupportId=id,CommentText=text,ReplyToUserId=replyTo,ReplyToName=replyToName,
                            CreatedBy=uid,CreatedByName=uname,CreatedOn=DateTime.Now}});
        }catch(Exception ex){
            _logger.LogError(ex,"Could not save a comment for ticket {SupportId}.",id);
            return StatusCode(StatusCodes.Status500InternalServerError,new{success=false,message="Could not save comment. Please try again."});
        }
    }
    [HttpGet] public IActionResult SearchClientByMobile(string mobile){
        if(string.IsNullOrWhiteSpace(mobile)) return Json(null);
        using var con=_db.GetConnection(); con.Open();
        // Sirf MobileNo par search karo - Client table me PhoneNo column nahi hota
        using var cmd=new SqlCommand("SELECT TOP 1 * FROM Client WHERE MobileNo LIKE @M",con);
        cmd.Parameters.AddWithValue("@M","%"+mobile+"%");
        using var reader=cmd.ExecuteReader();
        if(!reader.Read()) return Json(null);
        var row=new Dictionary<string,object?>();
        for(int i=0;i<reader.FieldCount;i++) row[reader.GetName(i)]=reader.IsDBNull(i)?null:reader.GetValue(i);
        return Json(new{ClientId=row.ContainsKey("ClientId")?row["ClientId"]:"",ClientName=row.ContainsKey("ClientName")?row["ClientName"]:"",MobileNo=row.ContainsKey("MobileNo")?row["MobileNo"]:""});
    }
}
