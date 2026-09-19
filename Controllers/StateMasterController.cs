// // using Microsoft.AspNetCore.Mvc;
// // using ClientVisitManagement.Data;
// // using ClientVisitManagement.Models;

// // namespace ClientVisitManagement.Controllers;

// // public class StateMasterController : Controller
// // {
// //     private readonly DynamicRepository _dynamicRepo;

// //     public StateMasterController(DynamicRepository dynamicRepo) => _dynamicRepo = dynamicRepo;

// //     [HttpGet]
// //     public IActionResult Index()
// //     {
// //         var states = _dynamicRepo.GetTable("StateMaster");
// //         return View(states);
// //     }

// //     [HttpGet]
// //     public IActionResult Create()
// //     {
// //         var fields = _dynamicRepo.GetColumns("StateMaster").Where(f => f.Name != "StateId").ToList();
// //         return View(fields);
// //     }

// //     [HttpPost]
// //     public IActionResult Create(IFormCollection form)
// //     {
// //         var values = form.Keys.Where(k => k != "__RequestVerificationToken")
// //                                .ToDictionary(k => k, k => form[k].ToString());
// //         _dynamicRepo.Insert("StateMaster", values);
// //         return RedirectToAction(nameof(Index));
// //     }

// //     [HttpGet]
// //     public IActionResult Edit(int id)
// //     {
// //         var data = _dynamicRepo.GetTable("StateMaster").Rows.FirstOrDefault(r => (int)r["StateId"]! == id);
// //         if (data == null) return NotFound();
// //         var fields = _dynamicRepo.GetColumns("StateMaster").Where(f => f.Name != "StateId").ToList();
// //         ViewBag.RowData = data;
// //         ViewBag.StateId = id;
// //         return View(fields);
// //     }

// //     [HttpPost]
// //     public IActionResult Edit(int id, IFormCollection form)
// //     {
// //         var values = form.Keys.Where(k => k != "__RequestVerificationToken" && k != "StateId")
// //                                .ToDictionary(k => k, k => form[k].ToString());
// //         _dynamicRepo.Update("StateMaster", "StateId", id, values);
// //         return RedirectToAction(nameof(Index));
// //     }
// // }



// using ClientVisitManagement.Data;
// using Microsoft.AspNetCore.Mvc;

// namespace ClientVisitManagement.Controllers;

// [Route("StateMaster")]
// public class StateController : Controller
// {
//     private readonly DynamicRepository _repo;

//     public StateController(DynamicRepository repo)
//     {
//         _repo = repo;
//     }


//     // =========================================================
//     // STATE MASTER LIST
//     // /StateMaster
//     // =========================================================

// [HttpGet("")]
// public IActionResult Index(
//     int page = 1,
//     int pageSize = 10,
//     string? search = null,
//     string? sort = null,
//     string direction = "DESC")
// {
//     // Allowed page sizes
//     if (pageSize != 10 &&
//         pageSize != 25 &&
//         pageSize != 50 &&
//         pageSize != 100)
//     {
//         pageSize = 10;
//     }

//     var data = _repo.GetTable(
//         "StateMaster",
//         page,
//         pageSize,
//         search,
//         sort,
//         direction);

//     if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
//         return PartialView("_StateTable", data);

//     return View(data);
// }

//     // =========================================================
//     // CREATE - GET
//     // /StateMaster/Create
//     // =========================================================

//     [HttpGet("Create")]
//     public IActionResult Create()
//     {
//         var fields = _repo
//             .GetColumns("StateMaster")
//             .Where(f =>
//                 f.Name != "StateId" &&
//                 f.Name != "CreatedDate")
//             .ToList();

//         return View(fields);
//     }


//     // =========================================================
//     // CREATE - POST
//     // =========================================================

//     [HttpPost("Create")]
//     [ValidateAntiForgeryToken]
//     public IActionResult Create(Dictionary<string, string> values)
//     {
//         values.Remove("__RequestVerificationToken");
//         values.Remove("StateId");
//         values.Remove("CreatedDate");

//         _repo.Insert(
//             "StateMaster",
//             values);

//         return RedirectToAction(nameof(Index));
//     }


//     // =========================================================
//     // EDIT - GET
//     // /StateMaster/Edit/5
//     // =========================================================

//     [HttpGet("Edit/{id}")]
//     public IActionResult Edit(int id)
//     {
//         var fields = _repo
//             .GetColumns("StateMaster")
//             .Where(f =>
//                 f.Name != "StateId" &&
//                 f.Name != "CreatedDate")
//             .ToList();

//         var row = _repo.GetById(
//             "StateMaster",
//             "StateId",
//             id);

//         if (row == null)
//             return NotFound();

//         ViewBag.StateId = id;
//         ViewBag.RowData = row;

//         return View(fields);
//     }


//     // =========================================================
//     // EDIT - POST
//     // =========================================================

//     [HttpPost("Edit/{id}")]
//     [ValidateAntiForgeryToken]
//     public IActionResult Edit(
//         int id,
//         Dictionary<string, string> values)
//     {
//         values.Remove("__RequestVerificationToken");
//         values.Remove("StateId");
//         values.Remove("CreatedDate");

//         _repo.Update(
//             "StateMaster",
//             "StateId",
//             id,
//             values);

//         return RedirectToAction(nameof(Index));
//     }


//     // =========================================================
//     // DELETE
//     // /StateMaster/Delete/5
//     // =========================================================

