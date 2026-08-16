using Brainy.Application.DTOs.Llm;

namespace Brainy.Application.Interfaces.Services;

/// <summary>Creates a minimal focus-planning snapshot for the current user.</summary>
public interface ILlmFocusExportService
{
    const string SchemaVersion = "1.0";

    Task<LlmFocusExportFileDto> ExportCurrentUserAsync(CancellationToken cancellationToken = default);
}
