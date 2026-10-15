using Microsoft.Data.SqlClient;
using ClientVisitManagement.Models;

namespace ClientVisitManagement.Data;

/// <summary>
/// Daily Follow Up module ka data layer.
///
///  - GetPage : grid + 4 cards (sab queries LIVE DB se)
///
/// SINGLE SOURCE OF TRUTH: Company / Mobile / City / Contact Person form me nahi bharte,
/// grid me Leads table se LIVE dikhte hain (Lead me value na ho to row ka purana snapshot).
///
/// NOTE: Table na ho (DDL na chali ho) to grid/cards khaali dikhte hain,
/// page toot-ta nahi. Insert/Update/Delete DynamicRepository ke through hote hain.
/// </summary>
public class DailyFollowUpRepository
{
    private readonly DBHelper _db;
    public DailyFollowUpRepository(DBHelper db) => _db = db;

    /// <summary>Ye columns grid me nahi dikhate (internal / join aliases).</summary>
    private static readonly HashSet<string> HiddenGridColumns =
        new(StringComparer.OrdinalIgnoreCase) { "LeadId", "CreatedBy", "AssignedTo" };

    private const string FromJoin =
        " FROM DailyFollowUp d LEFT JOIN Users u ON d.AssignedTo = u.UserId" +
        " LEFT JOIN Leads l ON d.LeadId = l.LeadId ";

    // Lead me value ho to wahi, warna row ka purana snapshot (legacy rows ke liye).
    private const string ContactExpr = "ISNULL(NULLIF(l.LeadPerson,''), d.ContactPerson)";
    private const string CompanyExpr = "ISNULL(NULLIF(l.Company,''), d.Company)";
    private const string MobileExpr  = "ISNULL(NULLIF(l.Mobile,''), d.Mobile)";
    private const string CityExpr    = "ISNULL(NULLIF(l.City,''), d.City)";

