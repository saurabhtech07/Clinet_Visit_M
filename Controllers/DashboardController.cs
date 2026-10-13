using ClientVisitManagement.Data;
using Microsoft.AspNetCore.Mvc;

namespace ClientVisitManagement.Controllers;

public class DashboardController : Controller
{
    private readonly DashboardRepository _repo;

    public DashboardController(DashboardRepository repo) => _repo = repo;

    [HttpGet]
    public IActionResult Index()
    {
        var model = _repo.GetOverview();
        return View(model);
    }
}