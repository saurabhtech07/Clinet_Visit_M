// using Microsoft.AspNetCore.Mvc;
// using ClientVisitManagement.Data;
// using ClientVisitManagement.Models;
// using ClosedXML.Excel;
// using System.Text;

// namespace ClientVisitManagement.Controllers;

// public class ClientController : Controller
// {
//     private readonly DynamicRepository _dynamicRepo;
//     private readonly ClientRepository _repo;

//     public ClientController(DynamicRepository dynamicRepo, ClientRepository repo)
//     {
//         _dynamicRepo = dynamicRepo;
//         _repo = repo;
//     }

//     [HttpGet]
//     public IActionResult Index(int page = 1, int pageSize = 10, string? search = null, string? sort = null, string direction = "DESC")
//     {
//         var allowedPageSizes = new[] { 10, 20, 50, 100 };
//         if (!allowedPageSizes.Contains(pageSize)) pageSize = 10;
//         var vm = new ClientDashboardViewModel
//         {
//             Clients = _dynamicRepo.GetClientTable(page, pageSize, search, sort, direction),
//             TotalClients = _repo.GetTotalClients(),
//             TotalVisits = _repo.GetTotalVisits(),
//             ThisMonthVisits = _repo.GetThisMonthVisits(),
//             ActiveClients = _repo.GetActiveClients()
//         };
//         if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
//             return PartialView("_ClientTable", vm.Clients);
//         return View(vm);
//     }

//     [HttpGet]
//     public IActionResult Create()
//     {
//         var fields = _dynamicRepo.GetColumns("Client")
//           .Where(f => f.Name!= "ClientId"
//                     && f.Name!= "CreatedOn"
//                     && f.Name!= "CompanyType" // <-- YE ADD KIYA - purana wala hatana hai
//                     && f.Name!= "NatureOfBusiness"
//                     && f.Name!= "Category"
//                     && f.Name!= "CustomerProfile").ToList();
//         ViewBag.States = _dynamicRepo.GetTable("StateMaster");
//         ViewBag.CompanyTypes = _dynamicRepo.GetTable("CompanyType");
//         return View(fields);
//     }

//     [HttpPost]
//     public IActionResult Create(IFormCollection form)
//     {
//         var values = form.Keys.Where(k => k!= "__RequestVerificationToken"
//                                        && k!= "ClientId"
//                                        && k!= "CreatedOn"
//                                        && k!= "CompanyType") // <-- YE BHI ADD
//           .ToDictionary(k => k, k => form[k].ToString());
//         int newId = _dynamicRepo.Insert("Client", values);
//         return Redirect($"/ClientVisit/Create?clientId={newId}");
//     }

//     [HttpGet]
//     public IActionResult Edit(int id)
//     {
//         var data = _dynamicRepo.GetTable("Client").Rows.FirstOrDefault(r => r["ClientId"]!= null && Convert.ToInt32(r["ClientId"]) == id);
//         if (data == null) return NotFound();
//         var fields = _dynamicRepo.GetColumns("Client")
//           .Where(f => f.Name!= "ClientId"
//                     && f.Name!= "CreatedOn"
//                     && f.Name!= "CompanyType" // <-- YE ADD KIYA
//                     && f.Name!= "NatureOfBusiness"
//                     && f.Name!= "Category"
//                     && f.Name!= "CustomerProfile").ToList();
//         ViewBag.RowData = data;
//         ViewBag.ClientId = id;
//         ViewBag.States = _dynamicRepo.GetTable("StateMaster");
//         ViewBag.CompanyTypes = _dynamicRepo.GetTable("CompanyType");
//         return View(fields);
//     }

//     [HttpPost]
//     public IActionResult Edit(int id, IFormCollection form)
//     {
//         var values = form.Keys.Where(k => k!= "__RequestVerificationToken"
//                                        && k!= "ClientId"
//                                        && k!= "CreatedOn"
//                                        && k!= "CompanyType") // <-- YE BHI ADD
//           .ToDictionary(k => k, k => form[k].ToString());
//         _dynamicRepo.Update("Client", "ClientId", id, values);
//         return RedirectToAction(nameof(Index));
//     }

