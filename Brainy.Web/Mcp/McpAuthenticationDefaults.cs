namespace Brainy.Web.Mcp;

/// <summary>
/// Names for the Model Context Protocol (MCP) bearer authentication scheme and its matching
/// authorization policy. The scheme resolves an <c>Authorization: Bearer</c> token to the
/// owning Brainy user (see <see cref="McpAuthenticationHandler"/>); the policy — applied to
/// the MCP endpoint with <see cref="McpAuthenticationExtensions.RequireMcpAuthorization{TBuilder}"/>
/// — requires that scheme <b>specifically</b>, so a login cookie can never authenticate an MCP
/// request and an MCP token can never authenticate a normal page or Blazor circuit.
/// </summary>
public static class McpAuthenticationDefaults
{
    /// <summary>The authentication scheme name for MCP bearer tokens.</summary>
    public const string AuthenticationScheme = "BrainyMcp";

    /// <summary>The authorization policy that requires a valid MCP bearer token.</summary>
    public const string AuthorizationPolicy = "BrainyMcp";
}
