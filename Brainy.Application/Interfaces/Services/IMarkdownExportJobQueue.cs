namespace Brainy.Application.Interfaces.Services;

/// <summary>
/// A single request to build one user's Markdown/Obsidian vault in the background.
/// </summary>
public sealed record MarkdownExportJobRequest(Guid JobId, string UserId);

/// <summary>
/// Process-local queue of pending Markdown export jobs. The Web host's background
/// service consumes it; <see cref="IMarkdownExportJobService"/> produces to it.
/// Registered as a singleton — see <see cref="IMarkdownExportJobStore"/> for why job
/// state itself is deliberately in-memory rather than a new database table.
/// </summary>
public interface IMarkdownExportJobQueue
{
    /// <summary>Enqueues a job for background processing.</summary>
    void Enqueue(MarkdownExportJobRequest request);

    /// <summary>
    /// Reads jobs as they are enqueued. Completes only when <paramref name="cancellationToken"/>
    /// is cancelled (host shutdown); intended for a single long-running consumer loop.
    /// </summary>
    IAsyncEnumerable<MarkdownExportJobRequest> ReadAllAsync(CancellationToken cancellationToken);
}
