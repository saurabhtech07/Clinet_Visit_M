using Microsoft.Data.SqlClient;
using ClientVisitManagement.Models;

namespace ClientVisitManagement.Data;

/// <summary>
/// Follow-up Center ka data - har query LIVE DB se.
/// GetPage ke queries try/catch me hain: agar FollowUp table abhi tak nahi bani,
/// to sirf khaali list dikhega, page nahi tootega. Actions (Mark Done, Postpone
/// waghera) table na hone pe error denge - taaki user ko pata chale ki DDL chalan hai.
/// </summary>
public class FollowUpRepository
{
    private readonly DBHelper _db;
    public FollowUpRepository(DBHelper db) => _db = db;

    private const string Select =
        "SELECT f.FollowUpId, f.ClientId, f.VisitId, f.UserId, f.SourceType, f.Description, " +
        "f.NextFollowUpDate, f.FollowUpStatus, f.Remarks, f.DoneOn, f.CreatedOn, " +
        "c.ClientName, c.City, ISNULL(NULLIF(u.FullName,''), u.Username) AS AssignedTo " +
        "FROM FollowUp f " +
        "LEFT JOIN Client c ON f.ClientId = c.ClientId " +
        "LEFT JOIN Users u ON c.AssignedTo = u.UserId ";

    // =====================================================================
    //  PAGE DATA
    // =====================================================================

