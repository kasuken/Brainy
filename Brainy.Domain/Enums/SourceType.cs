namespace Brainy.Domain.Enums;

/// <summary>
/// The kind of origin a captured item came from.
/// </summary>
public enum SourceType
{
    Text = 0,
    Url = 1,
    Pdf = 2,
    Email = 3,
    MeetingNotes = 4,
    Document = 5,
    VoiceNote = 6,
    Image = 7,

    /// <summary>Captured via the PWA's Web Share Target route rather than pasted manually.</summary>
    SharedLink = 8,

    Other = 99
}
