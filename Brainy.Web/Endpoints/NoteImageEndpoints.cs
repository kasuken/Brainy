using System.Security.Claims;
using Brainy.Application.Interfaces.Services;

namespace Brainy.Web.Endpoints;

/// <summary>
/// HTTP endpoints for serving note images stored in the database. Images are referenced
/// from note Markdown as <c>/api/note-images/{id}</c>.
/// </summary>
public static class NoteImageEndpoints
{
    public static IEndpointRouteBuilder MapNoteImageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/note-images/{id:guid}", async (
            Guid id,
            HttpContext http,
            INoteImageService imageService,
            CancellationToken cancellationToken) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Results.Unauthorized();

            var image = await imageService.GetContentAsync(id, userId, cancellationToken);
            return image is null
                ? Results.NotFound()
                : Results.File(image.Data, image.ContentType, image.FileName);
        })
        .RequireAuthorization();

        return endpoints;
    }
}
