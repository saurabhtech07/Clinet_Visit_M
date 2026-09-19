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

        // FIX: Login, Logout, GenerateHash ko allow karo warna infinite loop hoga
        if (controller == "Account" && 
           (action == "Login" || action == "Logout" || action == "GenerateHash"))
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

        // Agar Session bhi nahi aur Cookie auth bhi nahi to hi login pe bhej
        if (userId == null && !isAuthenticated)
        {
            context.Result = new RedirectToActionResult("Login", "Account", null);
        }
    }
}