//     [HttpPost("Delete/{id}")]
//     [ValidateAntiForgeryToken]
//     public IActionResult Delete(int id)
//     {
//         var state = _repo.GetById(
//             "StateMaster",
//             "StateId",
//             id);

//         if (state == null)
//             return NotFound();

//         _repo.Delete(
//             "StateMaster",
//             "StateId",
//             id);

//         return RedirectToAction(nameof(Index));
//     }
// }


using ClientVisitManagement.Data;
using Microsoft.AspNetCore.Mvc;
using System.Text;

namespace ClientVisitManagement.Controllers;

[Route("StateMaster")]
public class StateMasterController : Controller
{
    private readonly DynamicRepository _repo;
    public StateMasterController(DynamicRepository repo) { _repo = repo; }

[HttpGet("")]
public IActionResult Index(int page = 1, int pageSize = 10, string search = null, string sort = null, string direction = "DESC")
{
    if (pageSize != 10 && pageSize != 25 && pageSize != 50 && pageSize != 100) pageSize = 10;
    var data = _repo.GetTable("StateMaster", page, pageSize, search, sort, direction);

    ViewBag.TotalStates = data.TotalRecords;

    // Saare pages loop karke active ginega - isliye 10 ka limit khatam
    int activeCount = 0;
    int tempPage = 1;
    int tempPageSize = 100;
    while (true)
    {
        var chunk = _repo.GetTable("StateMaster", tempPage, tempPageSize, null, null, "ASC");
        if (chunk.Rows == null || chunk.Rows.Count == 0) break;
        
        foreach (var r in chunk.Rows)
        {
            var v = r.ContainsKey("IsActive") ? r["IsActive"]?.ToString().Trim().ToLower() : "";
            if (v == "1" || v == "true") activeCount++;
        }
        if (chunk.Rows.Count < tempPageSize) break;
        tempPage++;
    }
    ViewBag.ActiveStates = activeCount; // Ab jab 0 karega to 28, fir 0 karega to 27

    if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
    {
        var q = Request.Query;
        bool tableRequest = q.ContainsKey("page") || q.ContainsKey("pageSize")
            || !string.IsNullOrWhiteSpace(search) || !string.IsNullOrWhiteSpace(sort);
        if (tableRequest)
            return PartialView("_StateTable", data);
        return PartialView("Index", data);
    }
    return View(data);
}


    [HttpGet("Create")]
    public IActionResult Create()
    {
        var fields = _repo.GetColumns("StateMaster")
          .Where(f => f.Name!= "StateId" && f.Name!= "CreatedDate").ToList();
        return View(fields);
    }

    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public IActionResult Create(Dictionary<string, string> values)
    {
        values.Remove("__RequestVerificationToken");
        values.Remove("StateId");
        values.Remove("CreatedDate");
        values.Remove("Country");

        if (!values.ContainsKey("IsActive") || string.IsNullOrWhiteSpace(values["IsActive"]))
            values["IsActive"] = "1";

        if (values.ContainsKey("StateCode") && values["StateCode"]!= null)
            values["StateCode"] = values["StateCode"].ToUpper().Trim();

        _repo.Insert("StateMaster", values);

        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            return Json(new { success = true });

        return RedirectToAction(nameof(Index));
    }

    [HttpGet("Edit/{id}")]
    public IActionResult Edit(int id)
    {
        var fields = _repo.GetColumns("StateMaster").Where(f => f.Name!= "StateId" && f.Name!= "CreatedDate").ToList();
        var row = _repo.GetById("StateMaster", "StateId", id);
        if (row == null) return NotFound();
        ViewBag.StateId = id;
        ViewBag.RowData = row;
        return View(fields);
    }

    [HttpPost("Edit/{id}")]
    [ValidateAntiForgeryToken]
    public IActionResult Edit(int id, Dictionary<string, string> values)
    {
        values.Remove("__RequestVerificationToken");
        values.Remove("StateId");
        values.Remove("CreatedDate");
        values.Remove("Country");

        if (values.ContainsKey("StateCode") && values["StateCode"]!= null)
            values["StateCode"] = values["StateCode"].ToUpper().Trim();

        _repo.Update("StateMaster", "StateId", id, values);

        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            return Json(new { success = true });

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("Delete/{id}")]
    [ValidateAntiForgeryToken]
    public IActionResult Delete(int id)
    {
        var state = _repo.GetById("StateMaster", "StateId", id);
        if (state == null) return NotFound();

        _repo.Delete("StateMaster", "StateId", id);

        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            return Json(new { success = true });

        return RedirectToAction(nameof(Index));
    }

    [HttpGet("ExportExcel")]
    public IActionResult ExportExcel(string search = null)
    {
        var data = _repo.GetTable("StateMaster", 1, 100000, search, null, "ASC");
        var csv = new StringBuilder();
        csv.AppendLine("Sr.No,StateId,StateCode,StateName,IsActive,Date & Time");
        int sr = 1;
        foreach (var row in data.Rows)
        {
            var id = row.ContainsKey("StateId")? row["StateId"] : "";
            var code = row.ContainsKey("StateCode")? row["StateCode"] : "";
            var name = row.ContainsKey("StateName")? row["StateName"]?.ToString().Replace(","," ") : "";
            var active = row.ContainsKey("IsActive")? row["IsActive"] : "";
            var date = row.ContainsKey("CreatedDate")? row["CreatedDate"] : "";
            csv.AppendLine($"{sr},{id},{code},{name},{active},{date}");
            sr++;
        }
        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"StateMaster_{DateTime.Now:ddMMyyyy}.csv");
    }
}