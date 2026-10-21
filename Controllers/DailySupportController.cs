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
    public DailySupportController(DBHelper db, DynamicRepository dynamicRepo){ _db=db; _dynamicRepo=dynamicRepo; }

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
        if(!string.IsNullOrWhiteSpace(fromDate)){ cmd.Parameters.AddWithValue("@F",fromDate); if(cmd.Parameters.Contains("@Cf")) cmd.Parameters["@Cf"].Value=fromDate; }
        if(!string.IsNullOrWhiteSpace(toDate)){ cmd.Parameters.AddWithValue("@T",toDate); if(cmd.Parameters.Contains("@Ct")) cmd.Parameters["@Ct"].Value=toDate; }
    }

    [HttpGet]
    public IActionResult Index(string? search, string? fromDate, string? toDate, string? status, string? user, int page=1, int pageSize=10){
        // ---- Pagination safety ----
        // Pehle page/pageSize validate nahi hote the. page=0 / pageSize=0 aane
        // par SQL OFFSET/FETCH galat ho jata tha aur grid KHAALI dikhta tha.
        if(pageSize!=10&&pageSize!=20&&pageSize!=50&&pageSize!=100) pageSize=10;
        if(page<1) page=1;
        using var con=_db.GetConnection(); con.Open();
        var vm=new DailySupportDashboardViewModel(); vm.SearchTerm=search; vm.FromDate=fromDate; vm.ToDate=toDate; vm.FilterStatus=status; vm.FilterUser=user;
        // User filter dropdown ke liye active users
        try{ using var ucmd=new SqlCommand("SELECT UserId, FullName FROM Users WHERE IsActive=1 ORDER BY FullName",con); using var ureader=ucmd.ExecuteReader(); while(ureader.Read()){ vm.Users.Add(new Dictionary<string,object?>{ ["UserId"]=ureader.GetInt32(0), ["FullName"]=ureader.IsDBNull(1)?ureader.GetInt32(0).ToString():ureader.GetString(1) }); } }catch{}
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
        try{
            using(var cmd=new SqlCommand($"SELECT COUNT(*) FROM DailySupport d WHERE 1=1 {todaySql}",con)){ AddDateParams(cmd,fromDate,toDate); vm.TodayCalls=Convert.ToInt32(cmd.ExecuteScalar()); }
            using(var cmd=new SqlCommand($"SELECT COUNT(*) FROM DailySupport d WHERE 1=1 {dateSql}",con)){ AddDateParams(cmd,fromDate,toDate); vm.TotalCalls=Convert.ToInt32(cmd.ExecuteScalar()); }
            using(var cmd=new SqlCommand($"SELECT COUNT(*) FROM DailySupport d WHERE Status IN ('Open','In Progress','On Hold','Pending') {dateSql}",con)){ AddDateParams(cmd,fromDate,toDate); vm.OpenCalls=Convert.ToInt32(cmd.ExecuteScalar()); }
            using(var cmd=new SqlCommand($"SELECT COUNT(*) FROM DailySupport d WHERE Status='Closed' {todaySql}",con)){ AddDateParams(cmd,fromDate,toDate); vm.ClosedToday=Convert.ToInt32(cmd.ExecuteScalar()); }
            using(var cmd=new SqlCommand($"SELECT COUNT(*) FROM DailySupport d WHERE Status IN ('In Progress','Pending','On Hold') {dateSql}",con)){ AddDateParams(cmd,fromDate,toDate); vm.PendingCalls=Convert.ToInt32(cmd.ExecuteScalar()); }
        }catch{}
        string where=" WHERE 1=1 "; if(!string.IsNullOrWhiteSpace(search)) where+=" AND (d.TicketNo LIKE @Search OR c.ClientName LIKE @Search OR d.Mobile LIKE @Search OR d.Issue LIKE @Search) "; if(!string.IsNullOrWhiteSpace(fromDate)) where+=" AND d.SupportDate >= @FromDate "; if(!string.IsNullOrWhiteSpace(toDate)) where+=" AND d.SupportDate < DATEADD(DAY,1,@ToDate) "; if(!string.IsNullOrWhiteSpace(status)) where+=" AND d.Status = @Status "; if(!string.IsNullOrWhiteSpace(user)) where+=" AND d.AssignedTo = @UserId ";
        using var countCmd=new SqlCommand($"SELECT COUNT(*) FROM DailySupport d LEFT JOIN Client c ON d.ClientId=c.ClientId {where}",con); if(!string.IsNullOrWhiteSpace(search)) countCmd.Parameters.AddWithValue("@Search","%"+search+"%"); if(!string.IsNullOrWhiteSpace(fromDate)){countCmd.Parameters.AddWithValue("@FromDate",fromDate);countCmd.Parameters["@FromDate"].SqlDbType=SqlDbType.DateTime;} if(!string.IsNullOrWhiteSpace(toDate)){countCmd.Parameters.AddWithValue("@ToDate",toDate);countCmd.Parameters["@ToDate"].SqlDbType=SqlDbType.DateTime;} if(!string.IsNullOrWhiteSpace(status)) countCmd.Parameters.AddWithValue("@Status",status); if(!string.IsNullOrWhiteSpace(user)) countCmd.Parameters.AddWithValue("@UserId",user); int total=0; try{total=(int)countCmd.ExecuteScalar();}catch{}
        // Last page se aage ka page request aaye (ya data delete ho gaya) to
        // grid khaali na dikhe - use last valid page par le aao
        int totalPages = total>0 ? (int)Math.Ceiling(total/(double)pageSize) : 1;
        if(page>totalPages) page=totalPages;
        string dataSql=$@"SELECT d.*, c.ClientName, c.ClientName as CompanyName, u.FullName as AssignedName FROM DailySupport d LEFT JOIN Client c ON d.ClientId=c.ClientId LEFT JOIN Users u ON d.AssignedTo=u.UserId {where} ORDER BY d.SupportId DESC OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY";
        using var dataCmd=new SqlCommand(dataSql,con); if(!string.IsNullOrWhiteSpace(search)) dataCmd.Parameters.AddWithValue("@Search","%"+search+"%"); if(!string.IsNullOrWhiteSpace(fromDate)){dataCmd.Parameters.AddWithValue("@FromDate",fromDate);dataCmd.Parameters["@FromDate"].SqlDbType=SqlDbType.DateTime;} if(!string.IsNullOrWhiteSpace(toDate)){dataCmd.Parameters.AddWithValue("@ToDate",toDate);dataCmd.Parameters["@ToDate"].SqlDbType=SqlDbType.DateTime;} if(!string.IsNullOrWhiteSpace(status)) dataCmd.Parameters.AddWithValue("@Status",status); if(!string.IsNullOrWhiteSpace(user)) dataCmd.Parameters.AddWithValue("@UserId",user); dataCmd.Parameters.AddWithValue("@Skip",(page-1)*pageSize); dataCmd.Parameters.AddWithValue("@Take",pageSize);
        var table=new DailySupportGridViewModel{CurrentPage=page,PageSize=pageSize,TotalRecords=total,SearchTerm=search,FilterStatus=status,FromDate=fromDate,ToDate=toDate,FilterUser=user};
        try{ using var reader=dataCmd.ExecuteReader(); for(int i=0;i<reader.FieldCount;i++) table.Columns.Add(reader.GetName(i)); while(reader.Read()){ var row=new Dictionary<string,object?>(); for(int i=0;i<reader.FieldCount;i++) row[reader.GetName(i)]=reader.IsDBNull(i)?null:reader.GetValue(i); table.Rows.Add(row);} }catch{}
        vm.TicketTable=table;
        
        // CSV/Excel export from Index (poora filtered data)
        if(!string.IsNullOrWhiteSpace(Request.Query["export"])){
            var exp = Request.Query["export"].ToString().ToLower();
            string exWhere=" WHERE 1=1 "; if(!string.IsNullOrWhiteSpace(search)) exWhere+=" AND (d.TicketNo LIKE @Search OR c.ClientName LIKE @Search OR d.Mobile LIKE @Search OR d.Issue LIKE @Search) "; if(!string.IsNullOrWhiteSpace(fromDate)) exWhere+=" AND d.SupportDate >= @FromDate "; if(!string.IsNullOrWhiteSpace(toDate)) exWhere+=" AND d.SupportDate < DATEADD(DAY,1,@ToDate) "; if(!string.IsNullOrWhiteSpace(status)) exWhere+=" AND d.Status = @Status "; if(!string.IsNullOrWhiteSpace(user)) exWhere+=" AND d.AssignedTo = @UserId ";
            string exSql=$@"SELECT d.TicketNo, d.SupportDate, c.ClientName, d.Mobile, d.Channel, d.CallType, d.Category, d.Priority, d.Status, u.FullName as AssignedTo, d.Issue FROM DailySupport d LEFT JOIN Client c ON d.ClientId=c.ClientId LEFT JOIN Users u ON d.AssignedTo=u.UserId {exWhere} ORDER BY d.SupportId DESC";
            using var excmd=new SqlCommand(exSql,con); if(!string.IsNullOrWhiteSpace(search)) excmd.Parameters.AddWithValue("@Search","%"+search+"%"); if(!string.IsNullOrWhiteSpace(fromDate)){excmd.Parameters.AddWithValue("@FromDate",fromDate);excmd.Parameters["@FromDate"].SqlDbType=SqlDbType.DateTime;} if(!string.IsNullOrWhiteSpace(toDate)){excmd.Parameters.AddWithValue("@ToDate",toDate);excmd.Parameters["@ToDate"].SqlDbType=SqlDbType.DateTime;} if(!string.IsNullOrWhiteSpace(status)) excmd.Parameters.AddWithValue("@Status",status); if(!string.IsNullOrWhiteSpace(user)) excmd.Parameters.AddWithValue("@UserId",user);
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

    [HttpGet] public IActionResult DSSReports(string? search,string? fromDate,string? toDate,string? status,string? priority,int page=1,int pageSize=10){
        if(pageSize!=10&&pageSize!=25&&pageSize!=50&&pageSize!=100) pageSize=10; if(page<1) page=1;
        using var con=_db.GetConnection(); con.Open(); string where=" WHERE 1=1 ";
        if(!string.IsNullOrWhiteSpace(search)) where+=" AND (d.TicketNo LIKE @Search OR c.ClientName LIKE @Search OR d.Mobile LIKE @Search OR d.Issue LIKE @Search OR d.CallerName LIKE @Search) ";
        if(!string.IsNullOrWhiteSpace(fromDate)&&!string.IsNullOrWhiteSpace(toDate)) where+=" AND d.SupportDate >= @FromDate AND d.SupportDate < DATEADD(DAY,1,@ToDate) ";
        if(!string.IsNullOrWhiteSpace(status)) where+=" AND d.Status = @Status "; if(!string.IsNullOrWhiteSpace(priority)) where+=" AND d.Priority = @Priority ";
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
        if(!string.IsNullOrWhiteSpace(status)) where+=" AND d.Status = @Status "; if(!string.IsNullOrWhiteSpace(priority)) where+=" AND d.Priority = @Priority ";
        string dataSql=$@"SELECT d.TicketNo, d.SupportDate as [DateTime], c.ClientName as Customer, d.Mobile, d.CallerName, d.Issue, d.Priority, d.Status, u.FullName as AssignedTo, d.CreatedOn FROM DailySupport d LEFT JOIN Client c ON d.ClientId=c.ClientId LEFT JOIN Users u ON d.AssignedTo=u.UserId {where} ORDER BY d.SupportId DESC";
        using var cmd=new SqlCommand(dataSql,con); if(!string.IsNullOrWhiteSpace(search)) cmd.Parameters.AddWithValue("@Search","%"+search+"%"); if(!string.IsNullOrWhiteSpace(fromDate)){cmd.Parameters.AddWithValue("@FromDate",fromDate);cmd.Parameters.AddWithValue("@ToDate",toDate);} if(!string.IsNullOrWhiteSpace(status)) cmd.Parameters.AddWithValue("@Status",status); if(!string.IsNullOrWhiteSpace(priority)) cmd.Parameters.AddWithValue("@Priority",priority);
        var dt=new DataTable(); try{ using var adapter=new SqlDataAdapter(cmd); adapter.Fill(dt);}catch{}
        using var workbook=new XLWorkbook(); var worksheet=workbook.Worksheets.Add("DSS Reports"); worksheet.Cell(1,1).Value="DSS Detailed Report"; worksheet.Cell(1,1).Style.Font.Bold=true; worksheet.Cell(1,1).Style.Font.FontSize=14;
        worksheet.Cell(2,1).Value=$"Filters: From {fromDate??"All"} To {toDate??"All"} | Status: {status??"All"} | Priority: {priority??"All"} | Search: {search??"All"} | Exported: {DateTime.Now:dd-MM-yyyy hh:mm tt}";
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
        try{ ViewBag.Customers=_dynamicRepo.GetTable("Client").Rows; }catch{ ViewBag.Customers=new List<Dictionary<string,object?>>(); }
        try{ ViewBag.Engineers=_dynamicRepo.GetTable("Users").Rows; }catch{ ViewBag.Engineers=new List<Dictionary<string,object?>>(); }
        // AJAX (loadPage) -> sirf form ka partial, warna poora layout page
        // #mainContentArea ke andar inject hokar toot jata tha.
        if(Request.Headers["X-Requested-With"]=="XMLHttpRequest") return PartialView(cols);
        return View(cols);
    }
    [HttpPost][ValidateAntiForgeryToken] public IActionResult Create(IFormCollection form){
        var values=form.Keys.Where(k=>k!="__RequestVerificationToken").ToDictionary(k=>k,k=>form[k].ToString());
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
        var cols=_dynamicRepo.GetColumns("DailySupport");
        using var con=_db.GetConnection(); con.Open(); using var cmd=new SqlCommand("SELECT * FROM DailySupport WHERE SupportId=@Id",con); cmd.Parameters.AddWithValue("@Id",id); using var reader=cmd.ExecuteReader(); if(!reader.Read()) return NotFound();
        var row=new Dictionary<string,object?>(); for(int i=0;i<reader.FieldCount;i++) row[reader.GetName(i)]=reader.IsDBNull(i)?null:reader.GetValue(i); reader.Close();
        ViewBag.RowData=row; try{ ViewBag.Customers=_dynamicRepo.GetTable("Client").Rows; }catch{ ViewBag.Customers=new List<Dictionary<string,object?>>(); } try{ ViewBag.Engineers=_dynamicRepo.GetTable("Users").Rows; }catch{ ViewBag.Engineers=new List<Dictionary<string,object?>>(); }
        // AJAX (loadPage) -> sirf form ka partial (Add Lead wala pattern)
        if(Request.Headers["X-Requested-With"]=="XMLHttpRequest") return PartialView(cols);
        return View(cols);
    }
    [HttpPost][ValidateAntiForgeryToken] public IActionResult Edit(int id,IFormCollection form){
        var values=form.Keys.Where(k=>k!="__RequestVerificationToken"&&k!="SupportId"&&k!="TicketNo").ToDictionary(k=>k,k=>form[k].ToString());
        // Update se PEHLE purane values -> taaki kya badla (old->new) history me aa sake
        var oldVals=new Dictionary<string,object?>();
        try{ using var con=_db.GetConnection(); con.Open(); using var cmd=new SqlCommand("SELECT * FROM DailySupport WHERE SupportId=@Id",con); cmd.Parameters.AddWithValue("@Id",id); using var r=cmd.ExecuteReader(); if(r.Read()) for(int i=0;i<r.FieldCount;i++) oldVals[r.GetName(i)]=r.IsDBNull(i)?null:r.GetValue(i); }catch{}
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
        var list=new List<object>();
        try{ using var con=_db.GetConnection(); con.Open(); using var cmd=new SqlCommand("SELECT UserId, FullName FROM Users ORDER BY FullName",con); using var r=cmd.ExecuteReader();
            while(r.Read()) list.Add(new{ id=r.GetInt32(0), name=r.IsDBNull(1)||string.IsNullOrWhiteSpace(r.GetString(1)) ? "User #"+r.GetInt32(0) : r.GetString(1) });
        }catch{}
        return Json(list);
    }
    // Ticket ka AssignedTo badal + history me "Reassigned" log (reason ke saath)
    [HttpPost][ValidateAntiForgeryToken] public IActionResult Reassign(int id,int assignedTo,string? reason){
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