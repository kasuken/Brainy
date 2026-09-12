using Brainy.Application.DTOs.DataExport;

namespace Brainy.Application.Interfaces.Services;

/// <summary>
/// Builds a Markdown/Obsidian-compatible vault (a zip of folders and <c>.md</c> files) for
/// one user's data. Complements <see cref="IDataExportService"/>: this format is for reading
/// and using the export elsewhere (e.g. opening it in Obsidian), not for round-tripping back
/// into Brainy — JSON stays the round-trip format.
/// </summary>
public interface IMarkdownExportService
{
    /// <summary>
    /// Builds the vault for <paramref name="userId"/> directly, without going through
    /// <see cref="Identity.ICurrentUserService"/>. Callers running outside a request/circuit
    /// context (a background job) can supply the id they already resolved when the job was
    /// queued; request-scoped callers should prefer resolving the current user first and
    /// then calling this with that id, exactly as <see cref="IDataExportService"/> does
    /// internally for its own current-user variant.
    /// </summary>
    Task<MarkdownExportFileDto> ExportUserAsync(string userId, CancellationToken cancellationToken = default);
}
