// using Microsoft.AspNetCore.Mvc;
// using ClosedXML.Excel;
// using ClientVisitManagement.Data;
// using ClientVisitManagement.Models;

// namespace ClientVisitManagement.Controllers;

// public class ClientVisitController : Controller
// {
//     private readonly ClientVisitRepository _repo;
//     private readonly ClientRepository _clientRepo;
//     private readonly DynamicRepository _dynamicRepo;

//     public ClientVisitController(
//         ClientVisitRepository repo,
//         ClientRepository clientRepo,
//         DynamicRepository dynamicRepo)
//     {
//         _repo = repo;
//         _clientRepo = clientRepo;
//         _dynamicRepo = dynamicRepo;
//     }

//     // SELECT CLIENT PAGE - Ab direct Create par redirect karega
//     [HttpGet]
//     public IActionResult SelectClient()
//     {
//         return RedirectToAction(nameof(Create));
//     }

//     // =========================================================
//     // CREATE - GET - AB YAHAN DROPDOWN AYEGA
//     // =========================================================
//     [HttpGet]
//     public IActionResult Create(int clientId = 0)
//     {
//         ViewBag.Clients = _clientRepo.GetAll();
//         Client? client = null;
//         if (clientId > 0)
//         {
//             client = _clientRepo.GetById(clientId);
//         }
//         ViewBag.Client = client;
//         ViewBag.SelectedClientId = clientId;
//         var fields = _dynamicRepo.GetColumns("ClientVisit")
//           .Where(f => f.Name!= "VisitId" && f.Name!= "ClientId" && f.Name!= "UserId").ToList();
//         var serverDateTime = _dynamicRepo.GetServerDateTime();
//         ViewBag.CurrentDate = serverDateTime.CurrentDate;
//         ViewBag.CurrentTime = serverDateTime.CurrentTime;
//         return View(fields);
//     }

//     // CREATE - POST
//     [HttpPost]
//     [ValidateAntiForgeryToken]
//     public IActionResult Create(IFormCollection form)
//     {
//         try
//         {
//             var userId = HttpContext.Session.GetInt32("UserId");
//             if (userId == null) return RedirectToAction("Login", "Account");

//             if (!form.ContainsKey("ClientId") ||!int.TryParse(form["ClientId"], out int clientId) || clientId == 0)
//             {
//                 TempData["Error"] = "Please select a Client first!";
//                 return RedirectToAction(nameof(Create));
//             }

//             var v = new ClientVisit
//             {
//                 ClientId = clientId,
//                 UserId = userId.Value,
//                 VisitDate = DateTime.TryParse(form["VisitDate"], out var vd)? vd : DateTime.Now,
//                 VisitTime = form["VisitTime"].ToString(),
//                 PersonMet = form["PersonMet"].ToString(),
//                 PersonName = form["PersonName"].ToString(),
//                 Designation = form["Designation"].ToString(),
//                 ContactNo = form["ContactNo"].ToString(),
//                 Email = form["Email"].ToString(),
//                 DiscussionRequirement = form["DiscussionRequirement"].ToString(),
//                 NextFollowUpDate = DateTime.TryParse(form["NextFollowUpDate"], out var nfd)? nfd : (DateTime?)null,
//                 FollowUpStatus = string.IsNullOrWhiteSpace(form["FollowUpStatus"])? "Pending" : form["FollowUpStatus"].ToString(),
//                 Remarks = form["Remarks"].ToString(),
//                 CreatedOn = DateTime.Now
//             };
//             _repo.Add(v);
//             return RedirectToAction(nameof(Index));
//         }
//         catch (Exception ex)
//         {
//             TempData["Error"] = "Save Failed: " + ex.Message;
//             return RedirectToAction(nameof(Create), new { clientId = form["ClientId"] });
//         }
//     }

//     // =========================================================
//     // ANALYTICS & REPORTS - Full Report - FINAL FIXED
//     // =========================================================
//     [HttpGet]
//     public IActionResult ClientVisitReport(string? term, DateTime? fromDate, DateTime? toDate, string? sort, string? direction = "DESC", int page = 1, int pageSize = 20)
//     {
//         var allowedPageSizes = new[] { 10, 20, 50, 100 };
//         if (!allowedPageSizes.Contains(pageSize)) pageSize = 20;
//         page = Math.Max(1, page);

//         var (items, total) = _repo.Search(term, fromDate, toDate, page, pageSize, sort, direction);

