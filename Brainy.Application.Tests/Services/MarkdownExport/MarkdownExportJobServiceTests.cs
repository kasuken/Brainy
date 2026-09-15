using Brainy.Application.DTOs.DataExport;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Services;
using Brainy.Application.Tests.Fakes;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Brainy.Application.Tests.Services.MarkdownExport;

/// <summary>
/// Covers the background-job orchestration around <see cref="IMarkdownExportService"/>:
/// starting a job, the one-in-flight-per-user rate limit, and that job status/results never
/// leak across users. The actual vault-building logic is covered separately in
/// <see cref="MarkdownExportServiceTests"/>.
/// </summary>
public class MarkdownExportJobServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 13, 10, 11, 12, TimeSpan.Zero);

    private static IServiceProvider BuildProvider(
        ICurrentUserService currentUser,
        IMarkdownExportJobQueue? sharedQueue = null,
        IMarkdownExportJobStore? sharedStore = null)
    {
        var services = new ServiceCollection();

        // Registering these first (as plain instances, not via AddBrainyApplication) lets two
        // separate containers — simulating two separate requests/users — share one underlying
        // in-memory coordinator: AddBrainyApplication below only TryAdds its own singleton, so
        // an already-registered instance always wins.
        if (sharedQueue is not null)
            services.AddSingleton(sharedQueue);
        if (sharedStore is not null)
            services.AddSingleton(sharedStore);

        services.AddSingleton(currentUser);
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(FixedNow));
        services.AddBrainyApplication();

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task StartExportAsync_WithoutAuthenticatedUser_IsRejected()
    {
        var provider = BuildProvider(new UnauthenticatedCurrentUserService());
        var sut = provider.GetRequiredService<IMarkdownExportJobService>();

        var act = () => sut.StartExportAsync();

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task StartExportAsync_EnqueuesAJobAndReturnsQueuedStatus()
    {
        var provider = BuildProvider(new FakeCurrentUserService("job-user"));
        var sut = provider.GetRequiredService<IMarkdownExportJobService>();
        var queue = provider.GetRequiredService<IMarkdownExportJobQueue>();

        var status = await sut.StartExportAsync();

        status.State.Should().Be(MarkdownExportJobState.Queued);
        status.FileName.Should().BeNull();
        status.ErrorMessage.Should().BeNull();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var enumerator = queue.ReadAllAsync(cts.Token).GetAsyncEnumerator(cts.Token);
        (await enumerator.MoveNextAsync()).Should().BeTrue();
        enumerator.Current.JobId.Should().Be(status.JobId);
        enumerator.Current.UserId.Should().Be("job-user");
    }

    [Fact]
    public async Task StartExportAsync_ReturnsTheSameJob_WhenOneIsAlreadyQueued()
    {
        var provider = BuildProvider(new FakeCurrentUserService("job-user"));
        var sut = provider.GetRequiredService<IMarkdownExportJobService>();

        var first = await sut.StartExportAsync();
        var second = await sut.StartExportAsync();

        second.JobId.Should().Be(first.JobId);
        second.State.Should().Be(MarkdownExportJobState.Queued);
    }

    [Fact]
    public async Task StartExportAsync_ReturnsTheSameJob_WhileItIsRunning()
    {
        var provider = BuildProvider(new FakeCurrentUserService("job-user"));
        var sut = provider.GetRequiredService<IMarkdownExportJobService>();
        var store = provider.GetRequiredService<IMarkdownExportJobStore>();

        var first = await sut.StartExportAsync();
        store.MarkRunning(first.JobId);

        var second = await sut.StartExportAsync();

        second.JobId.Should().Be(first.JobId);
        second.State.Should().Be(MarkdownExportJobState.Running);
    }

    [Fact]
    public async Task StartExportAsync_StartsANewJob_AfterThePreviousOneCompleted()
    {
        var provider = BuildProvider(new FakeCurrentUserService("job-user"));
        var sut = provider.GetRequiredService<IMarkdownExportJobService>();
        var store = provider.GetRequiredService<IMarkdownExportJobStore>();

        var first = await sut.StartExportAsync();
        store.MarkCompleted(
            first.JobId,
            new MarkdownExportFileDto("vault.zip", "application/zip", []),
            FixedNow.UtcDateTime);

        var second = await sut.StartExportAsync();

        second.JobId.Should().NotBe(first.JobId);
    }

    [Fact]
    public async Task StartExportAsync_StartsANewJob_AfterThePreviousOneFailed()
    {
        var provider = BuildProvider(new FakeCurrentUserService("job-user"));
        var sut = provider.GetRequiredService<IMarkdownExportJobService>();
        var store = provider.GetRequiredService<IMarkdownExportJobStore>();

        var first = await sut.StartExportAsync();
        store.MarkFailed(first.JobId, "boom", FixedNow.UtcDateTime);

        var second = await sut.StartExportAsync();

        second.JobId.Should().NotBe(first.JobId);
    }

    [Fact]
    public async Task GetStatusAsync_ReturnsNull_ForAnotherUsersJob()
    {
        // Two separate containers sharing one coordinator, simulating two different users'
        // requests against the same running process.
        var seedProvider = BuildProvider(new FakeCurrentUserService("owner"));
        var sharedQueue = seedProvider.GetRequiredService<IMarkdownExportJobQueue>();
        var sharedStore = seedProvider.GetRequiredService<IMarkdownExportJobStore>();

        var ownerProvider = BuildProvider(new FakeCurrentUserService("owner"), sharedQueue, sharedStore);
        var intruderProvider = BuildProvider(new FakeCurrentUserService("intruder"), sharedQueue, sharedStore);

        var ownerSut = ownerProvider.GetRequiredService<IMarkdownExportJobService>();
        var intruderSut = intruderProvider.GetRequiredService<IMarkdownExportJobService>();

        var status = await ownerSut.StartExportAsync();

        (await intruderSut.GetStatusAsync(status.JobId)).Should().BeNull();
        (await ownerSut.GetStatusAsync(status.JobId)).Should().NotBeNull();
    }

    [Fact]
    public void JobStore_CompletedFile_IsScopedToItsOwningUser()
    {
        var provider = BuildProvider(new FakeCurrentUserService("owner"));
        var store = provider.GetRequiredService<IMarkdownExportJobStore>();

        var jobId = Guid.NewGuid();
        store.CreateQueued(jobId, "owner", FixedNow.UtcDateTime);
        store.MarkCompleted(
            jobId,
            new MarkdownExportFileDto("vault.zip", "application/zip", [1, 2, 3]),
            FixedNow.UtcDateTime);

        store.GetCompletedFile(jobId, "owner").Should().NotBeNull();
        store.GetCompletedFile(jobId, "someone-else").Should().BeNull();
    }

    [Fact]
    public void JobStore_GetCompletedFile_ReturnsNull_WhileStillRunning()
    {
        var provider = BuildProvider(new FakeCurrentUserService("owner"));
        var store = provider.GetRequiredService<IMarkdownExportJobStore>();

        var jobId = Guid.NewGuid();
        store.CreateQueued(jobId, "owner", FixedNow.UtcDateTime);
        store.MarkRunning(jobId);

        store.GetCompletedFile(jobId, "owner").Should().BeNull();
    }

    [Fact]
    public void JobStore_MarkFailed_RecordsAUserFacingErrorMessageWithoutAFile()
    {
        var provider = BuildProvider(new FakeCurrentUserService("owner"));
        var store = provider.GetRequiredService<IMarkdownExportJobStore>();

        var jobId = Guid.NewGuid();
        store.CreateQueued(jobId, "owner", FixedNow.UtcDateTime);
        store.MarkFailed(jobId, "Brainy could not build your Markdown export. Try again.", FixedNow.UtcDateTime);

        var status = store.GetStatus(jobId, "owner");
        status!.State.Should().Be(MarkdownExportJobState.Failed);
        status.ErrorMessage.Should().Be("Brainy could not build your Markdown export. Try again.");
        store.GetCompletedFile(jobId, "owner").Should().BeNull();
    }

    private sealed class UnauthenticatedCurrentUserService : ICurrentUserService
    {
        public Task<string?> GetUserIdAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);

        public Task<string> GetRequiredUserIdAsync(CancellationToken cancellationToken = default)
            => Task.FromException<string>(new UnauthorizedAccessException());
    }
}
