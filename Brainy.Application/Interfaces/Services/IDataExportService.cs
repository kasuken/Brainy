using Brainy.Application.DTOs.DataExport;

namespace Brainy.Application.Interfaces.Services;

/// <summary>Creates a portable archive containing only the current user's Brainy data.</summary>
public interface IDataExportService
{
    /// <summary>
    /// Stable schema identifier. Breaking changes require a new major version; additive
    /// backward-compatible changes require a new minor version.
    /// </summary>
    const string SchemaVersion = "1.1";

    /// <summary>Builds a UTF-8 JSON archive without Identity credentials or application secrets.</summary>
    Task<DataExportFileDto> ExportCurrentUserAsync(CancellationToken cancellationToken = default);
}
