using Microsoft.Data.SqlClient;
using ClientVisitManagement.Models;

namespace ClientVisitManagement.Data;

/// <summary>
/// Daily Report (daily lead activity) module ka data layer.
///
///  - GetPage : grid + 4 cards (sab queries LIVE DB se)
///
/// NOTE: Table na ho (DDL na chali ho) to grid/cards khaali dikhte hain,
/// page toot-ta nahi. Insert/Update/Delete DynamicRepository ke through hote hain.
/// </summary>
public class DailyReportRepository
{
    private readonly DBHelper _db;
    public DailyReportRepository(DBHelper db) => _db = db;

    /// <summary>Ye columns grid me nahi dikhate (internal / join aliases / removed). Status: module se hata diya gaya hai.</summary>
    private static readonly HashSet<string> HiddenGridColumns =
        new(StringComparer.OrdinalIgnoreCase) { "AssignedTo", "UserId", "CreatedBy", "ModifiedBy", "Status", "Calls", "Visit", "Summary", "Remark", "InterestedLeads" };

    private const string FromJoin =
        " FROM DailyReport r LEFT JOIN Users u ON r.AssignedTo = u.UserId ";

    // =====================================================================
    //  PAGE DATA
    // =====================================================================
    public DailyReportViewModel GetPage(int page, int pageSize, string? search,
        int? assignedTo, DateTime? fromDate, DateTime? toDate, string? sort, string direction)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 10;   // Index allowed sizes clamp karta hai; Export all-rows (int.MaxValue) bhejta hai

        var vm = new DailyReportViewModel
        {
            FilterAssignedTo = assignedTo,
            FilterFrom = fromDate,
            FilterTo = toDate
        };

