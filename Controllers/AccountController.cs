// using Microsoft.AspNetCore.Mvc;
// using ClientVisitManagement.Data;
// using ClientVisitManagement.Models;

// namespace ClientVisitManagement.Controllers;

// public class AccountController : Controller
// {
//     private readonly UserRepository _userRepo;

//     public AccountController(UserRepository userRepo)
//     {
//         _userRepo = userRepo;
//     }

//     [HttpGet]
//     public IActionResult Login()
//     {
//         return View();
//     }

//     [HttpGet]
//     public IActionResult GenerateHash(string pwd)
//     {
//         if (string.IsNullOrWhiteSpace(pwd))
//             return BadRequest("Password is required.");

//         return Content(BCrypt.Net.BCrypt.HashPassword(pwd));
//     }


// //LOGIN POST


//     [HttpPost]
//     public IActionResult Login(LoginViewModel model)
//     {
//         var user = _userRepo.GetByUsername(model.Username);

//         if (user == null ||
//             !BCrypt.Net.BCrypt.Verify(model.Password, user.PasswordHash))
//         {
//             ViewBag.Error = "Invalid username or password.";
//             return View(model);
//         }

//         HttpContext.Session.SetInt32(
//             "UserId",
//             user.UserId
//         );

//         HttpContext.Session.SetString(
//             "FullName",
//             user.FullName
//         );

//         HttpContext.Session.SetString(
//             "Role",
//             user.RoleName ?? ""
//         );

//         return RedirectToAction("Index", "User");
//     }

//     public IActionResult Logout()
//     {
//         HttpContext.Session.Clear();

//         return RedirectToAction(nameof(Login));
//     }
// }


using Microsoft.AspNetCore.Mvc;
using ClientVisitManagement.Data;
using ClientVisitManagement.Models;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace ClientVisitManagement.Controllers;

public class AccountController : Controller
{
    private readonly UserRepository _userRepo;

    public AccountController(UserRepository userRepo)
    {
        _userRepo = userRepo;
    }

    [HttpGet]
    public IActionResult Login()
    {
        // Agar already login hai to direct redirect
        if (User.Identity!= null && User.Identity.IsAuthenticated)
        {
            return RedirectToAction("Index", "Client");
        }
        return View();
    }

    [HttpGet]
    public IActionResult GenerateHash(string pwd)
    {
        if (string.IsNullOrWhiteSpace(pwd))
            return BadRequest("Password is required.");
        return Content(BCrypt.Net.BCrypt.HashPassword(pwd));
    }

    // LOGIN POST - FIXED
    [HttpPost]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        var user = _userRepo.GetByUsername(model.Username);

        if (user == null ||!BCrypt.Net.BCrypt.Verify(model.Password, user.PasswordHash))
        {
            ViewBag.Error = "Invalid username or password.";
            return View(model);
        }

        // Session set
        HttpContext.Session.SetInt32("UserId", user.UserId);
        HttpContext.Session.SetString("FullName", user.FullName);
        HttpContext.Session.SetString("Role", user.RoleName?? "");

        // FIX: Cookie set with IsPersistent = true - yehi refresh wala fix hai
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Name, user.Username),
            new Claim("FullName", user.FullName),
            new Claim(ClaimTypes.Role, user.RoleName?? ""),
            new Claim("UserId", user.UserId.ToString())
        };

        var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var authProperties = new AuthenticationProperties
        {
            IsPersistent = true, // browser band karne pe bhi login rahega
            ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7),
            AllowRefresh = true
        };

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(claimsIdentity),
            authProperties);

        return RedirectToAction("Index", "Client");
    }

    public async Task<IActionResult> Logout()
    {
        HttpContext.Session.Clear();
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }
}