//         int totalPages = (int)Math.Ceiling(total / (double)pageSize);
//         if (page > totalPages && totalPages > 0)
//         {
//             page = totalPages;
//             (items, total) = _repo.Search(term, fromDate, toDate, page, pageSize, sort, direction);
//         }

//         var vm = new VisitListViewModel
//         {
//             Visits = items,
//             SearchTerm = term,
//             FromDate = fromDate,
//             ToDate = toDate,
//             Page = page,
//             PageSize = pageSize,
//             TotalCount = total
//         };

//         ViewBag.Sort = sort;
//         ViewBag.Direction = direction;
//         ViewBag.TotalPages = totalPages;
//         ViewBag.TotalClientsVisited = total;

//         if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
//             return PartialView("_VisitReportTable", vm);

//         return View(vm);
//     }

//     // =========================================================
//     // INDEX - Transaction Modules - Short Table
//     // =========================================================
//     [HttpGet]
//     public IActionResult Index(string? term, DateTime? fromDate, DateTime? toDate, string? sort, string direction = "DESC", int page = 1, int pageSize = 10)
//     {
//         var allowedPageSizes = new[] { 10, 20, 50, 100 };
//         if (!allowedPageSizes.Contains(pageSize)) pageSize = 10;
//         page = Math.Max(1, page);
//         var (items, total) = _repo.Search(term, fromDate, toDate, page, pageSize, sort, direction);
//         int totalPages = (int)Math.Ceiling(total / (double)pageSize);
//         if (page > totalPages && totalPages > 0) { page = totalPages; (items, total) = _repo.Search(term, fromDate, toDate, page, pageSize, sort, direction); }
//         ViewBag.TotalClientsVisited = _repo.GetTotalClientsVisited();
//         ViewBag.VisitsThisMonth = _repo.GetVisitsThisMonth();
//         ViewBag.TodaysVisits = _repo.GetTodaysVisits();
//         ViewBag.PendingFollowUps = _repo.GetPendingFollowUps();
//         var vm = new VisitListViewModel { Visits = items, SearchTerm = term, FromDate = fromDate, ToDate = toDate, Page = page, PageSize = pageSize, TotalCount = total };
//         ViewBag.Sort = sort; ViewBag.Direction = direction; ViewBag.TotalPages = totalPages;
//         if (Request.Headers["X-Requested-With"] == "XMLHttpRequest") return PartialView("_VisitTable", vm);
//         return View(vm);
//     }

//     [HttpGet] public IActionResult Details(int id) { var v = _repo.GetById(id); return v == null? NotFound() : PartialView("_DetailsModal", v); }

//     [HttpGet]
//     public IActionResult Edit(int id)
//     {
//         var v = _dynamicRepo.GetTable("ClientVisit").Rows.FirstOrDefault(r => r["VisitId"]!= null && Convert.ToInt32(r["VisitId"]) == id);
//         if (v == null) return NotFound();
//         if (!v.ContainsKey("ClientId") || v["ClientId"] == null) return NotFound();
//         int clientId = Convert.ToInt32(v["ClientId"]);
//         ViewBag.Client = _clientRepo.GetById(clientId);
//         ViewBag.RowData = v;
//         var fields = _dynamicRepo.GetColumns("ClientVisit").Where(f => f.Name!= "VisitId" && f.Name!= "ClientId" && f.Name!= "UserId").ToList();
//         return View(fields);
//     }

//     [HttpPost][ValidateAntiForgeryToken]
//     public IActionResult Edit(int id, IFormCollection form)
//     {
//         var values = form.Keys.Where(k => k!= "__RequestVerificationToken" && k!= "VisitId" && k!= "ClientId" && k!= "UserId").ToDictionary(k => k, k => form[k].ToString());
//         _dynamicRepo.Update("ClientVisit", "VisitId", id, values);
//         return RedirectToAction(nameof(Index));
//     }

//     [HttpPost][ValidateAntiForgeryToken] public IActionResult Delete(int id) { _repo.Delete(id); return RedirectToAction(nameof(Index)); }
//     [HttpGet] public IActionResult Transaction() { var transactions = _repo.GetTransactions(); return View(transactions); }

