namespace Brainy.Application.DTOs.Notes;

/// <summary>Classification of a single line in a <see cref="NoteRevisionDiffDto"/>.</summary>
public enum NoteRevisionDiffLineKind
{
    Unchanged,
    Added,
    Removed
}

/// <summary>One line of a line-based diff, tagged with how it differs between the two revisions.</summary>
public record NoteRevisionDiffLineDto(NoteRevisionDiffLineKind Kind, string Text);

/// <summary>Line-based diff between two revisions of the same note's title and content.</summary>
public record NoteRevisionDiffDto(
    Guid FromRevisionId,
    Guid ToRevisionId,
    IReadOnlyList<NoteRevisionDiffLineDto> TitleDiff,
    IReadOnlyList<NoteRevisionDiffLineDto> ContentDiff);
