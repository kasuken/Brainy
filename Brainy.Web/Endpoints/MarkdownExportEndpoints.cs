using System.Security.Claims;
using Brainy.Application.Interfaces.Services;

namespace Brainy.Web.Endpoints;

/// <summary>
/// HTTP endpoint that serves a completed Markdown/Obsidian vault export as a download,
/// once its background job has finished. See <see cref="MarkdownExportEndpoints"/>'s
/// counterpart, <c>NoteImageEndpoints</c>, for the same authenticated-download pattern.
/// </summary>
public static class MarkdownExportEndpoints
{
    public static IEndpointRouteBuilder MapMarkdownExportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/markdown-export/{jobId:guid}", (
            Guid jobId,
            HttpContext http,
            IMarkdownExportJobStore jobStore) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Results.Unauthorized();

            var file = jobStore.GetCompletedFile(jobId, userId);
            return file is null
                ? Results.NotFound()
                : Results.File(file.Content, file.ContentType, file.FileName);
        })
        .RequireAuthorization();

        return endpoints;
    }
}
