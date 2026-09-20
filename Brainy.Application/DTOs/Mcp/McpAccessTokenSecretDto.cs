namespace Brainy.Application.DTOs.Mcp;

/// <summary>
/// Returned exactly once, immediately after issuing an MCP access token. The Web layer shows
/// <see cref="RawToken"/> to the user so they can paste it into their MCP client's
/// <c>Authorization: Bearer</c> configuration; the raw value itself is never persisted,
/// logged, or included in telemetry, and cannot be retrieved again afterwards.
/// </summary>
public record McpAccessTokenSecretDto(
    Guid Id,
    string Name,
    string RawToken,
    DateTime CreatedAtUtc);
