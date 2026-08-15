using Brainy.Application.DTOs.DataImport;

namespace Brainy.Application.Interfaces.Services;

/// <summary>Imports a safe subset of a current user's versioned Brainy data export.</summary>
public interface IDataImportService
{
    /// <summary>
    /// Validates the uploaded Brainy export schema and restores only the supported entities for
    /// the current user. Unsupported entities are counted and reported without being imported.
    /// </summary>
    Task<DataImportResultDto> ImportCurrentUserAsync(Stream content, CancellationToken cancellationToken = default);
}
