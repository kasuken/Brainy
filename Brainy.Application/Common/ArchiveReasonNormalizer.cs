namespace Brainy.Application.Common;

internal static class ArchiveReasonNormalizer
{
    internal const int MaxLength = 2000;

    public static string? Normalize(string? archivedReason)
    {
        if (string.IsNullOrWhiteSpace(archivedReason))
        {
            return null;
        }

        var normalized = archivedReason.Trim();
        if (normalized.Length > MaxLength)
        {
            throw new ArgumentException($"Archive reason cannot exceed {MaxLength} characters.", nameof(archivedReason));
        }

        return normalized;
    }
}