//     [HttpGet]
//     public IActionResult ExportToExcel(string? term, DateTime? fromDate, DateTime? toDate)
//     {
//         var list = _repo.SearchAll(term, fromDate, toDate);
//         using var wb = new XLWorkbook();
//         var ws = wb.Worksheets.Add("Client Visits");
//         string[] headers = { "Client ID", "Company Name", "City", "State", "Visited By", "Pincode", "Person Met", "Person Name", "Visit Date" };
//         for (int i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];
//         int row = 2; foreach (var v in list) { ws.Cell(row, 1).Value = v.ClientCode; ws.Cell(row, 2).Value = v.ClientName; ws.Cell(row, 3).Value = v.City; ws.Cell(row, 4).Value = v.State; ws.Cell(row, 5).Value = v.VisitedBy; ws.Cell(row, 6).Value = v.Pincode; ws.Cell(row, 7).Value = v.PersonMet; ws.Cell(row, 8).Value = v.PersonName; ws.Cell(row, 9).Value = v.VisitDate.ToString("dd-MM-yyyy"); row++; }
//         ws.Columns().AdjustToContents(); using var stream = new MemoryStream(); wb.SaveAs(stream);
//         return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "ClientVisitList.xlsx");
//     }
// }


using ClientVisitManagement.Data;
using Microsoft.AspNetCore.Mvc;
using ClosedXML.Excel;
using System.Text;

namespace ClientVisitManagement.Controllers;

public class ClientVisitController : Controller
{
    private readonly DynamicRepository _dynamicRepo;
    private readonly ClientRepository _clientRepo;

    public ClientVisitController(DynamicRepository dynamicRepo, ClientRepository clientRepo)
    {
        _dynamicRepo = dynamicRepo;
        _clientRepo = clientRepo;
    }

    // INDEX - 100% DYNAMIC JAISA USER HAI
    [HttpGet]
    public IActionResult Index(int page = 1, int pageSize = 10, string? term = null, string? sort = null, string direction = "DESC", DateTime? fromDate = null, DateTime? toDate = null)
    {
        var allowed = new[] {10,20,50,100};
        if(!allowed.Contains(pageSize)) pageSize = 10;

        var data = _dynamicRepo.GetClientVisitTable(page, pageSize, term, sort, direction);

        if(fromDate.HasValue || toDate.HasValue)
        {
            data.Rows = data.Rows.Where(r => {
                if(!r.ContainsKey("VisitDate") || r["VisitDate"]==null) return true;
                DateTime.TryParse(r["VisitDate"].ToString(), out var d);
                if(fromDate.HasValue && d.Date < fromDate.Value.Date) return false;
                if(toDate.HasValue && d.Date > toDate.Value.Date) return false;
                return true;
            }).ToList();
        }

        ViewBag.TotalClientsVisited = data.TotalRecords;
        ViewBag.VisitsThisMonth = data.Rows.Count(r => r.ContainsKey("VisitDate") && DateTime.TryParse(r["VisitDate"]?.ToString(), out var d) && d.Month==DateTime.Now.Month);
        ViewBag.TodaysVisits = data.Rows.Count(r => r.ContainsKey("VisitDate") && DateTime.TryParse(r["VisitDate"]?.ToString(), out var d) && d.Date==DateTime.Now.Date);
        ViewBag.PendingFollowUps = data.Rows.Count(r => r.ContainsKey("FollowUpStatus") && r["FollowUpStatus"]?.ToString()=="Pending");

        data.SearchTerm = term;
        data.SortColumn = sort;
        data.SortDirection = direction;
        ViewBag.FromDate = fromDate?.ToString("yyyy-MM-dd");
        ViewBag.ToDate = toDate?.ToString("yyyy-MM-dd");

        if(Request.Headers["X-Requested-With"]=="XMLHttpRequest")
        {
            if(Request.Query.Count > 0) return PartialView("_VisitTable", data);
            return View(data);
        }

        return View(data);
    }

    // REPORT - BHI DYNAMIC
[HttpGet]
public IActionResult ClientVisitReport(int page = 1, int pageSize = 10, string? term = null, string? sort = null, string direction = "DESC", DateTime? fromDate = null, DateTime? toDate = null)
{
    var allowed = new[] {10,20,50,100};
    if(!allowed.Contains(pageSize)) pageSize = 10; // 10 se khulega

    var data = _dynamicRepo.GetClientVisitTable(page, pageSize, term, sort, direction);

    if(fromDate.HasValue || toDate.HasValue)
    {
        data.Rows = data.Rows.Where(r => {
            if(!r.ContainsKey("VisitDate") || r["VisitDate"]==null) return true;
            if(!DateTime.TryParse(r["VisitDate"].ToString(), out var d)) return true;
            if(fromDate.HasValue && d.Date < fromDate.Value.Date) return false;
            if(toDate.HasValue && d.Date > toDate.Value.Date) return false;
            return true;
        }).ToList();
    }

    data.SearchTerm = term;
    data.SortColumn = sort;
    data.SortDirection = direction;
    ViewBag.FromDate = fromDate?.ToString("yyyy-MM-dd");
    ViewBag.ToDate = toDate?.ToString("yyyy-MM-dd");

    if(Request.Headers["X-Requested-With"]=="XMLHttpRequest")
    {
        if(Request.Query.Count > 0) return PartialView("_VisitReportTable", data);
        return View(data);
    }

    return View(data);
}