    // =====================================================================
    //  PAGE DATA
    // =====================================================================
    public DailyFollowUpViewModel GetPage(int page, int pageSize, string? search, string? status, string? priority,
        int? assignedTo, DateTime? fromDate, DateTime? toDate, string? sort, string direction)
    {
        if (page < 1) page = 1;
        if (pageSize is not (10 or 20 or 50 or 100)) pageSize = 10;

        var vm = new DailyFollowUpViewModel
        {
            FilterStatus = status,
            FilterPriority = priority,
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

            // ---- 4 CARDS ----
            using (var cardCmd = new SqlCommand(
                @"SELECT COUNT(*),
                         ISNULL(SUM(CASE WHEN ISNULL(Status,'')<>'Done' THEN 1 ELSE 0 END),0),
                         ISNULL(SUM(CASE WHEN ISNULL(Status,'')<>'Done' AND CAST(FollowUpDate AS DATE)=CAST(GETDATE() AS DATE) THEN 1 ELSE 0 END),0),
                         ISNULL(SUM(CASE WHEN ISNULL(Status,'')<>'Done' AND NextFollowUpDate IS NOT NULL AND NextFollowUpDate < CAST(GETDATE() AS DATE) THEN 1 ELSE 0 END),0)
                  FROM DailyFollowUp", con))
            {
                using var r = cardCmd.ExecuteReader();
                if (r.Read())
                {
                    vm.TotalFollowUps = r.IsDBNull(0) ? 0 : r.GetInt32(0);
                    vm.PendingCount   = r.IsDBNull(1) ? 0 : Convert.ToInt32(r.GetValue(1));
                    vm.DueTodayCount  = r.IsDBNull(2) ? 0 : Convert.ToInt32(r.GetValue(2));
                    vm.OverdueCount   = r.IsDBNull(3) ? 0 : Convert.ToInt32(r.GetValue(3));
                }
            }

            // ---- GRID (filtered + paged) ----
            string where = BuildWhere(search, status, priority, assignedTo, fromDate, toDate);

            using (var countCmd = new SqlCommand("SELECT COUNT(*)" + FromJoin + where, con))
            {
                AddParams(countCmd, search, status, priority, assignedTo, fromDate, toDate);
                grid.TotalRecords = Convert.ToInt32(countCmd.ExecuteScalar());
            }

            string order = BuildOrder(sort, grid.SortDirection!);
            string sql =
                BuildSelectList(con) + FromJoin +
                where + order + " OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY";

            using var cmd = new SqlCommand(sql, con);
            AddParams(cmd, search, status, priority, assignedTo, fromDate, toDate);
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
        catch
        {
            // table na ho / koi DB issue -> khaali grid (page safe)
        }

        grid.Columns.RemoveAll(c => HiddenGridColumns.Contains(c));
        vm.Grid = grid;
        return vm;
    }

    // =====================================================================
    //  HELPERS
    // =====================================================================
    /// <summary>
    /// Grid ka SELECT: DailyFollowUp ke saare columns dynamic (DB se), lekin
    /// ContactPerson / Company / Mobile / City Leads se LIVE aate hain.
    /// Naya column add karo to grid me khud aa jaayega.
    /// </summary>
    private static string BuildSelectList(SqlConnection con)
    {
        var cols = new List<string>();
        using (var cmd = new SqlCommand("SELECT TOP 0 * FROM DailyFollowUp", con))
        using (var r = cmd.ExecuteReader())
            for (int i = 0; i < r.FieldCount; i++) cols.Add(r.GetName(i));

        var parts = new List<string>();
        foreach (var c in cols)
        {
            parts.Add(c.ToLowerInvariant() switch
            {
                "contactperson" => ContactExpr + " AS ContactPerson",
                "company"       => CompanyExpr + " AS Company",
                "mobile"        => MobileExpr  + " AS Mobile",
                "city"          => CityExpr    + " AS City",
                _               => "d." + Quote(c)
            });
        }
        parts.Add("ISNULL(NULLIF(u.FullName,''), u.Username) AS AssignedName");
        return "SELECT " + string.Join(", ", parts);
    }

    private static string Quote(string col) => "[" + col.Replace("]", "]]") + "]";

    private static string BuildWhere(string? search, string? status, string? priority,
        int? assignedTo, DateTime? fromDate, DateTime? toDate)
    {
        var w = " WHERE 1=1";
        if (!string.IsNullOrWhiteSpace(status)) w += " AND ISNULL(d.Status,'') = @Status";
        if (!string.IsNullOrWhiteSpace(priority)) w += " AND ISNULL(d.Priority,'') = @Priority";
        if (assignedTo.HasValue) w += " AND d.AssignedTo = @AssignedTo";
        if (fromDate.HasValue) w += " AND CAST(d.FollowUpDate AS DATE) >= @From";
        if (toDate.HasValue) w += " AND CAST(d.FollowUpDate AS DATE) <= @To";
        if (!string.IsNullOrWhiteSpace(search))
            w += " AND (" + ContactExpr + " LIKE @Search OR " + CompanyExpr + " LIKE @Search" +
                 " OR " + MobileExpr + " LIKE @Search OR " + CityExpr + " LIKE @Search" +
                 " OR ISNULL(d.Remarks,'') LIKE @Search)";
        return w;
    }

    private static void AddParams(SqlCommand cmd, string? search, string? status, string? priority,
        int? assignedTo, DateTime? fromDate, DateTime? toDate)
    {
        if (!string.IsNullOrWhiteSpace(search)) cmd.Parameters.AddWithValue("@Search", "%" + search.Trim() + "%");
        if (!string.IsNullOrWhiteSpace(status)) cmd.Parameters.AddWithValue("@Status", status);
        if (!string.IsNullOrWhiteSpace(priority)) cmd.Parameters.AddWithValue("@Priority", priority);
        if (assignedTo.HasValue) cmd.Parameters.AddWithValue("@AssignedTo", assignedTo.Value);
        if (fromDate.HasValue) cmd.Parameters.AddWithValue("@From", fromDate.Value.Date);
        if (toDate.HasValue) cmd.Parameters.AddWithValue("@To", toDate.Value.Date);
    }

    private static string BuildOrder(string? sort, string direction)
    {
        var dir = string.Equals(direction, "ASC", StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC";
        switch ((sort ?? "").Trim().ToLowerInvariant())
        {
            case "contactperson":     return $" ORDER BY {ContactExpr} {dir}, d.DailyFollowUpId DESC";
            case "company":           return $" ORDER BY {CompanyExpr} {dir}, d.DailyFollowUpId DESC";
            case "mobile":            return $" ORDER BY {MobileExpr} {dir}, d.DailyFollowUpId DESC";
            case "city":              return $" ORDER BY {CityExpr} {dir}, d.DailyFollowUpId DESC";
            case "followupdate":      return $" ORDER BY d.FollowUpDate {dir}, d.DailyFollowUpId DESC";
            case "nextfollowupdate":  return $" ORDER BY CASE WHEN d.NextFollowUpDate IS NULL THEN 1 ELSE 0 END, d.NextFollowUpDate {dir}, d.DailyFollowUpId DESC";
            case "status":            return $" ORDER BY CASE WHEN ISNULL(d.Status,'')='Done' THEN 1 ELSE 0 END {dir}, d.DailyFollowUpId DESC";
            case "priority":          return $" ORDER BY d.Priority {dir}, d.DailyFollowUpId DESC";
            case "assignedname":      return $" ORDER BY ISNULL(NULLIF(u.FullName,''), u.Username) {dir}, d.DailyFollowUpId DESC";
            default:
                // Default: Pending pehle, phir next-date, phir newest
                return " ORDER BY CASE WHEN ISNULL(d.Status,'')='Done' THEN 1 ELSE 0 END," +
                       " CASE WHEN d.NextFollowUpDate IS NULL THEN 1 ELSE 0 END," +
                       " d.NextFollowUpDate ASC, d.DailyFollowUpId DESC";
        }
    }
}
