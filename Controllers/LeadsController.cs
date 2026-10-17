using Microsoft.AspNetCore.Mvc;
using ClientVisitManagement.Data;
using ClientVisitManagement.Models;
using ClosedXML.Excel;
using System.Text;

namespace ClientVisitManagement.Controllers;

/// <summary>
/// Leads page — 4 cards + filters + dynamic grid + Add/Edit/Delete + Lead->Client Convert.
/// Poora dynamic: Leads table me naya column add karo, grid aur form me khud aa jaayega.
/// </summary>
public class LeadsController : Controller
{
    private readonly DynamicRepository _dynamicRepo;
    private readonly LeadRepository _leadRepo;
    private readonly UserRepository _userRepo;
    private readonly ClientRepository _clientRepo;

    public LeadsController(DynamicRepository dynamicRepo, LeadRepository leadRepo, UserRepository userRepo, ClientRepository clientRepo)
    {
        _dynamicRepo = dynamicRepo;
        _leadRepo = leadRepo;
        _userRepo = userRepo;
        _clientRepo = clientRepo;
    }

    private static readonly int[] AllowedPageSizes = { 10, 20, 50, 100 };

    private static readonly string[] StatusOptions = LeadOptions.StatusOptions;

    // Ye columns form me nahi dikhte (auto / internal).
    private static readonly string[] ExcludeFromForm =
        { "LeadId", "CreatedOn", "EntryTime", "IsConverted", "ConvertedClientId", "ConvertedOn", "LastContactedOn" };

    private void PopulateLookups()
    {
        var statesTable = _dynamicRepo.GetTable("StateMaster");
        statesTable.Rows = statesTable.Rows
            .Where(r => !r.ContainsKey("IsActive") || r["IsActive"] == null || System.Convert.ToBoolean(r["IsActive"]))
            .ToList();

        ViewBag.States = statesTable;
        ViewBag.Users = _userRepo.GetAll();
        ViewBag.StatusOptions = LeadOptions.StatusOptions;
        ViewBag.PriorityOptions = LeadOptions.PriorityOptions;
        ViewBag.SourceOptions = LeadOptions.SourceOptions;
        ViewBag.ActivityTypeOptions = LeadOptions.ActivityTypeOptions;
        ViewBag.OutcomeOptions = LeadOptions.OutcomeOptions;
    }

    // =====================================================================
    //  LIST
    // =====================================================================
    [HttpGet]
    public IActionResult Index(int page = 1, int pageSize = 10, string? search = null, string? status = null,
        string? priority = null, string? source = null, int? stateId = null, int? assignedTo = null,
        DateTime? fromDate = null, DateTime? toDate = null, string? sort = null, string direction = "DESC")
    {
        if (!AllowedPageSizes.Contains(pageSize)) pageSize = 10;

        var leads = _leadRepo.GetLeads(page, pageSize, search, status, priority, source, stateId, assignedTo, fromDate, toDate, sort, direction);
        var cards = _leadRepo.GetCards();

        ViewBag.TotalLeads = cards.Total;
        ViewBag.OpenLeads = cards.Open;
        ViewBag.ConvertedLeads = cards.Converted;
        ViewBag.DealValue = cards.DealValue;

        ViewBag.FilterStatus = status;
        ViewBag.FilterPriority = priority;
        ViewBag.FilterSource = source;
        ViewBag.FilterStateId = stateId;
        ViewBag.FilterAssignedTo = assignedTo;
        ViewBag.FilterFrom = fromDate?.ToString("yyyy-MM-dd");
        ViewBag.FilterTo = toDate?.ToString("yyyy-MM-dd");

        PopulateLookups();

        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
        {
            var q = Request.Query;
            bool tableRequest = q.ContainsKey("page") || q.ContainsKey("pageSize")
                || !string.IsNullOrWhiteSpace(search) || !string.IsNullOrWhiteSpace(sort)
                || !string.IsNullOrWhiteSpace(status) || !string.IsNullOrWhiteSpace(priority)
                || !string.IsNullOrWhiteSpace(source) || stateId.HasValue || assignedTo.HasValue
                || fromDate.HasValue || toDate.HasValue;

            if (tableRequest) return PartialView("_LeadTable", leads);
            return PartialView("Index", leads);
        }

        return View(leads);
    }

