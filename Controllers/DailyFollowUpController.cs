using Microsoft.AspNetCore.Mvc;
using ClientVisitManagement.Data;
using ClientVisitManagement.Models;
using ClosedXML.Excel;

namespace ClientVisitManagement.Controllers;

/// <summary>
/// Daily Follow Up page — 4 cards + filters + dynamic grid + Add/Edit/Delete.
/// Poora dynamic: DailyFollowUp table me naya column add karo, grid aur form me
/// khud aa jaayega (jaise Leads module me hota hai).
/// </summary>
public class DailyFollowUpController : Controller
{
    private readonly DynamicRepository _dynamicRepo;
    private readonly DailyFollowUpRepository _repo;
    private readonly UserRepository _userRepo;

    public DailyFollowUpController(DynamicRepository dynamicRepo, DailyFollowUpRepository repo, UserRepository userRepo)
    {
        _dynamicRepo = dynamicRepo;
        _repo = repo;
        _userRepo = userRepo;
    }

    private static readonly int[] AllowedPageSizes = { 10, 20, 50, 100 };

    // Ye columns form me nahi dikhte.
    //  - auto / internal : DailyFollowUpId, CreatedBy, CreatedOn
    //  - Lead ka data (single source of truth) : ContactPerson, Company, Mobile, City
    //    ye grid me Leads table se LIVE dikhte hain, form me dobara nahi bharte.
    private static readonly string[] ExcludeFromForm =
        { "DailyFollowUpId", "CreatedBy", "CreatedOn", "ContactPerson", "Company", "Mobile", "City" };

    // =====================================================================
    //  LOOKUPS
    // =====================================================================
    private void PopulateLookups()
    {
        ViewBag.Users = _userRepo.GetAll();
        ViewBag.StatusOptions = DailyFollowUpViewModel.StatusOptions;
        ViewBag.PriorityOptions = DailyFollowUpViewModel.PriorityOptions;

        try { ViewBag.Leads = _dynamicRepo.GetTable("Leads").Rows; }
        catch { ViewBag.Leads = new List<Dictionary<string, object?>>(); }
    }

    // =====================================================================
    //  LIST
    // =====================================================================
    [HttpGet]
    public IActionResult Index(int page = 1, int pageSize = 10, string? search = null, string? status = null,
        string? priority = null, int? assignedTo = null, DateTime? fromDate = null, DateTime? toDate = null,
        string? sort = null, string direction = "DESC")
    {
        if (!AllowedPageSizes.Contains(pageSize)) pageSize = 10;

        var vm = _repo.GetPage(page, pageSize, search, status, priority, assignedTo, fromDate, toDate, sort, direction);
        vm.Users = _userRepo.GetAll();

        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
        {
            var q = Request.Query;
            bool tableRequest = q.ContainsKey("page") || q.ContainsKey("pageSize")
                || !string.IsNullOrWhiteSpace(search) || !string.IsNullOrWhiteSpace(sort)
                || !string.IsNullOrWhiteSpace(status) || !string.IsNullOrWhiteSpace(priority)
                || assignedTo.HasValue || fromDate.HasValue || toDate.HasValue;

            if (tableRequest) return PartialView("_DailyFollowUpTable", vm);
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
        var fields = _dynamicRepo.GetColumns("DailyFollowUp")
            .Where(f => !ExcludeFromForm.Contains(f.Name, StringComparer.OrdinalIgnoreCase)).ToList();

        PopulateLookups();
        ViewBag.FollowUpDateDefault = DateTime.Now.ToString("yyyy-MM-dd");
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
        if (!values.ContainsKey("FollowUpDate") || string.IsNullOrWhiteSpace(values["FollowUpDate"]))
            values["FollowUpDate"] = DateTime.Now.ToString("yyyy-MM-dd");

        try
        {
            int newId = _dynamicRepo.Insert("DailyFollowUp", values);
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                return Json(new { success = true, id = newId, message = "Follow-up saved successfully." });
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
        var data = _dynamicRepo.GetById("DailyFollowUp", "DailyFollowUpId", id);
        if (data == null) return NotFound();

        var fields = _dynamicRepo.GetColumns("DailyFollowUp")
            .Where(f => !ExcludeFromForm.Contains(f.Name, StringComparer.OrdinalIgnoreCase)).ToList();

        ViewBag.RowData = data;
        ViewBag.FollowUpId = id;
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

        try
        {
            _dynamicRepo.Update("DailyFollowUp", "DailyFollowUpId", id, values);
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                return Json(new { success = true, id = id, message = "Follow-up updated successfully." });
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
            _dynamicRepo.Delete("DailyFollowUp", "DailyFollowUpId", id);
            return Json(new { success = true, message = "Follow-up deleted successfully." });
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
            _dynamicRepo.Update("DailyFollowUp", "DailyFollowUpId", id,
                new Dictionary<string, string> { ["Status"] = "Done" });
            return Json(new { success = true, message = "Follow-up done ✓" });
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
    public IActionResult ExportExcel(string? search = null, string? status = null, string? priority = null,
        int? assignedTo = null, DateTime? fromDate = null, DateTime? toDate = null)
    {
        var data = _repo.GetPage(1, int.MaxValue, search, status, priority, assignedTo, fromDate, toDate, null, "DESC").Grid;

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Daily Follow Up");
        for (int i = 0; i < data.Columns.Count; i++) ws.Cell(1, i + 1).Value = DisplayName(data.Columns[i]);
        for (int r = 0; r < data.Rows.Count; r++)
        {
            var row = data.Rows[r];
            for (int c = 0; c < data.Columns.Count; c++)
            {
                var val = row.ContainsKey(data.Columns[c]) ? row[data.Columns[c]] : null;
                ws.Cell(r + 2, c + 1).Value = val is DateTime dt ? dt.ToString("dd-MM-yyyy") : (val?.ToString() ?? "");
            }
        }
        ws.Columns().AdjustToContents();
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return File(stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"DailyFollowUp_{DateTime.Now:ddMMyyyy}.xlsx");
    }

    private static string DisplayName(string col) => col switch
    {
        "DailyFollowUpId" => "Id",
        "ContactPerson" => "Contact Person",
        "FollowUpDate" => "Follow Up Date",
        "NextFollowUpDate" => "Next Follow Up",
        "AssignedName" => "Assigned To",
        "CreatedOn" => "Created On",
        _ => System.Text.RegularExpressions.Regex.Replace(col, "([a-z])([A-Z])", "$1 $2")
    };
}
