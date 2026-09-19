using ClientVisitManagement.Data;
using Microsoft.AspNetCore.Mvc;
using ClosedXML.Excel;
using System.Text;

namespace ClientVisitManagement.Controllers;

public class UserController : Controller
{
    private readonly DynamicRepository _repo;
    public UserController(DynamicRepository repo) { _repo = repo; }

    [HttpGet]
    public IActionResult Index(int page = 1, int pageSize = 10, string? search = null, string? sort = null, string direction = "DESC")
    {
        var allowedPageSizes = new[] { 10, 20, 50, 100 };
        if (!allowedPageSizes.Contains(pageSize)) pageSize = 10;
        var data = _repo.GetTable("Users", page, pageSize, search, sort, direction);
        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
        {
            var q = Request.Query;
            bool tableRequest = q.ContainsKey("page") || q.ContainsKey("pageSize")
                || !string.IsNullOrWhiteSpace(search) || !string.IsNullOrWhiteSpace(sort);
            if (tableRequest)
                return PartialView("_UserTable", data);
            return PartialView("Index", data);
        }
        return View(data);
    }

    //... Tera Create / Edit / Delete wala code same rahega...

    // =========================================================
    // EXPORT EXCEL - FIXED (search + all data)
    // =========================================================
    [HttpGet]
    public IActionResult ExportExcel(string? search)
    {
        // Ab search bhi ayega aur 100000 ki jagah int.MaxValue se pura data
        var data = _repo.GetTable("Users", 1, int.MaxValue, search, null, "DESC");

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Users");

        // HEADER
        for (int i = 0; i < data.Columns.Count; i++)
            worksheet.Cell(1, i + 1).Value = data.Columns[i];

        // DATA
        for (int r = 0; r < data.Rows.Count; r++)
        {
            for (int c = 0; c < data.Columns.Count; c++)
            {
                var col = data.Columns[c];
                var val = data.Rows[r].ContainsKey(col)? data.Rows[r][col] : null;
                worksheet.Cell(r + 2, c + 1).Value = val?.ToString()?? "";
            }
        }
        worksheet.Columns().AdjustToContents();
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0; // <-- ye line add ki
        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"UserMaster_{DateTime.Now:ddMMyyyy}.xlsx");
    }

    // =========================================================
    // EXPORT CSV - FIXED
    // =========================================================
    [HttpGet]
    public IActionResult ExportCsv(string? search)
    {
        var data = _repo.GetTable("Users", 1, int.MaxValue, search, null, "DESC");
        var csv = new StringBuilder();
        csv.AppendLine(string.Join(",", data.Columns.Select(EscapeCsv)));
        foreach (var row in data.Rows)
        {
            var values = data.Columns.Select(col => EscapeCsv(row.ContainsKey(col)? row[col]?.ToString()?? "" : ""));
            csv.AppendLine(string.Join(",", values));
        }
        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"UserMaster_{DateTime.Now:ddMMyyyy}.csv");
    }

    // =========================================================
    // PRINT ALL - NAYA ACTION (pura data print hoga)
    // =========================================================
    [HttpGet]
    public IActionResult PrintAll(string? search)
    {
        var data = _repo.GetTable("Users", 1, int.MaxValue, search, null, "DESC");
        return View(data); // Iske liye Views/User/PrintAll.cshtml banana padega
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }

    //... Create / Edit / Delete same as yours...
    [HttpGet] public IActionResult Create(){ ViewBag.Roles = _repo.GetRoles(); return View(_repo.GetColumns("Users")); }
    [HttpPost][ValidateAntiForgeryToken] public IActionResult Create(Dictionary<string, string> values){ values.Remove("__RequestVerificationToken"); if (values.TryGetValue("PasswordHash", out var plainPwd) &&!string.IsNullOrWhiteSpace(plainPwd)) values["PasswordHash"] = BCrypt.Net.BCrypt.HashPassword(plainPwd); values.Remove("RoleName"); _repo.Insert("Users", values); return RedirectToAction(nameof(Index)); }
    [HttpGet] public IActionResult Edit(int id){ var fields = _repo.GetColumns("Users"); var row = _repo.GetById("Users","UserId",id); if(row==null) return NotFound(); ViewBag.UserId=id; ViewBag.RowData=row; ViewBag.Roles=_repo.GetRoles(); return View(fields); }
    [HttpPost][ValidateAntiForgeryToken] public IActionResult Edit(int id, Dictionary<string, string> values){ values.Remove("__RequestVerificationToken"); values.Remove("UserId"); values.Remove("CreatedDate"); values.Remove("RoleName"); if (!values.TryGetValue("PasswordHash", out var password) || string.IsNullOrWhiteSpace(password)) values.Remove("PasswordHash"); else values["PasswordHash"] = BCrypt.Net.BCrypt.HashPassword(password); _repo.Update("Users","UserId",id,values); return RedirectToAction(nameof(Index)); }
    [HttpPost][ValidateAntiForgeryToken] public IActionResult Delete(int id){ var user = _repo.GetById("Users","UserId",id); if(user==null) return NotFound(); _repo.Delete("Users","UserId",id); return RedirectToAction(nameof(Index)); }
}