// using Microsoft.AspNetCore.Mvc;
// using Microsoft.Data.SqlClient;
// using ClientVisitManagement.Data;
// using ClientVisitManagement.Models;
// using System.Data;
// using ClosedXML.Excel;

// namespace ClientVisitManagement.Controllers;

// public class DailySupportController : Controller
// {
//     private readonly DBHelper _db;
//     private readonly DynamicRepository _dynamicRepo;

//     public DailySupportController(DBHelper db, DynamicRepository dynamicRepo)
//     {
//         _db = db;
//         _dynamicRepo = dynamicRepo;
//     }

//     // ===== MAIN INDEX =====
//     [HttpGet]
//     public IActionResult Index(string? search, string? fromDate, string? toDate, int page = 1, int pageSize = 10)
//     {
//         using var con = _db.GetConnection();
//         con.Open();
//         var vm = new DailySupportDashboardViewModel();
//         vm.SearchTerm = search; vm.FromDate = fromDate; vm.ToDate = toDate;
//         string dateFilter = "";
//         if (!string.IsNullOrWhiteSpace(fromDate) &&!string.IsNullOrWhiteSpace(toDate))
//             dateFilter = " AND CAST(SupportDate AS DATE) BETWEEN @FromDate AND @ToDate";
//         try {
//             using (var cmd = new SqlCommand($"SELECT COUNT(*) FROM DailySupport WHERE CAST(SupportDate AS DATE)=CAST(GETDATE() AS DATE) {dateFilter}", con))
//             { if (!string.IsNullOrWhiteSpace(fromDate)) { cmd.Parameters.AddWithValue("@FromDate", fromDate); cmd.Parameters.AddWithValue("@ToDate", toDate); } vm.TodayCalls = (int)cmd.ExecuteScalar(); }
//             using (var cmd = new SqlCommand($"SELECT COUNT(*) FROM DailySupport WHERE 1=1 {dateFilter}", con))
//             { if (!string.IsNullOrWhiteSpace(fromDate)) { cmd.Parameters.AddWithValue("@FromDate", fromDate); cmd.Parameters.AddWithValue("@ToDate", toDate); } vm.TotalCalls = (int)cmd.ExecuteScalar(); }
//             using (var cmd = new SqlCommand($"SELECT COUNT(*) FROM DailySupport WHERE Status IN ('Open','In Progress','On Hold','Pending') {dateFilter}", con))
//             { if (!string.IsNullOrWhiteSpace(fromDate)) { cmd.Parameters.AddWithValue("@FromDate", fromDate); cmd.Parameters.AddWithValue("@ToDate", toDate); } vm.OpenCalls = (int)cmd.ExecuteScalar(); }
//             using (var cmd = new SqlCommand($"SELECT COUNT(*) FROM DailySupport WHERE Status='Closed' AND CAST(SupportDate AS DATE)=CAST(GETDATE() AS DATE) {dateFilter}", con))
//             { if (!string.IsNullOrWhiteSpace(fromDate)) { cmd.Parameters.AddWithValue("@FromDate", fromDate); cmd.Parameters.AddWithValue("@ToDate", toDate); } vm.ClosedToday = (int)cmd.ExecuteScalar(); }
//             using (var cmd = new SqlCommand($"SELECT COUNT(*) FROM DailySupport WHERE Status IN ('In Progress','Pending','On Hold') {dateFilter}", con))
//             { if (!string.IsNullOrWhiteSpace(fromDate)) { cmd.Parameters.AddWithValue("@FromDate", fromDate); cmd.Parameters.AddWithValue("@ToDate", toDate); } vm.PendingCalls = (int)cmd.ExecuteScalar(); }
//         } catch { }
//         string where = " WHERE 1=1 ";
//         if (!string.IsNullOrWhiteSpace(search))
//             where += " AND (d.TicketNo LIKE @Search OR c.ClientName LIKE @Search OR d.Mobile LIKE @Search OR d.Issue LIKE @Search) ";
//         if (!string.IsNullOrWhiteSpace(fromDate) &&!string.IsNullOrWhiteSpace(toDate))
//             where += " AND CAST(d.SupportDate AS DATE) BETWEEN @FromDate AND @ToDate ";
//         using var countCmd = new SqlCommand($"SELECT COUNT(*) FROM DailySupport d LEFT JOIN Client c ON d.ClientId=c.ClientId {where}", con);
//         if (!string.IsNullOrWhiteSpace(search)) countCmd.Parameters.AddWithValue("@Search", "%" + search + "%");
//         if (!string.IsNullOrWhiteSpace(fromDate)) { countCmd.Parameters.AddWithValue("@FromDate", fromDate); countCmd.Parameters.AddWithValue("@ToDate", toDate); }
//         int total = 0; try { total = (int)countCmd.ExecuteScalar(); } catch { }
//         string dataSql = $@"SELECT d.*, c.ClientName, c.ClientName as CompanyName, u.FullName as AssignedName
//                             FROM DailySupport d LEFT JOIN Client c ON d.ClientId=c.ClientId LEFT JOIN Users u ON d.AssignedTo=u.UserId
//                             {where} ORDER BY d.SupportId DESC OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY";
//         using var dataCmd = new SqlCommand(dataSql, con);
//         if (!string.IsNullOrWhiteSpace(search)) dataCmd.Parameters.AddWithValue("@Search", "%" + search + "%");
//         if (!string.IsNullOrWhiteSpace(fromDate)) { dataCmd.Parameters.AddWithValue("@FromDate", fromDate); dataCmd.Parameters.AddWithValue("@ToDate", toDate); }
//         dataCmd.Parameters.AddWithValue("@Skip", (page - 1) * pageSize);
//         dataCmd.Parameters.AddWithValue("@Take", pageSize);
//         var table = new DynamicTableViewModel { CurrentPage = page, PageSize = pageSize, TotalRecords = total, SearchTerm = search };
//         try { using var reader = dataCmd.ExecuteReader(); while (reader.Read()) { var row = new Dictionary<string, object?>(); for (int i = 0; i < reader.FieldCount; i++) row[reader.GetName(i)] = reader.IsDBNull(i)? null : reader.GetValue(i); table.Rows.Add(row); } } catch { }
//         vm.TicketTable = table;
//         if (Request.Headers["X-Requested-With"] == "XMLHttpRequest") return PartialView("_CallTable", table);
//         return View(vm);
//     }

