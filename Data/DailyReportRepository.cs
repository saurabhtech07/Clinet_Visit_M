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

    /// <summary>Ye columns grid me nahi dikhate (internal / join aliases).</summary>
    private static readonly HashSet<string> HiddenGridColumns =
        new(StringComparer.OrdinalIgnoreCase) { "AssignedTo", "CreatedBy" };

    private const string FromJoin =
        " FROM DailyReport r LEFT JOIN Users u ON r.AssignedTo = u.UserId ";

    // =====================================================================
    //  PAGE DATA
    // =====================================================================
    public DailyReportViewModel GetPage(int page, int pageSize, string? search, string? status,
        int? assignedTo, DateTime? fromDate, DateTime? toDate, string? sort, string direction)
    {
        if (page < 1) page = 1;
        if (pageSize is not (10 or 20 or 50 or 100)) pageSize = 10;

        var vm = new DailyReportViewModel
        {
            FilterStatus = status,
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
                         ISNULL(SUM(ISNULL(Calls,0)),0),
                         ISNULL(SUM(ISNULL(Visit,0)),0),
                         ISNULL(SUM(ISNULL(NewLeads,0)),0)
                  FROM DailyReport", con))
            {
                using var r = cardCmd.ExecuteReader();
                if (r.Read())
                {
                    vm.TotalReports  = r.IsDBNull(0) ? 0 : r.GetInt32(0);
                    vm.TotalCalls    = r.IsDBNull(1) ? 0 : Convert.ToInt32(r.GetValue(1));
                    vm.TotalVisits   = r.IsDBNull(2) ? 0 : Convert.ToInt32(r.GetValue(2));
                    vm.TotalNewLeads = r.IsDBNull(3) ? 0 : Convert.ToInt32(r.GetValue(3));
                }
            }

            // ---- GRID (filtered + paged) ----
            string where = BuildWhere(search, status, assignedTo, fromDate, toDate);

            using (var countCmd = new SqlCommand("SELECT COUNT(*)" + FromJoin + where, con))
            {
                AddParams(countCmd, search, status, assignedTo, fromDate, toDate);
                grid.TotalRecords = Convert.ToInt32(countCmd.ExecuteScalar());
            }

            string order = BuildOrder(sort, grid.SortDirection!);
            string sql =
                "SELECT r.*, ISNULL(NULLIF(u.FullName,''), u.Username) AS AssignedName" + FromJoin +
                where + order + " OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY";

            using var cmd = new SqlCommand(sql, con);
            AddParams(cmd, search, status, assignedTo, fromDate, toDate);
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

        // Assigned To ko Date ke turant baad dikhao (matching column order)
        var assignedIdx = grid.Columns.FindIndex(c => c.Equals("AssignedName", StringComparison.OrdinalIgnoreCase));
        if (assignedIdx >= 0)
        {
            grid.Columns.RemoveAt(assignedIdx);
            grid.Columns.Insert(Math.Min(2, grid.Columns.Count), "AssignedName");
        }

        vm.Grid = grid;
        return vm;
    }

    // =====================================================================
    //  HELPERS
    // =====================================================================
    private static string BuildWhere(string? search, string? status, int? assignedTo,
        DateTime? fromDate, DateTime? toDate)
    {
        var w = " WHERE 1=1";
        if (!string.IsNullOrWhiteSpace(status)) w += " AND ISNULL(r.Status,'') = @Status";
        if (assignedTo.HasValue) w += " AND r.AssignedTo = @AssignedTo";
        if (fromDate.HasValue) w += " AND CAST(r.ReportDate AS DATE) >= @From";
        if (toDate.HasValue) w += " AND CAST(r.ReportDate AS DATE) <= @To";
        if (!string.IsNullOrWhiteSpace(search))
            w += " AND (ISNULL(r.Summary,'') LIKE @Search OR ISNULL(r.Remark,'') LIKE @Search" +
                 " OR ISNULL(u.FullName,'') LIKE @Search OR ISNULL(u.Username,'') LIKE @Search)";
        return w;
    }

    private static void AddParams(SqlCommand cmd, string? search, string? status, int? assignedTo,
        DateTime? fromDate, DateTime? toDate)
    {
        if (!string.IsNullOrWhiteSpace(search)) cmd.Parameters.AddWithValue("@Search", "%" + search.Trim() + "%");
        if (!string.IsNullOrWhiteSpace(status)) cmd.Parameters.AddWithValue("@Status", status);
        if (assignedTo.HasValue) cmd.Parameters.AddWithValue("@AssignedTo", assignedTo.Value);
        if (fromDate.HasValue) cmd.Parameters.AddWithValue("@From", fromDate.Value.Date);
        if (toDate.HasValue) cmd.Parameters.AddWithValue("@To", toDate.Value.Date);
    }

    private static string BuildOrder(string? sort, string direction)
    {
        var dir = string.Equals(direction, "ASC", StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC";
        switch ((sort ?? "").Trim().ToLowerInvariant())
        {
            case "reportdate":   return $" ORDER BY r.ReportDate {dir}, r.DailyReportId DESC";
            case "calls":        return $" ORDER BY ISNULL(r.Calls,0) {dir}, r.DailyReportId DESC";
            case "visit":        return $" ORDER BY ISNULL(r.Visit,0) {dir}, r.DailyReportId DESC";
            case "newleads":     return $" ORDER BY ISNULL(r.NewLeads,0) {dir}, r.DailyReportId DESC";
            case "status":       return $" ORDER BY CASE WHEN ISNULL(r.Status,'')='Done' THEN 1 ELSE 0 END {dir}, r.DailyReportId DESC";
            case "summary":      return $" ORDER BY r.Summary {dir}, r.DailyReportId DESC";
            case "assignedname": return $" ORDER BY ISNULL(NULLIF(u.FullName,''), u.Username) {dir}, r.DailyReportId DESC";
            case "entrytime":    return $" ORDER BY r.EntryTime {dir}, r.DailyReportId DESC";
            default:
                return " ORDER BY r.ReportDate DESC, r.DailyReportId DESC";
        }
    }
}