        var grid = new DynamicTableViewModel
        {
            CurrentPage = page,
            PageSize = pageSize,
            SearchTerm = search,
            SortColumn = sort,
            SortDirection = string.Equals(direction, "ASC", StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC"
        };

        try
        {
            using var con = _db.GetConnection();
            con.Open();

            HasFollowUp = TableExists(con, "DailyFollowUp");

            // ---- CARDS (sab overall, filter se independent) ----
            // Total Leads       -> saare leads (all-time)  [Total Records]
            // Total Leads Today -> aaj banaye leads
            // Total Follow Up   -> aaj ke follow ups
            string cardSql =
                "SELECT (SELECT COUNT(*) FROM Leads)," +
                " (SELECT COUNT(*) FROM Leads l WHERE CAST(l.CreatedOn AS DATE) = CAST(GETDATE() AS DATE))," +
                " " + (HasFollowUp
                    ? "(SELECT COUNT(*) FROM DailyFollowUp f WHERE f.FollowUpDate = CAST(GETDATE() AS DATE))"
                    : "0");

            using (var cardCmd = new SqlCommand(cardSql, con))
            {
                using var r = cardCmd.ExecuteReader();
                if (r.Read())
                {
                    vm.TotalLeads      = r.IsDBNull(0) ? 0 : Convert.ToInt32(r.GetValue(0));
                    vm.TotalLeadsToday = r.IsDBNull(1) ? 0 : Convert.ToInt32(r.GetValue(1));
                    vm.TotalFollowUps  = r.IsDBNull(2) ? 0 : Convert.ToInt32(r.GetValue(2));
                }
            }

            // ---- SUMMARY GRID (user-wise) ----
            // Ek row = ek SP user, SIRF jisne us din kuch kaam kiya (kuch na kiya ho to row hi nahi).
            // Columns: Date, Assigned To, New Leads, Total Follow Up, Converted Leads.
            DateTime dayFrom = (fromDate ?? DateTime.Today).Date;
            DateTime dayTo = (toDate ?? dayFrom).Date;
            if (dayTo < dayFrom) dayTo = dayFrom;
            vm.SummaryDay = dayFrom;
            vm.IsUserSummary = true;

            // Sirf wahi users jinke paas us din kam se kam ek activity ho (lead / follow-up / converted).
            string activityFilter =
                " AND (" +
                "EXISTS (SELECT 1 FROM Leads l WHERE l.AssignedTo = u.UserId AND CAST(l.CreatedOn AS DATE) BETWEEN @DayFrom AND @DayTo)" +
                " OR EXISTS (SELECT 1 FROM Leads l WHERE l.AssignedTo = u.UserId AND ISNULL(l.IsConverted,0) = 1 AND CAST(l.ConvertedOn AS DATE) BETWEEN @DayFrom AND @DayTo)";
            if (HasFollowUp)
                activityFilter += " OR EXISTS (SELECT 1 FROM DailyFollowUp f WHERE f.AssignedTo = u.UserId AND f.FollowUpDate BETWEEN @DayFrom AND @DayTo)";
            activityFilter += ")";

            string sumWhere = " WHERE ISNULL(u.IsActive,1) = 1";
            if (assignedTo.HasValue) sumWhere += " AND u.UserId = @AssignedTo";
            if (!string.IsNullOrWhiteSpace(search))
                sumWhere += " AND ISNULL(NULLIF(u.FullName,''), u.Username) LIKE @Search";
            sumWhere += activityFilter;

            using (var countCmd = new SqlCommand("SELECT COUNT(*) FROM Users u" + sumWhere, con))
            {
                countCmd.Parameters.AddWithValue("@DayFrom", dayFrom);
                countCmd.Parameters.AddWithValue("@DayTo", dayTo);
                if (assignedTo.HasValue) countCmd.Parameters.AddWithValue("@AssignedTo", assignedTo.Value);
                if (!string.IsNullOrWhiteSpace(search)) countCmd.Parameters.AddWithValue("@Search", "%" + search.Trim() + "%");
                grid.TotalRecords = Convert.ToInt32(countCmd.ExecuteScalar());
            }

            string followUpSum = HasFollowUp
                ? "(SELECT COUNT(*) FROM DailyFollowUp f WHERE f.AssignedTo = u.UserId AND f.FollowUpDate BETWEEN @DayFrom AND @DayTo)"
                : "0";

            string order = BuildSummaryOrder(sort, grid.SortDirection!);
            string sumSql =
                "SELECT u.UserId AS [UserId], " +
                "@DayFrom AS [ReportDate], " +
                "ISNULL(NULLIF(u.FullName,''), u.Username) AS [AssignedName], " +
                "(SELECT COUNT(*) FROM Leads l WHERE l.AssignedTo = u.UserId AND CAST(l.CreatedOn AS DATE) BETWEEN @DayFrom AND @DayTo) AS [NewLeads], " +
                followUpSum + " AS [TotalFollowUp], " +
                "(SELECT COUNT(*) FROM Leads l WHERE l.AssignedTo = u.UserId AND ISNULL(l.IsConverted,0) = 1 AND CAST(l.ConvertedOn AS DATE) BETWEEN @DayFrom AND @DayTo) AS [ConvertedLeads] " +
                "FROM Users u" + sumWhere + order + " OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY";

            using (var cmd = new SqlCommand(sumSql, con))
            {
                cmd.Parameters.AddWithValue("@DayFrom", dayFrom);
                cmd.Parameters.AddWithValue("@DayTo", dayTo);
                if (assignedTo.HasValue) cmd.Parameters.AddWithValue("@AssignedTo", assignedTo.Value);
                if (!string.IsNullOrWhiteSpace(search)) cmd.Parameters.AddWithValue("@Search", "%" + search.Trim() + "%");
                cmd.Parameters.AddWithValue("@Skip", (page - 1) * pageSize);
                cmd.Parameters.AddWithValue("@Take", pageSize);

                using var reader = cmd.ExecuteReader();
                for (int i = 0; i < reader.FieldCount; i++) grid.Columns.Add(reader.GetName(i));
                while (reader.Read())
                {
                    var row = new Dictionary<string, object?>();
                    for (int i = 0; i < reader.FieldCount; i++)
                        row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    grid.Rows.Add(row);
                }
            }
        }
        catch
        {
            // table na ho / koi DB issue -> khaali grid (page safe)
        }

        grid.Columns.RemoveAll(c => HiddenGridColumns.Contains(c));

        // Assigned To ko Date ke turant baad dikhao (matching column order)
        var assignedIdx = grid.Columns.FindIndex(c => c.Equals("AssignedName", StringComparison.OrdinalIgnoreCase));
        if (assignedIdx >= 0)
        {
            grid.Columns.RemoveAt(assignedIdx);
            var dateIdx = grid.Columns.FindIndex(c => c.Equals("ReportDate", StringComparison.OrdinalIgnoreCase));
            grid.Columns.Insert(dateIdx >= 0 ? dateIdx + 1 : Math.Min(1, grid.Columns.Count), "AssignedName");
        }

        // Naye count columns ko NewLeads ke turant baad dikhao
        var newCounts = new[] { "TotalFollowUp", "InterestedLeads", "ConvertedLeads" };
        var anchorIdx = grid.Columns.FindIndex(c => c.Equals("NewLeads", StringComparison.OrdinalIgnoreCase));
        if (anchorIdx >= 0)
        {
            int insertAt = anchorIdx + 1;
            foreach (var nc in newCounts)
            {
                var idx = grid.Columns.FindIndex(c => c.Equals(nc, StringComparison.OrdinalIgnoreCase));
                if (idx < 0) continue;
                grid.Columns.RemoveAt(idx);
                if (idx < insertAt) insertAt--;
                grid.Columns.Insert(insertAt, nc);
                insertAt++;
            }
        }

        vm.Grid = grid;
        return vm;
    }