    public FollowUpViewModel GetPage(int page, int pageSize, int? userId, string? search, string? status, string? sort = null, string direction = "DESC")
    {
        // Visit ke pending/overdue follow-ups ko FollowUp table me sync karo
        // (taaki purane visits bhi Follow-up Center me dikhen)
        SyncVisitsToFollowUps();

        if (page < 1) page = 1;
        if (pageSize is not (10 or 20 or 50 or 100)) pageSize = 10;

        var vm = new FollowUpViewModel
        {
            FilterUserId = userId,
            Search = search,
            StatusFilter = status,
            CurrentPage = page,
            PageSize = pageSize,
            SortColumn = sort,
            SortDirection = string.Equals(direction, "ASC", StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC"
        };

        // Summary chips ke counts (userId + search ke hisab se, status-independent)
        var (where, pars) = BuildWhere(userId, search, null);

        vm.Overdue    = Query(Select + where + " AND f.FollowUpStatus='Pending' AND f.NextFollowUpDate IS NOT NULL AND f.NextFollowUpDate < CAST(GETDATE() AS DATE) ORDER BY f.NextFollowUpDate ASC", pars);
        vm.DueToday   = Query(Select + where + " AND f.FollowUpStatus='Pending' AND f.NextFollowUpDate = CAST(GETDATE() AS DATE) ORDER BY f.FollowUpId DESC", pars);
        vm.Upcoming   = Query(Select + where + " AND f.FollowUpStatus='Pending' AND f.NextFollowUpDate IS NOT NULL AND f.NextFollowUpDate > CAST(GETDATE() AS DATE) ORDER BY f.NextFollowUpDate ASC", pars);
        vm.NoSchedule = Query(Select + where + " AND f.FollowUpStatus='Pending' AND f.NextFollowUpDate IS NULL ORDER BY f.FollowUpId DESC", pars);
        vm.Done       = Query(Select + where + " AND f.FollowUpStatus='Done' ORDER BY ISNULL(f.DoneOn, f.CreatedOn) DESC", pars);

        // Grid page (status filter SQL me hi lagta hai taaki pagination sahi rahe)
        var (gridWhere, gridPars) = BuildWhere(userId, search, status);
        vm.TotalRecords = CountRows(gridWhere, gridPars);
        vm.TotalPages = vm.TotalRecords == 0 ? 0 : (int)Math.Ceiling(vm.TotalRecords / (double)pageSize);

        var order = BuildOrder(sort, vm.SortDirection!);
        var pagePars = new Dictionary<string, object?>(gridPars)
        {
            ["@Skip"] = (page - 1) * pageSize,
            ["@Take"] = pageSize
        };
        vm.All = Query(Select + gridWhere + order + " OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY", pagePars);
        return vm;
    }

    /// <summary>ClientVisit ke Pending/Overdue follow-ups ko FollowUp table me laata hai + status match karta hai.</summary>
    public void SyncVisitsToFollowUps()
    {
        try
        {
            using var con = _db.GetConnection();
            con.Open();

            // 1) Jo visits FollowUp table me nahi hain, unke liye row banao (date ho ya na ho)
            using (var ins = new SqlCommand(@"
                INSERT INTO FollowUp (ClientId, VisitId, UserId, SourceType, Description, NextFollowUpDate, FollowUpStatus, Remarks, CreatedOn)
                SELECT v.ClientId, v.VisitId, v.UserId, 'Visit', N'Visit follow-up', v.NextFollowUpDate, 'Pending', v.Remarks, GETDATE()
                FROM ClientVisit v
                WHERE ISNULL(v.FollowUpStatus,'') IN ('Pending','Overdue')
                  AND NOT EXISTS (SELECT 1 FROM FollowUp f WHERE f.VisitId = v.VisitId)", con))
                ins.ExecuteNonQuery();

            // 2) Visit-based rows ka status/date ClientVisit se match karao
            using (var upd = new SqlCommand(@"
                UPDATE f
                   SET f.NextFollowUpDate = v.NextFollowUpDate,
                       f.FollowUpStatus   = CASE WHEN ISNULL(v.FollowUpStatus,'') IN ('Done','Completed','Closed') THEN 'Done' ELSE 'Pending' END,
                       f.DoneOn           = CASE WHEN ISNULL(v.FollowUpStatus,'') IN ('Done','Completed','Closed') THEN GETDATE() ELSE NULL END
                FROM FollowUp f
                JOIN ClientVisit v ON f.VisitId = v.VisitId
                WHERE f.SourceType = 'Visit'
                  AND (ISNULL(f.FollowUpStatus,'') <> CASE WHEN ISNULL(v.FollowUpStatus,'') IN ('Done','Completed','Closed') THEN 'Done' ELSE 'Pending' END
                       OR ISNULL(f.NextFollowUpDate,'1900-01-01') <> ISNULL(v.NextFollowUpDate,'1900-01-01'))", con))
                upd.ExecuteNonQuery();
        }
        catch { /* table na ho to chup-chaap chhodo */ }
    }

    /// <summary>Topbar badge ke liye - aur kuch nahi.</summary>
    public (int Overdue, int Today) GetBadgeCounts()
    {
        try
        {
            using var con = _db.GetConnection();
            con.Open();
            using var cmd = new SqlCommand(@"
                SELECT
                  (SELECT COUNT(*) FROM FollowUp
                     WHERE FollowUpStatus='Pending' AND NextFollowUpDate IS NOT NULL
                       AND NextFollowUpDate < CAST(GETDATE() AS DATE)),
                  (SELECT COUNT(*) FROM FollowUp
                     WHERE FollowUpStatus='Pending' AND NextFollowUpDate = CAST(GETDATE() AS DATE))", con);
            using var r = cmd.ExecuteReader();
            if (r.Read()) return (r.GetInt32(0), r.GetInt32(1));
            return (0, 0);
        }
        catch { return (0, 0); }
    }

    // =====================================================================
    //  ACTIONS
    // =====================================================================

    public void MarkDone(int id) => Exec(
        @"UPDATE FollowUp SET FollowUpStatus='Done', DoneOn=GETDATE() WHERE FollowUpId=@Id;
          UPDATE ClientVisit SET FollowUpStatus='Done'
            WHERE VisitId = (SELECT VisitId FROM FollowUp WHERE FollowUpId=@Id);",
        ("@Id", (object)id));

    public void Postpone(int id, DateTime nextDate) => Exec(
        @"UPDATE FollowUp SET NextFollowUpDate=@D, FollowUpStatus='Pending', DoneOn=NULL WHERE FollowUpId=@Id;
          UPDATE ClientVisit SET NextFollowUpDate=@D, FollowUpStatus='Pending'
            WHERE VisitId = (SELECT VisitId FROM FollowUp WHERE FollowUpId=@Id);",
        ("@Id", (object)id), ("@D", nextDate));

    public void Delete(int id) => Exec(
        "DELETE FROM FollowUp WHERE FollowUpId=@Id",
        ("@Id", (object)id));

    /// <summary>Manual nahi, balki manual follow-up add (Add button se).</summary>
    public void Add(int clientId, int? userId, DateTime? nextDate, string? description, string? remarks, int createdBy)
    {
        using var con = _db.GetConnection();
        using var cmd = new SqlCommand(
            @"INSERT INTO FollowUp (ClientId, VisitId, UserId, SourceType, Description, NextFollowUpDate, FollowUpStatus, Remarks, CreatedBy, CreatedOn)
              VALUES (@Client, NULL, @User, 'Manual', @Desc, @Date, 'Pending', @Remarks, @CreatedBy, GETDATE())", con);
        cmd.Parameters.AddWithValue("@Client", clientId);
        cmd.Parameters.AddWithValue("@User", (object?)userId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Desc", (object?)description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Date", (object?)nextDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Remarks", (object?)remarks ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@CreatedBy", createdBy);
        con.Open();
        cmd.ExecuteNonQuery();
    }

    public void Update(int id, DateTime? nextDate, int? userId, string? description, string? remarks)
    {
        using var con = _db.GetConnection();
        using var cmd = new SqlCommand(
            @"UPDATE FollowUp SET NextFollowUpDate=@Date, UserId=@User, Description=@Desc, Remarks=@Remarks WHERE FollowUpId=@Id;
              UPDATE ClientVisit SET NextFollowUpDate=@Date, FollowUpStatus='Pending'
                WHERE VisitId = (SELECT VisitId FROM FollowUp WHERE FollowUpId=@Id);", con);
        cmd.Parameters.AddWithValue("@Date", (object?)nextDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@User", (object?)userId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Desc", (object?)description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Remarks", (object?)remarks ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Id", id);
        con.Open();
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// ClientVisit save hone pe call hota hai - visit ke next follow-up date se
    /// FollowUp row automatically banega/update hoga. (Main isi se visits ka
    /// follow-up bhi is center me aata hai.)
    /// </summary>
    public void SyncFromVisit(int visitId, int clientId, int? userId, DateTime? nextDate, string? status, string? remarks)
    {
        using var con = _db.GetConnection();
        con.Open();

        int? existingId = null;
        using (var sel = new SqlCommand("SELECT FollowUpId FROM FollowUp WHERE VisitId=@V", con))
        {
            sel.Parameters.AddWithValue("@V", visitId);
            var o = sel.ExecuteScalar();
            if (o != null && o != DBNull.Value) existingId = Convert.ToInt32(o);
        }

        bool done = string.Equals(status, "Done", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(status, "Closed", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase);

        if (!existingId.HasValue)
        {
            if (nextDate == null) return; // koi date nahi -> follow-up schedule nahi karna
            using var ins = new SqlCommand(
                @"INSERT INTO FollowUp (ClientId, VisitId, UserId, SourceType, Description, NextFollowUpDate, FollowUpStatus, Remarks, CreatedOn)
                  VALUES (@Client, @V, @User, 'Visit', @Desc, @Date, @Status, @Remarks, GETDATE())", con);
            ins.Parameters.AddWithValue("@Client", clientId);
            ins.Parameters.AddWithValue("@V", visitId);
            ins.Parameters.AddWithValue("@User", (object?)userId ?? DBNull.Value);
            ins.Parameters.AddWithValue("@Desc", "Follow-up – visit ke baad");
            ins.Parameters.AddWithValue("@Date", (object?)nextDate ?? DBNull.Value);
            ins.Parameters.AddWithValue("@Status", done ? "Done" : "Pending");
            ins.Parameters.AddWithValue("@Remarks", (object?)remarks ?? DBNull.Value);
            ins.ExecuteNonQuery();
        }
        else
        {
            string newStatus = (done || nextDate == null) ? "Done" : "Pending";
            using var upd = new SqlCommand(
                @"UPDATE FollowUp SET NextFollowUpDate=@Date, FollowUpStatus=@Status, Remarks=@Remarks,
                       DoneOn = CASE WHEN @Status='Done' THEN GETDATE() ELSE NULL END
                  WHERE FollowUpId=@Id", con);
            upd.Parameters.AddWithValue("@Date", (object?)nextDate ?? DBNull.Value);
            upd.Parameters.AddWithValue("@Status", newStatus);
            upd.Parameters.AddWithValue("@Remarks", (object?)remarks ?? DBNull.Value);
            upd.Parameters.AddWithValue("@Id", existingId.Value);
            upd.ExecuteNonQuery();
        }
    }

    // =====================================================================
    //  HELPERS
    // =====================================================================

    private static (string Where, Dictionary<string, object?> Pars) BuildWhere(int? userId, string? search, string? status)
    {
        var w = " WHERE 1=1";
        var p = new Dictionary<string, object?>();
        if (userId.HasValue) { w += " AND c.AssignedTo = @UserId"; p["@UserId"] = userId.Value; }
        if (!string.IsNullOrWhiteSpace(search))
        {
            w += " AND (c.ClientName LIKE @Search OR ISNULL(f.Description,'') LIKE @Search OR ISNULL(u.FullName,'') LIKE @Search OR u.Username LIKE @Search)";
            p["@Search"] = "%" + search.Trim() + "%";
        }
        switch ((status ?? "").Trim().ToLowerInvariant())
        {
            case "pending":  w += " AND ISNULL(f.FollowUpStatus,'') <> 'Done'"; break;
            case "overdue":  w += " AND ISNULL(f.FollowUpStatus,'') <> 'Done' AND f.NextFollowUpDate IS NOT NULL AND f.NextFollowUpDate < CAST(GETDATE() AS DATE)"; break;
            case "today":    w += " AND ISNULL(f.FollowUpStatus,'') <> 'Done' AND f.NextFollowUpDate = CAST(GETDATE() AS DATE)"; break;
            case "upcoming": w += " AND ISNULL(f.FollowUpStatus,'') <> 'Done' AND f.NextFollowUpDate IS NOT NULL AND f.NextFollowUpDate > CAST(GETDATE() AS DATE)"; break;
            case "nodate":   w += " AND ISNULL(f.FollowUpStatus,'') <> 'Done' AND f.NextFollowUpDate IS NULL"; break;
            case "done":     w += " AND ISNULL(f.FollowUpStatus,'') = 'Done'"; break;
        }
        return (w, p);
    }

    private static string BuildOrder(string? sort, string direction)
    {
        var dir = string.Equals(direction, "ASC", StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC";
        var col = (sort ?? "").Trim();
        switch (col.ToLowerInvariant())
        {
            case "clientname":       return $" ORDER BY c.ClientName {dir}, f.FollowUpId DESC";
            case "nextfollowupdate": return $" ORDER BY CASE WHEN f.NextFollowUpDate IS NULL THEN 1 ELSE 0 END, f.NextFollowUpDate {dir}, f.FollowUpId DESC";
            case "followupstatus":   return $" ORDER BY CASE WHEN ISNULL(f.FollowUpStatus,'')='Done' THEN 1 ELSE 0 END {dir}, f.FollowUpId DESC";
            default:
                // Default: Pending pehle, phir bina date, phir date asc, phir newest
                return " ORDER BY CASE WHEN ISNULL(f.FollowUpStatus,'')='Done' THEN 1 ELSE 0 END, CASE WHEN f.NextFollowUpDate IS NULL THEN 1 ELSE 0 END, f.NextFollowUpDate ASC, f.FollowUpId DESC";
        }
    }

    private int CountRows(string where, Dictionary<string, object?> pars)
    {
        try
        {
            using var con = _db.GetConnection();
            using var cmd = new SqlCommand("SELECT COUNT(*) FROM FollowUp f LEFT JOIN Client c ON f.ClientId = c.ClientId LEFT JOIN Users u ON c.AssignedTo = u.UserId" + where, con);
            foreach (var (k, v) in pars) cmd.Parameters.AddWithValue(k, (object?)v ?? DBNull.Value);
            con.Open();
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
        catch { return 0; }
    }

    private List<FollowUpRow> Query(string sql, Dictionary<string, object?> pars)
    {
        try
        {
            var list = new List<FollowUpRow>();
            using var con = _db.GetConnection();
            using var cmd = new SqlCommand(sql, con);
            foreach (var (k, v) in pars) cmd.Parameters.AddWithValue(k, (object?)v ?? DBNull.Value);
            con.Open();
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) list.Add(Map(reader));
            return list;
        }
        catch { return new List<FollowUpRow>(); }
    }

    private static FollowUpRow Map(SqlDataReader r) => new()
    {
        FollowUpId  = Convert.ToInt32(r["FollowUpId"]),
        ClientId    = Convert.ToInt32(r["ClientId"]),
        ClientCode  = "CL-" + Convert.ToInt32(r["ClientId"]).ToString("D6"),
        ClientName  = r["ClientName"]?.ToString(),
        City        = r["City"]?.ToString(),
        VisitId     = r["VisitId"] == DBNull.Value ? null : Convert.ToInt32(r["VisitId"]),
        UserId      = r["UserId"] == DBNull.Value ? null : Convert.ToInt32(r["UserId"]),
        AssignedTo  = r["AssignedTo"]?.ToString(),
        SourceType  = r["SourceType"]?.ToString(),
        Description = r["Description"]?.ToString(),
        NextFollowUpDate = r["NextFollowUpDate"] == DBNull.Value ? null : Convert.ToDateTime(r["NextFollowUpDate"]),
        FollowUpStatus = r["FollowUpStatus"]?.ToString() ?? "Pending",
        Remarks = r["Remarks"]?.ToString(),
        DoneOn  = r["DoneOn"] == DBNull.Value ? null : Convert.ToDateTime(r["DoneOn"]),
        CreatedOn = r["CreatedOn"] == DBNull.Value ? null : Convert.ToDateTime(r["CreatedOn"])
    };

    private void Exec(string sql, params (string Name, object Value)[] parameters)
    {
        using var con = _db.GetConnection();
        using var cmd = new SqlCommand(sql, con);
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value);
        con.Open();
        cmd.ExecuteNonQuery();
    }
}