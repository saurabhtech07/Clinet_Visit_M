using Microsoft.AspNetCore.Mvc;
using ClientVisitManagement.Data;
using ClientVisitManagement.Models;
using ClosedXML.Excel;

namespace ClientVisitManagement.Controllers;

/// <summary>
/// Daily Report page — daily lead activity entry (Calls / Visit / New Leads +
/// Summary / Remark / Status). 4 cards + filters + dynamic grid + Add/Edit/Delete.
/// Poora dynamic: DailyReport table me naya column add karo, grid aur form me khud aa jaayega.
/// </summary>
public class DailyReportController : Controller
{
    private readonly DynamicRepository _dynamicRepo;
    private readonly DailyReportRepository _repo;
    private readonly UserRepository _userRepo;

    public DailyReportController(DynamicRepository dynamicRepo, DailyReportRepository repo, UserRepository userRepo)
    {
        _dynamicRepo = dynamicRepo;
        _repo = repo;
        _userRepo = userRepo;
    }

    private static readonly int[] AllowedPageSizes = { 10, 20, 50, 100 };

    // Ye columns form me nahi dikhte (auto / internal).
    private static readonly string[] ExcludeFromForm = { "DailyReportId", "CreatedBy", "EntryTime" };

    // Numeric fields jo blank aaye to 0 rakhne hain (NULL ke bajaye).
    private static readonly string[] ZeroDefaults = { "Calls", "Visit", "NewLeads" };

    // =====================================================================
    //  LOOKUPS
    // =====================================================================
    private void PopulateLookups()
    {
        ViewBag.Users = _userRepo.GetAll();
        ViewBag.StatusOptions = DailyReportViewModel.StatusOptions;
    }

    // =====================================================================
    //  LIST
    // =====================================================================
    [HttpGet]
    public IActionResult Index(int page = 1, int pageSize = 10, string? search = null, string? status = null,
        int? assignedTo = null, DateTime? fromDate = null, DateTime? toDate = null,
        string? sort = null, string direction = "DESC")
    {
        if (!AllowedPageSizes.Contains(pageSize)) pageSize = 10;

        var vm = _repo.GetPage(page, pageSize, search, status, assignedTo, fromDate, toDate, sort, direction);
        vm.Users = _userRepo.GetAll();

        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
        {
            var q = Request.Query;
            bool tableRequest = q.ContainsKey("page") || q.ContainsKey("pageSize")
                || !string.IsNullOrWhiteSpace(search) || !string.IsNullOrWhiteSpace(sort)
                || !string.IsNullOrWhiteSpace(status)
                || assignedTo.HasValue || fromDate.HasValue || toDate.HasValue;

            if (tableRequest) return PartialView("_DailyReportTable", vm);
            return PartialView("Index", vm);
        }

        return View(vm);
    }

