using AwesomeAssertions;
using Brainy.Application;
using Brainy.Application.Interfaces.Caching;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Brainy.Application.Tests.Caching;

public sealed class ApplicationCacheTests
{
    [Fact]
    public async Task GetOrCreateAsync_WhenEntryExists_InvokesFactoryOnce()
    {
        await using var provider = CreateProvider();
        var cache = provider.GetRequiredService<IApplicationCache>();
        var factoryCalls = 0;

        _ = await cache.GetOrCreateAsync(
            "user-1",
            "notes:all",
            ["notes"],
            _ => Task.FromResult(++factoryCalls));
        _ = await cache.GetOrCreateAsync(
            "user-1",
            "notes:all",
            ["notes"],
            _ => Task.FromResult(++factoryCalls));

        factoryCalls.Should().Be(1);
    }

    [Fact]
    public async Task InvalidateTagsAsync_WhenEntryDependsOnTag_EvictsEntry()
    {
        await using var provider = CreateProvider();
        var cache = provider.GetRequiredService<IApplicationCache>();
        var factoryCalls = 0;

        _ = await cache.GetOrCreateAsync(
            "user-1",
            "notes:all",
            ["notes"],
            _ => Task.FromResult(++factoryCalls));

        await cache.InvalidateTagsAsync("user-1", ["notes"]);
        _ = await cache.GetOrCreateAsync(
            "user-1",
            "notes:all",
            ["notes"],
            _ => Task.FromResult(++factoryCalls));

        factoryCalls.Should().Be(2);
    }

    [Fact]
    public async Task InvalidateTagsAsync_WhenEntryHasUnrelatedTag_KeepsEntry()
    {
        await using var provider = CreateProvider();
        var cache = provider.GetRequiredService<IApplicationCache>();
        var factoryCalls = 0;

        _ = await cache.GetOrCreateAsync(
            "user-1",
            "projects:all",
            ["projects"],
            _ => Task.FromResult(++factoryCalls));

        await cache.InvalidateTagsAsync("user-1", ["notes"]);
        _ = await cache.GetOrCreateAsync(
            "user-1",
            "projects:all",
            ["projects"],
            _ => Task.FromResult(++factoryCalls));

        factoryCalls.Should().Be(1);
    }

    [Fact]
    public async Task InvalidateTagsAsync_ForOneUser_KeepsOtherUsersEntry()
    {
        await using var provider = CreateProvider();
        var cache = provider.GetRequiredService<IApplicationCache>();
        var secondUserFactoryCalls = 0;

        _ = await cache.GetOrCreateAsync(
            "user-1",
            "projects:active",
            ["projects"],
            _ => Task.FromResult("first-user"));
        _ = await cache.GetOrCreateAsync(
            "user-2",
            "projects:active",
            ["projects"],
            _ => Task.FromResult(++secondUserFactoryCalls));

        await cache.InvalidateTagsAsync("user-1", ["projects"]);
        _ = await cache.GetOrCreateAsync(
            "user-2",
            "projects:active",
            ["projects"],
            _ => Task.FromResult(++secondUserFactoryCalls));

        secondUserFactoryCalls.Should().Be(1);
    }

    [Fact]
    public async Task InvalidateTagsAsync_WhenEntryHasMultipleTags_EvictsEntryForAnyTag()
    {
        await using var provider = CreateProvider();
        var cache = provider.GetRequiredService<IApplicationCache>();
        var factoryCalls = 0;

        _ = await cache.GetOrCreateAsync(
            "user-1",
            "search",
            ["notes", "projects"],
            _ => Task.FromResult(++factoryCalls));

        await cache.InvalidateTagsAsync("user-1", ["projects"]);
        _ = await cache.GetOrCreateAsync(
            "user-1",
            "search",
            ["notes", "projects"],
            _ => Task.FromResult(++factoryCalls));

        factoryCalls.Should().Be(2);
    }

    [Fact]
    public async Task InvalidateTagsAsync_WhenFactoryStartedEarlier_DoesNotCacheStaleResult()
    {
        await using var provider = CreateProvider();
        var cache = provider.GetRequiredService<IApplicationCache>();
        var factoryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFactory = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var staleResultTask = cache.GetOrCreateAsync(
            "user-1",
            "notes:all",
            ["notes"],
            async _ =>
            {
                factoryStarted.SetResult();
                await releaseFactory.Task;
                return "stale";
            });

        await factoryStarted.Task;
        await cache.InvalidateTagsAsync("user-1", ["notes"]);
        releaseFactory.SetResult();
        _ = await staleResultTask;

        var refreshedResult = await cache.GetOrCreateAsync(
            "user-1",
            "notes:all",
            ["notes"],
            _ => Task.FromResult("fresh"));

        refreshedResult.Should().Be("fresh");
    }

    [Fact]
    public async Task GetOrCreateAsync_WhenConcurrentRequestsMiss_CoalescesFactoryExecution()
    {
        await using var provider = CreateProvider();
        var cache = provider.GetRequiredService<IApplicationCache>();
        var factoryCalls = 0;
        var factoryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFactory = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<int> CreateValueAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref factoryCalls);
            factoryStarted.TrySetResult();
            await releaseFactory.Task.WaitAsync(cancellationToken);
            return 42;
        }

        var first = cache.GetOrCreateAsync(
            "user-1",
            "preferences",
            ["preferences"],
            CreateValueAsync);
        await factoryStarted.Task;
        var second = cache.GetOrCreateAsync(
            "user-1",
            "preferences",
            ["preferences"],
            CreateValueAsync);

        releaseFactory.SetResult();
        await Task.WhenAll(first, second);

        factoryCalls.Should().Be(1);
    }

    [Fact]
    public async Task InvalidateUserAsync_RemovesOnlySpecifiedUsersEntries()
    {
        await using var provider = CreateProvider();
        var cache = provider.GetRequiredService<IApplicationCache>();
        var firstUserFactoryCalls = 0;
        var secondUserFactoryCalls = 0;

        _ = await cache.GetOrCreateAsync(
            "user-1",
            "tasks:active",
            ["tasks"],
            _ => Task.FromResult(++firstUserFactoryCalls));
        _ = await cache.GetOrCreateAsync(
            "user-2",
            "tasks:active",
            ["tasks"],
            _ => Task.FromResult(++secondUserFactoryCalls));

        await cache.InvalidateUserAsync("user-1");

        _ = await cache.GetOrCreateAsync(
            "user-1",
            "tasks:active",
            ["tasks"],
            _ => Task.FromResult(++firstUserFactoryCalls));
        _ = await cache.GetOrCreateAsync(
            "user-2",
            "tasks:active",
            ["tasks"],
            _ => Task.FromResult(++secondUserFactoryCalls));

        (firstUserFactoryCalls, secondUserFactoryCalls).Should().Be((2, 1));
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddBrainyApplication();
        return services.BuildServiceProvider();
    }
}
