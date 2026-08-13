using Brainy.Application;
using Brainy.Application.Interfaces.Identity;
using Brainy.Data;
using Brainy.Data.Identity;
using Brainy.Web.Components;
using Brainy.Web.Components.Account;
using Brainy.Web.Endpoints;
using Brainy.Web.Health;
using Brainy.Web.Identity;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using MudBlazor.Services;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);


// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddHubOptions(options =>
    {
        // The note editor sends the full textarea content on every input event.
        // The SignalR default (32 KB) silently drops large pastes and kills the
        // circuit, so notes end up saved without content. Allow up to 512 KB.
        options.MaximumReceiveMessageSize = 512 * 1024;
    });

builder.Services.AddMudServices();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Azure App Service proxy addresses are dynamic. HTTPS Only remains the
    // authoritative edge control; forwarded headers restore the original scheme.
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(180);
});

builder.Services.AddAntiforgery(options =>
{
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        if (!HttpMethods.IsPost(context.Request.Method))
            return RateLimitPartition.GetNoLimiter("read");

        if (context.Request.Path.Equals("/Account/Login", StringComparison.OrdinalIgnoreCase))
        {
            return RateLimitPartition.GetFixedWindowLimiter(
                $"login:{context.Connection.RemoteIpAddress}",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromMinutes(5),
                    QueueLimit = 0,
                    AutoReplenishment = true
                });
        }

        if (context.Request.Path.Equals("/Account/Register", StringComparison.OrdinalIgnoreCase))
        {
            return RateLimitPartition.GetFixedWindowLimiter(
                $"register:{context.Connection.RemoteIpAddress}",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 5,
                    Window = TimeSpan.FromHours(1),
                    QueueLimit = 0,
                    AutoReplenishment = true
                });
        }

        return RateLimitPartition.GetNoLimiter("other");
    });
});

// Authentication / Identity.
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});

// Data access layer (EF Core / SQL Server).
builder.Services.AddBrainyData(builder.Configuration);

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = builder.Configuration.GetValue(
            "Identity:RequireConfirmedAccount", false);
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.Password.RequiredLength = 10;
    })
    .AddEntityFrameworkStores<BrainyDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

// Current-user accessor used by the application layer for per-user data scoping.
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<IAccountDeletionService, AccountDeletionService>();

// Application-layer services.
builder.Services.AddBrainyApplication();

builder.Services.AddScoped<Brainy.Web.Themes.ThemeService>();

// Provider=None remains a safe no-op; configured providers can now be enabled
// without changing the application binary.
builder.Services.AddAiAssistant(builder.Configuration);

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseReadinessHealthCheck>("database", tags: ["ready"]);

var app = builder.Build();

if (builder.Configuration.GetValue("Database:ApplyMigrationsOnStartup", true))
{
    await DatabaseInitializer.MigrateAsync(app.Services, app.Environment.IsDevelopment());
}

// Configure the HTTP request pipeline.
app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Permissions-Policy"] = "camera=(), geolocation=(), payment=(), usb=()";
    context.Response.Headers.ContentSecurityPolicy =
        "default-src 'self'; base-uri 'self'; frame-ancestors 'self'; form-action 'self'; " +
        "img-src 'self' data: blob:; font-src 'self' data: https://fonts.gstatic.com; " +
        "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
        "script-src 'self' 'unsafe-inline'; connect-src 'self' https: wss:; object-src 'none'";
    await next();
});

app.UseRateLimiter();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Map additional endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();

// Serve note images stored in the database.
app.MapNoteImageEndpoints();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
});
// Keep the legacy platform probe process-only so regular App Service checks do not
// continuously wake the serverless SQL database. Deployments use /health/ready.
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => false
});

app.Run();

/// <summary>Entry point exposed for in-process web integration tests.</summary>
public partial class Program;
