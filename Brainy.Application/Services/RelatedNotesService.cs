using System.Text.RegularExpressions;
using Brainy.Application.Caching;
using Brainy.Application.DTOs.RelatedNotes;
using Brainy.Application.Interfaces.Caching;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

/// <summary>
/// Computes note similarity using word-level Jaccard similarity.
/// Title words are counted twice to give them higher weight.
/// No external AI calls — runs entirely in-process.
/// </summary>
internal sealed class RelatedNotesService(
    IApplicationDbContext context,
    ICurrentUserService currentUser,
    IApplicationCache cache) : IRelatedNotesService
{
    private const int MinScore = 0; // keep notes with any overlap

    /// <summary>
    /// Upper bound on candidate notes loaded for similarity scoring. Full note content
    /// is materialized per candidate, so the query is capped to the most recently
    /// updated notes to keep memory bounded for large knowledge bases.
    /// </summary>
    private const int MaxCandidates = 500;

    public async Task<IReadOnlyList<RelatedNoteDto>> GetRelatedAsync(
        Guid noteId,
        int topN = 5,
        CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await cache.GetOrCreateAsync(
            userId,
            $"related-notes:{noteId}:top:{topN}",
            [ApplicationCacheKey.EntityTypeTag<Note>()],
            ct => GetRelatedCoreAsync(noteId, userId, topN, ct),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<RelatedNoteDto>> GetRelatedCoreAsync(
        Guid noteId,
        string userId,
        int topN,
        CancellationToken cancellationToken)
    {
        var pivot = await context.Notes
            .AsNoTracking()
            .Where(n => n.Id == noteId && n.UserId == userId)
            .Select(n => new { n.Title, n.Content })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (pivot is null) return [];

        return await GetRelatedByContentAsync(
            noteId, pivot.Title, pivot.Content, topN, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RelatedNoteDto>> GetRelatedByContentAsync(
        Guid excludeNoteId,
        string title,
        string content,
        int topN = 5,
        CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var pivotTokens = Tokenize(title, content);
        if (pivotTokens.Count == 0) return [];

        var candidates = await context.Notes
            .AsNoTracking()
            .Where(n => n.UserId == userId && n.Id != excludeNoteId)
            .OrderByDescending(n => n.UpdatedAtUtc)
            .Take(MaxCandidates)
            .Select(n => new
            {
                n.Id,
                n.Title,
                n.Content,
                n.ParaCategory,
                n.Status,
                n.UpdatedAtUtc
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return candidates
            .Select(n =>
            {
                var tokens  = Tokenize(n.Title, n.Content);
                var score   = Jaccard(pivotTokens, tokens);
                return (n, score);
            })
            .Where(x => x.score > MinScore)
            .OrderByDescending(x => x.score)
            .Take(topN)
            .Select(x => new RelatedNoteDto(
                x.n.Id,
                x.n.Title,
                x.n.ParaCategory,
                x.n.Status,
                Math.Round(x.score, 3),
                x.n.UpdatedAtUtc))
            .ToList();
    }

    // ── Similarity helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Tokenises title (2×) + content into a multiset (Dictionary word→count).
    /// Strips Markdown syntax, lowercases, removes stop words and short tokens.
    /// </summary>
    private static Dictionary<string, int> Tokenize(string title, string content)
    {
        var bag = new Dictionary<string, int>(StringComparer.Ordinal);

        AddTokens(bag, title,   weight: 2);
        AddTokens(bag, content, weight: 1);

        return bag;
    }

    private static void AddTokens(Dictionary<string, int> bag, string text, int weight)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        // Strip common Markdown markers
        var clean = MarkdownStripper.Replace(text, " ");

        foreach (var raw in WordSplitter.Split(clean))
        {
            var word = raw.ToLowerInvariant();
            if (word.Length < 3 || StopWords.Contains(word)) continue;

            bag[word] = bag.TryGetValue(word, out var cur) ? cur + weight : weight;
        }
    }

    /// <summary>
    /// Jaccard similarity on the word sets (ignores counts — just presence).
    /// </summary>
    private static double Jaccard(Dictionary<string, int> a, Dictionary<string, int> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0;

        var intersect = 0;
        foreach (var key in a.Keys)
            if (b.ContainsKey(key)) intersect++;

        var union = a.Count + b.Count - intersect;
        return union == 0 ? 0 : (double)intersect / union;
    }

    private static readonly Regex MarkdownStripper =
        new(@"[#*_`~\[\]()>!|]+", RegexOptions.Compiled);

    private static readonly Regex WordSplitter =
        new(@"\W+", RegexOptions.Compiled);

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "the","and","for","are","but","not","you","all","can","had","her","was","one",
        "our","out","who","get","its","has","him","his","how","man","new","now","old",
        "see","two","way","may","did","let","put","say","she","too","use","that","this",
        "with","from","they","will","been","have","when","what","your","more","also",
        "into","than","then","them","were","these","there","their","would","could",
        "which","about","after","other","over","such","time","even","most","made",
        "some","only","very","just","first","much","well","like","make","take","know",
        "back","here","give","most","those","through","should","where","come","any",
        "both","each","many","same","does","each","been","being","while","own"
    };
}
