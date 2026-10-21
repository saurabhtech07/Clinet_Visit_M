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
        using(var cmd=new SqlCommand("SELECT COUNT(*) FROM DailySupport WHERE SupportDate>=@DayStart AND SupportDate<@DayEnd",con)){
            cmd.Parameters.AddWithValue("@DayStart",AppTime.Today);
            cmd.Parameters.AddWithValue("@DayEnd",AppTime.Today.AddDays(1));
            seq=Convert.ToInt32(cmd.ExecuteScalar())+1;
        }
        string ticketNo;
        using(var cmd=new SqlCommand("SELECT COUNT(*) FROM DailySupport WHERE TicketNo=@T",con)){
            cmd.Parameters.Add("@T",System.Data.SqlDbType.VarChar,40);
            do{ ticketNo=$"{prefix}{seq:D3}"; cmd.Parameters["@T"].Value=ticketNo; seq++; }
            while(Convert.ToInt32(cmd.ExecuteScalar())>0);
        }
        return ticketNo;
    }

    [HttpGet]
    public IActionResult Index(string? search, string? fromDate, string? toDate, string? status, string? user, int page=1, int pageSize=10){
        using var con=_db.GetConnection(); con.Open();
        var vm=new DailySupportDashboardViewModel(); vm.SearchTerm=search; vm.FromDate=fromDate; vm.ToDate=toDate; vm.FilterStatus=status; vm.FilterUser=user;
        // User filter dropdown ke liye active users
        try{ using var ucmd=new SqlCommand("SELECT UserId, FullName FROM Users WHERE IsActive=1 ORDER BY FullName",con); using var ureader=ucmd.ExecuteReader(); while(ureader.Read()){ vm.Users.Add(new Dictionary<string,object?>{ ["UserId"]=ureader.GetInt32(0), ["FullName"]=ureader.IsDBNull(1)?ureader.GetInt32(0).ToString():ureader.GetString(1) }); } }catch{}
        string dateFilter=""; if(!string.IsNullOrWhiteSpace(fromDate)&&!string.IsNullOrWhiteSpace(toDate)) dateFilter=" AND CAST(SupportDate AS DATE) BETWEEN @FromDate AND @ToDate";
        try{
            using(var cmd=new SqlCommand($"SELECT COUNT(*) FROM DailySupport WHERE CAST(SupportDate AS DATE)=CAST(GETDATE() AS DATE) {dateFilter}",con)){ if(!string.IsNullOrWhiteSpace(fromDate)){cmd.Parameters.AddWithValue("@FromDate",fromDate);cmd.Parameters.AddWithValue("@ToDate",toDate);} vm.TodayCalls=(int)cmd.ExecuteScalar(); }
            using(var cmd=new SqlCommand($"SELECT COUNT(*) FROM DailySupport WHERE 1=1 {dateFilter}",con)){ if(!string.IsNullOrWhiteSpace(fromDate)){cmd.Parameters.AddWithValue("@FromDate",fromDate);cmd.Parameters.AddWithValue("@ToDate",toDate);} vm.TotalCalls=(int)cmd.ExecuteScalar(); }
            using(var cmd=new SqlCommand($"SELECT COUNT(*) FROM DailySupport WHERE Status IN ('Open','In Progress','On Hold','Pending') {dateFilter}",con)){ if(!string.IsNullOrWhiteSpace(fromDate)){cmd.Parameters.AddWithValue("@FromDate",fromDate);cmd.Parameters.AddWithValue("@ToDate",toDate);} vm.OpenCalls=(int)cmd.ExecuteScalar(); }
            using(var cmd=new SqlCommand($"SELECT COUNT(*) FROM DailySupport WHERE Status='Closed' AND CAST(SupportDate AS DATE)=CAST(GETDATE() AS DATE) {dateFilter}",con)){ if(!string.IsNullOrWhiteSpace(fromDate)){cmd.Parameters.AddWithValue("@FromDate",fromDate);cmd.Parameters.AddWithValue("@ToDate",toDate);} vm.ClosedToday=(int)cmd.ExecuteScalar(); }
            using(var cmd=new SqlCommand($"SELECT COUNT(*) FROM DailySupport WHERE Status IN ('In Progress','Pending','On Hold') {dateFilter}",con)){ if(!string.IsNullOrWhiteSpace(fromDate)){cmd.Parameters.AddWithValue("@FromDate",fromDate);cmd.Parameters.AddWithValue("@ToDate",toDate);} vm.PendingCalls=(int)cmd.ExecuteScalar(); }
        }catch{}
        string where=" WHERE 1=1 "; if(!string.IsNullOrWhiteSpace(search)) where+=" AND (d.TicketNo LIKE @Search OR c.ClientName LIKE @Search OR d.Mobile LIKE @Search OR d.Issue LIKE @Search) "; if(!string.IsNullOrWhiteSpace(fromDate)&&!string.IsNullOrWhiteSpace(toDate)) where+=" AND CAST(d.SupportDate AS DATE) BETWEEN @FromDate AND @ToDate "; if(!string.IsNullOrWhiteSpace(status)) where+=" AND d.Status = @Status "; if(!string.IsNullOrWhiteSpace(user)) where+=" AND d.AssignedTo = @UserId ";
        using var countCmd=new SqlCommand($"SELECT COUNT(*) FROM DailySupport d LEFT JOIN Client c ON d.ClientId=c.ClientId {where}",con); if(!string.IsNullOrWhiteSpace(search)) countCmd.Parameters.AddWithValue("@Search","%"+search+"%"); if(!string.IsNullOrWhiteSpace(fromDate)){countCmd.Parameters.AddWithValue("@FromDate",fromDate);countCmd.Parameters.AddWithValue("@ToDate",toDate);} if(!string.IsNullOrWhiteSpace(status)) countCmd.Parameters.AddWithValue("@Status",status); if(!string.IsNullOrWhiteSpace(user)) countCmd.Parameters.AddWithValue("@UserId",user); int total=0; try{total=(int)countCmd.ExecuteScalar();}catch{}
        string dataSql=$@"SELECT d.*, c.ClientName, c.ClientName as CompanyName, u.FullName as AssignedName FROM DailySupport d LEFT JOIN Client c ON d.ClientId=c.ClientId LEFT JOIN Users u ON d.AssignedTo=u.UserId {where} ORDER BY d.SupportId DESC OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY";
        using var dataCmd=new SqlCommand(dataSql,con); if(!string.IsNullOrWhiteSpace(search)) dataCmd.Parameters.AddWithValue("@Search","%"+search+"%"); if(!string.IsNullOrWhiteSpace(fromDate)){dataCmd.Parameters.AddWithValue("@FromDate",fromDate);dataCmd.Parameters.AddWithValue("@ToDate",toDate);} if(!string.IsNullOrWhiteSpace(status)) dataCmd.Parameters.AddWithValue("@Status",status); if(!string.IsNullOrWhiteSpace(user)) dataCmd.Parameters.AddWithValue("@UserId",user); dataCmd.Parameters.AddWithValue("@Skip",(page-1)*pageSize); dataCmd.Parameters.AddWithValue("@Take",pageSize);
        var table=new DynamicTableViewModel{CurrentPage=page,PageSize=pageSize,TotalRecords=total,SearchTerm=search};
        try{ using var reader=dataCmd.ExecuteReader(); for(int i=0;i<reader.FieldCount;i++) table.Columns.Add(reader.GetName(i)); while(reader.Read()){ var row=new Dictionary<string,object?>(); for(int i=0;i<reader.FieldCount;i++) row[reader.GetName(i)]=reader.IsDBNull(i)?null:reader.GetValue(i); table.Rows.Add(row);} }catch{}
        vm.TicketTable=table;
        
        if(Request.Headers["X-Requested-With"]=="XMLHttpRequest"){
            if(Request.Query.Count>0) return PartialView("_CallTable",table);
            return View(vm);
        }
        return View(vm);
    }

    [HttpGet] public IActionResult DSSReports(string? search,string? fromDate,string? toDate,string? status,string? priority,int page=1,int pageSize=10){
        if(pageSize!=10&&pageSize!=25&&pageSize!=50&&pageSize!=100) pageSize=10; if(page<1) page=1;
        using var con=_db.GetConnection(); con.Open(); string where=" WHERE 1=1 ";
        if(!string.IsNullOrWhiteSpace(search)) where+=" AND (d.TicketNo LIKE @Search OR c.ClientName LIKE @Search OR d.Mobile LIKE @Search OR d.Issue LIKE @Search OR d.CallerName LIKE @Search) ";
        if(!string.IsNullOrWhiteSpace(fromDate)&&!string.IsNullOrWhiteSpace(toDate)) where+=" AND CAST(d.SupportDate AS DATE) BETWEEN @FromDate AND @ToDate ";
        if(!string.IsNullOrWhiteSpace(status)) where+=" AND d.Status = @Status "; if(!string.IsNullOrWhiteSpace(priority)) where+=" AND d.Priority = @Priority ";
        int total=0; try{ using var countCmd=new SqlCommand($"SELECT COUNT(*) FROM DailySupport d LEFT JOIN Client c ON d.ClientId=c.ClientId {where}",con); if(!string.IsNullOrWhiteSpace(search)) countCmd.Parameters.AddWithValue("@Search","%"+search+"%"); if(!string.IsNullOrWhiteSpace(fromDate)){countCmd.Parameters.AddWithValue("@FromDate",fromDate);countCmd.Parameters.AddWithValue("@ToDate",toDate);} if(!string.IsNullOrWhiteSpace(status)) countCmd.Parameters.AddWithValue("@Status",status); if(!string.IsNullOrWhiteSpace(priority)) countCmd.Parameters.AddWithValue("@Priority",priority); total=(int)countCmd.ExecuteScalar(); }catch{}
        string dataSql=$@"SELECT d.*, c.ClientName as CompanyName, u.FullName as AssignedToName, u.FullName as AssignedName FROM DailySupport d LEFT JOIN Client c ON d.ClientId=c.ClientId LEFT JOIN Users u ON d.AssignedTo=u.UserId {where} ORDER BY d.SupportId DESC OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY";
        using var dataCmd=new SqlCommand(dataSql,con); if(!string.IsNullOrWhiteSpace(search)) dataCmd.Parameters.AddWithValue("@Search","%"+search+"%"); if(!string.IsNullOrWhiteSpace(fromDate)){dataCmd.Parameters.AddWithValue("@FromDate",fromDate);dataCmd.Parameters.AddWithValue("@ToDate",toDate);} if(!string.IsNullOrWhiteSpace(status)) dataCmd.Parameters.AddWithValue("@Status",status); if(!string.IsNullOrWhiteSpace(priority)) dataCmd.Parameters.AddWithValue("@Priority",priority); dataCmd.Parameters.AddWithValue("@Skip",(page-1)*pageSize); dataCmd.Parameters.AddWithValue("@Take",pageSize);
        var rows=new List<Dictionary<string,object?>>(); try{ using var reader=dataCmd.ExecuteReader(); while(reader.Read()){ var row=new Dictionary<string,object?>(); for(int i=0;i<reader.FieldCount;i++) row[reader.GetName(i)]=reader.IsDBNull(i)?null:reader.GetValue(i); rows.Add(row);} }catch{}
        var vm=new DSSReportViewModel{Calls=rows,TotalCalls=total,FromDate=fromDate,ToDate=toDate,SearchTerm=search,FilterStatus=status,FilterPriority=priority,CurrentPage=page,PageSize=pageSize};
        if(Request.Headers["X-Requested-With"]=="XMLHttpRequest"){
            if(Request.Query.Count>0){ Response.Headers["X-Total-Calls"]=total.ToString(); Response.Headers["X-Total-Pages"]=((int)Math.Ceiling((double)total/pageSize)).ToString(); Response.Headers["X-Current-Page"]=page.ToString(); Response.Headers["X-Showing-From"]=((page-1)*pageSize+1).ToString(); Response.Headers["X-Showing-To"]=Math.Min(page*pageSize,total).ToString(); return PartialView("_DSSReportTable",vm); }
            return View(vm);
        }
        return View(vm);
    }

    [HttpGet] public IActionResult ExportExcel(string? fromDate,string? toDate,string? status,string? priority,string? search){
        using var con=_db.GetConnection(); con.Open(); string where=" WHERE 1=1 ";
        if(!string.IsNullOrWhiteSpace(search)) where+=" AND (d.TicketNo LIKE @Search OR c.ClientName LIKE @Search OR d.Mobile LIKE @Search OR d.Issue LIKE @Search OR d.CallerName LIKE @Search) ";
        if(!string.IsNullOrWhiteSpace(fromDate)&&!string.IsNullOrWhiteSpace(toDate)) where+=" AND CAST(d.SupportDate AS DATE) BETWEEN @FromDate AND @ToDate ";
        if(!string.IsNullOrWhiteSpace(status)) where+=" AND d.Status = @Status "; if(!string.IsNullOrWhiteSpace(priority)) where+=" AND d.Priority = @Priority ";
        string dataSql=$@"SELECT d.TicketNo, d.SupportDate as [DateTime], c.ClientName as Customer, d.Mobile, d.CallerName, d.Issue, d.Priority, d.Status, u.FullName as AssignedTo, d.Solution FROM DailySupport d LEFT JOIN Client c ON d.ClientId=c.ClientId LEFT JOIN Users u ON d.AssignedTo=u.UserId {where} ORDER BY d.SupportId DESC";
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
        try{ using var con=_db.GetConnection(); con.Open();
            // Pehle Reason column ke saath try; purana DB (column nahi) to bina Reason ke insert
            try{ using var cmd=new SqlCommand(@"INSERT INTO dbo.DailySupportHistory(SupportId,Action,FieldName,OldValue,NewValue,ChangedBy,ChangedByName,Reason)
                                           VALUES(@Sid,@Act,@F,@O,@N,@By,@ByName,@Reason)",con);
                cmd.Parameters.AddWithValue("@Sid",supportId);
                cmd.Parameters.AddWithValue("@Act",action);
                cmd.Parameters.AddWithValue("@F",(object?)field??DBNull.Value);
                cmd.Parameters.AddWithValue("@O",(object?)Trunc200(oldVal)??DBNull.Value);
                cmd.Parameters.AddWithValue("@N",(object?)Trunc200(newVal)??DBNull.Value);
                cmd.Parameters.AddWithValue("@By",(object?)HttpContext.Session.GetInt32("UserId")??DBNull.Value);
                cmd.Parameters.AddWithValue("@ByName",(object?)HttpContext.Session.GetString("FullName")??DBNull.Value);
                cmd.Parameters.AddWithValue("@Reason",(object?)Trunc300(reason)??DBNull.Value);
                cmd.ExecuteNonQuery();
            }catch{ using var cmd2=new SqlCommand(@"INSERT INTO dbo.DailySupportHistory(SupportId,Action,FieldName,OldValue,NewValue,ChangedBy,ChangedByName)
                                           VALUES(@Sid,@Act,@F,@O,@N,@By,@ByName)",con);
                cmd2.Parameters.AddWithValue("@Sid",supportId);
                cmd2.Parameters.AddWithValue("@Act",action);
                cmd2.Parameters.AddWithValue("@F",(object?)field??DBNull.Value);
                cmd2.Parameters.AddWithValue("@O",(object?)Trunc200(oldVal)??DBNull.Value);
                cmd2.Parameters.AddWithValue("@N",(object?)Trunc200(newVal)??DBNull.Value);
                cmd2.Parameters.AddWithValue("@By",(object?)HttpContext.Session.GetInt32("UserId")??DBNull.Value);
                cmd2.Parameters.AddWithValue("@ByName",(object?)HttpContext.Session.GetString("FullName")??DBNull.Value);
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
    [HttpGet] public IActionResult GetDetails(int id, bool single=false){
        using var con=_db.GetConnection(); con.Open(); Dictionary<string,object?>? ticket=null; Dictionary<string,object?>? customer=null; List<Dictionary<string,object?>> history=new();
        using(var cmd=new SqlCommand(@"SELECT d.*, c.ClientName as CompanyName, u.FullName as AssignedToName FROM DailySupport d LEFT JOIN Client c ON d.ClientId=c.ClientId LEFT JOIN Users u ON d.AssignedTo=u.UserId WHERE d.SupportId=@Id",con)){cmd.Parameters.AddWithValue("@Id",id); using var reader=cmd.ExecuteReader(); if(reader.Read()){ticket=new Dictionary<string,object?>(); for(int i=0;i<reader.FieldCount;i++) ticket[reader.GetName(i)]=reader.IsDBNull(i)?null:reader.GetValue(i);} reader.Close();}
        if(ticket!=null&&ticket.ContainsKey("ClientId")&&ticket["ClientId"]!=null){ try{ using var cmd2=new SqlCommand("SELECT TOP 1 * FROM Client WHERE ClientId=@Cid",con); cmd2.Parameters.AddWithValue("@Cid",ticket["ClientId"]); using var reader2=cmd2.ExecuteReader(); if(reader2.Read()){customer=new Dictionary<string,object?>(); for(int i=0;i<reader2.FieldCount;i++) customer[reader2.GetName(i)]=reader2.IsDBNull(i)?null:reader2.GetValue(i);} reader2.Close(); }catch{}
            try{
                string sql = single
                    ? "SELECT TicketNo, SupportDate, Issue, Status, Priority FROM DailySupport WHERE SupportId=@Id"
                    : "SELECT TOP 10 TicketNo, SupportDate, Issue, Status, Priority FROM DailySupport WHERE ClientId=@Cid ORDER BY SupportId DESC";
                using var cmd3=new SqlCommand(sql,con); cmd3.Parameters.AddWithValue("@Cid",ticket["ClientId"]); cmd3.Parameters.AddWithValue("@Id",id);
                using var reader3=cmd3.ExecuteReader(); while(reader3.Read()){ var r=new Dictionary<string,object?>(); for(int i=0;i<reader3.FieldCount;i++) r[reader3.GetName(i)]=reader3.IsDBNull(i)?null:reader3.GetValue(i); history.Add(r);}
            }catch{} }
        // Audit trail (sirf Index View ke liye): kaun banaya, kisne kya kab edit kiya
        List<Dictionary<string,object?>> audit=new();
        if(single){ try{
            // Pehle Reason column ke saath try karo; column na ho (purana DB) to bina Reason ke dikhao
            try{ using var cmd4=new SqlCommand("SELECT Action, FieldName, OldValue, NewValue, ChangedByName, ChangedOn, Reason FROM DailySupportHistory WHERE SupportId=@Id ORDER BY HistoryId",con); cmd4.Parameters.AddWithValue("@Id",id); using var r4=cmd4.ExecuteReader(); while(r4.Read()){ var r=new Dictionary<string,object?>(); for(int i=0;i<r4.FieldCount;i++) r[r4.GetName(i)]=r4.IsDBNull(i)?null:r4.GetValue(i); audit.Add(r);} }
            catch{ audit.Clear(); using var cmd4b=new SqlCommand("SELECT Action, FieldName, OldValue, NewValue, ChangedByName, ChangedOn FROM DailySupportHistory WHERE SupportId=@Id ORDER BY HistoryId",con); cmd4b.Parameters.AddWithValue("@Id",id); using var r4b=cmd4b.ExecuteReader(); while(r4b.Read()){ var r=new Dictionary<string,object?>(); for(int i=0;i<r4b.FieldCount;i++) r[r4b.GetName(i)]=r4b.IsDBNull(i)?null:r4b.GetValue(i); audit.Add(r);} }
        }catch{} }
        return Json(new{ticket,customer,history,audit});
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
        if(string.IsNullOrWhiteSpace(mobile)) return Json(null); using var con=_db.GetConnection(); con.Open(); using var cmd=new SqlCommand("SELECT TOP 1 * FROM Client WHERE MobileNo LIKE @M OR PhoneNo LIKE @M",con); cmd.Parameters.AddWithValue("@M","%"+mobile+"%"); using var reader=cmd.ExecuteReader(); if(!reader.Read()) return Json(null);
        var row=new Dictionary<string,object?>(); for(int i=0;i<reader.FieldCount;i++) row[reader.GetName(i)]=reader.IsDBNull(i)?null:reader.GetValue(i); return Json(new{ClientId=row.ContainsKey("ClientId")?row["ClientId"]:"",ClientName=row.ContainsKey("ClientName")?row["ClientName"]:"",MobileNo=row.ContainsKey("MobileNo")?row["MobileNo"]:""});
    }
}