using ClientVisitManagement.Data;
using Microsoft.AspNetCore.Mvc;

namespace ClientVisitManagement.Controllers;

public class FollowUpController : Controller
{
    private readonly FollowUpRepository _repo;
    private readonly UserRepository _userRepo;
    private readonly ClientRepository _clientRepo;

    public FollowUpController(FollowUpRepository repo, UserRepository userRepo, ClientRepository clientRepo)
    {
        _repo = repo;
        _userRepo = userRepo;
        _clientRepo = clientRepo;
    }

    // ================== PAGE ==================

    [HttpGet]
    public IActionResult Index(int page = 1, int pageSize = 10, int? userId = null, string? search = null, string? status = null, string? sort = null, string direction = "DESC", string? section = null)
    {
        var vm = _repo.GetPage(page, pageSize, userId, search, status ?? section, sort, direction);
        vm.Users   = _userRepo.GetAll();
        vm.Clients = _clientRepo.GetAll();
        vm.Section = string.IsNullOrEmpty(section) ? "all" : section;
        return View(vm);
    }

    /// <summary>AJAX filter - sirf summary chips + list partial return karta hai.</summary>
    [HttpGet]
    public IActionResult Filter(int page = 1, int pageSize = 10, int? userId = null, string? search = null, string? status = null, string? sort = null, string direction = "DESC")
    {
        var vm = _repo.GetPage(page, pageSize, userId, search, status, sort, direction);
        vm.Users = _userRepo.GetAll();
        return PartialView("_FuResult", vm);
    }

    /// <summary>Topbar 🔔 badge ke liye - halka sa JSON count.</summary>
    [HttpGet]
    public JsonResult Badge()
    {
        var (overdue, today) = _repo.GetBadgeCounts();
        return Json(new { overdue, today });
    }

    // ================== ACTIONS ==================

    [HttpPost][ValidateAntiForgeryToken]
    public IActionResult MarkDone(int id)
        => Do(() => _repo.MarkDone(id), "Follow-up Done ✓");

    [HttpPost][ValidateAntiForgeryToken]
    public IActionResult Postpone(int id, DateTime nextDate)
        => Do(() => _repo.Postpone(id, nextDate), "Follow-up postponed to " + nextDate.ToString("dd-MM-yyyy"));

    [HttpPost][ValidateAntiForgeryToken]
    public IActionResult Delete(int id)
        => Do(() => _repo.Delete(id), "Follow-up deleted.");

    [HttpPost][ValidateAntiForgeryToken]
    public IActionResult Save(int clientId, int? userId, DateTime? nextDate, string? description, string? remarks)
    {
        var createdBy = HttpContext.Session.GetInt32("UserId") ?? 0;
        return Do(() => _repo.Add(clientId, userId, nextDate, description, remarks, createdBy), "Follow-up added ✓");
    }

    [HttpPost][ValidateAntiForgeryToken]
    public IActionResult Update(int id, int? userId, DateTime? nextDate, string? description, string? remarks)
        => Do(() => _repo.Update(id, nextDate, userId, description, remarks), "Follow-up updated ✓");

    // ================== COMMON RESPONSE ==================

    private IActionResult Do(Action work, string successMessage)
    {
        try
        {
            work();
            return Respond(success: true, successMessage);
        }
        catch (Exception ex)
        {
            return Respond(success: false, ex.Message);
        }
    }

    private IActionResult Respond(bool success, string message)
    {
        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            return Json(new { success, message },
                        success ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest);

        if (success) TempData["Success"] = message;
        else TempData["Error"] = message;
        return RedirectToAction(nameof(Index));
    }
}