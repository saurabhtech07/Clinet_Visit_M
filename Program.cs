

// using ClientVisitManagement.Data;

// var builder = WebApplication.CreateBuilder(args);

// builder.Services.AddControllersWithViews(options =>
// {
//     options.Filters.Add<ClientVisitManagement.Filters.AuthFilter>();
// });

// builder.Services.AddScoped<DBHelper>();
// builder.Services.AddScoped<ClientRepository>();
// builder.Services.AddScoped<ClientVisitRepository>();
// builder.Services.AddScoped<DynamicRepository>();
// builder.Services.AddScoped<UserRepository>();

// builder.Services.AddSession(options =>
// {
//     options.IdleTimeout = TimeSpan.FromMinutes(30);
//     options.Cookie.HttpOnly = true;
// });

// var app = builder.Build();

// if (!app.Environment.IsDevelopment())
// {
//     app.UseExceptionHandler("/Home/Error");
//     app.UseHsts();
// }

// app.UseHttpsRedirection();
// app.UseStaticFiles();

// app.UseRouting();

// app.UseSession();

// app.UseAuthorization();


// // ================= CLIENT VISIT =================

// app.MapControllerRoute(
//     name: "clientVisit",
//     pattern: "ClientVisit",
//     defaults: new
//     {
//         controller = "ClientVisit",
//         action = "Index"
//     }
// );


// // ================= CLIENT MASTER =================

// app.MapControllerRoute(
//     name: "clientMaster",
//     pattern: "Client",
//     defaults: new
//     {
//         controller = "Client",
//         action = "Index"
//     }
// );


// // ================= DEFAULT ROUTE =================

// app.MapControllerRoute(
//     name: "default",
//     pattern: "{controller=Client}/{action=Index}/{id?}"
// );

// app.Run();



using ClientVisitManagement.Data;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add<ClientVisitManagement.Filters.AuthFilter>();
});

builder.Services.AddScoped<DBHelper>();
builder.Services.AddScoped<ClientRepository>();
builder.Services.AddScoped<ClientVisitRepository>();
builder.Services.AddScoped<DynamicRepository>();
builder.Services.AddScoped<UserRepository>();

builder.Services.AddDistributedMemoryCache();

builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(8);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.Name = ".ClientVisit.Session";
});

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
.AddCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.ExpireTimeSpan = TimeSpan.FromDays(7);
    options.SlidingExpiration = true;
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.Name = ".ClientVisit.Auth";
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

// CUSTOM ROUTES
app.MapControllerRoute(
    name: "clientVisit",
    pattern: "ClientVisit/{action=Index}/{id?}",
    defaults: new { controller = "ClientVisit" }
);

app.MapControllerRoute(
    name: "clientMaster",
    pattern: "Client/{action=Index}/{id?}",
    defaults: new { controller = "Client" }
);

// DEFAULT ROUTE - Wapas Client/Index kiya, Account/Login automatic kaam karega
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Client}/{action=Index}/{id?}"
);

app.Run();