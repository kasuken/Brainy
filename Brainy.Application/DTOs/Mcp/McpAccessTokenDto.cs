namespace Brainy.Application.DTOs.Mcp;

/// <summary>
/// Token-management view of a single MCP access token's lifecycle. Never carries the raw
/// token — only <see cref="McpAccessTokenSecretDto"/> (returned once, at issue time) does.
/// </summary>
public record McpAccessTokenDto(
    Guid Id,
    string Name,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime? LastUsedAtUtc,
    DateTime? RevokedAtUtc);
