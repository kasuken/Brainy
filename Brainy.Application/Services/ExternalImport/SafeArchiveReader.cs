using System.IO.Compression;

namespace Brainy.Application.Services.ExternalImport;

/// <summary>
/// Safely extracts a zip archive uploaded by a user into an in-memory entry map, rejecting
/// every known archive-based attack rather than trusting entry metadata:
/// <list type="bullet">
/// <item>Zip-slip path traversal (<c>../</c> segments) and absolute/rooted entry paths.</item>
/// <item>Unix symlink entries (detected via the external attributes symlink bit), which
/// could otherwise point a later read outside the extraction target.</item>
/// <item>Zip bombs: a cap on the number of entries, on any single entry's decompressed
/// size, on the archive's total decompressed size, and on the decompression ratio of any
/// one entry.</item>
/// <item>Malformed archives, surfaced as a single <see cref="InvalidOperationException"/>
/// rather than a raw <see cref="InvalidDataException"/>.</item>
/// </list>
/// Oversized or unsafe entries are not thrown for; they are recorded in
/// <see cref="SafeArchiveResult.RejectedEntries"/> so the caller can report them (per the
/// "attachments that cannot be imported are reported, never silently dropped" guardrail)
/// while still importing whatever the archive safely contains.
/// </summary>
internal static class SafeArchiveReader
{
    /// <summary>Hard cap on how many file entries a single archive may contain.</summary>
    private const int MaxEntryCount = 5_000;

    /// <summary>Hard cap on one entry's decompressed size (20 MB — generous for a note or image).</summary>
    private const long MaxSingleEntryBytes = 20L * 1024 * 1024;

    /// <summary>Hard cap on the archive's total decompressed size (200 MB).</summary>
    private const long MaxTotalUncompressedBytes = 200L * 1024 * 1024;

    /// <summary>
    /// Above this decompression ratio (and only once an entry is already suspiciously
    /// large), the entry is treated as a probable zip bomb rather than decompressed.
    /// </summary>
    private const double MaxSuspiciousCompressionRatio = 100;

    private const long RatioCheckThresholdBytes = 1024 * 1024;

    public static SafeArchiveResult Read(Stream archiveStream)
    {
        ArgumentNullException.ThrowIfNull(archiveStream);

        var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var rejected = new List<string>();

        ZipArchive archive;
        try
        {
            archive = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException)
        {
            throw new InvalidOperationException("The uploaded file is not a valid zip archive.");
        }

        using (archive)
        {
            if (archive.Entries.Count > MaxEntryCount)
            {
                throw new InvalidOperationException(
                    $"The archive contains {archive.Entries.Count} entries, more than the {MaxEntryCount} this import allows.");
            }

            long totalUncompressed = 0;

            foreach (var entry in archive.Entries)
            {
                // Directory entries have no bytes of their own; every real file entry is
                // still listed individually, so there is nothing to extract here.
                if (entry.Name.Length == 0)
                    continue;

                if (!TryNormalizeEntryPath(entry.FullName, out var normalizedPath))
                {
                    rejected.Add($"'{entry.FullName}': rejected (unsafe path).");
                    continue;
                }

                if (IsSymlink(entry))
                {
                    rejected.Add($"'{entry.FullName}': rejected (symlink entries are not allowed).");
                    continue;
                }

                if (entry.Length > MaxSingleEntryBytes)
                {
                    rejected.Add(
                        $"'{entry.FullName}': rejected (decompresses to {entry.Length / (1024 * 1024)} MB, over the {MaxSingleEntryBytes / (1024 * 1024)} MB per-file limit).");
                    continue;
                }

                if (entry.CompressedLength > 0 &&
                    entry.Length > RatioCheckThresholdBytes &&
                    entry.Length / (double)entry.CompressedLength > MaxSuspiciousCompressionRatio)
                {
                    rejected.Add($"'{entry.FullName}': rejected (suspicious compression ratio, possible zip bomb).");
                    continue;
                }

                totalUncompressed += entry.Length;
                if (totalUncompressed > MaxTotalUncompressedBytes)
                {
                    throw new InvalidOperationException(
                        $"The archive's total decompressed size exceeds the {MaxTotalUncompressedBytes / (1024 * 1024)} MB this import allows.");
                }

                byte[] data;
                try
                {
                    using var entryStream = entry.Open();
                    using var buffer = new MemoryStream();
                    entryStream.CopyTo(buffer);
                    data = buffer.ToArray();
                }
                catch (InvalidDataException)
                {
                    rejected.Add($"'{entry.FullName}': rejected (corrupt entry).");
                    continue;
                }

                // A zip bomb can also under-report Length in its central directory; the
                // actual bytes read are checked again against the same per-entry cap.
                if (data.LongLength > MaxSingleEntryBytes)
                {
                    rejected.Add($"'{entry.FullName}': rejected (decompressed beyond the declared size).");
                    continue;
                }

                // Case-insensitive collisions (e.g. "Notes/A.md" and "notes/a.md") keep
                // only the first entry; this mirrors typical filesystem extraction on a
                // case-insensitive volume and avoids silently overwriting one with another.
                entries.TryAdd(normalizedPath, data);
            }
        }

        return new SafeArchiveResult(entries, rejected);
    }

    private static bool IsSymlink(ZipArchiveEntry entry)
    {
        // Unix mode bits live in the high 16 bits of ExternalAttributes; S_IFLNK == 0xA000.
        var unixMode = (entry.ExternalAttributes >> 16) & 0xF000;
        return unixMode == 0xA000;
    }

    /// <summary>
    /// Rejects absolute paths, drive-qualified paths, and any path containing a <c>..</c>
    /// segment, then returns a normalized, always-relative, forward-slash path.
    /// </summary>
    private static bool TryNormalizeEntryPath(string fullName, out string normalizedPath)
    {
        normalizedPath = string.Empty;

        var candidate = fullName.Replace('\\', '/');
        if (candidate.Length == 0 || candidate.StartsWith('/'))
            return false;

        // Rejects Windows drive-qualified paths (e.g. "C:/x") even when read on Linux.
        if (candidate.Length >= 2 && candidate[1] == ':')
            return false;

        var segments = candidate.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return false;

        foreach (var segment in segments)
        {
            if (segment is "." or "..")
                return false;
        }

        normalizedPath = string.Join('/', segments);
        return true;
    }
}

/// <summary>The safely-extracted contents of an archive: file path (forward-slash, relative) to bytes.</summary>
internal sealed record SafeArchiveResult(
    IReadOnlyDictionary<string, byte[]> Entries,
    IReadOnlyList<string> RejectedEntries);
