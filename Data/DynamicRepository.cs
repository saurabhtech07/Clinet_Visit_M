using System.Globalization;
using Microsoft.Data.SqlClient;
using ClientVisitManagement.Models;

namespace ClientVisitManagement.Data;

public class DynamicRepository
{
    private readonly DBHelper _db;

    private sealed class ColInfo
    {
        public string Type { get; set; } = "";
        public bool Nullable { get; set; }
    }

    private static readonly Dictionary<string, Dictionary<string, ColInfo>> _colInfoCache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] AllowedTables =
    {
        "Client", "StateMaster", "ClientVisit", "Users", "Roles", "CompanyType", "VisitTypeMaster",
        "DailySupport", "Leads", "DailyFollowUp", "DailyReport"
    };

    public DynamicRepository(DBHelper db) => _db = db;

    // FIX 1: Ye wala GetTable connection open nahi kar raha tha
    public DynamicTableViewModel GetTable(string tableName)
    {
        ValidateTable(tableName);
        using var con = _db.GetConnection();
        con.Open();
        if (tableName.Equals("Users", StringComparison.OrdinalIgnoreCase))
        {
            using var cmd = new SqlCommand("""
                SELECT u.*, r.RoleName AS Role
                FROM Users u LEFT JOIN Roles r ON u.RoleId = r.RoleId ORDER BY u.UserId DESC
                """, con);
            return ReadTable(cmd);
        }
        using var genericCmd = new SqlCommand($"SELECT * FROM [{tableName}] ORDER BY 1 DESC", con);
        return ReadTable(genericCmd);
    }

    public DynamicTableViewModel GetClientTable(int page, int pageSize, string? search, string? sort, string direction = "DESC")
    {
        ValidateTable("Client");
        page = Math.Max(1, page);
        pageSize = pageSize is 10 or 20 or 50 or 100? pageSize : 10;

        string orderColumn =!string.IsNullOrWhiteSpace(sort)? sort : "ClientId";
        string orderDir = string.Equals(direction, "ASC", StringComparison.OrdinalIgnoreCase)? "ASC" : "DESC";

        string where = "";
        if (!string.IsNullOrWhiteSpace(search))
        {
where = @" WHERE CAST(c.ClientId AS NVARCHAR(MAX)) LIKE @Search OR c.ClientName LIKE @Search OR c.City LIKE @Search OR c.MobileNo LIKE @Search OR c.Email LIKE @Search";
        }

        using var con = _db.GetConnection();
        con.Open();

        using var countCmd = new SqlCommand($"SELECT COUNT(*) FROM Client c {where}", con);
        AddSearch(countCmd, search);
        int total = Convert.ToInt32(countCmd.ExecuteScalar());

        using var cmd = new SqlCommand($"""
            SELECT c.*, s.StateName, ct.Name as CompanyTypeName,
                   ISNULL(NULLIF(u.FullName,''), u.Username) as AssignedToName
            FROM Client c
            LEFT JOIN StateMaster s ON c.StateId = s.StateId
            LEFT JOIN CompanyType ct ON c.CompanyTypeId = ct.Id
            LEFT JOIN Users u ON c.AssignedTo = u.UserId
            {where} ORDER BY [{orderColumn}] {orderDir} OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY
            """, con);
        AddSearch(cmd, search);
        cmd.Parameters.AddWithValue("@Skip", (page - 1) * pageSize);
        cmd.Parameters.AddWithValue("@Take", pageSize);
        return ReadTable(cmd, page, pageSize, total, search, sort, orderDir);
    }

    public DynamicTableViewModel GetClientVisitTable(int page, int pageSize, string? search, string? sort, string direction = "DESC")
    {
        ValidateTable("ClientVisit");
        page = Math.Max(1, page);
        pageSize = pageSize is 10 or 20 or 50 or 100? pageSize : 10;
        string orderDir = string.Equals(direction, "ASC", StringComparison.OrdinalIgnoreCase)? "ASC" : "DESC";
        string orderColumn =!string.IsNullOrWhiteSpace(sort)? sort : "VisitId";
        string where =!string.IsNullOrWhiteSpace(search)? " WHERE CAST(cv.VisitId AS NVARCHAR(MAX)) LIKE @Search OR c.ClientName LIKE @Search" : "";

        using var con = _db.GetConnection();
        con.Open();

        using var countCmd = new SqlCommand($"SELECT COUNT(*) FROM ClientVisit cv LEFT JOIN Client c ON cv.ClientId = c.ClientId {where}", con);
        AddSearch(countCmd, search);
        int total = Convert.ToInt32(countCmd.ExecuteScalar());

        using var cmd = new SqlCommand($"""
            SELECT cv.*, c.ClientName, s.StateName
            FROM ClientVisit cv
            LEFT JOIN Client c ON cv.ClientId = c.ClientId
            LEFT JOIN StateMaster s ON c.StateId = s.StateId
            {where} ORDER BY [{orderColumn}] {orderDir} OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY
            """, con);
        AddSearch(cmd, search);
        cmd.Parameters.AddWithValue("@Skip", (page - 1) * pageSize);
        cmd.Parameters.AddWithValue("@Take", pageSize);
        return ReadTable(cmd, page, pageSize, total, search, sort, orderDir);
    }

    public DynamicTableViewModel GetTable(string tableName, int page, int pageSize, string? search, string? sort, string direction = "DESC")
    {
        ValidateTable(tableName);
        page = Math.Max(1, page);
        pageSize = pageSize is 10 or 20 or 50 or 100? pageSize : 10;
        using var con = _db.GetConnection();
        con.Open();

        if (tableName.Equals("Users", StringComparison.OrdinalIgnoreCase))
        {
            string where =!string.IsNullOrWhiteSpace(search)? """WHERE CAST(u.UserId AS NVARCHAR(MAX)) LIKE @Search OR u.FullName LIKE @Search OR u.Username LIKE @Search OR u.Email LIKE @Search OR u.Mobile LIKE @Search OR r.RoleName LIKE @Search""" : "";
            string userOrderColumn = sort switch { "FullName" => "u.FullName", "Username" => "u.Username", "Email" => "u.Email", "Mobile" => "u.Mobile", "Role" => "r.RoleName", "IsActive" => "u.IsActive", "CreatedDate" => "u.CreatedDate", _ => "u.UserId" };
            string orderDirection = string.Equals(direction, "ASC", StringComparison.OrdinalIgnoreCase)? "ASC" : "DESC";
            using var countCmd = new SqlCommand($"SELECT COUNT(*) FROM Users u LEFT JOIN Roles r ON u.RoleId = r.RoleId {where}", con);
            AddSearch(countCmd, search);
            int total = Convert.ToInt32(countCmd.ExecuteScalar());

            using var cmd = new SqlCommand($"""SELECT u.*, r.RoleName AS Role FROM Users u LEFT JOIN Roles r ON u.RoleId = r.RoleId {where} ORDER BY {userOrderColumn} {orderDirection} OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY""", con);
            AddSearch(cmd, search);
            cmd.Parameters.AddWithValue("@Skip", (page - 1) * pageSize);
            cmd.Parameters.AddWithValue("@Take", pageSize);
            return ReadTable(cmd, page, pageSize, total, search, sort, orderDirection);
        }

        var columns = GetColumns(tableName).Select(x => x.Name).ToList();
        string pk = GetPrimaryKey(tableName);
        string genericWhere =!string.IsNullOrWhiteSpace(search)? " WHERE " + string.Join(" OR ", columns.Select(c => $"CAST([{c}] AS NVARCHAR(MAX)) LIKE @Search")) : "";
        string orderColumn =!string.IsNullOrWhiteSpace(sort) && columns.Contains(sort)? sort : pk;
        string orderDirectionGeneric = string.Equals(direction, "ASC", StringComparison.OrdinalIgnoreCase)? "ASC" : "DESC";
        using var genericCountCmd = new SqlCommand($"SELECT COUNT(*) FROM [{tableName}]{genericWhere}", con);
        AddSearch(genericCountCmd, search);
        int genericTotal = Convert.ToInt32(genericCountCmd.ExecuteScalar());
        using var genericCmd = new SqlCommand($"SELECT * FROM [{tableName}] {genericWhere} ORDER BY [{orderColumn}] {orderDirectionGeneric} OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY", con);
        AddSearch(genericCmd, search);
        genericCmd.Parameters.AddWithValue("@Skip", (page - 1) * pageSize);
        genericCmd.Parameters.AddWithValue("@Take", pageSize);
        return ReadTable(genericCmd, page, pageSize, genericTotal, search, sort, orderDirectionGeneric);
    }

    public List<DynamicField> GetColumns(string tableName)
    {
        ValidateTable(tableName);
        var fields = new List<DynamicField>();
        using var con = _db.GetConnection();
        con.Open();
        using var cmd = new SqlCommand("SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @TableName ORDER BY ORDINAL_POSITION", con);
        cmd.Parameters.AddWithValue("@TableName", tableName);
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) { fields.Add(new DynamicField { Name = reader["COLUMN_NAME"].ToString()!, DataType = reader["DATA_TYPE"].ToString()!, IsNullable = reader["IS_NULLABLE"].ToString() == "YES" }); }
        return fields;
    }

    public Dictionary<string, object?>? GetById(string tableName, string idColumn, int id)
    {
        ValidateTable(tableName);
        using var con = _db.GetConnection();
        con.Open();
        using var cmd = new SqlCommand($"SELECT * FROM [{tableName}] WHERE [{idColumn}] = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        var row = new Dictionary<string, object?>();
        for (int i = 0; i < reader.FieldCount; i++) { row[reader.GetName(i)] = reader.IsDBNull(i)? null : reader.GetValue(i); }
        return row;
    }

    public List<Role> GetRoles()
    {
        var list = new List<Role>();
        using var con = _db.GetConnection();
        con.Open();
        using var cmd = new SqlCommand("SELECT RoleId, RoleName, IsActive, CreatedDate FROM Roles WHERE IsActive = 1 ORDER BY RoleName", con);
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) { list.Add(new Role { RoleId = (int)reader["RoleId"], RoleName = reader["RoleName"].ToString()!, IsActive = (bool)reader["IsActive"], CreatedDate = (DateTime)reader["CreatedDate"] }); }
        return list;
    }

    public (string CurrentDate, string CurrentTime) GetServerDateTime()
    {
        using var con = _db.GetConnection();
        con.Open();
        using var cmd = new SqlCommand("SELECT CONVERT(VARCHAR(10), GETDATE(), 23) AS CurrentDate, CONVERT(VARCHAR(5), GETDATE(), 108) AS CurrentTime", con);
        using var reader = cmd.ExecuteReader();
        if (reader.Read()) { return (reader["CurrentDate"].ToString()!, reader["CurrentTime"].ToString()!); }
        throw new InvalidOperationException("Unable to get server date and time.");
    }

    public int Insert(string tableName, Dictionary<string, string> values)
    {
        ValidateTable(tableName);
        string pk = GetPrimaryKey(tableName);
        var cols = values.Keys.Where(k =>!k.Equals(pk, StringComparison.OrdinalIgnoreCase)).ToList();
        if (cols.Count == 0) throw new ArgumentException("No values supplied.");
        using var con = _db.GetConnection();
        con.Open();
        if (tableName.Equals("Users", StringComparison.OrdinalIgnoreCase) && values.ContainsKey("Username"))
        {
            using var checkCmd = new SqlCommand("SELECT COUNT(*) FROM Users WHERE Username = @Username", con);
            checkCmd.Parameters.AddWithValue("@Username", values["Username"]);
            if (Convert.ToInt32(checkCmd.ExecuteScalar()) > 0)
                throw new InvalidOperationException($"Username '{values["Username"]}' already exists!");
        }
        string colList = string.Join(",", cols.Select(c => $"[{c}]"));
        string paramList = string.Join(",", cols.Select(c => "@" + c));
        using var cmd = new SqlCommand($"INSERT INTO [{tableName}] ({colList}) OUTPUT INSERTED.[{pk}] VALUES ({paramList})", con);
        var info = GetColumnInfo(tableName);
        foreach (var c in cols) { cmd.Parameters.AddWithValue("@" + c, PrepareValue(info.TryGetValue(c, out var ci) ? ci : null, values[c])); }
        int newId = Convert.ToInt32(cmd.ExecuteScalar());
        EnsureDailyReportRow(con, tableName, newId);
        return newId;
    }

    public void Delete(string tableName, string idColumn, int id)
    {
        ValidateTable(tableName);
        using var con = _db.GetConnection();
        con.Open();
        using var cmd = new SqlCommand($"DELETE FROM [{tableName}] WHERE [{idColumn}] = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.ExecuteNonQuery();
    }

    public void Update(string tableName, string idColumn, int id, Dictionary<string, string> values)
    {
        ValidateTable(tableName);
        var cols = values.Keys.Where(k =>!k.Equals(idColumn, StringComparison.OrdinalIgnoreCase)).ToList();
        if (cols.Count == 0) return;
        using var con = _db.GetConnection();
        con.Open();
        if (tableName.Equals("Users", StringComparison.OrdinalIgnoreCase) && values.ContainsKey("Username"))
        {
            using var checkCmd = new SqlCommand("SELECT COUNT(*) FROM Users WHERE Username = @Username AND UserId!= @Id", con);
            checkCmd.Parameters.AddWithValue("@Username", values["Username"]);
            checkCmd.Parameters.AddWithValue("@Id", id);
            if (Convert.ToInt32(checkCmd.ExecuteScalar()) > 0)
                throw new InvalidOperationException($"Username '{values["Username"]}' already exists!");
        }
        string setClause = string.Join(",", cols.Select(c => $"[{c}] = @{c}"));
        using var cmd = new SqlCommand($"UPDATE [{tableName}] SET {setClause} WHERE [{idColumn}] = @Id", con);
        var info = GetColumnInfo(tableName);
        foreach (var c in cols) { cmd.Parameters.AddWithValue("@" + c, PrepareValue(info.TryGetValue(c, out var ci) ? ci : null, values[c])); }
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.ExecuteNonQuery();
        EnsureDailyReportRow(con, tableName, id);
    }

private static DynamicTableViewModel ReadTable(SqlCommand cmd, int page = 1, int pageSize = 0, int total = 0, string? search = null, string? sort = null, string direction = "DESC")
{
    using var reader = cmd.ExecuteReader();
    var result = new DynamicTableViewModel 
    { 
        CurrentPage = page, 
        PageSize = pageSize, 
        TotalRecords = total, 
        SearchTerm = search, 
        SortColumn = sort, 
        SortDirection = direction 
    };
    for (int i = 0; i < reader.FieldCount; i++) 
        result.Columns.Add(reader.GetName(i));
    
    while (reader.Read()) 
    { 
        var row = new Dictionary<string, object?>(); 
        for (int i = 0; i < reader.FieldCount; i++) 
        { 
            row[reader.GetName(i)] = reader.IsDBNull(i)? null : reader.GetValue(i); 
        } 
        result.Rows.Add(row); 
    }
    // TotalPages auto-calculate hoga TotalRecords se, isko assign nahi karna
    return result;
}

    private static void AddSearch(SqlCommand cmd, string? search) { if (!string.IsNullOrWhiteSpace(search)) { cmd.Parameters.AddWithValue("@Search", "%" + search + "%"); } }
    private static void ValidateTable(string tableName) { if (!AllowedTables.Contains(tableName, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("Invalid table name."); }

    private Dictionary<string, ColInfo> GetColumnInfo(string tableName)
    {
        lock (_colInfoCache)
        {
            if (_colInfoCache.TryGetValue(tableName, out var cached)) return cached;
        }
        var map = new Dictionary<string, ColInfo>(StringComparer.OrdinalIgnoreCase);
        using (var con = _db.GetConnection())
        {
            con.Open();
            using var cmd = new SqlCommand("SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @t", con);
            cmd.Parameters.AddWithValue("@t", tableName);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                map[r.GetString(0)] = new ColInfo { Type = r.GetString(1), Nullable = r.GetString(2).Equals("YES", StringComparison.OrdinalIgnoreCase) };
            }
        }
        lock (_colInfoCache) { _colInfoCache[tableName] = map; }
        return map;
    }

    /// <summary>
    /// Safely converts a posted string into the correct CLR type for its column.
    /// Non-numeric text sent to a nullable numeric column becomes NULL instead of
    /// crashing with a SQL conversion error (protects against stale/cached forms).
    /// </summary>
    private static object PrepareValue(ColInfo? info, string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return DBNull.Value;
        if (info == null) return raw!;
        switch (info.Type.ToLowerInvariant())
        {
            case "int":
            case "bigint":
            case "smallint":
            case "tinyint":
                if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)) return l;
                if (info.Nullable) return DBNull.Value;
                throw new InvalidOperationException($"'{raw}' is not a valid whole number.");
            case "decimal":
            case "numeric":
            case "money":
            case "smallmoney":
            case "float":
            case "real":
                if (decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)) return d;
                if (info.Nullable) return DBNull.Value;
                throw new InvalidOperationException($"'{raw}' is not a valid number.");
            case "bit":
                if (raw is "1" or "true" or "True" or "on" or "yes") return true;
                if (raw is "0" or "false" or "False" or "off" or "no") return false;
                return info.Nullable ? DBNull.Value : false;
            case "date":
            case "datetime":
            case "datetime2":
            case "smalldatetime":
                if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)) return dt;
                if (info.Nullable) return DBNull.Value;
                throw new InvalidOperationException($"'{raw}' is not a valid date.");
            default:
                return raw!;
        }
    }

    /// <summary>
    /// AUTO DAILY REPORT ROW:
    /// Jab bhi Leads / ClientVisit / DailySupport me koi entry hoti hai (add ya edit),
    /// us employee + us date ka DailyReport row (agar pehle se nahi hai to) khud-ba-khud ban jaata hai.
    /// Isse Daily Report grid me Calls/Visits/New Leads counts automatically dikhne lagte hain —
    /// kyunki report row ke bina wo counts kahin show nahi hote.
    ///
    ///   Leads        -> AssignedTo + CreatedOn
    ///   ClientVisit  -> UserId     + VisitDate
    ///   DailySupport -> AssignedTo + SupportDate
    ///
    /// Summary / Remark khaali rehte hain (employee baad me bhare). Koi status workflow nahi.
    /// Note: ye automation fail ho to source insert ko rokta nahi (best-effort).
    /// </summary>
    private void EnsureDailyReportRow(SqlConnection con, string tableName, int newId)
    {
        string empCol, dateCol;
        switch (tableName.ToLowerInvariant())
        {
            case "leads":        empCol = "AssignedTo"; dateCol = "CreatedOn";   break;
            case "clientvisit":  empCol = "UserId";     dateCol = "VisitDate";   break;
            case "dailysupport": empCol = "AssignedTo"; dateCol = "SupportDate"; break;
            default: return;   // baaki tables (DailyReport, Client, Users, ...) ke liye kuch nahi
        }

        try
        {
            // Insert/Update ke baad asli row se employee + date padho (DB default create time bhi cover ho jaata hai)
            int? emp = null;
            DateTime? date = null;
            string pk = GetPrimaryKey(tableName);
            using (var read = new SqlCommand($"SELECT [{empCol}], [{dateCol}] FROM [{tableName}] WHERE [{pk}] = @Id", con))
            {
                read.Parameters.AddWithValue("@Id", newId);
                using var r = read.ExecuteReader();
                if (r.Read())
                {
                    if (!r.IsDBNull(0)) emp = Convert.ToInt32(r.GetValue(0));
                    if (!r.IsDBNull(1)) date = Convert.ToDateTime(r.GetValue(1));
                }
            }

            // Employee ya date nahi mile to count attribute nahi kar sakte
            if (emp is null || emp <= 0 || date is null) return;

            // DailyReport row sirf tab banao jab table ho aur us combo ka row pehle se na ho
            using var cmd = new SqlCommand(
                "IF OBJECT_ID('DailyReport','U') IS NOT NULL " +
                "AND NOT EXISTS (SELECT 1 FROM DailyReport WHERE ReportDate = @Date AND AssignedTo = @Emp) " +
                "INSERT INTO DailyReport (ReportDate, AssignedTo) VALUES (@Date, @Emp)", con);
            cmd.Parameters.AddWithValue("@Date", date.Value.Date);
            cmd.Parameters.AddWithValue("@Emp", emp.Value);
            cmd.ExecuteNonQuery();
        }
        catch
        {
            // Best-effort: DailyReport automation fail ho to asli source insert revert na ho
        }
    }

    private static string GetPrimaryKey(string tableName) => tableName switch
    {
        "Client" => "ClientId",
        "ClientVisit" => "VisitId",
        "Leads" => "LeadId",
        "DailyFollowUp" => "DailyFollowUpId",
        "DailyReport" => "DailyReportId",
        "StateMaster" => "StateId",
        "Users" => "UserId",
        "Roles" => "RoleId",
        "DailySupport" => "SupportId",
        _ => "Id"
    };
}