//     // ===== DSS REPORTS - NO REFRESH + HEADER FIX =====
//     [HttpGet]
//     public IActionResult DSSReports(string? search, string? fromDate, string? toDate, string? status, string? priority, int page = 1, int pageSize = 10)
//     {
//         if(pageSize!= 10 && pageSize!= 25 && pageSize!= 50 && pageSize!= 100) pageSize = 10;
//         if(page < 1) page = 1;

//         using var con = _db.GetConnection();
//         con.Open();

//         string where = " WHERE 1=1 ";
//         if (!string.IsNullOrWhiteSpace(search))
//             where += " AND (d.TicketNo LIKE @Search OR c.ClientName LIKE @Search OR d.Mobile LIKE @Search OR d.Issue LIKE @Search OR d.CallerName LIKE @Search) ";
//         if (!string.IsNullOrWhiteSpace(fromDate) &&!string.IsNullOrWhiteSpace(toDate))
//             where += " AND CAST(d.SupportDate AS DATE) BETWEEN @FromDate AND @ToDate ";
//         if (!string.IsNullOrWhiteSpace(status))
//             where += " AND d.Status = @Status ";
//         if (!string.IsNullOrWhiteSpace(priority))
//             where += " AND d.Priority = @Priority ";

//         int total = 0;
//         try
//         {
//             using var countCmd = new SqlCommand($"SELECT COUNT(*) FROM DailySupport d LEFT JOIN Client c ON d.ClientId=c.ClientId {where}", con);
//             if (!string.IsNullOrWhiteSpace(search)) countCmd.Parameters.AddWithValue("@Search", "%" + search + "%");
//             if (!string.IsNullOrWhiteSpace(fromDate)) { countCmd.Parameters.AddWithValue("@FromDate", fromDate); countCmd.Parameters.AddWithValue("@ToDate", toDate); }
//             if (!string.IsNullOrWhiteSpace(status)) countCmd.Parameters.AddWithValue("@Status", status);
//             if (!string.IsNullOrWhiteSpace(priority)) countCmd.Parameters.AddWithValue("@Priority", priority);
//             total = (int)countCmd.ExecuteScalar();
//         } catch { }

