using Brainy.Application.Services.MarkdownExport;
using AwesomeAssertions;
using Xunit;

namespace Brainy.Application.Tests.Services.MarkdownExport;

public class UniqueNameAllocatorTests
{
    [Fact]
    public void Allocate_ReturnsTheDesiredStem_WhenNotYetTaken()
    {
        var allocator = new UniqueNameAllocator();

        allocator.Allocate("notes", "Weekly Review").Should().Be("Weekly Review");
    }

    [Fact]
    public void Allocate_ResolvesDuplicatesWithANumericSuffix_InsteadOfOverwriting()
    {
        var allocator = new UniqueNameAllocator();

        var first = allocator.Allocate("notes", "Weekly Review");
        var second = allocator.Allocate("notes", "Weekly Review");
        var third = allocator.Allocate("notes", "Weekly Review");

        first.Should().Be("Weekly Review");
        second.Should().Be("Weekly Review (2)");
        third.Should().Be("Weekly Review (3)");

        // All three names remain distinct: nothing was overwritten.
        new[] { first, second, third }.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Allocate_TreatsNamesCaseInsensitively()
    {
        var allocator = new UniqueNameAllocator();

        var first = allocator.Allocate("notes", "Weekly Review");
        var second = allocator.Allocate("notes", "weekly review");

        first.Should().Be("Weekly Review");
        second.Should().Be("weekly review (2)");
    }

    [Fact]
    public void Allocate_KeepsScopesIndependent()
    {
        var allocator = new UniqueNameAllocator();

        var inProjects = allocator.Allocate("Projects", "Launch");
        var inAreas = allocator.Allocate("Areas", "Launch");

        inProjects.Should().Be("Launch");
        inAreas.Should().Be("Launch");
    }

    [Fact]
    public void Allocate_SkipsAlreadyReservedSuffixedNames()
    {
        var allocator = new UniqueNameAllocator();

        // Reserve "Launch (2)" first, out of band, before any duplicate resolution happens.
        allocator.Allocate("notes", "Launch (2)");

        var first = allocator.Allocate("notes", "Launch");
        var second = allocator.Allocate("notes", "Launch");
        var third = allocator.Allocate("notes", "Launch");

        first.Should().Be("Launch");
        second.Should().Be("Launch (3)"); // "Launch (2)" is already taken, so skip straight to (3).
        third.Should().Be("Launch (4)");
    }

    [Fact]
    public void Allocate_TruncatesBaseNameSoSuffixedNameStaysWithinLengthLimit()
    {
        var allocator = new UniqueNameAllocator();
        var longStem = new string('a', MarkdownFileNameSanitizer.MaxStemLength);

        allocator.Allocate("notes", longStem);
        var second = allocator.Allocate("notes", longStem);

        second.Should().EndWith(" (2)");
        second.Length.Should().BeLessThanOrEqualTo(MarkdownFileNameSanitizer.MaxStemLength + " (2)".Length);
    }
}