    // =====================================================================
    //  HELPERS
    // =====================================================================
    /// <summary>
    /// Company standard: ek employee = ek din = ek report.
    /// Duplicate mile to user ko friendly error, null = koi duplicate nahi.
    /// </summary>
    public string? FindDuplicateError(DateTime date, int assignedTo, int? excludeReportId)
    {
        using var con = _db.GetConnection();
        con.Open();
        var sql = "SELECT COUNT(*) FROM DailyReport WHERE ReportDate = @Date AND AssignedTo = @AssignedTo";
        if (excludeReportId.HasValue) sql += " AND DailyReportId <> @ExcludeId";
        using var cmd = new SqlCommand(sql, con);
        cmd.Parameters.AddWithValue("@Date", date.Date);
        cmd.Parameters.AddWithValue("@AssignedTo", assignedTo);
        if (excludeReportId.HasValue) cmd.Parameters.AddWithValue("@ExcludeId", excludeReportId.Value);
        if (Convert.ToInt32(cmd.ExecuteScalar()) > 0)
            return "This employee already has a report for " + date.ToString("dd-MM-yyyy") +
                   " — edit that report (or delete the duplicate) instead of creating another.";
        return null;
    }

    private static string BuildWhere(string? search, int? assignedTo,
        DateTime? fromDate, DateTime? toDate)
    {
        var w = " WHERE 1=1";
        if (assignedTo.HasValue) w += " AND r.AssignedTo = @AssignedTo";
        if (fromDate.HasValue) w += " AND CAST(r.ReportDate AS DATE) >= @From";
        if (toDate.HasValue) w += " AND CAST(r.ReportDate AS DATE) <= @To";
        if (!string.IsNullOrWhiteSpace(search))
            w += " AND (ISNULL(r.Summary,'') LIKE @Search OR ISNULL(r.Remark,'') LIKE @Search" +
                 " OR ISNULL(u.FullName,'') LIKE @Search OR ISNULL(u.Username,'') LIKE @Search)";
        return w;
    }

    private static void AddParams(SqlCommand cmd, string? search, int? assignedTo,
        DateTime? fromDate, DateTime? toDate)
    {
        if (!string.IsNullOrWhiteSpace(search)) cmd.Parameters.AddWithValue("@Search", "%" + search.Trim() + "%");
        if (assignedTo.HasValue) cmd.Parameters.AddWithValue("@AssignedTo", assignedTo.Value);
        if (fromDate.HasValue) cmd.Parameters.AddWithValue("@From", fromDate.Value.Date);
        if (toDate.HasValue) cmd.Parameters.AddWithValue("@To", toDate.Value.Date);
    }