//         string dataSql = $@"SELECT d.*, c.ClientName as CompanyName, u.FullName as AssignedToName, u.FullName as AssignedName
//                             FROM DailySupport d LEFT JOIN Client c ON d.ClientId=c.ClientId LEFT JOIN Users u ON d.AssignedTo=u.UserId
//                             {where} ORDER BY d.SupportId DESC OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY";

//         using var dataCmd = new SqlCommand(dataSql, con);
//         if (!string.IsNullOrWhiteSpace(search)) dataCmd.Parameters.AddWithValue("@Search", "%" + search + "%");
//         if (!string.IsNullOrWhiteSpace(fromDate)) { dataCmd.Parameters.AddWithValue("@FromDate", fromDate); dataCmd.Parameters.AddWithValue("@ToDate", toDate); }
//         if (!string.IsNullOrWhiteSpace(status)) dataCmd.Parameters.AddWithValue("@Status", status);
//         if (!string.IsNullOrWhiteSpace(priority)) dataCmd.Parameters.AddWithValue("@Priority", priority);
//         dataCmd.Parameters.AddWithValue("@Skip", (page - 1) * pageSize);
//         dataCmd.Parameters.AddWithValue("@Take", pageSize);

//         var rows = new List<Dictionary<string, object?>>();
//         try
//         {
//             using var reader = dataCmd.ExecuteReader();
//             while (reader.Read())
//             {
//                 var row = new Dictionary<string, object?>();
//                 for (int i = 0; i < reader.FieldCount; i++) row[reader.GetName(i)] = reader.IsDBNull(i)? null : reader.GetValue(i);
//                 rows.Add(row);
//             }
//         } catch { }

//         var vm = new DSSReportViewModel
//         {
//             Calls = rows,
//             TotalCalls = total,
//             FromDate = fromDate,
//             ToDate = toDate,
//             SearchTerm = search,
//             FilterStatus = status,
//             FilterPriority = priority,
//             CurrentPage = page,
//             PageSize = pageSize
//         };

//         // AJAX - TABLE CRASH FIX KE LIYE HEADER SE DATA BHEJ RAHE HAIN
//         if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
//         {
//             Response.Headers["X-Total-Calls"] = total.ToString();
//             Response.Headers["X-Total-Pages"] = ((int)Math.Ceiling((double)total / pageSize)).ToString();
//             Response.Headers["X-Current-Page"] = page.ToString();
//             Response.Headers["X-Showing-From"] = ((page-1)*pageSize + 1).ToString();
//             Response.Headers["X-Showing-To"] = Math.Min(page*pageSize, total).ToString();
//             return PartialView("_DSSReportTable", vm);
//         }

//         return View(vm);
//     }

//     // ===== EXPORT EXCEL - YEH NAYA ADD KARNA HAI =====
//     [HttpGet]
//     public IActionResult ExportExcel(string? fromDate, string? toDate, string? status, string? priority, string? search)
//     {
//         using var con = _db.GetConnection();
//         con.Open();

//         string where = " WHERE 1=1 ";
//         if (!string.IsNullOrWhiteSpace(search))
//             where += " AND (d.TicketNo LIKE @Search OR c.ClientName LIKE @Search OR d.Mobile LIKE @Search OR d.Issue LIKE @Search OR d.CallerName LIKE @Search) ";
//         if (!string.IsNullOrWhiteSpace(fromDate) &&!string.IsNullOrWhiteSpace(toDate))
//             where += " AND CAST(d.SupportDate AS DATE) BETWEEN @FromDate AND @ToDate ";
//         if (!string.IsNullOrWhiteSpace(status))
//             where += " AND d.Status = @Status ";
//         if (!string.IsNullOrWhiteSpace(priority))
//             where += " AND d.Priority = @Priority ";

