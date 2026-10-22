using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace ClientVisitManagement.Data;

/// <summary>
/// Role / current-user ke liye ek hi jagah helper.
/// Daily Support me "kaam kisne kya kiya" (AssignedTo) aur "kaam kisko de
/// sakte hain" (Reassign) ka rule yahan se aata hai, taaki har controller
/// me alag-alag role-check na likhna pade.
///
/// Role 2 jagah se aata hai:
///   1. Session["Role"]        - login ke time set hota hai
///   2. Cookie claim (Role)    - app restart ke baad session wipe ho jaata
///                               hai, tab cookie se wapas restore hota hai
/// Dono fallback se role restart-proof rehta hai.
/// </summary>
public static class AccessControl
{
    /// <summary>Role ke naam me "Admin" ya "Manager" hai kya? (sirf ye assign kar sakte hain)</summary>
    public static bool CanAssignWork(HttpContext ctx)
    {
        var role = RoleName(ctx);
        return role.Equals("Admin", StringComparison.OrdinalIgnoreCase)
            || role.Equals("Manager", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Logged-in user ka UserId (Session se, nahi mila to cookie claim se)</summary>
    public static int? CurrentUserId(HttpContext ctx)
    {
        var uid = ctx.Session.GetInt32("UserId");
        if (uid.HasValue && uid.Value > 0) return uid;

        var claim = ctx.User?.FindFirst("UserId")?.Value;
        if (int.TryParse(claim, out var parsed) && parsed > 0) return parsed;

        return null;
    }

    /// <summary>Role ka naam (Admin / Manager / kuch bhi) - khaali string bhi ho sakti hai</summary>
    public static string RoleName(HttpContext ctx)
    {
        var role = ctx.Session.GetString("Role");
        if (!string.IsNullOrWhiteSpace(role)) return role.Trim();

        var claim = ctx.User?.FindFirst(ClaimTypes.Role)?.Value;
        return claim?.Trim() ?? "";
    }

    /// <summary>Logged-in user ka naam (UI me "My Work" ke title ke liye)</summary>
    public static string CurrentUserName(HttpContext ctx)
    {
        var name = ctx.Session.GetString("FullName");
        if (!string.IsNullOrWhiteSpace(name)) return name;

        var claim = ctx.User?.FindFirst("FullName")?.Value;
        return claim?.Trim() ?? "";
    }
}