    // =====================================================================
    //  CREATE
    // =====================================================================
    [HttpGet]
    public IActionResult Create()
    {
        var fields = _dynamicRepo.GetColumns("Leads")
            .Where(f => !ExcludeFromForm.Contains(f.Name, StringComparer.OrdinalIgnoreCase)).ToList();

        PopulateLookups();
        ViewBag.Clients = _clientRepo.GetAll();
        return View(fields);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Create(IFormCollection form)
    {
        var exclude = ExcludeFromForm.Concat(new[] { "__RequestVerificationToken" }).ToArray();
        var values = form.Keys.Where(k => !exclude.Contains(k, StringComparer.OrdinalIgnoreCase))
            .ToDictionary(k => k, k => form[k].ToString());

        try
        {
            int newId = _dynamicRepo.Insert("Leads", values);
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                return Json(new { success = true, id = newId, message = "Lead saved successfully." });
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
        var data = _dynamicRepo.GetById("Leads", "LeadId", id);
        if (data == null) return NotFound();

        var fields = _dynamicRepo.GetColumns("Leads")
            .Where(f => !ExcludeFromForm.Contains(f.Name, StringComparer.OrdinalIgnoreCase)).ToList();

        ViewBag.RowData = data;
        ViewBag.LeadId = id;
        PopulateLookups();
        ViewBag.Clients = _clientRepo.GetAll();
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
            _dynamicRepo.Update("Leads", "LeadId", id, values);
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                return Json(new { success = true, id = id, message = "Lead updated successfully." });
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
            _dynamicRepo.Delete("Leads", "LeadId", id);
            return Json(new { success = true, message = "Lead deleted successfully." });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status400BadRequest, new { success = false, message = ex.Message });
        }
    }

    // =====================================================================
    //  CONVERT  (Lead -> Client)
    // =====================================================================
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Convert(int id)
    {
        var result = _leadRepo.ConvertToClient(id);

        if (!result.Ok)
            return StatusCode(StatusCodes.Status400BadRequest,
                new { success = false, message = result.Message, clientId = result.ClientId });

        // KPI cards turant update ho sakein (converted lead Total/Deal Value se hat jaye)
        var cards = _leadRepo.GetCards();

        return Json(new
        {
            success = true,
            message = result.Message,
            clientId = result.ClientId,
            total = cards.Total,
            open = cards.Open,
            converted = cards.Converted,
            dealValue = cards.DealValue
        });
    }

    // =====================================================================
    //  LEAD DETAIL (360°) — info + activity timeline + follow-up history
    // =====================================================================
    [HttpGet]
    public IActionResult Details(int id)
    {
        var lead = _leadRepo.GetLeadById(id);
        if (lead == null) return NotFound();

        var vm = new LeadDetailViewModel
        {
            LeadId = id,
            Lead = lead,
            Activities = _leadRepo.GetActivities(id),
            FollowUps = _leadRepo.GetFollowUps(id),
            Users = _userRepo.GetAll()
        };

        PopulateLookups();
        ViewBag.LeadId = id;

        // AJAX (in-app) navigation -> sirf content partial do, warna poora layout
        // mainContentArea ke andar nested ho jaata hai aur page toot jaata hai.
        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            return PartialView("Details", vm);

        return View(vm);
    }

