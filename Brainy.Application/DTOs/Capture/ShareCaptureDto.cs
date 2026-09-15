using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.Capture;

/// <summary>
/// Raw input for one capture attempt from the PWA's Web Share Target route or the same
/// route's manual "type something and save" form. Values are exactly as supplied by the
/// browser/OS share sheet or the user; <see cref="IShareCaptureService"/> owns trimming,
/// length validation and de-duplication of it.
/// </summary>
/// <param name="ChangeReason">
/// Why the resulting note's initial revision is being captured. Defaults to
/// <see cref="NoteRevisionReason.UserEdit"/> when not supplied; the offline-sync flush path
/// passes <see cref="NoteRevisionReason.OfflineSync"/> instead.
/// </param>
public sealed record ShareCaptureDto(
    string? Title, string? Text, string? Url, NoteRevisionReason? ChangeReason = null);
