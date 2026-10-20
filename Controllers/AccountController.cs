

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
            return RedirectToAction("Index", "Dashboard");
        }
        return View();
    }

    // LOGIN POST - FIXED (plain password system, no hashing)
    [HttpPost]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        var user = _userRepo.GetByUsername(model.Username);

        if (user == null || string.IsNullOrEmpty(model.Password) || user.PasswordHash != model.Password)
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

        return RedirectToAction("Index", "Dashboard");
    }

    public async Task<IActionResult> Logout()
    {
        HttpContext.Session.Clear();
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }
}