    // =====================================================================
    //  ACTIVITY — add / delete (activity history timeline)
    // =====================================================================
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult AddActivity(int id, IFormCollection form)
    {
        try
        {
            var type = form["ActivityType"].ToString().Trim();
            var summary = form["Summary"].ToString().Trim();
            var outcome = form["Outcome"].ToString().Trim();

            var date = DateTime.TryParse(form["ActivityDate"].ToString(), out var d) ? d : DateTime.Now;

            DateTime? next = null;
            if (DateTime.TryParse(form["NextFollowUpDate"].ToString(), out var nf)) next = nf;

            int? assignedTo = int.TryParse(form["AssignedTo"].ToString(), out var at) && at > 0 ? at : null;
            int createdBy = HttpContext.Session.GetInt32("UserId") ?? 0;

            int newId = _leadRepo.AddActivity(id, type, date, summary, outcome, next, assignedTo, createdBy);

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                return Json(new { success = true, id = newId, message = "Activity added successfully." });
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (Exception ex)
        {
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                return StatusCode(StatusCodes.Status400BadRequest, new { success = false, message = ex.Message });
            throw;
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult DeleteActivity(int id, int leadId)
    {
        try
        {
            _leadRepo.DeleteActivity(id);
            return Json(new { success = true, message = "Activity deleted." });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status400BadRequest, new { success = false, message = ex.Message });
        }
    }

    // =====================================================================
    //  PIPELINE (Kanban board by Status)
    // =====================================================================
    [HttpGet]
    public IActionResult Pipeline()
    {
        var leads = _leadRepo.GetPipeline();
        ViewBag.StatusOptions = LeadOptions.StatusOptions;
        ViewBag.Users = _userRepo.GetAll();

        // AJAX (in-app) navigation / browser-back -> sirf content partial do,
        // warna poora layout mainContentArea ke andar nested ho jaata hai.
        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            return PartialView("Pipeline", leads);

        return View(leads);
    }

    // Drag-drop se stage badalna
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult UpdateStatus(int id, string status)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(status))
                return StatusCode(StatusCodes.Status400BadRequest, new { success = false, message = "Status required." });

            _leadRepo.UpdateStatus(id, status.Trim());
            return Json(new { success = true, message = "Stage updated." });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status400BadRequest, new { success = false, message = ex.Message });
        }
    }

    // =====================================================================
    //  EXPORT (dynamic columns)
    // =====================================================================
    [HttpGet]
    public IActionResult ExportExcel(string? search = null, string? status = null, string? priority = null,
        string? source = null, int? stateId = null, int? assignedTo = null, DateTime? fromDate = null, DateTime? toDate = null)
    {
        var data = _leadRepo.GetLeads(1, int.MaxValue, search, status, priority, source, stateId, assignedTo, fromDate, toDate, null, "DESC");

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Leads");
        for (int i = 0; i < data.Columns.Count; i++) ws.Cell(1, i + 1).Value = DisplayName(data.Columns[i]);
        for (int r = 0; r < data.Rows.Count; r++)
        {
            var row = data.Rows[r];
            for (int c = 0; c < data.Columns.Count; c++)
            {
                var val = row.ContainsKey(data.Columns[c]) ? row[data.Columns[c]] : null;
                ws.Cell(r + 2, c + 1).Value = val?.ToString() ?? "";
            }
        }
        ws.Columns().AdjustToContents();
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return File(stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Leads_{DateTime.Now:ddMMyyyy}.xlsx");
    }

    [HttpGet]
    public IActionResult ExportCsv(string? search = null, string? status = null, string? priority = null,
        string? source = null, int? stateId = null, int? assignedTo = null, DateTime? fromDate = null, DateTime? toDate = null)
    {
        var data = _leadRepo.GetLeads(1, int.MaxValue, search, status, priority, source, stateId, assignedTo, fromDate, toDate, null, "DESC");

        var csv = new StringBuilder();
        csv.AppendLine(string.Join(",", data.Columns.Select(c => EscapeCsv(DisplayName(c)))));
        foreach (var row in data.Rows)
        {
            var values = data.Columns.Select(column =>
            {
                var value = row.ContainsKey(column) ? row[column] : null;
                return EscapeCsv(value?.ToString() ?? "");
            });
            csv.AppendLine(string.Join(",", values));
        }
        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"Leads_{DateTime.Now:ddMMyyyy}.csv");
    }

    private static string DisplayName(string col) => col switch
    {
        "LeadId" => "Lead Id",
        "LeadPerson" => "Lead Person",
        "StateName" => "State",
        "AssignedToName" => "Assigned To",
        "DealValue" => "Deal Value",
        "LeadSource" => "Source",
        "Priority" => "Priority",
        "NextFollowUpDate" => "Next Follow Up",
        "LastContactedOn" => "Last Contacted",
        "ActivityCount" => "Activities",
        "CreatedOn" => "Created On",
        "EntryTime" => "Entry Time",
        _ => System.Text.RegularExpressions.Regex.Replace(col, "([a-z])([A-Z])", "$1 $2")
    };

    private static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }
}
