using Brainy.Application.DTOs.Notes;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Application.Tests.Fakes;
using Brainy.Data;
using Brainy.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Brainy.Application.Tests.Services;

/// <summary>
/// Unit tests for <see cref="INoteImageService"/> resolved via the real DI container
/// with an EF Core InMemory database. Each test uses a unique database name for isolation.
/// The happy path of <c>AssociateWithNoteAsync</c> uses <c>ExecuteUpdateAsync</c>, which the
/// InMemory provider does not support; only its guard paths are covered here.
/// </summary>
public class NoteImageServiceTests
{
    private const string DefaultUserId = "u1";
    private const string OtherUserId = "u2";

    private static (INoteImageService sut, BrainyDbContext db) BuildService(
        string dbName,
        string userId = DefaultUserId)
    {
        var services = new ServiceCollection();

        services.AddDbContext<BrainyDbContext>(o =>
            o.UseInMemoryDatabase(dbName));

        services.AddScoped<IApplicationDbContext>(sp =>
            sp.GetRequiredService<BrainyDbContext>());

        services.AddSingleton<ICurrentUserService>(new FakeCurrentUserService(userId));

        services.AddBrainyApplication();

        var sp = services.BuildServiceProvider();
        return (sp.GetRequiredService<INoteImageService>(), sp.GetRequiredService<BrainyDbContext>());
    }

    private static UploadNoteImageDto CreateUpload(
        byte[]? data = null,
        string contentType = "image/png",
        string fileName = "shot.png")
        => new(data ?? [1, 2, 3], contentType, fileName);

    // ── UploadAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task UploadAsync_WithValidImage_PersistsImageScopedToCurrentUser()
    {
        var (sut, db) = BuildService(nameof(UploadAsync_WithValidImage_PersistsImageScopedToCurrentUser));

        var result = await sut.UploadAsync(CreateUpload());

        var stored = await db.NoteImages.AsNoTracking().SingleAsync();
        stored.Id.Should().Be(result.Id);
        stored.UserId.Should().Be(DefaultUserId);
        stored.ContentType.Should().Be("image/png");
        stored.SizeBytes.Should().Be(3);
        stored.Data.Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task UploadAsync_WithNullDto_ThrowsArgumentNullException()
    {
        var (sut, _) = BuildService(nameof(UploadAsync_WithNullDto_ThrowsArgumentNullException));

        var act = () => sut.UploadAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task UploadAsync_WithEmptyData_ThrowsArgumentException()
    {
        var (sut, _) = BuildService(nameof(UploadAsync_WithEmptyData_ThrowsArgumentException));

        var act = () => sut.UploadAsync(CreateUpload(data: []));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task UploadAsync_WithDataOverSizeLimit_ThrowsArgumentException()
    {
        var (sut, _) = BuildService(nameof(UploadAsync_WithDataOverSizeLimit_ThrowsArgumentException));
        var oversized = new byte[INoteImageService.MaxSizeBytes + 1];

        var act = () => sut.UploadAsync(CreateUpload(data: oversized));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task UploadAsync_WithDisallowedContentType_ThrowsArgumentException()
    {
        var (sut, _) = BuildService(nameof(UploadAsync_WithDisallowedContentType_ThrowsArgumentException));

        var act = () => sut.UploadAsync(CreateUpload(contentType: "application/pdf"));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task UploadAsync_AllowsContentTypeCaseInsensitively()
    {
        var (sut, db) = BuildService(nameof(UploadAsync_AllowsContentTypeCaseInsensitively));

        await sut.UploadAsync(CreateUpload(contentType: "IMAGE/PNG"));

        (await db.NoteImages.AsNoTracking().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task UploadAsync_WithBlankFileName_DefaultsToImage()
    {
        var (sut, db) = BuildService(nameof(UploadAsync_WithBlankFileName_DefaultsToImage));

        await sut.UploadAsync(CreateUpload(fileName: "   "));

        var stored = await db.NoteImages.AsNoTracking().SingleAsync();
        stored.FileName.Should().Be("image");
    }

    [Fact]
    public async Task UploadAsync_TruncatesFileNameTo255Characters()
    {
        var (sut, db) = BuildService(nameof(UploadAsync_TruncatesFileNameTo255Characters));
        var longName = new string('n', 300) + ".png";

        await sut.UploadAsync(CreateUpload(fileName: longName));

        var stored = await db.NoteImages.AsNoTracking().SingleAsync();
        stored.FileName.Length.Should().Be(255);
    }

    // ── GetContentAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetContentAsync_WithOwnedImage_ReturnsContent()
    {
        var (sut, _) = BuildService(nameof(GetContentAsync_WithOwnedImage_ReturnsContent));
        var uploaded = await sut.UploadAsync(CreateUpload());

        var result = await sut.GetContentAsync(uploaded.Id, DefaultUserId);

        result.Should().NotBeNull();
        result!.Data.Should().Equal(1, 2, 3);
        result.ContentType.Should().Be("image/png");
    }

    [Fact]
    public async Task GetContentAsync_WhenImageBelongsToAnotherUser_ReturnsNull()
    {
        var (sut, _) = BuildService(nameof(GetContentAsync_WhenImageBelongsToAnotherUser_ReturnsNull));
        var uploaded = await sut.UploadAsync(CreateUpload());

        var result = await sut.GetContentAsync(uploaded.Id, OtherUserId);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetContentAsync_WithEmptyUserId_ReturnsNull()
    {
        var (sut, _) = BuildService(nameof(GetContentAsync_WithEmptyUserId_ReturnsNull));
        var uploaded = await sut.UploadAsync(CreateUpload());

        var result = await sut.GetContentAsync(uploaded.Id, string.Empty);

        result.Should().BeNull();
    }

    // ── AssociateWithNoteAsync (guard paths only — see class remarks) ─────────

    [Fact]
    public async Task AssociateWithNoteAsync_WithNullImageIds_ThrowsArgumentNullException()
    {
        var (sut, _) = BuildService(nameof(AssociateWithNoteAsync_WithNullImageIds_ThrowsArgumentNullException));

        var act = () => sut.AssociateWithNoteAsync(Guid.NewGuid(), null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task AssociateWithNoteAsync_WithEmptyImageIds_ReturnsZero()
    {
        var (sut, _) = BuildService(nameof(AssociateWithNoteAsync_WithEmptyImageIds_ReturnsZero));

        var result = await sut.AssociateWithNoteAsync(Guid.NewGuid(), []);

        result.Should().Be(0);
    }
}