//     [HttpGet]
//     public IActionResult ExportExcel()
//     {
//         var data = _dynamicRepo.GetClientTable(1, 100000, null, null, "DESC");
//         using var workbook = new XLWorkbook();
//         var worksheet = workbook.Worksheets.Add("Clients");
//         for (int i = 0; i < data.Columns.Count; i++) worksheet.Cell(1, i + 1).Value = data.Columns[i];
//         for (int r = 0; r < data.Rows.Count; r++)
//         {
//             var row = data.Rows[r];
//             for (int c = 0; c < data.Columns.Count; c++)
//             {
//                 var col = data.Columns[c];
//                 var val = row.ContainsKey(col)? row[col] : null;
//                 worksheet.Cell(r + 2, c + 1).Value = val?.ToString()?? "";
//             }
//         }
//         worksheet.Columns().AdjustToContents();
//         using var stream = new MemoryStream();
//         workbook.SaveAs(stream);
//         return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "ClientMaster.xlsx");
//     }

//     [HttpGet]
//     public IActionResult ExportCsv()
//     {
//         var data = _dynamicRepo.GetClientTable(1, 100000, null, null, "DESC");
//         var csv = new StringBuilder();
//         csv.AppendLine(string.Join(",", data.Columns.Select(EscapeCsv)));
//         foreach (var row in data.Rows)
//         {
//             var values = data.Columns.Select(column =>
//             {
//                 var value = row.ContainsKey(column)? row[column] : null;
//                 return EscapeCsv(value?.ToString()?? "");
//             });
//             csv.AppendLine(string.Join(",", values));
//         }
//         return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", "ClientMaster.csv");
//     }

//     private static string EscapeCsv(string value)
//     {
//         if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
//         {
//             return "\"" + value.Replace("\"", "\"\"") + "\"";
//         }
//         return value;
//     }
// }



using Microsoft.AspNetCore.Mvc;
using ClientVisitManagement.Data;
using ClientVisitManagement.Models;
using ClosedXML.Excel;
using System.Text;

namespace ClientVisitManagement.Controllers;

public class ClientController : Controller
{
    private readonly DynamicRepository _dynamicRepo;
    private readonly ClientRepository _repo;

    public ClientController(DynamicRepository dynamicRepo, ClientRepository repo)
    {
        _dynamicRepo = dynamicRepo;
        _repo = repo;
    }

    [HttpGet]
    public IActionResult Index(int page = 1, int pageSize = 10, string? search = null, string? sort = null, string direction = "DESC")
    {
        var allowedPageSizes = new[] { 10, 20, 50, 100 };
        if (!allowedPageSizes.Contains(pageSize)) pageSize = 10;

        var vm = new ClientDashboardViewModel
        {
            Clients = _dynamicRepo.GetClientTable(page, pageSize, search, sort, direction),
            TotalClients = _repo.GetTotalClients(),
            TotalVisits = _repo.GetTotalVisits(),
            ThisMonthVisits = _repo.GetThisMonthVisits(),
            ActiveClients = _repo.GetActiveClients()
        };
        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
        {
            var q = Request.Query;
            bool tableRequest = q.ContainsKey("page") || q.ContainsKey("pageSize")
                || !string.IsNullOrWhiteSpace(search) || !string.IsNullOrWhiteSpace(sort);
            if (tableRequest)
                return PartialView("_ClientTable", vm.Clients);
            return PartialView("Index", vm);
        }
        return View(vm);
    }