//         string dataSql = $@"SELECT d.TicketNo, d.SupportDate as [DateTime], c.ClientName as Customer, d.Mobile, d.CallerName,
//                             d.Issue, d.Priority, d.Status, u.FullName as AssignedTo, d.Solution
//                             FROM DailySupport d
//                             LEFT JOIN Client c ON d.ClientId=c.ClientId
//                             LEFT JOIN Users u ON d.AssignedTo=u.UserId
//                             {where} ORDER BY d.SupportId DESC";

//         using var cmd = new SqlCommand(dataSql, con);
//         if (!string.IsNullOrWhiteSpace(search)) cmd.Parameters.AddWithValue("@Search", "%" + search + "%");
//         if (!string.IsNullOrWhiteSpace(fromDate)) { cmd.Parameters.AddWithValue("@FromDate", fromDate); cmd.Parameters.AddWithValue("@ToDate", toDate); }
//         if (!string.IsNullOrWhiteSpace(status)) cmd.Parameters.AddWithValue("@Status", status);
//         if (!string.IsNullOrWhiteSpace(priority)) cmd.Parameters.AddWithValue("@Priority", priority);

//         var dt = new DataTable();
//         try
//         {
//             using var adapter = new SqlDataAdapter(cmd);
//             adapter.Fill(dt);
//         } catch { }

//         // ClosedXML se Excel banao
//         using var workbook = new XLWorkbook();
//         var worksheet = workbook.Worksheets.Add("DSS Reports");

//         // Header
//         worksheet.Cell(1, 1).Value = "DSS Detailed Report";
//         worksheet.Cell(1, 1).Style.Font.Bold = true;
//         worksheet.Cell(1, 1).Style.Font.FontSize = 14;

//         worksheet.Cell(2, 1).Value = $"Filters: From {fromDate?? "All"} To {toDate?? "All"} | Status: {status?? "All"} | Priority: {priority?? "All"} | Search: {search?? "All"} | Exported: {DateTime.Now:dd-MM-yyyy hh:mm tt}";
//         worksheet.Cell(2, 1).Style.Font.FontSize = 10;
//         worksheet.Cell(2, 1).Style.Font.FontColor = XLColor.Gray;

//         // Data
//         if (dt.Rows.Count > 0)
//         {
//             worksheet.Cell(4, 1).InsertTable(dt, "DSSData", true);
//             var table = worksheet.Tables.FirstOrDefault();
//             if (table!= null)
//             {
//                 table.ShowAutoFilter = true;
//                 table.Theme = XLTableTheme.TableStyleLight9;
//             }
//         }
//         else
//         {
//             worksheet.Cell(4, 1).Value = "No records found";
//         }

//         worksheet.Columns().AdjustToContents();

//         using var stream = new MemoryStream();
//         workbook.SaveAs(stream);
//         stream.Position = 0;

//         string fileName = $"DSS_Report_{DateTime.Now:ddMMyyyy_hhmmtt}.xlsx";
//         return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
//     }

//     [HttpGet]
//     public IActionResult Create()
//     {
//         ViewBag.TicketNo = $"TKT-{DateTime.Now:ddMMyy}-{new Random().Next(10,99)}";
//         try { ViewBag.Customers = _dynamicRepo.GetTable("Client").Rows; } catch { ViewBag.Customers = new List<Dictionary<string, object?>>(); }
//         try { ViewBag.Engineers = _dynamicRepo.GetTable("Users").Rows; } catch { ViewBag.Engineers = new List<Dictionary<string, object?>>(); }
//         try { ViewBag.AppMasters = _dynamicRepo.GetTable("AppMasters").Rows; } catch { ViewBag.AppMasters = new List<Dictionary<string, object?>>(); }
//         return View();
//     }

//     [HttpPost]
//     [ValidateAntiForgeryToken]
//     public IActionResult Create(IFormCollection form)
//     {
//         var values = form.Keys.Where(k => k!= "__RequestVerificationToken").ToDictionary(k => k, k => form[k].ToString());
//         if (!values.ContainsKey("TicketNo") || string.IsNullOrWhiteSpace(values["TicketNo"]))
//             values["TicketNo"] = $"TKT-{DateTime.Now:ddMMyy}-{new Random().Next(10,99)}";
//         if (!values.ContainsKey("SupportDate")) values["SupportDate"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
//         _dynamicRepo.Insert("DailySupport", values);
//         return RedirectToAction(nameof(Index));
//     }

