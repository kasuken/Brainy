using System.IO.Compression;
using System.Reflection;
using System.Text;

namespace Brainy.Application.Tests.Services.ExternalImport;

/// <summary>Builds zip archives (including deliberately adversarial ones) for import tests.</summary>
internal static class ExternalImportTestZipBuilder
{
    public static byte[] Build(IReadOnlyDictionary<string, string> textEntries) =>
        Build(textEntries, new Dictionary<string, byte[]>());

    public static byte[] Build(
        IReadOnlyDictionary<string, string> textEntries,
        IReadOnlyDictionary<string, byte[]> binaryEntries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, text) in textEntries)
                WriteText(archive, path, text);

            foreach (var (path, data) in binaryEntries)
                WriteBinary(archive, path, data);
        }

        return stream.ToArray();
    }

    public static void WriteText(ZipArchive archive, string path, string text)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var entryStream = entry.Open();
        using var writer = new StreamWriter(entryStream, new UTF8Encoding(false));
        writer.Write(text);
    }

    public static void WriteBinary(ZipArchive archive, string path, byte[] data)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var entryStream = entry.Open();
        entryStream.Write(data, 0, data.Length);
    }

    /// <summary>Writes an entry and marks it as a Unix symlink via its external attributes.</summary>
    public static void WriteSymlink(ZipArchive archive, string path, string linkTarget)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.NoCompression);
        using (var entryStream = entry.Open())
        {
            var bytes = Encoding.UTF8.GetBytes(linkTarget);
            entryStream.Write(bytes, 0, bytes.Length);
        }

        // Unix mode bits live in the high 16 bits of ExternalAttributes; S_IFLNK (0xA000)
        // combined with 0777 permissions is what real zip tools (e.g. `zip -y`) write for a
        // symlink entry.
        const int symlinkModeBits = 0xA1FF << 16;
        entry.ExternalAttributes = symlinkModeBits;
    }

    /// <summary>Highly compressible bytes, sized to trip the compression-ratio zip-bomb guard.</summary>
    public static byte[] HighlyCompressibleBytes(int count) => new byte[count];
}
