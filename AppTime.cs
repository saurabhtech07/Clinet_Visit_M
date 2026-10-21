namespace ClientVisitManagement;

// App server chahe kisi bhi timezone me ho (live MonsterASP = CEST, dev = IST),
// SupportDate/ticket prefix hamesha India Standard Time me banao. DB GETDATE()
// bhi IST hai - warna SupportDate CreatedOn se 3.5h peeche chala jata tha.
public static class AppTime
{
    public static DateTime Now
    {
        get
        {
            try { return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("India Standard Time")); }
            catch { try { return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata")); } catch { return DateTime.UtcNow.AddMinutes(330); } }
        }
    }
    public static DateTime Today => Now.Date;
}