//     [HttpGet]
//     public IActionResult Edit(int id)
//     {
//         using var con = _db.GetConnection(); con.Open();
//         using var cmd = new SqlCommand("SELECT * FROM DailySupport WHERE SupportId=@Id", con);
//         cmd.Parameters.AddWithValue("@Id", id);
//         using var reader = cmd.ExecuteReader();
//         if (!reader.Read()) return NotFound();
//         var row = new Dictionary<string, object?>(); for (int i = 0; i < reader.FieldCount; i++) row[reader.GetName(i)] = reader.IsDBNull(i)? null : reader.GetValue(i);
//         reader.Close();
//         ViewBag.RowData = row;
//         try { ViewBag.Customers = _dynamicRepo.GetTable("Client").Rows; } catch { ViewBag.Customers = new List<Dictionary<string, object?>>(); }
//         try { ViewBag.Engineers = _dynamicRepo.GetTable("Users").Rows; } catch { ViewBag.Engineers = new List<Dictionary<string, object?>>(); }
//         return View();
//     }


//     [HttpGet]
// public IActionResult GetDetails(int id)
// {
//     using var con = _db.GetConnection();
//     con.Open();

//     Dictionary<string, object?>? ticket = null;
//     Dictionary<string, object?>? customer = null;
//     List<Dictionary<string, object?>> history = new List<Dictionary<string, object?>>();

//     // Ticket details - sirf DailySupport ke apne columns + naam fields (Client ke unknown columns yahan select nahi kar rahe)
//     using (var cmd = new SqlCommand(@"SELECT d.*, c.ClientName as CompanyName, u.FullName as AssignedToName
//                                      FROM DailySupport d
//                                      LEFT JOIN Client c ON d.ClientId=c.ClientId
//                                      LEFT JOIN Users u ON d.AssignedTo=u.UserId
//                                      WHERE d.SupportId=@Id", con))
//     {
//         cmd.Parameters.AddWithValue("@Id", id);
//         using var reader = cmd.ExecuteReader();
//         if (reader.Read())
//         {
//             ticket = new Dictionary<string, object?>();
//             for (int i = 0; i < reader.FieldCount; i++) ticket[reader.GetName(i)] = reader.IsDBNull(i)? null : reader.GetValue(i);
//         }
//         reader.Close();
//     }

//     if (ticket!= null && ticket.ContainsKey("ClientId") && ticket["ClientId"]!= null)
//     {
//         // Customer details - SELECT * so jo bhi actual columns table me hain wahi aayenge
//         try
//         {
//             using var cmd2 = new SqlCommand("SELECT TOP 1 * FROM Client WHERE ClientId=@Cid", con);
//             cmd2.Parameters.AddWithValue("@Cid", ticket["ClientId"]);
//             using var reader2 = cmd2.ExecuteReader();
//             if (reader2.Read())
//             {
//                 customer = new Dictionary<string, object?>();
//                 for (int i = 0; i < reader2.FieldCount; i++) customer[reader2.GetName(i)] = reader2.IsDBNull(i)? null : reader2.GetValue(i);
//             }
//             reader2.Close();
//         } catch { }

//         // Customer ka history - last 10 tickets
//         try
//         {
//             using var cmd3 = new SqlCommand("SELECT TOP 10 TicketNo, SupportDate, Issue, Status, Priority FROM DailySupport WHERE ClientId=@Cid ORDER BY SupportId DESC", con);
//             cmd3.Parameters.AddWithValue("@Cid", ticket["ClientId"]);
//             using var reader3 = cmd3.ExecuteReader();
//             while (reader3.Read())
//             {
//                 var row = new Dictionary<string, object?>();
//                 for (int i = 0; i < reader3.FieldCount; i++) row[reader3.GetName(i)] = reader3.IsDBNull(i)? null : reader3.GetValue(i);
//                 history.Add(row);
//             }
//         } catch { }
//     }

//     return Json(new { ticket, customer, history });
// }

