namespace Brainy.Application.DTOs.Outputs;

/// <summary>
/// Returned exactly once, immediately after enabling or regenerating an output's share link.
/// The Web layer combines <see cref="RawToken"/> with its own base URL to build the public
/// share URL; the raw value itself is never persisted, logged, or included in telemetry.
/// </summary>
public record OutputShareLinkCreatedDto(string RawToken, DateTime CreatedAtUtc, DateTime? ExpiresAtUtc);
