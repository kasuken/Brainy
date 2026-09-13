namespace Brainy.Application.Services.Templates;

/// <summary>
/// Resolves the small set of tokens a template's name/title pattern may contain against
/// the user's own calendar date (never UTC — see AGENTS.md) at instantiation time.
/// </summary>
internal static class TemplatePatternResolver
{
    public static string Resolve(string pattern, DateTime userToday) =>
        pattern
            .Replace("{Date}", userToday.ToString("MMM d, yyyy"), StringComparison.Ordinal)
            .Replace("{Year}", userToday.Year.ToString(), StringComparison.Ordinal);
}
