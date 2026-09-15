using AwesomeAssertions;
using Brainy.Application.Common;
using Brainy.Application.DTOs.Notes;
using Xunit;

namespace Brainy.Application.Tests.Common;

public class TextDiffTests
{
    [Fact]
    public void ComputeLineDiff_WithIdenticalText_ReturnsAllUnchanged()
    {
        var diff = TextDiff.ComputeLineDiff("a\nb\nc", "a\nb\nc");

        diff.Should().OnlyContain(line => line.Kind == NoteRevisionDiffLineKind.Unchanged);
        diff.Select(line => line.Text).Should().Equal("a", "b", "c");
    }

    [Fact]
    public void ComputeLineDiff_WithAppendedLine_MarksOnlyTheNewLineAsAdded()
    {
        var diff = TextDiff.ComputeLineDiff("a\nb", "a\nb\nc");

        diff.Should().HaveCount(3);
        diff[0].Should().Be(new NoteRevisionDiffLineDto(NoteRevisionDiffLineKind.Unchanged, "a"));
        diff[1].Should().Be(new NoteRevisionDiffLineDto(NoteRevisionDiffLineKind.Unchanged, "b"));
        diff[2].Should().Be(new NoteRevisionDiffLineDto(NoteRevisionDiffLineKind.Added, "c"));
    }

    [Fact]
    public void ComputeLineDiff_WithRemovedLine_MarksItRemoved()
    {
        var diff = TextDiff.ComputeLineDiff("a\nb\nc", "a\nc");

        diff.Select(l => (l.Kind, l.Text)).Should().Equal(
            (NoteRevisionDiffLineKind.Unchanged, "a"),
            (NoteRevisionDiffLineKind.Removed, "b"),
            (NoteRevisionDiffLineKind.Unchanged, "c"));
    }

    [Fact]
    public void ComputeLineDiff_WithReplacedLine_MarksBothTheRemovalAndTheAddition()
    {
        var diff = TextDiff.ComputeLineDiff("a\nb\nc", "a\nx\nc");

        diff.Should().HaveCount(4);
        diff.Should().Contain(new NoteRevisionDiffLineDto(NoteRevisionDiffLineKind.Unchanged, "a"));
        diff.Should().Contain(new NoteRevisionDiffLineDto(NoteRevisionDiffLineKind.Removed, "b"));
        diff.Should().Contain(new NoteRevisionDiffLineDto(NoteRevisionDiffLineKind.Added, "x"));
        diff.Should().Contain(new NoteRevisionDiffLineDto(NoteRevisionDiffLineKind.Unchanged, "c"));
        diff[0].Should().Be(new NoteRevisionDiffLineDto(NoteRevisionDiffLineKind.Unchanged, "a"));
        diff[3].Should().Be(new NoteRevisionDiffLineDto(NoteRevisionDiffLineKind.Unchanged, "c"));
    }

    [Fact]
    public void ComputeLineDiff_WithEmptyOldText_MarksEveryLineAdded()
    {
        var diff = TextDiff.ComputeLineDiff("", "a\nb");

        diff.Should().OnlyContain(line => line.Kind == NoteRevisionDiffLineKind.Added);
        diff.Select(line => line.Text).Should().Equal("a", "b");
    }

    [Fact]
    public void ComputeLineDiff_WithEmptyNewText_MarksEveryLineRemoved()
    {
        var diff = TextDiff.ComputeLineDiff("a\nb", "");

        diff.Should().OnlyContain(line => line.Kind == NoteRevisionDiffLineKind.Removed);
        diff.Select(line => line.Text).Should().Equal("a", "b");
    }

    [Fact]
    public void ComputeLineDiff_NormalizesWindowsLineEndings()
    {
        var diff = TextDiff.ComputeLineDiff("a\r\nb", "a\nb");

        diff.Should().OnlyContain(line => line.Kind == NoteRevisionDiffLineKind.Unchanged);
    }
}
