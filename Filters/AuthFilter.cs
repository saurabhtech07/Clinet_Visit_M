// using Microsoft.AspNetCore.Mvc;
// using Microsoft.AspNetCore.Mvc.Filters;

// namespace ClientVisitManagement.Filters;

// public class AuthFilter : IActionFilter
// {
//     public void OnActionExecuting(ActionExecutingContext context)
//     {
//         var controller = context.Controller.GetType().Name;

//         if (controller == "AccountController")
//             return;

//         var userId = context.HttpContext.Session.GetInt32("UserId");

//         if (userId == null)
//         {
//             context.Result = new RedirectResult("/Account/Login");
//         }
//     }

//     public void OnActionExecuted(ActionExecutedContext context)
//     {
//     }
// }



using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Authorization;

namespace ClientVisitManagement.Filters;

public class AuthFilter : IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var controller = context.RouteData.Values["controller"]?.ToString();
        var action = context.RouteData.Values["action"]?.ToString();

        // FIX: Login, Logout ko allow karo warna infinite loop hoga
        if (controller == "Account" && 
           (action == "Login" || action == "Logout"))
        {
            return;
        }

        // AllowAnonymous attribute ko bhi allow karo
        if (context.ActionDescriptor.EndpointMetadata
            .Any(m => m is AllowAnonymousAttribute))
        {
            return;
        }

        var userId = context.HttpContext.Session.GetInt32("UserId");
        var isAuthenticated = context.HttpContext.User.Identity?.IsAuthenticated == true;

        /* ---- SESSION RESTORE ------------------------------------------------
           Session in-memory hai (AddDistributedMemoryCache). App restart /
           rebuild hone par UserId + FullName session se wipe ho jaate hain.
           Auth cookie 7 din valid rehta hai (IsPersistent), to user login
           rehne ke bajaye andar ghus jaata hai - par session khaali hone ki
           wajah se history me "ChangedBy / ChangedByName" NULL likh diye
           jaate the aur View modal me user ka naam BLANK dikhta tha.

           Ab cookie ke claims se session wapas bhar dete hain.               */
        if (userId == null && isAuthenticated)
        {
            var claims = context.HttpContext.User;
            var uidText = claims?.FindFirst("UserId")?.Value;
            var fullName = claims?.FindFirst("FullName")?.Value;
            if (string.IsNullOrWhiteSpace(fullName)) fullName = claims?.Identity?.Name;
            if (int.TryParse(uidText, out var restoredUid))
            {
                context.HttpContext.Session.SetInt32("UserId", restoredUid);
                context.HttpContext.Session.SetString("FullName", fullName ?? "");
                userId = restoredUid;
            }
        }

        // Agar Session bhi nahi aur Cookie auth bhi nahi to hi login pe bhej
        if (userId == null && !isAuthenticated)
        {
            context.Result = new RedirectToActionResult("Login", "Account", null);
        }
    }
}