    [HttpGet]
    public IActionResult Create(int? clientId)
    {
        var clients = _clientRepo.GetAll();
        ViewBag.Clients = clients;
        ViewBag.SelectedClientId = clientId?? 0;
        if(clientId.HasValue) ViewBag.Client = clients.FirstOrDefault(c => c.ClientId==clientId);
        var cols = _dynamicRepo.GetColumns("ClientVisit");
        var dt = _dynamicRepo.GetServerDateTime();
        ViewBag.CurrentDate = dt.CurrentDate;
        ViewBag.CurrentTime = dt.CurrentTime;
        return View(cols);
    }

    [HttpPost][ValidateAntiForgeryToken]
    public IActionResult Create(Dictionary<string,string> values)
    {
        values.Remove("__RequestVerificationToken");
        _dynamicRepo.Insert("ClientVisit", values);
        return RedirectToAction(nameof(Index));
    }

    // ================== YAHAN SE NAYA ADD KIYA HAI ==================

    // VIEW POPUP - DATABASE JAISA VAISA DIKHEGA
    [HttpGet]
    public IActionResult Details(int id)
    {
        var row = _dynamicRepo.GetById("ClientVisit", "VisitId", id);
        if(row == null) return NotFound();
        return PartialView("_VisitDetails", row);
    }

    // EDIT GET
    [HttpGet]
    public IActionResult Edit(int id)
    {
        var row = _dynamicRepo.GetById("ClientVisit", "VisitId", id);
        if(row == null) return NotFound();
        ViewBag.RowData = row;
        ViewBag.VisitId = id;
        if(row.ContainsKey("ClientId"))
            ViewBag.Client = _clientRepo.GetById(Convert.ToInt32(row["ClientId"]));
        ViewBag.Clients = _clientRepo.GetAll();
        var cols = _dynamicRepo.GetColumns("ClientVisit").Where(c=>c.Name!="VisitId" && c.Name!="UserId").ToList();
        return View(cols);
    }

    // EDIT POST
    [HttpPost][ValidateAntiForgeryToken]
    public IActionResult Edit(int id, Dictionary<string,string> values)
    {
        values.Remove("__RequestVerificationToken");
        values.Remove("VisitId");
        values.Remove("UserId");
        _dynamicRepo.Update("ClientVisit", "VisitId", id, values);
        return RedirectToAction(nameof(Index));
    }

    // DELETE - AJAX + NORMAL DONO KE LIYE
    [HttpPost][ValidateAntiForgeryToken]
    public IActionResult Delete(int id)
    {
        _dynamicRepo.Delete("ClientVisit", "VisitId", id);
        if(Request.Headers["X-Requested-With"]=="XMLHttpRequest")
            return Json(new { success = true });
        return RedirectToAction(nameof(Index));
    }

    // EXPORT EXCEL - DATABASE JAISA VAISA
    [HttpGet]
    public IActionResult ExportExcel(string? search)
    {
        var data = _dynamicRepo.GetClientVisitTable(1, 10000, search, null, "DESC");
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("ClientVisit");
        int colIdx = 1;
        foreach(var col in data.Columns){ ws.Cell(1,colIdx).Value = col; colIdx++; }
        int rowIdx = 2;
        foreach(var r in data.Rows){
            colIdx = 1;
            foreach(var col in data.Columns){
                ws.Cell(rowIdx,colIdx).Value = r.ContainsKey(col)? r[col]?.ToString() : "";
                colIdx++;
            }
            rowIdx++;
        }
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "ClientVisitList.xlsx");
    }

    [HttpGet]
    public IActionResult ExportCsv(string? search)
    {
        var data = _dynamicRepo.GetClientVisitTable(1, 10000, search, null, "DESC");
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", data.Columns));
        foreach(var r in data.Rows){
            var line = string.Join(",", data.Columns.Select(c => $"\"{(r.ContainsKey(c)? r[c]?.ToString()?.Replace("\"","\"\"") : "")}\""));
            sb.AppendLine(line);
        }
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", "ClientVisitList.csv");
    }
}