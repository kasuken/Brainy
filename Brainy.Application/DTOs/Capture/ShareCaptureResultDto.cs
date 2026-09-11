using Brainy.Application.DTOs.Notes;

namespace Brainy.Application.DTOs.Capture;

/// <summary>How a capture request resolved against existing Inbox notes.</summary>
public enum ShareCaptureOutcome
{
    /// <summary>A new Inbox note was created.</summary>
    Created,

    /// <summary>
    /// An identical share was captured moments earlier (e.g. an OS share-sheet retry or the
    /// page's own prerender-then-interactive re-run); the existing note was returned untouched.
    /// </summary>
    DuplicateIgnored
}

/// <summary>Result of one <see cref="Brainy.Application.Interfaces.Services.IShareCaptureService"/> capture attempt.</summary>
public sealed record ShareCaptureResultDto(NoteDto Note, ShareCaptureOutcome Outcome);
