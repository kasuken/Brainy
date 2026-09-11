using Brainy.Application.DTOs.DataImport;

namespace Brainy.Application.Interfaces.Services;

/// <summary>
/// Imports a current user's versioned Brainy data export, restoring every entity type
/// the export contains (excluding append-only audit ledgers, which are intentionally
/// not replayed — see remarks on the implementation).
/// </summary>
public interface IDataImportService
{
    /// <summary>
    /// Validates the uploaded export and computes what an import would do — creates,
    /// reuses (duplicate-safe no-op), and skips, per entity type — without writing to
    /// the database. Use this to show the user a report before <see cref="ImportCurrentUserAsync"/>.
    /// </summary>
    Task<DataImportPreviewDto> PreviewImportAsync(Stream content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates the uploaded Brainy export schema and restores every supported entity
    /// for the current user inside one transaction: either the whole import commits, or
    /// none of it does. Re-importing the same export is safe and does not duplicate data.
    /// </summary>
    Task<DataImportResultDto> ImportCurrentUserAsync(Stream content, CancellationToken cancellationToken = default);
}
