using Brainy.Application.DTOs.DataImport;

namespace Brainy.Application.Interfaces.Services;

/// <summary>
/// Imports notes for the current user from a non-Brainy source: an Obsidian vault, a
/// Notion "Markdown &amp; CSV" export, a generic folder of Markdown files, or an Evernote
/// <c>.enex</c> file. Unlike <see cref="IDataImportService"/> this is an onboarding path,
/// not a backup/restore tool: it never guesses a PARA category for imported content —
/// everything lands in the Inbox (see remarks on the implementation) so the user files it
/// deliberately — and it never fabricates structure (folders, dates, relationships) the
/// source did not actually contain.
/// </summary>
public interface IExternalImportService
{
    /// <summary>
    /// Parses the uploaded source and computes what an import would do — creates, reuses
    /// (duplicate-safe no-op), and skips, per entity type — without writing to the
    /// database. Use this to show the user a report before <see cref="ImportCurrentUserAsync"/>.
    /// </summary>
    Task<ExternalImportPreviewDto> PreviewImportAsync(
        ExternalImportSourceFormat format, Stream content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Parses the uploaded source and imports every note it contains for the current user
    /// inside one transaction: either the whole import commits, or none of it does.
    /// Re-importing the same source is safe and does not duplicate data.
    /// </summary>
    Task<ExternalImportResultDto> ImportCurrentUserAsync(
        ExternalImportSourceFormat format, Stream content, CancellationToken cancellationToken = default);
}
