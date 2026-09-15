using System.IO.Compression;
using System.Text;
using AwesomeAssertions;
using Brainy.Application.Services.ExternalImport;
using Xunit;

namespace Brainy.Application.Tests.Services.ExternalImport;

/// <summary>
/// Proves every archive-based attack this import surface must reject (issue #312's
/// "malformed, oversized and adversarial archives ... rejected safely" acceptance
/// criterion): zip-slip traversal, absolute paths, symlink entries, oversized entries,
/// zip bombs (suspicious compression ratio), too many entries, and corrupt zip data.
/// Safe entries are never thrown away silently — they are still extracted alongside a
/// rejected one, and the rejection is reported.
/// </summary>
public sealed class SafeArchiveReaderTests
{
    [Fact]
    public void Read_WithPathTraversalEntry_RejectsItButKeepsSafeEntries()
    {
        var bytes = ExternalImportTestZipBuilder.Build(new Dictionary<string, string>
        {
            ["Notes/good.md"] = "# Good\nSafe content.",
            ["../../etc/evil.md"] = "# Evil\nShould never land outside the vault."
        });

        var result = SafeArchiveReader.Read(new MemoryStream(bytes));

        result.Entries.Should().ContainKey("Notes/good.md");
        result.Entries.Keys.Should().NotContain(k => k.Contains("..", StringComparison.Ordinal));
        result.RejectedEntries.Should().ContainSingle(r => r.Contains("unsafe path", StringComparison.Ordinal));
    }

    [Fact]
    public void Read_WithAbsolutePathEntry_RejectsIt()
    {
        var bytes = ExternalImportTestZipBuilder.Build(new Dictionary<string, string>
        {
            ["/etc/passwd.md"] = "# root\nabsolute path entry"
        });

        var result = SafeArchiveReader.Read(new MemoryStream(bytes));

        result.Entries.Should().BeEmpty();
        result.RejectedEntries.Should().ContainSingle(r => r.Contains("unsafe path", StringComparison.Ordinal));
    }

    [Fact]
    public void Read_WithWindowsDriveQualifiedEntry_RejectsIt()
    {
        var bytes = ExternalImportTestZipBuilder.Build(new Dictionary<string, string>
        {
            ["C:/Windows/evil.md"] = "# evil"
        });

        var result = SafeArchiveReader.Read(new MemoryStream(bytes));

        result.Entries.Should().BeEmpty();
        result.RejectedEntries.Should().ContainSingle(r => r.Contains("unsafe path", StringComparison.Ordinal));
    }

    [Fact]
    public void Read_WithSymlinkEntry_RejectsIt()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            ExternalImportTestZipBuilder.WriteText(archive, "Notes/good.md", "# Good");
            ExternalImportTestZipBuilder.WriteSymlink(archive, "Notes/evil-link.md", "/etc/passwd");
        }

        var result = SafeArchiveReader.Read(new MemoryStream(stream.ToArray()));

        result.Entries.Should().ContainKey("Notes/good.md");
        result.Entries.Should().NotContainKey("Notes/evil-link.md");
        result.RejectedEntries.Should().ContainSingle(r => r.Contains("symlink", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Read_WithOversizedEntry_RejectsItButKeepsSafeEntries()
    {
        var oversized = new byte[21 * 1024 * 1024];
        Random.Shared.NextBytes(oversized); // incompressible, so it is the size cap (not the ratio guard) that trips.

        var bytes = ExternalImportTestZipBuilder.Build(
            new Dictionary<string, string> { ["Notes/good.md"] = "# Good" },
            new Dictionary<string, byte[]> { ["attachments/huge.png"] = oversized });

        var result = SafeArchiveReader.Read(new MemoryStream(bytes));

        result.Entries.Should().ContainKey("Notes/good.md");
        result.Entries.Should().NotContainKey("attachments/huge.png");
        result.RejectedEntries.Should().ContainSingle(r => r.Contains("per-file limit", StringComparison.Ordinal));
    }

    [Fact]
    public void Read_WithSuspiciousCompressionRatio_RejectsItAsAPossibleZipBomb()
    {
        var bytes = ExternalImportTestZipBuilder.Build(
            new Dictionary<string, string> { ["Notes/good.md"] = "# Good" },
            new Dictionary<string, byte[]> { ["attachments/bomb.png"] = ExternalImportTestZipBuilder.HighlyCompressibleBytes(5 * 1024 * 1024) });

        var result = SafeArchiveReader.Read(new MemoryStream(bytes));

        result.Entries.Should().ContainKey("Notes/good.md");
        result.Entries.Should().NotContainKey("attachments/bomb.png");
        result.RejectedEntries.Should().ContainSingle(r => r.Contains("zip bomb", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Read_WithTooManyEntries_ThrowsAndRejectsTheWholeArchive()
    {
        var entries = new Dictionary<string, string>();
        for (var i = 0; i < 5001; i++)
            entries[$"Notes/note-{i}.md"] = "x";

        var bytes = ExternalImportTestZipBuilder.Build(entries);

        var act = () => SafeArchiveReader.Read(new MemoryStream(bytes));

        act.Should().Throw<InvalidOperationException>().WithMessage("*more than*");
    }

    [Fact]
    public void Read_WithMalformedArchive_ThrowsAClearError()
    {
        var garbage = Encoding.UTF8.GetBytes("this is not a zip file, just plain garbage bytes");

        var act = () => SafeArchiveReader.Read(new MemoryStream(garbage));

        act.Should().Throw<InvalidOperationException>().WithMessage("*not a valid zip archive*");
    }

    [Fact]
    public void Read_WithSafeVault_ExtractsEveryEntry()
    {
        var bytes = ExternalImportTestZipBuilder.Build(new Dictionary<string, string>
        {
            ["Notes/a.md"] = "# A",
            ["Notes/sub/b.md"] = "# B"
        });

        var result = SafeArchiveReader.Read(new MemoryStream(bytes));

        result.Entries.Should().HaveCount(2);
        result.RejectedEntries.Should().BeEmpty();
    }
}
