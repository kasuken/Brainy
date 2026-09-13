using Brainy.Application.DTOs.Notes;

namespace Brainy.Application.Common;

/// <summary>
/// Minimal line-based diff (longest common subsequence) used to compare two note
/// revisions. Deliberately dependency-free: revision text is user content, and this
/// avoids pulling in a third-party diff library for what is a small, well-understood
/// algorithm at note-sized inputs.
/// </summary>
internal static class TextDiff
{
    /// <summary>
    /// Computes a line-based diff between <paramref name="oldText"/> and <paramref name="newText"/>.
    /// Lines common to both (in order) are <see cref="NoteRevisionDiffLineKind.Unchanged"/>;
    /// lines only in <paramref name="oldText"/> are <see cref="NoteRevisionDiffLineKind.Removed"/>;
    /// lines only in <paramref name="newText"/> are <see cref="NoteRevisionDiffLineKind.Added"/>.
    /// </summary>
    public static IReadOnlyList<NoteRevisionDiffLineDto> ComputeLineDiff(string oldText, string newText)
    {
        var oldLines = SplitLines(oldText);
        var newLines = SplitLines(newText);

        var lcsLengths = BuildLcsLengthTable(oldLines, newLines);

        var result = new List<NoteRevisionDiffLineDto>(oldLines.Count + newLines.Count);
        WalkForward(oldLines, newLines, lcsLengths, result);
        return result;
    }

    private static List<string> SplitLines(string text) =>
        string.IsNullOrEmpty(text)
            ? []
            : text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();

    private static int[,] BuildLcsLengthTable(List<string> oldLines, List<string> newLines)
    {
        var lengths = new int[oldLines.Count + 1, newLines.Count + 1];
        for (var i = oldLines.Count - 1; i >= 0; i--)
        {
            for (var j = newLines.Count - 1; j >= 0; j--)
            {
                lengths[i, j] = string.Equals(oldLines[i], newLines[j], StringComparison.Ordinal)
                    ? lengths[i + 1, j + 1] + 1
                    : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
            }
        }

        return lengths;
    }

    private static void WalkForward(
        List<string> oldLines,
        List<string> newLines,
        int[,] lengths,
        List<NoteRevisionDiffLineDto> result)
    {
        int i = 0, j = 0;
        while (true)
        {
            if (i < oldLines.Count && j < newLines.Count &&
                string.Equals(oldLines[i], newLines[j], StringComparison.Ordinal))
            {
                result.Add(new NoteRevisionDiffLineDto(NoteRevisionDiffLineKind.Unchanged, oldLines[i]));
                i++; j++;
                continue;
            }

            if (j < newLines.Count && (i == oldLines.Count || lengths[i, j + 1] >= lengths[i + 1, j]))
            {
                result.Add(new NoteRevisionDiffLineDto(NoteRevisionDiffLineKind.Added, newLines[j]));
                j++;
                continue;
            }

            if (i < oldLines.Count)
            {
                result.Add(new NoteRevisionDiffLineDto(NoteRevisionDiffLineKind.Removed, oldLines[i]));
                i++;
                continue;
            }

            return;
        }
    }
}
