using Microsoft.Data.SqlClient;
using ClientVisitManagement.Models;

namespace ClientVisitManagement.Data;

/// <summary>
/// Leads module ka data layer.
///
///  - GetLeads : grid (columns hi DB se aate hain -> naya column add karo, khud dikhega)
///  - GetCards : 4 dashboard cards
///  - ConvertToClient : Lead -> Client, ek transaction me, NOT NULL columns safe.
///
/// Sab kuch dynamic hai: Leads table me naya column add karo, bina code chhue
/// grid + form me aa jaayega.
/// </summary>
public class LeadRepository
{
    private readonly DBHelper _db;
    public LeadRepository(DBHelper db) => _db = db;

    /// <summary>Ye columns grid me nahi dikhate (internal / duplicate of join names).</summary>
    private static readonly HashSet<string> HiddenGridColumns =
        new(StringComparer.OrdinalIgnoreCase)
        { "IsConverted", "ConvertedClientId", "ConvertedOn", "StateId", "AssignedTo" };

    /// <summary>Leads table ke actual columns (INFORMATION_SCHEMA se — live).</summary>
    public List<string> GetColumns()
    {
        var list = new List<string>();
        using var con = _db.GetConnection();
        con.Open();
        using var cmd = new SqlCommand(
            "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Leads' ORDER BY ORDINAL_POSITION", con);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    // =====================================================================
    //  GRID
    // =====================================================================
    public DynamicTableViewModel GetLeads(
        int page, int pageSize, string? search, string? status,
        int? stateId, int? assignedTo, DateTime? fromDate, DateTime? toDate,
        string? sort, string direction)
    {
        if (page < 1) page = 1;

        var vm = new DynamicTableViewModel
        {
            CurrentPage = page,
            PageSize = pageSize,
            SearchTerm = search,
            SortColumn = sort,
            SortDirection = string.Equals(direction, "ASC", StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC"
        };

        using var con = _db.GetConnection();
        con.Open();

        var where = " WHERE 1=1";
        if (!string.IsNullOrWhiteSpace(status)) where += " AND l.Status = @Status";
        if (stateId.HasValue) where += " AND l.StateId = @StateId";
        if (assignedTo.HasValue) where += " AND l.AssignedTo = @AssignedTo";
        if (fromDate.HasValue) where += " AND CAST(l.CreatedOn AS DATE) >= @From";
        if (toDate.HasValue) where += " AND CAST(l.CreatedOn AS DATE) <= @To";

        // Search har column me — columns DB se aate hain, isliye naya column bhi
        // automatically search me shamil ho jaata hai.
        var cols = GetColumns();
        if (!string.IsNullOrWhiteSpace(search) && cols.Count > 0)
        {
            var likes = cols.Select(c => $"CAST(l.[{c}] AS NVARCHAR(MAX)) LIKE @Search");
            where += " AND (" + string.Join(" OR ", likes) + ")";
        }

        // Sort whitelist (SQL injection safe)
        var allowedSort = new HashSet<string>(cols, StringComparer.OrdinalIgnoreCase) { "StateName", "AssignedToName" };
        string orderCol = (!string.IsNullOrWhiteSpace(sort) && allowedSort.Contains(sort)) ? sort : "LeadId";
        string orderExpr =
            orderCol.Equals("StateName", StringComparison.OrdinalIgnoreCase) ? "s.StateName" :
            orderCol.Equals("AssignedToName", StringComparison.OrdinalIgnoreCase) ? "ISNULL(NULLIF(u.FullName,''), u.Username)" :
            "l.[" + orderCol + "]";

        using (var countCmd = new SqlCommand("SELECT COUNT(*) FROM Leads l" + where, con))
        {
            AddFilters(countCmd, search, status, stateId, assignedTo, fromDate, toDate);
            vm.TotalRecords = Convert.ToInt32(countCmd.ExecuteScalar());
        }

        var sql =
            "SELECT l.*, s.StateName, ISNULL(NULLIF(u.FullName,''), u.Username) AS AssignedToName " +
            "FROM Leads l " +
            "LEFT JOIN StateMaster s ON l.StateId = s.StateId " +
            "LEFT JOIN Users u ON l.AssignedTo = u.UserId " +
            where + $" ORDER BY {orderExpr} {vm.SortDirection} OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY";

        using var cmd = new SqlCommand(sql, con);
        AddFilters(cmd, search, status, stateId, assignedTo, fromDate, toDate);
        cmd.Parameters.AddWithValue("@Skip", (vm.CurrentPage - 1) * pageSize);
        cmd.Parameters.AddWithValue("@Take", pageSize);

        using var reader = cmd.ExecuteReader();
        for (int i = 0; i < reader.FieldCount; i++) vm.Columns.Add(reader.GetName(i));
        while (reader.Read())
        {
            var row = new Dictionary<string, object?>();
            for (int i = 0; i < reader.FieldCount; i++)
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            vm.Rows.Add(row);
        }

        // Internal columns grid se chhupa do (naye user columns waise hi rehte hain)
        vm.Columns.RemoveAll(c => HiddenGridColumns.Contains(c));
        return vm;
    }

    private static void AddFilters(SqlCommand cmd, string? search, string? status,
        int? stateId, int? assignedTo, DateTime? fromDate, DateTime? toDate)
    {
        if (!string.IsNullOrWhiteSpace(search)) cmd.Parameters.AddWithValue("@Search", "%" + search.Trim() + "%");
        if (!string.IsNullOrWhiteSpace(status)) cmd.Parameters.AddWithValue("@Status", status);
        if (stateId.HasValue) cmd.Parameters.AddWithValue("@StateId", stateId.Value);
        if (assignedTo.HasValue) cmd.Parameters.AddWithValue("@AssignedTo", assignedTo.Value);
        if (fromDate.HasValue) cmd.Parameters.AddWithValue("@From", fromDate.Value.Date);
        if (toDate.HasValue) cmd.Parameters.AddWithValue("@To", toDate.Value.Date);
    }

    // =====================================================================
    //  CARDS
    // =====================================================================
    public (int Total, int Open, int Converted, decimal DealValue) GetCards()
    {
        using var con = _db.GetConnection();
        con.Open();
        using var cmd = new SqlCommand(@"
            SELECT COUNT(*),
                   ISNULL(SUM(CASE WHEN ISNULL(IsConverted,0)=0 THEN 1 ELSE 0 END),0),
                   ISNULL(SUM(CASE WHEN ISNULL(IsConverted,0)=1 THEN 1 ELSE 0 END),0),
                   ISNULL(SUM(DealValue),0)
            FROM Leads", con);
        using var r = cmd.ExecuteReader();
        if (r.Read())
        {
            return (
                r.IsDBNull(0) ? 0 : r.GetInt32(0),
                r.IsDBNull(1) ? 0 : Convert.ToInt32(r.GetValue(1)),
                r.IsDBNull(2) ? 0 : Convert.ToInt32(r.GetValue(2)),
                r.IsDBNull(3) ? 0m : Convert.ToDecimal(r.GetValue(3)));
        }
        return (0, 0, 0, 0m);
    }

    // =====================================================================
    //  LEAD -> CLIENT CONVERSION
    // =====================================================================
    private sealed record ColDef(string Name, string DataType, bool Nullable, bool IsIdentity);

    /// <summary>
    /// Lead ko Client me convert karta hai — ek transaction me.
    /// Client ke NOT NULL columns (ClientName, Address1, City, Pincode) khud
    /// safe default se bhar dete hain, isliye conversion kabhi NULL par crash
    /// nahi karega. Client ke columns bhi live INFORMATION_SCHEMA se padhte hain.
    /// </summary>
    public (bool Ok, string Message, int ClientId) ConvertToClient(int leadId)
    {
        using var con = _db.GetConnection();
        con.Open();
        using var tx = con.BeginTransaction();

        try
        {
            // 1) Lead load
            var lead = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = new SqlCommand("SELECT * FROM Leads WHERE LeadId=@Id", con, tx))
            {
                cmd.Parameters.AddWithValue("@Id", leadId);
                using var r = cmd.ExecuteReader();
                if (!r.Read()) { tx.Rollback(); return (false, "Lead nahi mila.", 0); }
                for (int i = 0; i < r.FieldCount; i++) lead[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
            }

            if (Convert.ToBoolean(lead.GetValueOrDefault("IsConverted") ?? false))
            { tx.Rollback(); return (false, "Ye lead pehle hi Client me convert ho chuka hai.", 0); }

            string company = (lead.GetValueOrDefault("Company")?.ToString() ?? "").Trim();
            if (company.Length == 0)
            { tx.Rollback(); return (false, "Company name khaali hai — pehle Company bhar do, phir convert karo.", 0); }

            string? mobile = lead.GetValueOrDefault("Mobile")?.ToString()?.Trim();

            // 2) Duplicate check (same mobile ka client already hai?)
            if (!string.IsNullOrWhiteSpace(mobile))
            {
                using var dup = new SqlCommand("SELECT TOP 1 ClientId FROM Client WHERE MobileNo=@m", con, tx);
                dup.Parameters.AddWithValue("@m", mobile);
                var ex = dup.ExecuteScalar();
                if (ex != null && ex != DBNull.Value)
                {
                    tx.Rollback();
                    return (false, $"Is mobile ({mobile}) ka client already exist karta hai (ClientId {ex}).", Convert.ToInt32(ex));
                }
            }

            // 3) Lead -> Client auto-map.
            //    Same naam ka column Lead me ho to khud jaata hai; kuch aliases bhi hain.
            //    Isliye future me Leads me Address1 / Pincode add karo to conversion me
            //    automatically aa jayega — code chhune ki zarurat nahi.
            var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ClientName"] = "Company",
                ["MobileNo"]   = "Mobile",
            };

            // 4) Client ke actual columns se dynamic INSERT
            var clientCols = GetClientColumns(con, tx);
            var insertCols = new List<string>();
            var insertParams = new List<string>();
            using var ins = new SqlCommand { Connection = con, Transaction = tx };
            int p = 0;
            foreach (var col in clientCols)
            {
                if (col.IsIdentity) continue;

                string srcName = aliases.TryGetValue(col.Name, out var al) ? al : col.Name;
                object? value;

                if (col.Name.Equals("CreatedOn", StringComparison.OrdinalIgnoreCase) && !lead.ContainsKey("CreatedOn"))
                    value = DateTime.Now;
                else if (lead.TryGetValue(srcName, out var lv) && lv != null && !(lv is string sv && sv.Length == 0))
                    value = lv;
                else if (!col.Nullable)
                    value = DefaultFor(col.DataType);   // NOT NULL -> safe default ('' / 0 / GETDATE)
                else
                    value = DBNull.Value;

                string pn = "@p" + (p++);
                insertCols.Add("[" + col.Name + "]");
                insertParams.Add(pn);
                ins.Parameters.AddWithValue(pn, value ?? DBNull.Value);
            }

            ins.CommandText =
                $"INSERT INTO Client ({string.Join(",", insertCols)}) " +
                $"OUTPUT INSERTED.ClientId VALUES ({string.Join(",", insertParams)})";
            int newId = Convert.ToInt32(ins.ExecuteScalar());

            // 5) Lead ko converted mark karo
            using (var upd = new SqlCommand(
                "UPDATE Leads SET IsConverted=1, ConvertedClientId=@cid, ConvertedOn=GETDATE() WHERE LeadId=@id", con, tx))
            {
                upd.Parameters.AddWithValue("@cid", newId);
                upd.Parameters.AddWithValue("@id", leadId);
                upd.ExecuteNonQuery();
            }

            tx.Commit();
            return (true, $"Lead successfully convert ho gaya — Client #{newId}.", newId);
        }
        catch (Exception ex)
        {
            try { tx.Rollback(); } catch { /* ignore */ }
            return (false, "Conversion fail: " + ex.Message, 0);
        }
    }

    private static List<ColDef> GetClientColumns(SqlConnection con, SqlTransaction tx)
    {
        var list = new List<ColDef>();
        using var cmd = new SqlCommand(@"
            SELECT c.COLUMN_NAME, c.DATA_TYPE, c.IS_NULLABLE,
                   ISNULL(COLUMNPROPERTY(OBJECT_ID('dbo.Client'), c.COLUMN_NAME, 'IsIdentity'), 0) AS IsIdentity
            FROM INFORMATION_SCHEMA.COLUMNS c
            WHERE c.TABLE_NAME='Client'
            ORDER BY c.ORDINAL_POSITION", con, tx);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new ColDef(
                r.GetString(0),
                r.GetString(1),
                r.GetString(2).Equals("YES", StringComparison.OrdinalIgnoreCase),
                Convert.ToInt32(r.GetValue(3)) == 1));
        return list;
    }

    private static object DefaultFor(string type) => type.ToLowerInvariant() switch
    {
        "int" or "bigint" or "smallint" or "tinyint" or "decimal" or "numeric"
            or "money" or "smallmoney" or "float" or "real" => 0,
        "bit" => false,
        "date" or "datetime" or "datetime2" or "smalldatetime" or "datetimeoffset" or "time" => DateTime.Now,
        _ => ""
    };
}
