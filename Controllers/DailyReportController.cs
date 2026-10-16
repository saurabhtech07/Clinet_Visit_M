using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Globalization;
using ClientVisitManagement.Data;
using ClientVisitManagement.Models;
using ClosedXML.Excel;

namespace ClientVisitManagement.Controllers;

/// <summary>
/// Daily Report page — daily lead activity entry.
/// Manual: Summary + Remark. Auto (LIVE): Calls (DailySupport), Visit (ClientVisit), New Leads (Leads).
/// 4 cards + filters + dynamic grid + Add/Edit/Delete.
/// Koi status / approval workflow nahi — report ek plain record hai.
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

    // Ye columns form me nahi dikhte (auto / internal / audit).
    // Status module se hata diya gaya hai. Calls / Visit / NewLeads bhi manual nahi —
    // ye DailySupport / ClientVisit / Leads se LIVE count hote hain.
    private static readonly string[] ExcludeFromForm = { "DailyReportId", "CreatedBy", "EntryTime", "ModifiedBy", "ModifiedOn", "Status", "Calls", "Visit", "NewLeads" };

    private const int MaxNoteLength = 1000;

    // =====================================================================
    //  LOOKUPS
    // =====================================================================
    private void PopulateLookups()
    {
        ViewBag.Users = _userRepo.GetAll();
    }

    // =====================================================================
    //  LIST
    // =====================================================================
    [HttpGet]
    public IActionResult Index(int page = 1, int pageSize = 10, string? search = null,
        int? assignedTo = null, DateTime? fromDate = null, DateTime? toDate = null,
        string? sort = null, string direction = "DESC")
    {
        if (!AllowedPageSizes.Contains(pageSize)) pageSize = 10;

        var vm = _repo.GetPage(page, pageSize, search, assignedTo, fromDate, toDate, sort, direction);
        vm.Users = _userRepo.GetAll();

        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
        {
            var q = Request.Query;
            bool tableRequest = q.ContainsKey("page") || q.ContainsKey("pageSize")
                || !string.IsNullOrWhiteSpace(search) || !string.IsNullOrWhiteSpace(sort)
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
        var values = CollectValues(form, isEdit: false);

        values["CreatedBy"] = (HttpContext.Session.GetInt32("UserId") ?? 0).ToString();
        if (!values.ContainsKey("ReportDate") || string.IsNullOrWhiteSpace(values["ReportDate"]))
            values["ReportDate"] = DateTime.Now.ToString("yyyy-MM-dd");

        var error = ValidateReport(values);
        if (error != null) return ValidationError(error, nameof(Create));

        try
        {
            var duplicate = _repo.FindDuplicateError(
                DateTime.Parse(values["ReportDate"], CultureInfo.InvariantCulture),
                int.Parse(values["AssignedTo"], CultureInfo.InvariantCulture), null);
            if (duplicate != null) return ValidationError(duplicate, nameof(Create));

            int newId = _dynamicRepo.Insert("DailyReport", values);
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                return Json(new { success = true, id = newId, message = "Daily report saved successfully." });
            return RedirectToAction(nameof(Index));
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            // DB unique index (ek employee = ek din = ek report)
            return ValidationError("This employee already has a report for this date — edit that report instead of creating a duplicate.", nameof(Create));
        }
        catch (Exception ex)
        {
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                return StatusCode(StatusCodes.Status400BadRequest, new { success = false, message = ex.Message });
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Create));
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
        var values = CollectValues(form, isEdit: true);

        if (!values.ContainsKey("ReportDate") || string.IsNullOrWhiteSpace(values["ReportDate"]))
            values["ReportDate"] = DateTime.Now.ToString("yyyy-MM-dd");

        var error = ValidateReport(values);
        if (error != null) return ValidationError(error, nameof(Edit), new { id });

        try
        {
            var duplicate = _repo.FindDuplicateError(
                DateTime.Parse(values["ReportDate"], CultureInfo.InvariantCulture),
                int.Parse(values["AssignedTo"], CultureInfo.InvariantCulture), id);
            if (duplicate != null) return ValidationError(duplicate, nameof(Edit), new { id });

            _dynamicRepo.Update("DailyReport", "DailyReportId", id, values);
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                return Json(new { success = true, id = id, message = "Daily report updated successfully." });
            return RedirectToAction(nameof(Index));
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            return ValidationError("This employee already has a report for this date — edit that report instead of creating a duplicate.", nameof(Edit), new { id });
        }
        catch (Exception ex)
        {
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                return StatusCode(StatusCodes.Status400BadRequest, new { success = false, message = ex.Message });
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Edit), new { id });
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
    //  VIEW (detail popup)
    // =====================================================================
    [HttpGet]
    public IActionResult Details(int id)
    {
        var data = _repo.GetDetail(id);
        if (data == null) return NotFound();

        // Internal / duplicate columns popup me nahi dikhate
        foreach (var internalCol in new[] { "AssignedTo", "CreatedBy", "ModifiedBy", "Status" })
            data.Remove(internalCol);

        return PartialView("_DailyReportDetails", data);
    }

    /// <summary>User-wise summary grid ka View popup — ek user ka us din ka kaam.</summary>
    [HttpGet]
    public IActionResult UserDetails(int userId, DateTime? date)
    {
        if (userId <= 0) return NotFound();
        var day = (date ?? DateTime.Today).Date;
        var vm = _repo.GetUserDayDetail(userId, day);
        return PartialView("_DailyReportUserDetails", vm);
    }

    // =====================================================================
    //  VALIDATION HELPERS (company standard)
    // =====================================================================
    /// <summary>Form se sirf asli DB columns leta hai (crafted/fake keys reject) + edit par audit stamp lagata hai.</summary>
    private Dictionary<string, string> CollectValues(IFormCollection form, bool isEdit)
    {
        var exclude = new HashSet<string>(ExcludeFromForm, StringComparer.OrdinalIgnoreCase) { "__RequestVerificationToken" };
        var validCols = _dynamicRepo.GetColumns("DailyReport")
            .Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var values = form.Keys
            .Where(k => !exclude.Contains(k) && validCols.Contains(k))
            .ToDictionary(k => k, k => form[k].ToString());

        if (isEdit)
        {
            var userId = (HttpContext.Session.GetInt32("UserId") ?? 0).ToString();
            if (validCols.Contains("ModifiedBy")) values["ModifiedBy"] = userId;
            if (validCols.Contains("ModifiedOn")) values["ModifiedOn"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }
        return values;
    }

    /// <summary>Company-standard validation — pehli problem wali field ka message return karta hai (null = OK).</summary>
    private static string? ValidateReport(Dictionary<string, string> values)
    {
        // 1) Assigned To zaroori — report kis employee ke naam hai
        if (!int.TryParse(values.GetValueOrDefault("AssignedTo"), out var assignedTo) || assignedTo <= 0)
            return "Assigned To is required — select the employee this report belongs to.";

        // 2) Date valid ho aur aane wali (future) na ho
        if (!DateTime.TryParse(values.GetValueOrDefault("ReportDate"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var reportDate))
            return "Report Date is required and must be a valid date.";
        if (reportDate.Date > DateTime.Today)
            return "Report Date cannot be in the future.";

        // 3) Notes DB limit ke andar
        foreach (var c in new[] { "Summary", "Remark" })
            if (values.TryGetValue(c, out var note) && note.Length > MaxNoteLength)
                return $"{FieldLabel(c)} is too long — maximum {MaxNoteLength} characters.";

        return null;
    }

    private static string FieldLabel(string col) => col switch
    {
        "Summary" => "Work Summary",
        "Remark" => "Issues / Concerns",
        _ => col
    };

    /// <summary>Validation fail par AJAX ko JSON message, warna TempData + wapas form par.</summary>
    private IActionResult ValidationError(string message, string action, object? routeValues = null)
    {
        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            return StatusCode(StatusCodes.Status400BadRequest, new { success = false, message });
        TempData["Error"] = message;
        return routeValues == null ? RedirectToAction(action) : RedirectToAction(action, routeValues);
    }

    // =====================================================================
    //  EXPORT
    // =====================================================================
    [HttpGet]
    public IActionResult ExportExcel(string? search = null,
        int? assignedTo = null, DateTime? fromDate = null, DateTime? toDate = null)
    {
        var data = _repo.GetPage(1, int.MaxValue, search, assignedTo, fromDate, toDate, null, "DESC").Grid;

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
        "Visit" => "Visits",
        "NewLeads" => "New Leads",
        "TotalFollowUp" => "Total Follow Up",
        "InterestedLeads" => "Interested Leads",
        "ConvertedLeads" => "Converted Leads",
        "Summary" => "Work Summary",
        "Remark" => "Issues / Concerns",
        "EntryTime" => "Entry Time",
        "ModifiedOn" => "Modified On",
        "CreatedBy" => "Created By",
        _ => System.Text.RegularExpressions.Regex.Replace(col, "([a-z])([A-Z])", "$1 $2")
    };
}
