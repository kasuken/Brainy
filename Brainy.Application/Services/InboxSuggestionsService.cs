using Brainy.Application.DTOs.Inbox;
using Brainy.Application.DTOs.Notes;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Enums;

namespace Brainy.Application.Services;

/// <summary>
/// Keyword-based heuristic for suggesting a PARA category for an inbox note.
/// Returns null when no category scores above the confidence threshold.
/// </summary>
internal sealed class InboxSuggestionsService : IInboxSuggestionsService
{
    private static readonly string[] ProjectKeywords =
    [
        "task", "action", "deadline", "by ", "due", "complete", "finish", "build",
        "create", "launch", "deliver", "submit", "meeting", "sprint", "milestone",
        "plan", "goal", "objective", "project", "todo", "ship"
    ];

    private static readonly string[] AreaKeywords =
    [
        "responsibility", "maintain", "ongoing", "manage", "review", "health",
        "finance", "fitness", "relationship", "work", "career", "habit",
        "routine", "practice", "standard", "policy", "process", "team"
    ];

    private static readonly string[] ResourceKeywords =
    [
        "article", "reference", "research", "learn", "book", "guide",
        "documentation", "how to", "tutorial", "course", "resource",
        "reading", "notes on", "summary of", "overview", "study", "source"
    ];

    private static readonly string[] ArchiveKeywords =
    [
        "old", "outdated", "completed", "done", "finished", "closed",
        "past", "archive", "historical", "deprecated", "no longer"
    ];

    public Task<InboxSuggestionDto?> SuggestAsync(NoteDto note, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var text = $"{note.Title} {note.Content}".ToLowerInvariant();

        var scores = new Dictionary<ParaCategory, (int Score, List<string> Matches)>
        {
            [ParaCategory.Project] = Score(text, ProjectKeywords),
            [ParaCategory.Area] = Score(text, AreaKeywords),
            [ParaCategory.Resource] = Score(text, ResourceKeywords),
            [ParaCategory.Archive] = Score(text, ArchiveKeywords)
        };

        var best = scores.OrderByDescending(kv => kv.Value.Score).First();

        if (best.Value.Score == 0)
        {
            return Task.FromResult<InboxSuggestionDto?>(null);
        }

        var matchedWords = string.Join(", ", best.Value.Matches.Take(3).Select(w => $"\"{w.Trim()}\""));
        var reasoning = $"Matched {best.Value.Score} {best.Key.ToString().ToLowerInvariant()} keyword{(best.Value.Score > 1 ? "s" : "")}: {matchedWords}.";

        return Task.FromResult<InboxSuggestionDto?>(
            new InboxSuggestionDto(best.Key, reasoning));
    }

    private static (int Score, List<string> Matches) Score(string text, string[] keywords)
    {
        var matches = keywords.Where(k => text.Contains(k, StringComparison.OrdinalIgnoreCase)).ToList();
        return (matches.Count, matches);
    }
}
