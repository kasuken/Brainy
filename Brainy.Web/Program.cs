using Brainy.Application;
using Brainy.Application.Interfaces.Identity;
using Brainy.Data;
using Brainy.Data.Identity;
using Brainy.Web.BackgroundServices;
using Brainy.Web.Components;
using Brainy.Web.Components.Account;
using Brainy.Web.Components.Marketing;
using Brainy.Web.Configuration;
using Brainy.Web.Endpoints;
using Brainy.Web.Health;
using Brainy.Web.Identity;
using Brainy.Web.Localization;
using Brainy.Web.Telemetry;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MudBlazor.Services;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<SeoOptions>()
    .Bind(builder.Configuration.GetSection("Seo"))
    .ValidateDataAnnotations()
    .Validate(options =>
    {
        if (!Uri.TryCreate(options.SiteOrigin, UriKind.Absolute, out var origin))
            return false;

        return !builder.Environment.IsProduction() ||
               string.Equals(origin.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }, "Seo:SiteOrigin must be an absolute HTTPS URL in production.")
    .ValidateOnStart();

builder.Services.AddSingleton(serviceProvider =>
    serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<SeoOptions>>().Value);

// Defaults-closed email allowlist for the internal analytics dashboard; empty means
// nobody can see it until explicitly configured.
builder.Services.AddOptions<AnalyticsAccessOptions>()
    .Bind(builder.Configuration.GetSection(AnalyticsAccessOptions.SectionName));
builder.Services.AddSingleton(serviceProvider =>
    serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<AnalyticsAccessOptions>>().Value);


// Backs CurrentUserService's fallback path for the Offline Lite (issue #302) minimal API
// endpoints, which run outside any Razor component/circuit DI scope.
builder.Services.AddHttpContextAccessor();

// Localization infrastructure (issue #323). AddLocalization registers the default
// IStringLocalizerFactory; the AddSingleton below replaces it (last registration wins) with a
// decorator that, in Development only, visibly flags a string that fell back to English because
// the current UI culture's own resource is missing that key (see FallbackVisibleStringLocalizer).
// Anonymous/first-load negotiation (marketing pages, sign-in) comes from
// RequestLocalizationOptions' default providers (cookie, then Accept-Language, then
// SupportedCultures.Default below); an authenticated user's own stored preference
// (UserDashboardPreference.CultureId via IUserCultureService) is applied per-circuit in
// MainLayout, the same way IUserTimeZoneService's stored preference already is.
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services.AddSingleton<IStringLocalizerFactory>(serviceProvider => new FallbackVisibleStringLocalizerFactory(
    serviceProvider.GetRequiredService<IOptions<LocalizationOptions>>(),
    serviceProvider.GetRequiredService<ILoggerFactory>(),
    builder.Environment.IsDevelopment()));
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    // Every negotiable culture EXCEPT the default, expressed as real CultureInfo objects for
    // AcceptLanguage/cookie matching purposes. The default itself resolves to
    // CultureInfo.InvariantCulture (see SupportedCultures.Default's remarks) and is set as
    // DefaultRequestCulture below rather than listed here, so an unmatched/English request
    // falls back to exactly today's (pre-#323) ambient culture instead of a real "en-US" one.
    var negotiableCultures = Brainy.Application.Localization.SupportedCultures.All
        .Where(cultureId => cultureId != Brainy.Application.Localization.SupportedCultures.Default)
        .Select(Brainy.Application.Localization.SupportedCultures.Resolve)
        .Append(CultureInfo.InvariantCulture)
        .ToArray();

    options.DefaultRequestCulture = new RequestCulture(CultureInfo.InvariantCulture);
    options.SupportedCultures = negotiableCultures;
    options.SupportedUICultures = negotiableCultures;
});

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