//     [HttpPost]
//     [ValidateAntiForgeryToken]
//     public IActionResult Edit(int id, IFormCollection form)
//     {
//         var values = form.Keys.Where(k => k!= "__RequestVerificationToken" && k!= "SupportId" && k!= "TicketNo").ToDictionary(k => k, k => form[k].ToString());
//         _dynamicRepo.Update("DailySupport", "SupportId", id, values);
//         return RedirectToAction(nameof(Index));
//     }

//     [HttpGet]
//     public IActionResult SearchClientByMobile(string mobile)
//     {
//         if (string.IsNullOrWhiteSpace(mobile)) return Json(null);
//         using var con = _db.GetConnection(); con.Open();
//         using var cmd = new SqlCommand("SELECT TOP 1 * FROM Client WHERE MobileNo LIKE @M OR PhoneNo LIKE @M", con);
//         cmd.Parameters.AddWithValue("@M", "%" + mobile + "%");
//         using var reader = cmd.ExecuteReader();
//         if (!reader.Read()) return Json(null);
//         var row = new Dictionary<string, object?>(); for (int i = 0; i < reader.FieldCount; i++) row[reader.GetName(i)] = reader.IsDBNull(i)? null : reader.GetValue(i);
//         return Json(new { ClientId = row.ContainsKey("ClientId")? row["ClientId"] : "", ClientName = row.ContainsKey("ClientName")? row["ClientName"] : "", MobileNo = row.ContainsKey("MobileNo")? row["MobileNo"] : "" });
//     }
// }




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

    [HttpGet]
    public IActionResult Index(string? search, string? fromDate, string? toDate, int page=1, int pageSize=10){
        using var con=_db.GetConnection(); con.Open();
        var vm=new DailySupportDashboardViewModel(); vm.SearchTerm=search; vm.FromDate=fromDate; vm.ToDate=toDate;
        string dateFilter=""; if(!string.IsNullOrWhiteSpace(fromDate)&&!string.IsNullOrWhiteSpace(toDate)) dateFilter=" AND CAST(SupportDate AS DATE) BETWEEN @FromDate AND @ToDate";
        try{
            using(var cmd=new SqlCommand($"SELECT COUNT(*) FROM DailySupport WHERE CAST(SupportDate AS DATE)=CAST(GETDATE() AS DATE) {dateFilter}",con)){ if(!string.IsNullOrWhiteSpace(fromDate)){cmd.Parameters.AddWithValue("@FromDate",fromDate);cmd.Parameters.AddWithValue("@ToDate",toDate);} vm.TodayCalls=(int)cmd.ExecuteScalar(); }
            using(var cmd=new SqlCommand($"SELECT COUNT(*) FROM DailySupport WHERE 1=1 {dateFilter}",con)){ if(!string.IsNullOrWhiteSpace(fromDate)){cmd.Parameters.AddWithValue("@FromDate",fromDate);cmd.Parameters.AddWithValue("@ToDate",toDate);} vm.TotalCalls=(int)cmd.ExecuteScalar(); }
            using(var cmd=new SqlCommand($"SELECT COUNT(*) FROM DailySupport WHERE Status IN ('Open','In Progress','On Hold','Pending') {dateFilter}",con)){ if(!string.IsNullOrWhiteSpace(fromDate)){cmd.Parameters.AddWithValue("@FromDate",fromDate);cmd.Parameters.AddWithValue("@ToDate",toDate);} vm.OpenCalls=(int)cmd.ExecuteScalar(); }
            using(var cmd=new SqlCommand($"SELECT COUNT(*) FROM DailySupport WHERE Status='Closed' AND CAST(SupportDate AS DATE)=CAST(GETDATE() AS DATE) {dateFilter}",con)){ if(!string.IsNullOrWhiteSpace(fromDate)){cmd.Parameters.AddWithValue("@FromDate",fromDate);cmd.Parameters.AddWithValue("@ToDate",toDate);} vm.ClosedToday=(int)cmd.ExecuteScalar(); }
            using(var cmd=new SqlCommand($"SELECT COUNT(*) FROM DailySupport WHERE Status IN ('In Progress','Pending','On Hold') {dateFilter}",con)){ if(!string.IsNullOrWhiteSpace(fromDate)){cmd.Parameters.AddWithValue("@FromDate",fromDate);cmd.Parameters.AddWithValue("@ToDate",toDate);} vm.PendingCalls=(int)cmd.ExecuteScalar(); }
        }catch{}
        string where=" WHERE 1=1 "; if(!string.IsNullOrWhiteSpace(search)) where+=" AND (d.TicketNo LIKE @Search OR c.ClientName LIKE @Search OR d.Mobile LIKE @Search OR d.Issue LIKE @Search) "; if(!string.IsNullOrWhiteSpace(fromDate)&&!string.IsNullOrWhiteSpace(toDate)) where+=" AND CAST(d.SupportDate AS DATE) BETWEEN @FromDate AND @ToDate ";
        using var countCmd=new SqlCommand($"SELECT COUNT(*) FROM DailySupport d LEFT JOIN Client c ON d.ClientId=c.ClientId {where}",con); if(!string.IsNullOrWhiteSpace(search)) countCmd.Parameters.AddWithValue("@Search","%"+search+"%"); if(!string.IsNullOrWhiteSpace(fromDate)){countCmd.Parameters.AddWithValue("@FromDate",fromDate);countCmd.Parameters.AddWithValue("@ToDate",toDate);} int total=0; try{total=(int)countCmd.ExecuteScalar();}catch{}
        string dataSql=$@"SELECT d.*, c.ClientName, c.ClientName as CompanyName, u.FullName as AssignedName FROM DailySupport d LEFT JOIN Client c ON d.ClientId=c.ClientId LEFT JOIN Users u ON d.AssignedTo=u.UserId {where} ORDER BY d.SupportId DESC OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY";
        using var dataCmd=new SqlCommand(dataSql,con); if(!string.IsNullOrWhiteSpace(search)) dataCmd.Parameters.AddWithValue("@Search","%"+search+"%"); if(!string.IsNullOrWhiteSpace(fromDate)){dataCmd.Parameters.AddWithValue("@FromDate",fromDate);dataCmd.Parameters.AddWithValue("@ToDate",toDate);} dataCmd.Parameters.AddWithValue("@Skip",(page-1)*pageSize); dataCmd.Parameters.AddWithValue("@Take",pageSize);
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

    [HttpGet] public IActionResult Create(){
        var cols=_dynamicRepo.GetColumns("DailySupport");
        ViewBag.TicketNo=$"TKT-{DateTime.Now:ddMMyy}-{new Random().Next(10,99)}";
        try{ ViewBag.Customers=_dynamicRepo.GetTable("Client").Rows; }catch{ ViewBag.Customers=new List<Dictionary<string,object?>>(); }
        try{ ViewBag.Engineers=_dynamicRepo.GetTable("Users").Rows; }catch{ ViewBag.Engineers=new List<Dictionary<string,object?>>(); }
        return View(cols);
    }
    [HttpPost][ValidateAntiForgeryToken] public IActionResult Create(IFormCollection form){
        var values=form.Keys.Where(k=>k!="__RequestVerificationToken").ToDictionary(k=>k,k=>form[k].ToString());
        if(!values.ContainsKey("TicketNo")||string.IsNullOrWhiteSpace(values["TicketNo"])) values["TicketNo"]=$"TKT-{DateTime.Now:ddMMyy}-{new Random().Next(10,99)}";
        if(!values.ContainsKey("SupportDate")) values["SupportDate"]=DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        _dynamicRepo.Insert("DailySupport",values); return RedirectToAction(nameof(Index));
    }
    [HttpGet] public IActionResult Edit(int id){
        var cols=_dynamicRepo.GetColumns("DailySupport");
        using var con=_db.GetConnection(); con.Open(); using var cmd=new SqlCommand("SELECT * FROM DailySupport WHERE SupportId=@Id",con); cmd.Parameters.AddWithValue("@Id",id); using var reader=cmd.ExecuteReader(); if(!reader.Read()) return NotFound();
        var row=new Dictionary<string,object?>(); for(int i=0;i<reader.FieldCount;i++) row[reader.GetName(i)]=reader.IsDBNull(i)?null:reader.GetValue(i); reader.Close();
        ViewBag.RowData=row; try{ ViewBag.Customers=_dynamicRepo.GetTable("Client").Rows; }catch{ ViewBag.Customers=new List<Dictionary<string,object?>>(); } try{ ViewBag.Engineers=_dynamicRepo.GetTable("Users").Rows; }catch{ ViewBag.Engineers=new List<Dictionary<string,object?>>(); }
        return View(cols);
    }
    [HttpPost][ValidateAntiForgeryToken] public IActionResult Edit(int id,IFormCollection form){
        var values=form.Keys.Where(k=>k!="__RequestVerificationToken"&&k!="SupportId"&&k!="TicketNo").ToDictionary(k=>k,k=>form[k].ToString());
        _dynamicRepo.Update("DailySupport","SupportId",id,values); return RedirectToAction(nameof(Index));
    }
    [HttpGet] public IActionResult GetDetails(int id){
        using var con=_db.GetConnection(); con.Open(); Dictionary<string,object?>? ticket=null; Dictionary<string,object?>? customer=null; List<Dictionary<string,object?>> history=new();
        using(var cmd=new SqlCommand(@"SELECT d.*, c.ClientName as CompanyName, u.FullName as AssignedToName FROM DailySupport d LEFT JOIN Client c ON d.ClientId=c.ClientId LEFT JOIN Users u ON d.AssignedTo=u.UserId WHERE d.SupportId=@Id",con)){cmd.Parameters.AddWithValue("@Id",id); using var reader=cmd.ExecuteReader(); if(reader.Read()){ticket=new Dictionary<string,object?>(); for(int i=0;i<reader.FieldCount;i++) ticket[reader.GetName(i)]=reader.IsDBNull(i)?null:reader.GetValue(i);} reader.Close();}
        if(ticket!=null&&ticket.ContainsKey("ClientId")&&ticket["ClientId"]!=null){ try{ using var cmd2=new SqlCommand("SELECT TOP 1 * FROM Client WHERE ClientId=@Cid",con); cmd2.Parameters.AddWithValue("@Cid",ticket["ClientId"]); using var reader2=cmd2.ExecuteReader(); if(reader2.Read()){customer=new Dictionary<string,object?>(); for(int i=0;i<reader2.FieldCount;i++) customer[reader2.GetName(i)]=reader2.IsDBNull(i)?null:reader2.GetValue(i);} reader2.Close(); }catch{} try{ using var cmd3=new SqlCommand("SELECT TOP 10 TicketNo, SupportDate, Issue, Status, Priority FROM DailySupport WHERE ClientId=@Cid ORDER BY SupportId DESC",con); cmd3.Parameters.AddWithValue("@Cid",ticket["ClientId"]); using var reader3=cmd3.ExecuteReader(); while(reader3.Read()){ var r=new Dictionary<string,object?>(); for(int i=0;i<reader3.FieldCount;i++) r[reader3.GetName(i)]=reader3.IsDBNull(i)?null:reader3.GetValue(i); history.Add(r);} }catch{} }
        return Json(new{ticket,customer,history});
    }
    [HttpGet] public IActionResult SearchClientByMobile(string mobile){
        if(string.IsNullOrWhiteSpace(mobile)) return Json(null); using var con=_db.GetConnection(); con.Open(); using var cmd=new SqlCommand("SELECT TOP 1 * FROM Client WHERE MobileNo LIKE @M OR PhoneNo LIKE @M",con); cmd.Parameters.AddWithValue("@M","%"+mobile+"%"); using var reader=cmd.ExecuteReader(); if(!reader.Read()) return Json(null);
        var row=new Dictionary<string,object?>(); for(int i=0;i<reader.FieldCount;i++) row[reader.GetName(i)]=reader.IsDBNull(i)?null:reader.GetValue(i); return Json(new{ClientId=row.ContainsKey("ClientId")?row["ClientId"]:"",ClientName=row.ContainsKey("ClientName")?row["ClientName"]:"",MobileNo=row.ContainsKey("MobileNo")?row["MobileNo"]:""});
    }
}