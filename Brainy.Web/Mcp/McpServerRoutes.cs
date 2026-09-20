namespace Brainy.Web.Mcp;

/// <summary>
/// The base path the Model Context Protocol endpoint is mapped at. Shared between the endpoint
/// mapping (<c>app.MapMcp</c>) and the rate limiter so the two never drift apart.
/// </summary>
public static class McpServerRoutes
{
    public const string BasePath = "/api/mcp";
}
