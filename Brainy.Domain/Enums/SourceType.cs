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
    Other = 99
}
