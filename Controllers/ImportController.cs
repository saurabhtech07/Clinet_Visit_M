#nullable enable
using System.Text.Json;
using ClientVisitManagement.Data;
using Microsoft.AspNetCore.Mvc;

namespace ClientVisitManagement.Controllers;

/// <summary>
/// Import feature. IConfiguration comes from the built-in container, so this
/// controller needs no registration in Program.cs.
/// </summary>
public class ImportController : Controller
{
    private readonly ImportRepository _repo;
    private readonly ILogger<ImportController> _log;

    public ImportController(IConfiguration config, ILogger<ImportController> log)
    {
        _repo = new ImportRepository(config);
        _log = log;
    }

    private Dictionary<string, string> Config() => _repo.GetConfig();

    private string CurrentUserName =>
        HttpContext.Session.GetString("FullName")
        ?? HttpContext.Session.GetString("Username")
        ?? "Unknown user";

    // ------------------------------------------------------------------- page

    public IActionResult Index()
    {
        Dictionary<string, string> cfg;
        try
        {
            _repo.EnsureReady();
            cfg = Config();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Import schema could not be prepared.");
            ViewBag.SetupError = ex.Message;
            cfg = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var (key, value, _) in ImportRepository.DefaultConfig) cfg[key] = value;
        }

        ViewBag.Cfg = cfg;
        ViewBag.PageSizes = _repo.PageSizes(cfg);
        ViewBag.DefaultPageSize = _repo.ConfigInt(cfg, "DefaultPageSize", 50);
        ViewBag.AllowDelete = _repo.ConfigBool(cfg, "AllowDelete", true);
        ViewBag.TabSavedLabel = _repo.ConfigValue(cfg, "TabSavedLabel", "Saved Files");
        ViewBag.TabCurrentLabel = _repo.ConfigValue(cfg, "TabCurrentLabel", "Current File");
        ViewBag.MaxRows = _repo.ConfigInt(cfg, "MaxRowsPerFile", 10000);
        ViewBag.LargeFileWarnRows = _repo.ConfigInt(cfg, "LargeFileWarnRows", 50000);
        ViewBag.MaxFileBytes = _repo.ConfigInt(cfg, "MaxFileSizeMB", 10) * 1024L * 1024L;
        return View();
    }

    // -------------------------------------------------------------------- save

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Save([FromBody] SaveRequest request)
    {
        try
        {
            var cfg = Config();
            if (request == null) return BadRequest(new { success = false, message = "Nothing was sent." });

            var name = (request.FileName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name)) return BadRequest(new { success = false, message = "File name is missing." });
            if (name.Length > 260) name = name[..260];

            var ext = Path.GetExtension(name).TrimStart('.').ToLowerInvariant();
            var allowed = _repo.AllowedExtensions(cfg);
            if (!allowed.Contains(ext))
                return BadRequest(new { success = false, message = $"'.{ext}' files are not allowed. Allowed: {string.Join(", ", allowed.Select(a => "." + a))}." });

            if (request.Headers == null || request.Headers.Count == 0)
                return BadRequest(new { success = false, message = "No columns were found in the file." });

            request.Rows ??= new List<List<string>>();
            var result = _repo.Save(request, CurrentUserName, cfg);

            if (!result.Success) return BadRequest(new { success = false, message = result.Message });
            return Json(new { success = true, message = result.Message, fileId = result.FileId, savedRows = result.SavedRows });
        }
        catch (JsonException)
        {
            return BadRequest(new { success = false, message = "The uploaded data could not be read." });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Import save failed.");
            return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "Save failed: " + ex.Message });
        }
    }

    // ------------------------------------------------------------------- files

    [HttpGet]
    public IActionResult Files(string? search, int page = 1, int pageSize = 0)
    {
        try
        {
            var cfg = Config();
            if (pageSize <= 0) pageSize = _repo.ConfigInt(cfg, "DefaultPageSize", 50);
            return Json(_repo.GetFiles(cfg, page, pageSize, search));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Import file list failed.");
            return StatusCode(StatusCodes.Status500InternalServerError, new { files = Array.Empty<object>(), total = 0, message = ex.Message });
        }
    }

    // -------------------------------------------------------------------- rows

    /// <summary>
    /// Returns one saved file whole. Paging and searching are the grid's job,
    /// so there are no paging parameters to validate here.
    /// </summary>
    [HttpGet]
    public IActionResult Rows(int fileId)
    {
        try
        {
            var data = _repo.GetRows(fileId);
            if (data == null) return NotFound(new { message = "That saved file no longer exists." });
            return Json(data);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Import rows failed.");
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = ex.Message });
        }
    }

    // ------------------------------------------------------------------ delete

    /* No template on purpose: the conventional route is "{controller}/{action}/{id?}",
       which puts the segment on key "id". An attribute template would need a
       class-level [Route("Import")] to end up at /Import/Delete/{id}, and without
       one it would register as bare /Delete/{id} and 404. */
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Delete(int id)
    {
        try
        {
            var cfg = Config();
            if (!_repo.ConfigBool(cfg, "AllowDelete", true))
                return BadRequest(new { success = false, message = "Deleting saved files is turned off." });

            if (!_repo.Delete(id))
                return NotFound(new { success = false, message = "That saved file no longer exists." });

            return Json(new { success = true, message = "File deleted." });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Import delete failed.");
            return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = ex.Message });
        }
    }
}
