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
        { "IsConverted", "ConvertedClientId", "ConvertedOn", "StateId", "AssignedTo", "ClientId", "LastContactedOn" };

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
        int page, int pageSize, string? search, string? status, string? priority, string? source,
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

        bool hasActivity = TableExists(con, "LeadActivity");

        var where = " WHERE 1=1";
        // Converted (client ban chuka) lead grid/export me na aaye
        where += " AND ISNULL(l.IsConverted,0) = 0";
        if (!string.IsNullOrWhiteSpace(status)) where += " AND l.Status = @Status";
        if (!string.IsNullOrWhiteSpace(priority)) where += " AND ISNULL(l.Priority,'') = @Priority";
        if (!string.IsNullOrWhiteSpace(source)) where += " AND ISNULL(l.LeadSource,'') = @Source";
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
        var allowedSort = new HashSet<string>(cols, StringComparer.OrdinalIgnoreCase)
            { "StateName", "AssignedToName", "ActivityCount" };
        string orderCol = (!string.IsNullOrWhiteSpace(sort) && allowedSort.Contains(sort)) ? sort : "LeadId";
        string orderExpr =
            orderCol.Equals("StateName", StringComparison.OrdinalIgnoreCase) ? "s.StateName" :
            orderCol.Equals("AssignedToName", StringComparison.OrdinalIgnoreCase) ? "ISNULL(NULLIF(u.FullName,''), u.Username)" :
            orderCol.Equals("ActivityCount", StringComparison.OrdinalIgnoreCase) ? (hasActivity ? "(SELECT COUNT(*) FROM LeadActivity a WHERE a.LeadId = l.LeadId)" : "0") :
            "l.[" + orderCol + "]";

        using (var countCmd = new SqlCommand("SELECT COUNT(*) FROM Leads l" + where, con))
        {
            AddFilters(countCmd, search, status, priority, source, stateId, assignedTo, fromDate, toDate);
            vm.TotalRecords = Convert.ToInt32(countCmd.ExecuteScalar());
        }

        string activityExpr = hasActivity
            ? "(SELECT COUNT(*) FROM LeadActivity a WHERE a.LeadId = l.LeadId) AS ActivityCount"
            : "CAST(0 AS INT) AS ActivityCount";

        var sql =
            "SELECT l.*, s.StateName, ISNULL(NULLIF(u.FullName,''), u.Username) AS AssignedToName, " +
            activityExpr + " " +
            "FROM Leads l " +
            "LEFT JOIN StateMaster s ON l.StateId = s.StateId " +
            "LEFT JOIN Users u ON l.AssignedTo = u.UserId " +
            where + $" ORDER BY {orderExpr} {vm.SortDirection} OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY";

        using var cmd = new SqlCommand(sql, con);
        AddFilters(cmd, search, status, priority, source, stateId, assignedTo, fromDate, toDate);
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
        OrderGridColumns(vm.Columns);
        return vm;
    }

    /// <summary>Grid columns ko samajh-daar order me lagao (naye user columns end me safe).</summary>
    private static void OrderGridColumns(List<string> cols)
    {
        var order = new[]
        {
            "LeadId", "LeadPerson", "Company", "Mobile", "Email", "City", "StateName", "Status",
            "Priority", "LeadSource", "DealValue", "NextFollowUpDate", "LastContactedOn",
            "ActivityCount", "AssignedToName", "CreatedOn", "EntryTime", "Requirement"
        };
        var desired = order.Where(o => cols.Contains(o, StringComparer.OrdinalIgnoreCase)).ToList();
        var rest = cols.Where(c => !desired.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList();
        cols.Clear();
        cols.AddRange(desired);
        cols.AddRange(rest);
    }

    private static void AddFilters(SqlCommand cmd, string? search, string? status, string? priority, string? source,
        int? stateId, int? assignedTo, DateTime? fromDate, DateTime? toDate)
    {
        if (!string.IsNullOrWhiteSpace(search)) cmd.Parameters.AddWithValue("@Search", "%" + search.Trim() + "%");
        if (!string.IsNullOrWhiteSpace(status)) cmd.Parameters.AddWithValue("@Status", status);
        if (!string.IsNullOrWhiteSpace(priority)) cmd.Parameters.AddWithValue("@Priority", priority);
        if (!string.IsNullOrWhiteSpace(source)) cmd.Parameters.AddWithValue("@Source", source);
        if (stateId.HasValue) cmd.Parameters.AddWithValue("@StateId", stateId.Value);
        if (assignedTo.HasValue) cmd.Parameters.AddWithValue("@AssignedTo", assignedTo.Value);
        if (fromDate.HasValue) cmd.Parameters.AddWithValue("@From", fromDate.Value.Date);
        if (toDate.HasValue) cmd.Parameters.AddWithValue("@To", toDate.Value.Date);
    }

    private static bool TableExists(SqlConnection con, string table)
    {
        using var cmd = new SqlCommand("SELECT CASE WHEN OBJECT_ID(@t,'U') IS NULL THEN 0 ELSE 1 END", con);
        cmd.Parameters.AddWithValue("@t", "dbo." + table);
        return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
    }

    private static bool ColumnExists(SqlConnection con, string table, string column)
    {
        using var cmd = new SqlCommand("SELECT CASE WHEN COL_LENGTH(@t,@c) IS NULL THEN 0 ELSE 1 END", con);
        cmd.Parameters.AddWithValue("@t", "dbo." + table);
        cmd.Parameters.AddWithValue("@c", column);
        return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
    }

    // =====================================================================
    //  CARDS
    // =====================================================================
    public (int Total, int Open, int Converted, decimal DealValue, int DueToday, int Overdue) GetCards()
    {
        using var con = _db.GetConnection();
        con.Open();
        bool hasNext = ColumnExists(con, "Leads", "NextFollowUpDate");
        string dueToday = hasNext
            ? "ISNULL(SUM(CASE WHEN ISNULL(IsConverted,0)=0 AND NextFollowUpDate = CAST(GETDATE() AS DATE) THEN 1 ELSE 0 END),0)"
            : "0";
        string overdue = hasNext
            ? "ISNULL(SUM(CASE WHEN ISNULL(IsConverted,0)=0 AND NextFollowUpDate IS NOT NULL AND NextFollowUpDate < CAST(GETDATE() AS DATE) THEN 1 ELSE 0 END),0)"
            : "0";
        using var cmd = new SqlCommand($@"
            SELECT ISNULL(SUM(CASE WHEN ISNULL(IsConverted,0)=0 THEN 1 ELSE 0 END),0),
                   ISNULL(SUM(CASE WHEN ISNULL(IsConverted,0)=0 THEN 1 ELSE 0 END),0),
                   ISNULL(SUM(CASE WHEN ISNULL(IsConverted,0)=1 THEN 1 ELSE 0 END),0),
                   ISNULL(SUM(CASE WHEN ISNULL(IsConverted,0)=0 THEN DealValue ELSE 0 END),0),
                   {dueToday},
                   {overdue}
            FROM Leads", con);
        using var r = cmd.ExecuteReader();
        if (r.Read())
        {
            return (
                r.IsDBNull(0) ? 0 : r.GetInt32(0),
                r.IsDBNull(1) ? 0 : Convert.ToInt32(r.GetValue(1)),
                r.IsDBNull(2) ? 0 : Convert.ToInt32(r.GetValue(2)),
                r.IsDBNull(3) ? 0m : Convert.ToDecimal(r.GetValue(3)),
                r.IsDBNull(4) ? 0 : Convert.ToInt32(r.GetValue(4)),
                r.IsDBNull(5) ? 0 : Convert.ToInt32(r.GetValue(5)));
        }
        return (0, 0, 0, 0m, 0, 0);
    }

    // =====================================================================
    //  LEAD DETAIL (360°)  +  ACTIVITY HISTORY
    // =====================================================================
    public Dictionary<string, object?>? GetLeadById(int id)
    {
        using var con = _db.GetConnection();
        con.Open();
        string activityExpr = TableExists(con, "LeadActivity")
            ? "(SELECT COUNT(*) FROM LeadActivity a WHERE a.LeadId = l.LeadId) AS ActivityCount"
            : "CAST(0 AS INT) AS ActivityCount";
        using var cmd = new SqlCommand(
            "SELECT l.*, s.StateName, ISNULL(NULLIF(u.FullName,''), u.Username) AS AssignedToName, " + activityExpr + " " +
            "FROM Leads l LEFT JOIN StateMaster s ON l.StateId = s.StateId " +
            "LEFT JOIN Users u ON l.AssignedTo = u.UserId WHERE l.LeadId = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        var row = new Dictionary<string, object?>();
        for (int i = 0; i < r.FieldCount; i++) row[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
        return row;
    }

    /// <summary>Lead ki activity history (naya pehle) — AssignedName ke saath.</summary>
    public List<Dictionary<string, object?>> GetActivities(int leadId)
    {
        var list = new List<Dictionary<string, object?>>();
        using var con = _db.GetConnection();
        con.Open();
        if (!TableExists(con, "LeadActivity")) return list;
        using var cmd = new SqlCommand(
            "SELECT a.*, ISNULL(NULLIF(u.FullName,''), u.Username) AS AssignedName " +
            "FROM LeadActivity a LEFT JOIN Users u ON a.AssignedTo = u.UserId " +
            "WHERE a.LeadId = @Id ORDER BY a.ActivityDate DESC, a.ActivityId DESC", con);
        cmd.Parameters.AddWithValue("@Id", leadId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var row = new Dictionary<string, object?>();
            for (int i = 0; i < r.FieldCount; i++) row[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
            list.Add(row);
        }
        return list;
    }

    /// <summary>Lead ke DailyFollowUp rows (naya pehle).</summary>
    public List<Dictionary<string, object?>> GetFollowUps(int leadId)
    {
        var list = new List<Dictionary<string, object?>>();
        using var con = _db.GetConnection();
        con.Open();
        if (!TableExists(con, "DailyFollowUp")) return list;
        using var cmd = new SqlCommand(
            "SELECT d.*, ISNULL(NULLIF(u.FullName,''), u.Username) AS AssignedName " +
            "FROM DailyFollowUp d LEFT JOIN Users u ON d.AssignedTo = u.UserId " +
            "WHERE d.LeadId = @Id ORDER BY d.FollowUpDate DESC, d.DailyFollowUpId DESC", con);
        cmd.Parameters.AddWithValue("@Id", leadId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var row = new Dictionary<string, object?>();
            for (int i = 0; i < r.FieldCount; i++) row[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
            list.Add(row);
        }
        return list;
    }

    /// <summary>
    /// Activity add karo + Lead par LastContactedOn / NextFollowUpDate update karo (ek transaction me).
    /// </summary>
    public int AddActivity(int leadId, string? type, DateTime date, string? summary, string? outcome,
        DateTime? nextFollowUp, int? assignedTo, int createdBy)
    {
        using var con = _db.GetConnection();
        con.Open();
        using var tx = con.BeginTransaction();
        try
        {
            int newId;
            using (var cmd = new SqlCommand(
                "INSERT INTO LeadActivity (LeadId, ActivityType, ActivityDate, Summary, Outcome, NextFollowUpDate, AssignedTo, CreatedBy) " +
                "OUTPUT INSERTED.ActivityId VALUES (@LeadId, @Type, @Date, @Summary, @Outcome, @Next, @Assigned, @Created)", con, tx))
            {
                cmd.Parameters.AddWithValue("@LeadId", leadId);
                cmd.Parameters.AddWithValue("@Type", (object?)type ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Date", date);
                cmd.Parameters.AddWithValue("@Summary", (object?)summary ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Outcome", (object?)outcome ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Next", (object?)nextFollowUp ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Assigned", (object?)assignedTo ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Created", createdBy > 0 ? createdBy : (object)DBNull.Value);
                newId = Convert.ToInt32(cmd.ExecuteScalar());
            }

            var sets = new List<string> { "LastContactedOn = @lc" };
            if (nextFollowUp.HasValue) sets.Add("NextFollowUpDate = @nf");
            using (var upd = new SqlCommand($"UPDATE Leads SET {string.Join(", ", sets)} WHERE LeadId = @id", con, tx))
            {
                upd.Parameters.AddWithValue("@lc", date);
                if (nextFollowUp.HasValue) upd.Parameters.AddWithValue("@nf", nextFollowUp.Value.Date);
                upd.Parameters.AddWithValue("@id", leadId);
                upd.ExecuteNonQuery();
            }

            tx.Commit();
            return newId;
        }
        catch
        {
            try { tx.Rollback(); } catch { /* ignore */ }
            throw;
        }
    }

    public void DeleteActivity(int activityId)
    {
        using var con = _db.GetConnection();
        con.Open();
        using var cmd = new SqlCommand("DELETE FROM LeadActivity WHERE ActivityId = @Id", con);
        cmd.Parameters.AddWithValue("@Id", activityId);
        cmd.ExecuteNonQuery();
    }

    // =====================================================================
    //  PIPELINE (Kanban)  +  quick status change
    // =====================================================================
    public List<Dictionary<string, object?>> GetPipeline()
    {
        var list = new List<Dictionary<string, object?>>();
        using var con = _db.GetConnection();
        con.Open();
        try
        {
            using var cmd = new SqlCommand(
                "SELECT l.LeadId, l.LeadPerson, l.Company, l.Mobile, l.Priority, l.Status, l.LeadSource, " +
                "       l.DealValue, l.NextFollowUpDate, l.LastContactedOn, ISNULL(l.IsConverted,0) AS IsConverted, " +
                "       ISNULL(NULLIF(u.FullName,''), u.Username) AS AssignedToName " +
                "FROM Leads l LEFT JOIN Users u ON l.AssignedTo = u.UserId " +
                "ORDER BY CASE WHEN l.Priority='Hot' THEN 0 WHEN l.Priority='Warm' THEN 1 WHEN l.Priority='Cold' THEN 2 ELSE 3 END, " +
                "         l.DealValue DESC, l.LeadId DESC", con);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var row = new Dictionary<string, object?>();
                for (int i = 0; i < r.FieldCount; i++) row[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
                list.Add(row);
            }
        }
        catch
        {
            // column/table na ho to khaali board (page safe)
        }
        return list;
    }

    public void UpdateStatus(int leadId, string status)
    {
        using var con = _db.GetConnection();
        con.Open();
        using var cmd = new SqlCommand("UPDATE Leads SET Status = @Status WHERE LeadId = @Id", con);
        cmd.Parameters.AddWithValue("@Status", status);
        cmd.Parameters.AddWithValue("@Id", leadId);
        cmd.ExecuteNonQuery();
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

            // 2) Company Master me pehle se hai? (ClientId link ya naam se match)
            //    Duplicate company na bane — existing client se hi lead link ho jaye.
            int? existingClientId = null;
            if (lead.TryGetValue("ClientId", out var lcid) && lcid != null && lcid != DBNull.Value)
            {
                int cid = Convert.ToInt32(lcid);
                using var chk = new SqlCommand("SELECT COUNT(*) FROM Client WHERE ClientId=@id", con, tx);
                chk.Parameters.AddWithValue("@id", cid);
                if (Convert.ToInt32(chk.ExecuteScalar()) > 0) existingClientId = cid;
            }
            if (existingClientId == null)
            {
                using var find = new SqlCommand(
                    "SELECT TOP 1 ClientId FROM Client WHERE UPPER(LTRIM(RTRIM(ClientName))) = UPPER(LTRIM(RTRIM(@n)))", con, tx);
                find.Parameters.AddWithValue("@n", company);
                var found = find.ExecuteScalar();
                if (found != null && found != DBNull.Value) existingClientId = Convert.ToInt32(found);
            }

            if (existingClientId.HasValue)
            {
                using (var link = new SqlCommand(
                    "UPDATE Leads SET IsConverted=1, ConvertedClientId=@cid, ConvertedOn=GETDATE() WHERE LeadId=@id", con, tx))
                {
                    link.Parameters.AddWithValue("@cid", existingClientId.Value);
                    link.Parameters.AddWithValue("@id", leadId);
                    link.ExecuteNonQuery();
                }
                tx.Commit();
                return (true, $"Ye company Company Master me already thi — lead Client #{existingClientId.Value} se link ho gaya.", existingClientId.Value);
            }

            // 3) Duplicate check (same mobile ka client already hai?)
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
