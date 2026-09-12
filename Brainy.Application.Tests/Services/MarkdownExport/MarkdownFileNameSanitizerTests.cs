using Brainy.Application.Services.MarkdownExport;
using AwesomeAssertions;
using Xunit;

namespace Brainy.Application.Tests.Services.MarkdownExport;

public class MarkdownFileNameSanitizerTests
{
    [Theory]
    [InlineData("Notes/Ideas", "Notes Ideas")]
    [InlineData(@"C:\temp\file", "C temp file")]
    [InlineData("What now?", "What now")]
    [InlineData("Is this * that?", "Is this that")]
    [InlineData("\"Quoted\" title", "Quoted title")]
    [InlineData("<tag> and </tag>", "tag and tag")]
    [InlineData("pipe|here", "pipe here")]
    public void Sanitize_RemovesReservedCharacters(string input, string expected)
    {
        MarkdownFileNameSanitizer.Sanitize(input).Should().Be(expected);
    }

    [Fact]
    public void Sanitize_KeepsEmoji()
    {
        MarkdownFileNameSanitizer.Sanitize("Launch 🚀 plan").Should().Be("Launch 🚀 plan");
    }

    [Theory]
    [InlineData("   leading and trailing   ", "leading and trailing")]
    [InlineData("...dotted...", "dotted")]
    [InlineData(". . leading dots", "leading dots")]
    [InlineData("trailing dot.", "trailing dot")]
    public void Sanitize_TrimsLeadingAndTrailingDotsAndSpaces(string input, string expected)
    {
        MarkdownFileNameSanitizer.Sanitize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("...")]
    [InlineData("///")]
    [InlineData(null)]
    public void Sanitize_FallsBackToUntitled_WhenNothingSurvives(string? input)
    {
        MarkdownFileNameSanitizer.Sanitize(input).Should().Be("untitled");
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("PRN")]
    [InlineData("AUX")]
    [InlineData("NUL")]
    [InlineData("COM1")]
    [InlineData("com9")]
    [InlineData("LPT1")]
    [InlineData("lpt9")]
    public void Sanitize_EscapesReservedWindowsDeviceNames(string reservedName)
    {
        var sanitized = MarkdownFileNameSanitizer.Sanitize(reservedName);

        sanitized.Should().NotBe(reservedName);
        MarkdownFileNameSanitizer.IsReservedDeviceName(sanitized).Should().BeFalse();
    }

    [Fact]
    public void Sanitize_DoesNotFlagNonReservedNamesThatContainDeviceNames()
    {
        // "CONSOLE" and "COMPANY" are not reserved device names, only the exact stems are.
        MarkdownFileNameSanitizer.Sanitize("CONSOLE").Should().Be("CONSOLE");
        MarkdownFileNameSanitizer.Sanitize("COMPANY").Should().Be("COMPANY");
    }

    [Fact]
    public void Sanitize_TruncatesOverlongTitles()
    {
        var longTitle = new string('a', 500);

        var sanitized = MarkdownFileNameSanitizer.Sanitize(longTitle);

        sanitized.Length.Should().BeLessThanOrEqualTo(MarkdownFileNameSanitizer.MaxStemLength);
        sanitized.Should().Be(new string('a', MarkdownFileNameSanitizer.MaxStemLength));
    }

    [Fact]
    public void Sanitize_TruncationDoesNotSplitASurrogatePair()
    {
        // 🚀 is a surrogate pair; pad it so it straddles the truncation boundary.
        var padding = new string('a', MarkdownFileNameSanitizer.MaxStemLength - 1);
        var title = padding + "🚀🚀🚀";

        var sanitized = MarkdownFileNameSanitizer.Sanitize(title);

        sanitized.Length.Should().BeLessThanOrEqualTo(MarkdownFileNameSanitizer.MaxStemLength);
        // A split surrogate pair would produce an unpaired lone surrogate as the last char.
        char.IsSurrogate(sanitized[^1]).Should().BeFalse();
    }

    [Fact]
    public void Sanitize_NeverReturnsEmptyString()
    {
        MarkdownFileNameSanitizer.Sanitize(new string('.', 50)).Should().NotBeNullOrEmpty();
    }
}
