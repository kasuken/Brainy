namespace Brainy.Application.DTOs.Outputs;

/// <summary>
/// Everything the anonymous public share page (issue #319) is allowed to render. Deliberately
/// carries nothing beyond the output's own authored content: no owner, project, area, goal,
/// source-note, status, or provenance field belongs here — see
/// <c>OutputShareLinkService.ResolvePublicAsync</c> and the guardrails on the share-links
/// issue ("the shared page exposes only the output's own content").
/// </summary>
public record SharedOutputDto(string Title, string? Description, string Content);
