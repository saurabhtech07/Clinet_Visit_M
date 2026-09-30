#nullable enable
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace ClientVisitManagement.Data;

public class SaveRequest
{
    public string FileName { get; set; } = "";
    public List<string> Headers { get; set; } = new();
    public List<List<string>> Rows { get; set; } = new();
}

public class SaveResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public int FileId { get; set; }
    public int SavedRows { get; set; }
}

public class SavedFileDto
{
    public int FileId { get; set; }
    public string FileName { get; set; } = "";
    public int RowCount { get; set; }
    public int ColumnCount { get; set; }
    public string? SavedByName { get; set; }
    public string SavedOnDisplay { get; set; } = "";
}

public class FileListDto
{
    public List<SavedFileDto> Files { get; set; } = new();
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(Total / (double)PageSize) : 0;
}

/// <summary>One saved file, opened whole. Paging and searching happen in the grid.</summary>
public class FileRowsDto
{
    public int FileId { get; set; }
    public string FileName { get; set; } = "";
    public List<string> Headers { get; set; } = new();
    public List<List<string>> Rows { get; set; } = new();
    public int RowCount { get; set; }
    public bool HasHeaders { get; set; }
}

/// <summary>
/// The whole Import feature, in two tables.
///
///   ImportConfig - settings (page size, limits, labels). Already exists.
///   ImportFile   - one row per uploaded file; all of its rows live in
///                  DataJson as one JSON array.
///
/// Deliberately the simplest shape that works: no foreign keys, no second row
/// table, no bulk copy, no cascade delete. Saving a file is a single INSERT,
/// opening one is a single SELECT, deleting one is a single DELETE. Nothing
/// here touches a pre-existing table, so the feature stays isolated from
/// Client / ClientVisit / DailySupport / Users.
///
/// Rows are not paged in the database - a file is read whole and the grid pages
/// and searches it in the browser, the same way the live file already works.
///
/// Read the file top to bottom in this order:
///   1. DTOs          - what crosses the boundary to the view
///   2. table names   - the two tables, and nothing else
///   3. DefaultConfig - config rows written once when the tables are made
///   4. schema        - create the tables, insert the config
///   5. save / files / rows / delete / config
///   6. helpers       - small shared utilities
/// </summary>
public class ImportRepository
{
    private const string ConfigTable = "ImportConfig";
    private const string FileTable = "ImportFile";

    /* The tables only need creating once per app process. _schemaReady flips to
       true after the first success so every later request skips this work.
       SchemaGate is a plain in-process lock: one instance, one creator.
       (This used to be a SQL sp_getapplock so that two app instances could not
       race on a cold start. Removed as unnecessary - if two instances ever do
       start at once against a brand new database, the guarded DDL makes the
       loser fail with "object already exists", and running
       Database/DDL_Import.sql once and restarting clears it.) */
    private static readonly object SchemaGate = new();
    private static volatile bool _schemaReady;

    /// <summary>
    /// Default values written the very first time the tables are created.
    /// After that the table itself is the source of truth - edit a row there and
    /// the app picks it up, nothing is cached.
    /// </summary>
    public static IReadOnlyList<(string Key, string Value, string Description)> DefaultConfig { get; } =
        new List<(string, string, string)>
        {
            ("MaxRowsPerFile",    "10000",               "Ek file me kitni rows save ho sakti hain"),
            ("PageSizeOptions",   "25,50,100,200",       "Rows-per-page dropdown ke options"),
            ("DefaultPageSize",   "50",                  "Default rows per page"),
            ("AllowedExtensions", "xlsx,xls,xlsm,csv",   "Kaunsi file types allowed"),
            ("MaxFileSizeMB",     "10",                  "Kitna bada file (MB)"),
            ("LargeFileWarnRows", "50000",               "Is se zyada rows par warning"),
            ("TabSavedLabel",     "Saved Files",         "Tab 1 ka label"),
            ("TabCurrentLabel",   "Current File",        "Tab 2 ka label"),
            ("AllowDelete",       "1",                   "1 = delete button dikhega, 0 = nahi"),
            ("DefaultSortDir",    "ASC",                 "Default column sort"),
            ("DateFormat",        "dd MMM yyyy hh:mm tt", "Saved-on date ka format"),
            ("MaxFilesInList",   "50",                  "Ek page par kitni saved files"),
        };

    private readonly string _connectionString;

    public ImportRepository(IConfiguration config)
    {
        _connectionString = config.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");
    }

    private SqlConnection Open()
    {
        var con = new SqlConnection(_connectionString);
        con.Open();
        return con;
    }