builder.Services.AddMudServices(config =>
{
    // /Account/Manage forces its own interactive island (see the comment on that page)
    // with a local MudPopoverProvider as a fast, always-ready fallback alongside
    // MainLayout's; tolerate both being present instead of crashing the circuit.
    config.PopoverOptions.ThrowOnDuplicateProvider = false;
});

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
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        // The ICS feed is the one GET endpoint that needs a limit: it is a public,
        // anonymous URL (see CalendarFeedEndpoints) and calendar clients (Outlook, Google,
        // Apple) are known to poll far more aggressively than the hourly refresh hint the
        // feed itself publishes. Partition by the token rather than by IP — a shared
        // corporate egress IP or a calendar provider's shared fetcher pool must not let one
        // user's polling throttle every other subscriber, and a leaked/guessed token should
        // be the thing that gets throttled, not innocent traffic sharing its address.
        if (HttpMethods.IsGet(context.Request.Method) &&
            context.Request.Path.Equals(CalendarFeedEndpoints.FeedPath, StringComparison.OrdinalIgnoreCase))
        {
            var token = context.Request.Query["token"].ToString();
            var partitionKey = string.IsNullOrEmpty(token)
                ? $"calendar-feed:ip:{context.Connection.RemoteIpAddress}"
                : $"calendar-feed:token:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)))}";

            return RateLimitPartition.GetFixedWindowLimiter(
                partitionKey,
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 30,
                    Window = TimeSpan.FromMinutes(5),
                    QueueLimit = 0,
                    AutoReplenishment = true
                });
        }

        // The public output share page (issue #319) is the same shape of risk as the ICS
        // feed above: a public, anonymous URL whose only credential is a token in the query
        // string. Partition by the token itself for the same reason — a shared IP must not
        // throttle every other visitor, and a guessing attempt should throttle itself.
        if (HttpMethods.IsGet(context.Request.Method) &&
            context.Request.Path.Equals(OutputSharePage.RoutePath, StringComparison.OrdinalIgnoreCase))
        {
            var token = context.Request.Query["token"].ToString();
            var partitionKey = string.IsNullOrEmpty(token)
                ? $"output-share:ip:{context.Connection.RemoteIpAddress}"
                : $"output-share:token:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)))}";

            return RateLimitPartition.GetFixedWindowLimiter(
                partitionKey,
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 30,
                    Window = TimeSpan.FromMinutes(5),
                    QueueLimit = 0,
                    AutoReplenishment = true
                });
        }

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

        // Both endpoints trigger an outbound email keyed only by an attacker-suppliable
        // address, so they get the same per-IP ceiling as registration to prevent using
        // Brainy as a mail bomb / address-enumeration oracle.
        if (context.Request.Path.Equals("/Account/ForgotPassword", StringComparison.OrdinalIgnoreCase) ||
            context.Request.Path.Equals("/Account/ResendEmailConfirmation", StringComparison.OrdinalIgnoreCase))
        {
            return RateLimitPartition.GetFixedWindowLimiter(
                $"account-email:{context.Connection.RemoteIpAddress}",
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
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
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
// Lets the push dispatch background service (no request/circuit of its own) impersonate one
// user per DI scope so it can reuse ITodayNotificationService/IUserTimeZoneService unchanged.
builder.Services.AddScoped<Brainy.Web.Identity.BackgroundUserContext>();
builder.Services.AddScoped<Brainy.Application.Interfaces.Identity.IBackgroundUserContextAccessor>(
    sp => sp.GetRequiredService<Brainy.Web.Identity.BackgroundUserContext>());
builder.Services.AddScoped<IAccountDeletionService, AccountDeletionService>();
// Overrides the Application layer's zero-count NullUserDirectoryService registration with
// the real Identity-backed count, used by the internal analytics dashboard (issue #324).
builder.Services.AddScoped<Brainy.Application.Interfaces.Identity.IUserDirectoryService, Brainy.Web.Identity.UserDirectoryService>();

// Application-layer services.
builder.Services.AddBrainyApplication();

builder.Services.AddScoped<Brainy.Web.Themes.ThemeService>();

// Provider=None remains a safe no-op; configured providers can now be enabled
// without changing the application binary.
builder.Services.AddAiAssistant(builder.Configuration);

// Provider=None (the default) registers NullBillingProvider: entitlements are fully
// enforced, but plan changes only happen via the internal/admin path until a real
// payment provider is configured.
builder.Services.AddBilling(builder.Configuration);

// Provider=None (the default) registers NullEmailSender: the app starts and every
// outbound message (password reset, email confirmation) is logged instead of sent, so
// local dev/self-hosting keep working without a mail account configured. Adapts
// Identity's IEmailSender<ApplicationUser> callback shape onto the application-layer
// email abstraction.
builder.Services.AddEmail(builder.Configuration);
builder.Services.AddScoped<IEmailSender<ApplicationUser>, BrainyIdentityEmailSender>();

// No VAPID key pair configured (the default) registers NullPushNotificationSender: push
// settings and subscription management still work, but nothing is actually sent. Strictly
// opt-in and off by default regardless (see PushNotificationPreference.Enabled).
builder.Services.AddWebPush(builder.Configuration);

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseReadinessHealthCheck>("database", tags: ["ready"]);

// OpenTelemetry traces/metrics/logs (issue #322). Telemetry:Enabled defaults to false, so this
// registers nothing at all unless a host explicitly opts in and configures an OTLP endpoint —
// self-hosting is unaffected. See docs/production-runbook.md for the exporter setup.
builder.Services.AddBrainyTelemetry(builder.Configuration);

// Builds Markdown/Obsidian vault exports off the request path so a large account's export
// never times out the request that started it (see IMarkdownExportJobService).
builder.Services.AddHostedService<MarkdownExportBackgroundService>();

// Evaluates and sends due Web Push notifications for opted-in users (issue #315).
builder.Services.AddHostedService<PushNotificationDispatchBackgroundService>();

var app = builder.Build();

if (builder.Configuration.GetValue("Database:ApplyMigrationsOnStartup", true))
{
    await DatabaseInitializer.MigrateAsync(app.Services, app.Environment.IsDevelopment());
}

// Configure the HTTP request pipeline.
app.UseForwardedHeaders();
// Negotiates the request culture (cookie, then Accept-Language, then
// SupportedCultures.Default) for anonymous/first-load rendering, including prerendering.
// An authenticated user's own stored preference overrides this per-circuit in MainLayout.
app.UseRequestLocalization();
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

// Explicit so UserCulturePreference below can run after authentication (it reads
// HttpContext.User) and before the Razor Components render pipeline. Placed exactly where
// ASP.NET Core would otherwise auto-insert them (immediately before the first Map call) so
// this changes nothing about existing request handling/ordering above.
app.UseAuthentication();
app.UseAuthorization();
// Overrides RequestLocalizationMiddleware's negotiated culture with an authenticated user's
// own stored preference (see UserCulturePreferenceMiddlewareExtensions for why this must be
// real middleware and not something applied inside a component).
app.UseUserCulturePreference();

app.MapStaticAssets();
app.MapSeoEndpoints();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Map additional endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();

// Serve note images stored in the database.
app.MapNoteImageEndpoints();

// Serve completed Markdown/Obsidian vault exports (see MarkdownExportBackgroundService).
app.MapMarkdownExportEndpoints();

// Public, token-authenticated ICS calendar feed of deadlines (issue #314).
app.MapCalendarFeedEndpoints();

// Inbound billing-provider webhooks (plan/subscription state changes).
app.MapBillingWebhookEndpoints();

// CSV export of the internal analytics dashboard's aggregate metrics (issue #324).
app.MapAnalyticsExportEndpoints();

// Offline Lite (issue #302): Today snapshot + queued-capture sync, for the service worker's
// offline fallback page and the client-side capture queue.
app.MapOfflineEndpoints();

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
