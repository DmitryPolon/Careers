using Careers.Components;
using Careers.Data;
using Careers.Endpoints;
using Careers.Models;
using Careers.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/account/login";
        options.AccessDeniedPath = "/";
        options.Cookie.Name = "careers.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromDays(14);
    });
builder.Services.AddAuthorization();
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 6 * 1024 * 1024;
});
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.Configure<MailSettings>(builder.Configuration.GetSection("Mail"));
builder.Services.AddSingleton<ICareersStore, MongoCareersStore>();
builder.Services.AddSingleton<PasswordHasher<UserAccount>>();
builder.Services.AddSingleton<ApplicationPacketWriter>();
builder.Services.AddSingleton<ApplicationMailer>();
builder.Services.AddSingleton<CareersSeeder>();

var app = builder.Build();
app.Services.GetRequiredService<CareersSeeder>().Seed();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseForwardedHeaders();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
CareerEndpoints.Map(app);
app.MapRazorComponents<App>();

app.Run();