    // ------------------------------------------------------------------ schema

    /// <summary>
    /// Called at the top of every public method. Creates the three tables and
    /// seeds ImportConfig on the very first request of the process.
    /// </summary>
    public void EnsureReady()
    {
        if (_schemaReady) return;

        lock (SchemaGate)
        {
            if (_schemaReady) return;

            try
            {
                using var con = Open();
                CreateTables(con);
                SeedConfig(con);
                _schemaReady = true;
            }
            catch (Exception ex)
            {
                // A missing DDL right must never take the whole site down: the page
                // still loads, it just reports that the tables could not be made.
                throw new InvalidOperationException(
                    "Import tables could not be created. Run Database/DDL_Import.sql once, " +
                    "or grant the login permission to create tables. Cause: " + ex.Message, ex);
            }
        }
    }

    private static void CreateTables(SqlConnection con)
    {
        using var cmd = new SqlCommand(BuildSchemaDdl(), con);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Inserts any config key that is missing. An existing row is never
    /// overwritten, so edits made in the ImportConfig table survive restarts.
    /// </summary>
    private static void SeedConfig(SqlConnection con)
    {
        foreach (var (key, value, description) in DefaultConfig)
        {
            using var cmd = new SqlCommand(
                "IF NOT EXISTS (SELECT 1 FROM [" + ConfigTable + "] WHERE ConfigKey = @Key)" +
                " INSERT INTO [" + ConfigTable + "] (ConfigKey, ConfigValue, Description)" +
                " VALUES (@Key, @Value, @Description);", con);
            cmd.Parameters.AddWithValue("@Key", key);
            cmd.Parameters.AddWithValue("@Value", value);
            cmd.Parameters.AddWithValue("@Description", description);
            cmd.ExecuteNonQuery();
        }
    }

    private static string BuildSchemaDdl()
    {
        /* One interpolated verbatim string rather than a chain of @"" literals:
           mixing verbatim and non-verbatim fragments here is what made an
           earlier version of this file fail to compile. Single quotes are
           literal inside @"", so the SQL reads exactly as written.
           Beware: a double quote anywhere in here ends the string early, and
           C# comments are not allowed in the SQL body - use a SQL "--" comment.
           Database/DDL_Import.sql is the hand-runable copy of this script. */
        return $@"
IF OBJECT_ID(N'{ConfigTable}', N'U') IS NULL
CREATE TABLE [{ConfigTable}](
    ConfigKey    NVARCHAR(100) NOT NULL PRIMARY KEY,
    ConfigValue  NVARCHAR(MAX)  NULL,
    Description  NVARCHAR(300) NULL
);

IF OBJECT_ID(N'{FileTable}', N'U') IS NULL
CREATE TABLE [{FileTable}](
    FileId      INT IDENTITY(1,1) PRIMARY KEY,
    FileName    NVARCHAR(260) NOT NULL,
    Headers     NVARCHAR(MAX)  NULL,
    DataJson    NVARCHAR(MAX)  NULL,
    -- RowTotal, not RowCount: ROWCOUNT is a reserved T-SQL keyword and SQL
    -- Server rejects it as a column name.
    RowTotal    INT NOT NULL DEFAULT 0,
    ColumnCount INT NOT NULL DEFAULT 0,
    SavedByName NVARCHAR(100) NULL,
    -- Saved time is filled in by the database, never by the app.
    SavedOn     DATETIME NOT NULL DEFAULT GETDATE()
);
";
    }

    // ------------------------------------------------------------------ config

    public Dictionary<string, string> GetConfig()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        EnsureReady();
        using var con = Open();
        using var cmd = new SqlCommand("SELECT ConfigKey, ConfigValue FROM [" + ConfigTable + "];", con);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var key = reader.GetString(0);
            if (!string.IsNullOrWhiteSpace(key)) map[key] = reader.IsDBNull(1) ? "" : reader.GetValue(1).ToString() ?? "";
        }
        return map;
    }

    public string ConfigValue(Dictionary<string, string> cfg, string key, string fallback)
        => cfg.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;

    public int ConfigInt(Dictionary<string, string> cfg, string key, int fallback)
        => int.TryParse(ConfigValue(cfg, key, ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : fallback;

    public bool ConfigBool(Dictionary<string, string> cfg, string key, bool fallback)
    {
        var raw = ConfigValue(cfg, key, "").Trim();
        if (raw.Equals("1", StringComparison.Ordinal) || raw.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
        if (raw.Equals("0", StringComparison.Ordinal) || raw.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
        return fallback;
    }

    /// <summary>Page sizes the UI is allowed to ask for, from the config list.</summary>
    public List<int> PageSizes(Dictionary<string, string> cfg)
    {
        var raw = ConfigValue(cfg, "PageSizeOptions", "25,50,100,200");
        var sizes = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                       .Select(x => int.TryParse(x, out var n) ? n : 0)
                       .Where(n => n > 0)
                       .Distinct()
                       .OrderBy(n => n)
                       .ToList();
        if (sizes.Count == 0) sizes.AddRange(new[] { 25, 50, 100, 200 });
        return sizes;
    }

    public List<string> AllowedExtensions(Dictionary<string, string> cfg)
        => ConfigValue(cfg, "AllowedExtensions", "xlsx,xls,xlsm,csv")
                 .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                 .Select(x => x.Trim().TrimStart('.').ToLowerInvariant())
                 .Where(x => x.Length > 0)
                 .Distinct()
                 .ToList();

    // -------------------------------------------------------------------- save

    public SaveResult Save(SaveRequest request, string savedByName, Dictionary<string, string> cfg)
    {
        EnsureReady();

        int maxRows = Math.Max(1, ConfigInt(cfg, "MaxRowsPerFile", 10000));
        if (request.Rows.Count > maxRows)
            return new SaveResult { Success = false, Message = $"This file has {request.Rows.Count:N0} rows but the limit is {maxRows:N0}. Trim the file and try again." };

        if (request.Headers.Count == 0)
            return new SaveResult { Success = false, Message = "No columns were found in the file." };

        /* Every row is padded to the header width so the stored JSON is always
           a clean rectangle - a short row in the file must not make the grid
           columns wobble when the file is opened again. */
        var colCount = request.Headers.Count;
        var rows = new List<List<string>>(request.Rows.Count);
        foreach (var row in request.Rows)
        {
            var cells = row ?? new List<string>();
            var padded = new List<string>(colCount);
            for (var c = 0; c < colCount; c++) padded.Add(c < cells.Count ? cells[c] ?? "" : "");
            rows.Add(padded);
        }

        using var con = Open();
        using var cmd = new SqlCommand(
            "INSERT INTO [" + FileTable + "] (FileName, Headers, DataJson, RowTotal, ColumnCount, SavedByName)" +
            " OUTPUT INSERTED.FileId" +
            " VALUES (@FileName, @Headers, @DataJson, @RowTotal, @ColumnCount, @SavedByName);", con);
        cmd.Parameters.AddWithValue("@FileName", Truncate(request.FileName, 260));
        cmd.Parameters.AddWithValue("@Headers", JsonSerializer.Serialize(request.Headers));
        cmd.Parameters.AddWithValue("@DataJson", JsonSerializer.Serialize(rows));
        cmd.Parameters.AddWithValue("@RowTotal", rows.Count);
        cmd.Parameters.AddWithValue("@ColumnCount", colCount);
        cmd.Parameters.AddWithValue("@SavedByName", (object?)Truncate(savedByName, 100) ?? DBNull.Value);
        var fileId = Convert.ToInt32(cmd.ExecuteScalar());

        return new SaveResult { Success = true, Message = $"Saved {rows.Count:N0} rows.", FileId = fileId, SavedRows = rows.Count };
    }

    // ------------------------------------------------------------------- files

    public FileListDto GetFiles(Dictionary<string, string> cfg, int page, int pageSize, string? search)
    {
        EnsureReady();
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, Math.Max(1, ConfigInt(cfg, "MaxFilesInList", 50)));
        var dateFormat = ConfigValue(cfg, "DateFormat", "dd MMM yyyy hh:mm tt");

        var where = string.IsNullOrWhiteSpace(search) ? "" : " WHERE FileName LIKE @Search";
        var dto = new FileListDto { Page = page, PageSize = pageSize };

        using var con = Open();

        using (var countCmd = new SqlCommand("SELECT COUNT(*) FROM [" + FileTable + "]" + where + ";", con))
        {
            if (!string.IsNullOrWhiteSpace(search)) countCmd.Parameters.AddWithValue("@Search", "%" + search.Trim() + "%");
            dto.Total = Convert.ToInt32(countCmd.ExecuteScalar());
        }

        using var cmd = new SqlCommand(
            /* RowCount is a reserved T-SQL keyword, so the alias needs brackets too. */
            "SELECT FileId, FileName, RowTotal AS [RowCount], ColumnCount, SavedByName," +
            " CONVERT(VARCHAR(64), SavedOn, 100) AS SavedOnRaw" +
            " FROM [" + FileTable + "]" + where +
            " ORDER BY SavedOn DESC, FileId DESC" +
            " OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;", con);
        if (!string.IsNullOrWhiteSpace(search)) cmd.Parameters.AddWithValue("@Search", "%" + search.Trim() + "%");
        cmd.Parameters.AddWithValue("@Skip", (page - 1) * pageSize);
        cmd.Parameters.AddWithValue("@Take", pageSize);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var raw = reader.IsDBNull(5) ? null : reader.GetString(5);
            dto.Files.Add(new SavedFileDto
            {
                FileId = reader.GetInt32(0),
                FileName = reader.GetString(1),
                RowCount = reader.GetInt32(2),
                ColumnCount = reader.GetInt32(3),
                SavedByName = reader.IsDBNull(4) ? null : reader.GetString(4),
                SavedOnDisplay = FormatDate(raw, dateFormat)
            });
        }
        return dto;
    }

    // -------------------------------------------------------------------- rows

    /// <summary>
    /// Returns one saved file whole. The grid pages and searches it in the
    /// browser, so there is no paging here to keep in step with the database.
    /// </summary>
    public FileRowsDto? GetRows(int fileId)
    {
        EnsureReady();

        using var con = Open();
        using var cmd = new SqlCommand(
            "SELECT FileName, Headers, DataJson FROM [" + FileTable + "] WHERE FileId = @FileId;", con);
        cmd.Parameters.AddWithValue("@FileId", fileId);

        string fileName, headersJson, dataJson;
        using (var reader = cmd.ExecuteReader())
        {
            if (!reader.Read()) return null;      // deleted between click and load
            fileName = reader.GetString(0);
            headersJson = reader.IsDBNull(1) ? "" : reader.GetString(1);
            dataJson = reader.IsDBNull(2) ? "[]" : reader.GetString(2);
        }

        var dto = new FileRowsDto { FileId = fileId, FileName = fileName };

        if (!string.IsNullOrWhiteSpace(headersJson))
        {
            try { dto.Headers = JsonSerializer.Deserialize<List<string>>(headersJson) ?? new List<string>(); }
            catch (JsonException) { dto.Headers = new List<string>(); }
        }
        dto.HasHeaders = dto.Headers.Count > 0;

        try
        {
            dto.Rows = JsonSerializer.Deserialize<List<List<string>>>(dataJson) ?? new List<List<string>>();
        }
        catch (JsonException)
        {
            /* A single unreadable row must not cost the user the whole file,
               so that row is dropped and the rest is still shown. */
            dto.Rows = ReadRowsLenient(dataJson);
        }
        dto.RowCount = dto.Rows.Count;
        return dto;
    }

    // ------------------------------------------------------------------ delete

    public bool Delete(int fileId)
    {
        EnsureReady();
        using var con = Open();
        using var cmd = new SqlCommand("DELETE FROM [" + FileTable + "] WHERE FileId = @FileId;", con);
        cmd.Parameters.AddWithValue("@FileId", fileId);
        return cmd.ExecuteNonQuery() > 0;
    }

    // ----------------------------------------------------------------- helpers

    /// <summary>
    /// Last-resort reader for a DataJson array that will not parse as a whole.
    /// Walks the raw text, keeping only the row arrays that do parse, so one bad
    /// row costs the user that row instead of the whole saved file.
    /// </summary>
    private static List<List<string>> ReadRowsLenient(string dataJson)
    {
        var rows = new List<List<string>>();
        var depth = 0;
        var start = -1;
        for (var i = 0; i < dataJson.Length; i++)
        {
            var ch = dataJson[i];
            if (ch == '[')
            {
                if (depth == 1) start = i;
                depth++;
            }
            else if (ch == ']')
            {
                depth--;
                if (depth == 1 && start >= 0)
                {
                    try
                    {
                        rows.Add(JsonSerializer.Deserialize<List<string>>(dataJson[start..(i + 1)]) ?? new List<string>());
                    }
                    catch (JsonException) { /* skip just this row */ }
                    start = -1;
                }
            }
        }
        return rows;
    }

    private static string FormatDate(string? raw, string format)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        try
        {
            return DateTime.ParseExact(raw, "MMM dd yyyy HH:mm:ss", CultureInfo.InvariantCulture,
                                       DateTimeStyles.AllowWhiteSpaces).ToString(format, CultureInfo.InvariantCulture);
        }
        catch (FormatException) { return raw; }
    }

    private static string? Truncate(string? value, int max)
        => string.IsNullOrEmpty(value) ? null : (value.Length <= max ? value : value[..max]);
}
