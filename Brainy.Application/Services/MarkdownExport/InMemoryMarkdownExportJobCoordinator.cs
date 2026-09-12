using System.Collections.Concurrent;
using System.Threading.Channels;
using Brainy.Application.DTOs.DataExport;
using Brainy.Application.Interfaces.Services;

namespace Brainy.Application.Services.MarkdownExport;

/// <summary>
/// Single process-local implementation of both the job queue and the job status/result
/// store for Markdown export jobs. One instance, registered as a singleton, so the
/// background consumer and every request-scoped <see cref="IMarkdownExportJobService"/>
/// see the same state. See <see cref="IMarkdownExportJobStore"/> for why this is
/// deliberately in-memory rather than a new database table.
/// </summary>
internal sealed class InMemoryMarkdownExportJobCoordinator(TimeProvider timeProvider)
    : IMarkdownExportJobQueue, IMarkdownExportJobStore
{
    // Generous but bounded: a runaway producer (a UI bug retrying StartExportAsync) fills
    // this and then blocks rather than growing memory without limit.
    private readonly Channel<MarkdownExportJobRequest> _channel =
        Channel.CreateBounded<MarkdownExportJobRequest>(new BoundedChannelOptions(64)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

    private readonly ConcurrentDictionary<Guid, JobRecord> _jobs = new();

    // A job's file can be sizeable (images included); this bound keeps completed-but-never
    // downloaded exports from accumulating in memory forever without needing a timer thread.
    private static readonly TimeSpan RetentionPeriod = TimeSpan.FromHours(2);

    public void Enqueue(MarkdownExportJobRequest request)
    {
        if (!_channel.Writer.TryWrite(request))
            throw new InvalidOperationException("The Markdown export queue is full. Try again shortly.");
    }

    public IAsyncEnumerable<MarkdownExportJobRequest> ReadAllAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);

    public MarkdownExportJobStatusDto CreateQueued(Guid jobId, string userId, DateTime createdAtUtc)
    {
        var record = new JobRecord(jobId, userId, createdAtUtc);
        _jobs[jobId] = record;
        PruneExpired();
        return record.ToStatusDto();
    }

    public MarkdownExportJobStatusDto? FindActiveJobForUser(string userId)
    {
        PruneExpired();
        return _jobs.Values
            .Where(job => job.UserId == userId &&
                          job.State is MarkdownExportJobState.Queued or MarkdownExportJobState.Running)
            .OrderByDescending(job => job.CreatedAtUtc)
            .Select(job => job.ToStatusDto())
            .FirstOrDefault();
    }

    public MarkdownExportJobStatusDto? GetStatus(Guid jobId, string userId)
        => _jobs.TryGetValue(jobId, out var record) && record.UserId == userId
            ? record.ToStatusDto()
            : null;

    public MarkdownExportFileDto? GetCompletedFile(Guid jobId, string userId)
        => _jobs.TryGetValue(jobId, out var record) &&
           record.UserId == userId &&
           record.State == MarkdownExportJobState.Completed
            ? record.File
            : null;

    public void MarkRunning(Guid jobId)
    {
        if (_jobs.TryGetValue(jobId, out var record))
            record.State = MarkdownExportJobState.Running;
    }

    public void MarkCompleted(Guid jobId, MarkdownExportFileDto file, DateTime completedAtUtc)
    {
        if (!_jobs.TryGetValue(jobId, out var record))
            return;

        record.State = MarkdownExportJobState.Completed;
        record.File = file;
        record.CompletedAtUtc = completedAtUtc;
    }

    public void MarkFailed(Guid jobId, string errorMessage, DateTime completedAtUtc)
    {
        if (!_jobs.TryGetValue(jobId, out var record))
            return;

        record.State = MarkdownExportJobState.Failed;
        record.ErrorMessage = errorMessage;
        record.CompletedAtUtc = completedAtUtc;
    }

    private void PruneExpired()
    {
        var cutoff = timeProvider.GetUtcNow().UtcDateTime - RetentionPeriod;
        foreach (var (jobId, record) in _jobs)
        {
            var referenceTime = record.CompletedAtUtc ?? record.CreatedAtUtc;
            if (referenceTime < cutoff)
                _jobs.TryRemove(jobId, out _);
        }
    }

    private sealed class JobRecord(Guid jobId, string userId, DateTime createdAtUtc)
    {
        public Guid JobId { get; } = jobId;
        public string UserId { get; } = userId;
        public DateTime CreatedAtUtc { get; } = createdAtUtc;
        public MarkdownExportJobState State { get; set; } = MarkdownExportJobState.Queued;
        public DateTime? CompletedAtUtc { get; set; }
        public string? ErrorMessage { get; set; }
        public MarkdownExportFileDto? File { get; set; }

        public MarkdownExportJobStatusDto ToStatusDto() => new(
            JobId, State, CreatedAtUtc, CompletedAtUtc, File?.FileName, ErrorMessage);
    }
}