    [HttpGet]
    public IActionResult Create()
    {
        // Jo column form me nahi dikhane, yaha filter karo
        var exclude = new[] { "ClientId", "CreatedOn", "CompanyType", "NatureOfBusiness", "Category", "CustomerProfile" };
        var fields = _dynamicRepo.GetColumns("Client")
         .Where(f =>!exclude.Contains(f.Name, StringComparer.OrdinalIgnoreCase)).ToList();

        var statesTable = _dynamicRepo.GetTable("StateMaster");
        statesTable.Rows = statesTable.Rows.Where(r => r.ContainsKey("IsActive") && r["IsActive"] != null && Convert.ToBoolean(r["IsActive"])).ToList();
        ViewBag.States = statesTable;
        ViewBag.CompanyTypes = _dynamicRepo.GetTable("CompanyType");
        return View(fields);
    }

    [HttpPost]
    public IActionResult Create(IFormCollection form)
    {
        var exclude = new[] { "ClientId", "CreatedOn", "CompanyType", "__RequestVerificationToken" };
        var values = form.Keys.Where(k =>!exclude.Contains(k, StringComparer.OrdinalIgnoreCase))
         .ToDictionary(k => k, k => form[k].ToString());

        int newId = _dynamicRepo.Insert("Client", values);
        return Redirect($"/ClientVisit/Create?clientId={newId}");
    }

    [HttpGet]
    public IActionResult Edit(int id)
    {
        // FIX: Pehle pura table load kar raha tha, ab direct GetById se fast
        var data = _dynamicRepo.GetById("Client", "ClientId", id);
        if (data == null) return NotFound();

        var exclude = new[] { "ClientId", "CreatedOn", "CompanyType", "NatureOfBusiness", "Category", "CustomerProfile" };
        var fields = _dynamicRepo.GetColumns("Client")
         .Where(f =>!exclude.Contains(f.Name, StringComparer.OrdinalIgnoreCase)).ToList();

        ViewBag.RowData = data;
        ViewBag.ClientId = id;
        var statesTable = _dynamicRepo.GetTable("StateMaster");
        statesTable.Rows = statesTable.Rows.Where(r => r.ContainsKey("IsActive") && r["IsActive"] != null && Convert.ToBoolean(r["IsActive"])).ToList();
        ViewBag.States = statesTable;
        ViewBag.CompanyTypes = _dynamicRepo.GetTable("CompanyType");
        return View(fields);
    }

    [HttpPost]
    public IActionResult Edit(int id, IFormCollection form)
    {
        var exclude = new[] { "ClientId", "CreatedOn", "CompanyType", "__RequestVerificationToken" };
        var values = form.Keys.Where(k =>!exclude.Contains(k, StringComparer.OrdinalIgnoreCase))
         .ToDictionary(k => k, k => form[k].ToString());

        _dynamicRepo.Update("Client", "ClientId", id, values);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public IActionResult ExportExcel(string? search = null)
    {
        // Dynamic - GSTNo bhi Excel me khud aa jayega
        var data = _dynamicRepo.GetClientTable(1, int.MaxValue, search, null, "DESC");
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Clients");
        for (int i = 0; i < data.Columns.Count; i++) worksheet.Cell(1, i + 1).Value = data.Columns[i];
        for (int r = 0; r < data.Rows.Count; r++)
        {
            var row = data.Rows[r];
            for (int c = 0; c < data.Columns.Count; c++)
            {
                var col = data.Columns[c];
                var val = row.ContainsKey(col)? row[col] : null;
                worksheet.Cell(r + 2, c + 1).Value = val?.ToString()?? "";
            }
        }
        worksheet.Columns().AdjustToContents();
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"ClientMaster_{DateTime.Now:ddMMyyyy}.xlsx");
    }

    [HttpGet]
    public IActionResult ExportCsv(string? search = null)
    {
        var data = _dynamicRepo.GetClientTable(1, int.MaxValue, search, null, "DESC");
        var csv = new StringBuilder();
        csv.AppendLine(string.Join(",", data.Columns.Select(EscapeCsv)));
        foreach (var row in data.Rows)
        {
            var values = data.Columns.Select(column =>
            {
                var value = row.ContainsKey(column)? row[column] : null;
                return EscapeCsv(value?.ToString()?? "");
            });
            csv.AppendLine(string.Join(",", values));
        }
        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"ClientMaster_{DateTime.Now:ddMMyyyy}.csv");
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }
}