using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace Brainy.Web.Mcp;

/// <summary>
/// Registration helpers for the MCP bearer authentication scheme and its authorization policy,
/// keeping the wiring in <c>Program.cs</c> to a single call on each side.
/// </summary>
public static class McpAuthenticationExtensions
{
    /// <summary>
    /// Adds the <see cref="McpAuthenticationDefaults.AuthenticationScheme"/> scheme, backed by
    /// <see cref="McpAuthenticationHandler"/>. Chain this onto the existing
    /// <c>AddAuthentication(...).AddIdentityCookies()</c> call; it does not change the default
    /// scheme, so cookie authentication for the Blazor app is unaffected.
    /// </summary>
    public static AuthenticationBuilder AddMcpAuthentication(this AuthenticationBuilder builder) =>
        builder.AddScheme<AuthenticationSchemeOptions, McpAuthenticationHandler>(
            McpAuthenticationDefaults.AuthenticationScheme,
            _ => { });

    /// <summary>
    /// Registers <see cref="McpAuthenticationDefaults.AuthorizationPolicy"/>, which requires an
    /// authenticated user <b>via the MCP scheme specifically</b>. Naming the scheme on the
    /// policy is what stops a login cookie from satisfying an MCP endpoint and vice versa.
    /// </summary>
    public static IServiceCollection AddMcpAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(McpAuthenticationDefaults.AuthorizationPolicy, policy => policy
                .AddAuthenticationSchemes(McpAuthenticationDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser());

        return services;
    }

    /// <summary>
    /// Applies the MCP authorization policy to an endpoint (or endpoint group), so only a
    /// request carrying a valid MCP bearer token reaches it. Used in Step 3 on the mapped MCP
    /// endpoint: <c>app.MapMcp("/api/mcp").RequireMcpAuthorization()</c>.
    /// </summary>
    public static TBuilder RequireMcpAuthorization<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(McpAuthenticationDefaults.AuthorizationPolicy);
}
