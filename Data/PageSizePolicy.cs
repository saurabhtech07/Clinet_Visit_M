namespace ClientVisitManagement.Data;

/// <summary>
/// Page-size policy - har repository me ek hi rule.
///
/// Do alag use-case hain:
///
///  1. GRID   - screen par table. Size query string se aata hai aur user dropdown
///               se chunta hai (10 / 20 / 50 / 100). Iske liye sirf whitelist
///               wale size maane jaate hain - koi bhi random size default par gira
///               deta hai.
///
///  2. EXPORT - Excel / CSV / Print me poori data chahiye. Controller
///               int.MaxValue bhejta hai. Ab ise "all rows" samajh ke (par ek
///               ceiling ke saath) chalaya jata hai.
///
/// PEHLE ka pattern tha: "jo size whitelist me nahi =&gt; 10".
/// Isse export SILENT ho ke sirf 10 rows bhej deta tha - Client, ClientVisit,
/// Users, StateMaster aur DailyFollowUp ke Excel/CSV me bug tha.
///
/// NOTE: Grid ke liye bade size (>= ExportMin) bhi reject kiye jaate hain, taaki
/// koi URL me ?pageSize=999999 likh ke grid me se 1 lakh rows na kheech sake.
/// Export hamesha controller se aata hai, isliye wo safe rehta hai.
/// </summary>
public static class PageSizePolicy
{
    /// <summary>Grid dropdown ke allowed sizes (har grid view me same 4 options).</summary>
    public static readonly int[] GridSizes = { 10, 20, 50, 100 };

    /// <summary>Galat / missing / unsupported size =&gt; ye chalta hai.</summary>
    public const int Default = 10;

    /// <summary>
    /// Export ("all rows") request ki lower bound. Isse bada size export samjha
    /// jaata hai, chhota ya beech ka size default par gira diya jaata hai.
    /// </summary>
    public const int ExportMin = 1000;

    /// <summary>
    /// Export ka ceiling. 1 lakh rows ek Excel file me comfortably aa jaate hain
    /// aur memory bhi controlled rehta hai.
    /// </summary>
    public const int ExportMax = 100_000;

    /// <summary>
    /// Repository level clamp - grid size allow, export request cap ke saath allow,
    /// baaki sab default par.
    /// </summary>
    public static int Resolve(int pageSize)
    {
        // Grid dropdown ka size - as-is chalega.
        if (GridSizes.Contains(pageSize))
            return pageSize;

        // Export ka "all rows" request - ceiling ke saath.
        if (pageSize >= ExportMin)
            return Math.Min(pageSize, ExportMax);

        // 0, -1, 25, 500, random kuch bhi - default.
        return Default;
    }
}
