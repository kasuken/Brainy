using Brainy.Application;
using Brainy.Application.Interfaces.Identity;
using Brainy.Data;
using Brainy.Data.Identity;
using Brainy.Web.Components;
using Brainy.Web.Components.Account;
using Brainy.Web.Endpoints;
using Brainy.Web.Identity;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);


// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();

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

// Data access layer (EF Core / SQL Server).
builder.Services.AddBrainyData(builder.Configuration);

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        // No confirmation email is required to sign in.
        options.SignIn.RequireConfirmedAccount = false;
    })
    .AddEntityFrameworkStores<BrainyDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

// Current-user accessor used by the application layer for per-user data scoping.
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

// Application-layer services.
builder.Services.AddBrainyApplication();

// Temporarily disable all AI features while preserving implementation for future re-enable.
builder.Services.AddDisabledAiAssistant();

// Liveness endpoint for the App Service health check. Intentionally has no
// database check so the probe does not keep the serverless SQL database awake.
builder.Services.AddHealthChecks();

var app = builder.Build();

// Apply any pending EF Core migrations on startup.
await DatabaseInitializer.MigrateAsync(app.Services, app.Environment.IsDevelopment());

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Map additional endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();

// Serve note images stored in the database.
app.MapNoteImageEndpoints();

app.MapHealthChecks("/health");

app.Run();
