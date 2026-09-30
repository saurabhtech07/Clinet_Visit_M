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

        try
        {
            _repo.Insert("StateMaster", values);
        }
        catch (Exception ex)
        {
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                return Json(new { success = false, message = ex.Message }, StatusCode(StatusCodes.Status400BadRequest));
            throw;
        }

        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            return Json(new { success = true, message = "State saved successfully." });

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

        try
        {
            _repo.Update("StateMaster", "StateId", id, values);
        }
        catch (Exception ex)
        {
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                return Json(new { success = false, message = ex.Message }, StatusCode(StatusCodes.Status400BadRequest));
            throw;
        }

        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            return Json(new { success = true, id = id, message = "State updated successfully." });

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("Delete/{id}")]
    [ValidateAntiForgeryToken]
    public IActionResult Delete(int id)
    {
        var state = _repo.GetById("StateMaster", "StateId", id);
        if (state == null)
        {
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                return Json(new { success = false, message = "State not found." }, StatusCode(StatusCodes.Status404NotFound));
            return NotFound();
        }

        try
        {
            _repo.Delete("StateMaster", "StateId", id);
        }
        catch (Exception ex)
        {
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                return Json(new { success = false, message = ex.Message }, StatusCode(StatusCodes.Status400BadRequest));
            throw;
        }

        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            return Json(new { success = true, message = "State deleted successfully." });

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