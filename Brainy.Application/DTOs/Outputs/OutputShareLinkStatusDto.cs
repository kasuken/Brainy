namespace Brainy.Application.DTOs.Outputs;

/// <summary>
/// Owner-facing view of one output's share link lifecycle. Never carries the raw token —
/// only <see cref="OutputShareLinkCreatedDto"/> (returned once, from enabling/regenerating)
/// does. <see cref="IsActive"/> is true only when the link exists, is not revoked, and has
/// not expired, exactly the condition the public page itself checks.
/// </summary>
public record OutputShareLinkStatusDto(
    bool IsActive,
    DateTime? CreatedAtUtc,
    DateTime? ExpiresAtUtc,
    DateTime? RevokedAtUtc,
    DateTime? LastAccessedAtUtc);