    // =====================================================================
    //  CREATE
    // =====================================================================
    [HttpGet]
    public IActionResult Create()
    {
        var fields = _dynamicRepo.GetColumns("DailyReport")
            .Where(f => !ExcludeFromForm.Contains(f.Name, StringComparer.OrdinalIgnoreCase)).ToList();

        PopulateLookups();
        ViewBag.ReportDateDefault = DateTime.Now.ToString("yyyy-MM-dd");
        return View(fields);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Create(IFormCollection form)
    {
        var exclude = ExcludeFromForm.Concat(new[] { "__RequestVerificationToken" }).ToArray();
        var values = form.Keys.Where(k => !exclude.Contains(k, StringComparer.OrdinalIgnoreCase))
            .ToDictionary(k => k, k => form[k].ToString());

        values["CreatedBy"] = (HttpContext.Session.GetInt32("UserId") ?? 0).ToString();
        if (!values.ContainsKey("ReportDate") || string.IsNullOrWhiteSpace(values["ReportDate"]))
            values["ReportDate"] = DateTime.Now.ToString("yyyy-MM-dd");
        foreach (var c in ZeroDefaults)
            if (!values.ContainsKey(c) || string.IsNullOrWhiteSpace(values[c])) values[c] = "0";

        try
        {
            int newId = _dynamicRepo.Insert("DailyReport", values);
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                return Json(new { success = true, id = newId, message = "Daily report saved successfully." });
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                return StatusCode(StatusCodes.Status400BadRequest, new { success = false, message = ex.Message });
            throw;
        }
    }

    // =====================================================================
    //  EDIT
    // =====================================================================
    [HttpGet]
    public IActionResult Edit(int id)
    {
        var data = _dynamicRepo.GetById("DailyReport", "DailyReportId", id);
        if (data == null) return NotFound();

        var fields = _dynamicRepo.GetColumns("DailyReport")
            .Where(f => !ExcludeFromForm.Contains(f.Name, StringComparer.OrdinalIgnoreCase)).ToList();

        ViewBag.RowData = data;
        ViewBag.ReportId = id;
        PopulateLookups();
        return View(fields);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Edit(int id, IFormCollection form)
    {
        var exclude = ExcludeFromForm.Concat(new[] { "__RequestVerificationToken" }).ToArray();
        var values = form.Keys.Where(k => !exclude.Contains(k, StringComparer.OrdinalIgnoreCase))
            .ToDictionary(k => k, k => form[k].ToString());

        foreach (var c in ZeroDefaults)
            if (!values.ContainsKey(c) || string.IsNullOrWhiteSpace(values[c])) values[c] = "0";

        try
        {
            _dynamicRepo.Update("DailyReport", "DailyReportId", id, values);
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                return Json(new { success = true, id = id, message = "Daily report updated successfully." });
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                return StatusCode(StatusCodes.Status400BadRequest, new { success = false, message = ex.Message });
            throw;
        }
    }

    // =====================================================================
    //  DELETE
    // =====================================================================
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Delete(int id)
    {
        try
        {
            _dynamicRepo.Delete("DailyReport", "DailyReportId", id);
            return Json(new { success = true, message = "Daily report deleted successfully." });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status400BadRequest, new { success = false, message = ex.Message });
        }
    }

    // =====================================================================
    //  QUICK ACTION: Mark Done
    // =====================================================================
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult MarkDone(int id)
    {
        try
        {
            _dynamicRepo.Update("DailyReport", "DailyReportId", id,
                new Dictionary<string, string> { ["Status"] = "Done" });
            return Json(new { success = true, message = "Daily report done ✓" });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status400BadRequest, new { success = false, message = ex.Message });
        }
    }

    // =====================================================================
    //  EXPORT
    // =====================================================================
    [HttpGet]
    public IActionResult ExportExcel(string? search = null, string? status = null,
        int? assignedTo = null, DateTime? fromDate = null, DateTime? toDate = null)
    {
        var data = _repo.GetPage(1, int.MaxValue, search, status, assignedTo, fromDate, toDate, null, "DESC").Grid;

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Daily Report");
        for (int i = 0; i < data.Columns.Count; i++) ws.Cell(1, i + 1).Value = DisplayName(data.Columns[i]);
        for (int r = 0; r < data.Rows.Count; r++)
        {
            var row = data.Rows[r];
            for (int c = 0; c < data.Columns.Count; c++)
            {
                var val = row.ContainsKey(data.Columns[c]) ? row[data.Columns[c]] : null;
                ws.Cell(r + 2, c + 1).Value = val is DateTime dt ? dt.ToString("dd-MM-yyyy hh:mm tt") : (val?.ToString() ?? "");
            }
        }
        ws.Row(1).Style.Font.Bold = true;
        ws.Columns().AdjustToContents();
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return File(stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"DailyReport_{DateTime.Now:ddMMyyyy}.xlsx");
    }

    private static string DisplayName(string col) => col switch
    {
        "DailyReportId" => "Id",
        "ReportDate" => "Date",
        "AssignedTo" => "Assigned To",
        "AssignedName" => "Assigned To",
        "NewLeads" => "New Leads",
        "EntryTime" => "Entry Time",
        "CreatedBy" => "Created By",
        _ => System.Text.RegularExpressions.Regex.Replace(col, "([a-z])([A-Z])", "$1 $2")
    };
}
