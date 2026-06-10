using Brainy.Application.DTOs.Inbox;
using Brainy.Application.DTOs.Notes;

namespace Brainy.Application.Interfaces.Services;

/// <summary>Suggests a PARA destination for an inbox note based on its content.</summary>
public interface IInboxSuggestionsService
{
    /// <summary>Returns a suggestion or null if no confident match found.</summary>
    Task<InboxSuggestionDto?> SuggestAsync(NoteDto note, CancellationToken cancellationToken = default);
}
