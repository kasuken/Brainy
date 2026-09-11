namespace Brainy.Application.DTOs.Capture;

/// <summary>
/// Raw input for one capture attempt from the PWA's Web Share Target route or the same
/// route's manual "type something and save" form. Values are exactly as supplied by the
/// browser/OS share sheet or the user; <see cref="IShareCaptureService"/> owns trimming,
/// length validation and de-duplication of it.
/// </summary>
public sealed record ShareCaptureDto(string? Title, string? Text, string? Url);