    private static string BuildSourceWhere(string userCol, string dateCol, int? assignedTo,
        DateTime? fromDate, DateTime? toDate)
    {
        var w = " WHERE 1=1";
        if (assignedTo.HasValue) w += $" AND {userCol} = @AssignedTo";
        if (fromDate.HasValue) w += $" AND {dateCol} >= @From";
        if (toDate.HasValue) w += $" AND {dateCol} <= @To";
        return w;
    }

    /// <summary>DailyReport table ke asli columns (order me) — grid select list banane ke liye.</summary>
    private static List<string> GetDailyReportColumns(SqlConnection con)
    {
        var cols = new List<string>();
        using var cmd = new SqlCommand(
            "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS " +
            "WHERE TABLE_NAME = 'DailyReport' ORDER BY ORDINAL_POSITION", con);
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) cols.Add(reader.GetString(0));
        return cols;
    }

    /// <summary>DailyFollowUp table maujood hai ya nahi (nahi to follow-up counts 0).</summary>
    private bool HasFollowUp { get; set; }

    private static bool TableExists(SqlConnection con, string table)
    {
        using var cmd = new SqlCommand("SELECT CASE WHEN OBJECT_ID(@t,'U') IS NULL THEN 0 ELSE 1 END", con);
        cmd.Parameters.AddWithValue("@t", table);
        return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
    }

    // =====================================================================
    //  LIVE COUNT EXPRESSIONS (r = DailyReport row)
    // =====================================================================
    private string ExprCalls =>
        "(SELECT COUNT(*) FROM DailySupport ds WHERE ds.AssignedTo = r.AssignedTo AND CAST(ds.SupportDate AS DATE) = r.ReportDate)";
    private string ExprVisit =>
        "(SELECT COUNT(*) FROM ClientVisit cv WHERE cv.UserId = r.AssignedTo AND cv.VisitDate = r.ReportDate)";
    private string ExprNewLeads =>
        "(SELECT COUNT(*) FROM Leads l WHERE l.AssignedTo = r.AssignedTo AND CAST(l.CreatedOn AS DATE) = r.ReportDate)";
    private string ExprFollowUp =>
        HasFollowUp
            ? "(SELECT COUNT(*) FROM DailyFollowUp f WHERE f.AssignedTo = r.AssignedTo AND f.FollowUpDate = r.ReportDate)"
            : "0";
    private string ExprInterested =>
        "(SELECT COUNT(*) FROM Leads l WHERE l.AssignedTo = r.AssignedTo AND CAST(l.CreatedOn AS DATE) = r.ReportDate" +
        InterestedStatusFilter + ")";
    private string ExprConverted =>
        "(SELECT COUNT(*) FROM Leads l WHERE l.AssignedTo = r.AssignedTo AND ISNULL(l.IsConverted,0) = 1 AND CAST(l.ConvertedOn AS DATE) = r.ReportDate)";

    /// <summary>"Interested" ka matlab: status New/Lost ke alawa interest dikhane wale stage.</summary>
    private const string InterestedStatusFilter =
        " AND LOWER(ISNULL(l.Status,'')) IN ('contacted','qualified','proposal sent','negotiation','interested')";

    /// <summary>
    /// Grid ka SELECT list. Calls / Visit / NewLeads / TotalFollowUp / InterestedLeads / ConvertedLeads
    /// DailyReport me store nahi hote — ye source tables se LIVE count hote hain.
    /// Baaki columns jaise hain waise hi (dynamic).
    /// </summary>
    private string BuildSelectList(IEnumerable<string> cols)
    {
        var parts = new List<string>();
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in cols)
        {
            present.Add(c);
            if (c.Equals("Calls", StringComparison.OrdinalIgnoreCase))
                parts.Add(ExprCalls + " AS [Calls]");
            else if (c.Equals("Visit", StringComparison.OrdinalIgnoreCase))
                parts.Add(ExprVisit + " AS [Visit]");
            else if (c.Equals("NewLeads", StringComparison.OrdinalIgnoreCase))
                parts.Add(ExprNewLeads + " AS [NewLeads]");
            else if (c.Equals("TotalFollowUp", StringComparison.OrdinalIgnoreCase))
                parts.Add(ExprFollowUp + " AS [TotalFollowUp]");
            else if (c.Equals("InterestedLeads", StringComparison.OrdinalIgnoreCase))
                parts.Add(ExprInterested + " AS [InterestedLeads]");
            else if (c.Equals("ConvertedLeads", StringComparison.OrdinalIgnoreCase))
                parts.Add(ExprConverted + " AS [ConvertedLeads]");
            else
                parts.Add("r.[" + c + "]");
        }

        // Naye count columns table me na ho to bhi grid me dikhao (append)
        if (!present.Contains("TotalFollowUp")) parts.Add(ExprFollowUp + " AS [TotalFollowUp]");
        if (!present.Contains("InterestedLeads")) parts.Add(ExprInterested + " AS [InterestedLeads]");
        if (!present.Contains("ConvertedLeads")) parts.Add(ExprConverted + " AS [ConvertedLeads]");

        return string.Join(", ", parts);
    }

    private string BuildOrder(string? sort, string direction)
    {
        var dir = string.Equals(direction, "ASC", StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC";
        switch ((sort ?? "").Trim().ToLowerInvariant())
        {
            case "reportdate":     return $" ORDER BY r.ReportDate {dir}, r.DailyReportId DESC";
            case "calls":          return $" ORDER BY {ExprCalls} {dir}, r.DailyReportId DESC";
            case "visit":          return $" ORDER BY {ExprVisit} {dir}, r.DailyReportId DESC";
            case "newleads":       return $" ORDER BY {ExprNewLeads} {dir}, r.DailyReportId DESC";
            case "totalfollowup":  return $" ORDER BY {ExprFollowUp} {dir}, r.DailyReportId DESC";
            case "interestedleads":return $" ORDER BY {ExprInterested} {dir}, r.DailyReportId DESC";
            case "convertedleads": return $" ORDER BY {ExprConverted} {dir}, r.DailyReportId DESC";
            case "summary":        return $" ORDER BY r.Summary {dir}, r.DailyReportId DESC";
            case "assignedname":   return $" ORDER BY ISNULL(NULLIF(u.FullName,''), u.Username) {dir}, r.DailyReportId DESC";
            case "entrytime":      return $" ORDER BY r.EntryTime {dir}, r.DailyReportId DESC";
            case "modifiedon":     return $" ORDER BY r.ModifiedOn {dir}, r.DailyReportId DESC";
            default:
                return " ORDER BY r.ReportDate DESC, r.DailyReportId DESC";
        }
    }

    /// <summary>User-wise summary grid ka ORDER BY (columns: AssignedName / NewLeads / TotalFollowUp / ConvertedLeads).</summary>
    private static string BuildSummaryOrder(string? sort, string direction)
    {
        var dir = string.Equals(direction, "ASC", StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC";
        switch ((sort ?? "").Trim().ToLowerInvariant())
        {
            case "newleads":
                return $" ORDER BY (SELECT COUNT(*) FROM Leads l WHERE l.AssignedTo = u.UserId AND CAST(l.CreatedOn AS DATE) BETWEEN @DayFrom AND @DayTo) {dir}, ISNULL(NULLIF(u.FullName,''), u.Username) ASC";
            case "totalfollowup":
                return $" ORDER BY (SELECT COUNT(*) FROM DailyFollowUp f WHERE f.AssignedTo = u.UserId AND f.FollowUpDate BETWEEN @DayFrom AND @DayTo) {dir}, ISNULL(NULLIF(u.FullName,''), u.Username) ASC";
            case "convertedleads":
                return $" ORDER BY (SELECT COUNT(*) FROM Leads l WHERE l.AssignedTo = u.UserId AND ISNULL(l.IsConverted,0) = 1 AND CAST(l.ConvertedOn AS DATE) BETWEEN @DayFrom AND @DayTo) {dir}, ISNULL(NULLIF(u.FullName,''), u.Username) ASC";
            case "assignedname":
                return $" ORDER BY ISNULL(NULLIF(u.FullName,''), u.Username) {dir}";
            default:
                return " ORDER BY ISNULL(NULLIF(u.FullName,''), u.Username) ASC";
        }
    }

    /// <summary>Ek report ka poora detail (grid columns + LIVE counts) — View popup ke liye.</summary>
    public Dictionary<string, object?>? GetDetail(int id)
    {
        try
        {
            using var con = _db.GetConnection();
            con.Open();
            HasFollowUp = TableExists(con, "DailyFollowUp");

            var cols = GetDailyReportColumns(con);
            if (cols.Count == 0) return null;

            string sql =
                "SELECT " + BuildSelectList(cols) +
                ", ISNULL(NULLIF(u.FullName,''), u.Username) AS AssignedName" + FromJoin +
                " WHERE r.DailyReportId = @Id";

            using var cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@Id", id);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return null;

            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < reader.FieldCount; i++)
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            return row;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Ek user ka us din ka detail — View popup ke liye.
    /// Lists: us din ke naye leads, us din ke follow-ups, us din convert hue leads.
    /// </summary>
    public DailyReportUserDetailVM GetUserDayDetail(int userId, DateTime day)
    {
        var vm = new DailyReportUserDetailVM { Day = day.Date };

        try
        {
            using var con = _db.GetConnection();
            con.Open();

            HasFollowUp = TableExists(con, "DailyFollowUp");

            using (var cmd = new SqlCommand("SELECT ISNULL(NULLIF(FullName,''), Username) FROM Users WHERE UserId = @U", con))
            {
                cmd.Parameters.AddWithValue("@U", userId);
                vm.UserName = cmd.ExecuteScalar()?.ToString() ?? ("User #" + userId);
            }

            using (var cmd = new SqlCommand(
                "SELECT LeadPerson, Company, Mobile, City, Status, Requirement, CreatedOn FROM Leads " +
                "WHERE AssignedTo = @U AND CAST(CreatedOn AS DATE) = @D ORDER BY CreatedOn DESC", con))
            {
                cmd.Parameters.AddWithValue("@U", userId);
                cmd.Parameters.AddWithValue("@D", day.Date);
                using var r = cmd.ExecuteReader();
                while (r.Read()) vm.Leads.Add(ReadRow(r));
            }
            vm.NewLeads = vm.Leads.Count;

            using (var cmd = new SqlCommand(
                "SELECT LeadPerson, Company, Mobile, ConvertedClientId, ConvertedOn FROM Leads " +
                "WHERE AssignedTo = @U AND ISNULL(IsConverted,0) = 1 AND CAST(ConvertedOn AS DATE) = @D ORDER BY ConvertedOn DESC", con))
            {
                cmd.Parameters.AddWithValue("@U", userId);
                cmd.Parameters.AddWithValue("@D", day.Date);
                using var r = cmd.ExecuteReader();
                while (r.Read()) vm.Converted.Add(ReadRow(r));
            }
            vm.ConvertedLeads = vm.Converted.Count;

            if (HasFollowUp)
            {
                using var cmd = new SqlCommand(
                    "SELECT COALESCE(NULLIF(f.ContactPerson,''), l.LeadPerson) AS ContactPerson, " +
                    "COALESCE(NULLIF(f.Company,''), l.Company) AS Company, " +
                    "COALESCE(NULLIF(f.Mobile,''), l.Mobile) AS Mobile, " +
                    "f.FollowUpType, f.Status, f.Discussion " +
                    "FROM DailyFollowUp f LEFT JOIN Leads l ON f.LeadId = l.LeadId " +
                    "WHERE f.AssignedTo = @U AND f.FollowUpDate = @D ORDER BY f.DailyFollowUpId DESC", con);
                cmd.Parameters.AddWithValue("@U", userId);
                cmd.Parameters.AddWithValue("@D", day.Date);
                using var r = cmd.ExecuteReader();
                while (r.Read()) vm.FollowUps.Add(ReadRow(r));
                vm.TotalFollowUps = vm.FollowUps.Count;
            }
        }
        catch
        {
            // best-effort — popup khaali dikhega, page toot-ta nahi
        }

        return vm;
    }

    private static Dictionary<string, object?> ReadRow(SqlDataReader r)
    {
        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < r.FieldCount; i++)
            row[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
        return row;
    }
}
