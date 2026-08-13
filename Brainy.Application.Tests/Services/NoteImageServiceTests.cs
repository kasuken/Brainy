using Brainy.Application.DTOs.Notes;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Application.Tests.Fakes;
using Brainy.Data;
using Brainy.Domain.Entities;
using AwesomeAssertions;
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
        string fileName = "shot.png",
        Guid? noteId = null)
        => new(data ?? CreateValidImageData(contentType), contentType, fileName, noteId);

    private static byte[] CreateValidImageData(string contentType) => contentType.ToLowerInvariant() switch
    {
        "image/jpeg" => [0xFF, 0xD8, 0xFF, 0x00],
        "image/gif" => "GIF89a"u8.ToArray(),
        "image/webp" => "RIFF1234WEBP"u8.ToArray(),
        "image/bmp" => "BM"u8.ToArray(),
        _ => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]
    };

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
        stored.SizeBytes.Should().Be(8);
        stored.Data.Should().Equal(CreateValidImageData("image/png"));
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
    public async Task UploadAsync_WithSvg_ThrowsArgumentException()
    {
        var (sut, _) = BuildService(nameof(UploadAsync_WithSvg_ThrowsArgumentException));

        var act = () => sut.UploadAsync(CreateUpload(
            data: "<svg xmlns='http://www.w3.org/2000/svg'></svg>"u8.ToArray(),
            contentType: "image/svg+xml",
            fileName: "vector.svg"));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task UploadAsync_WhenBytesDoNotMatchMimeType_ThrowsArgumentException()
    {
        var (sut, _) = BuildService(nameof(UploadAsync_WhenBytesDoNotMatchMimeType_ThrowsArgumentException));

        var act = () => sut.UploadAsync(CreateUpload(
            data: CreateValidImageData("image/jpeg"),
            contentType: "image/png"));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    [InlineData("image/gif")]
    [InlineData("image/webp")]
    [InlineData("image/bmp")]
    public async Task UploadAsync_WithMatchingSupportedSignature_PersistsImage(string contentType)
    {
        var dbName = $"{nameof(UploadAsync_WithMatchingSupportedSignature_PersistsImage)}-{contentType}";
        var (sut, db) = BuildService(dbName);

        var result = await sut.UploadAsync(CreateUpload(contentType: contentType));

        result.ContentType.Should().Be(contentType);
        (await db.NoteImages.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task UploadAsync_AllowsContentTypeCaseInsensitively()
    {
        var (sut, db) = BuildService(nameof(UploadAsync_AllowsContentTypeCaseInsensitively));

        await sut.UploadAsync(CreateUpload(contentType: "IMAGE/PNG"));

        var stored = await db.NoteImages.AsNoTracking().SingleAsync();
        stored.ContentType.Should().Be("image/png");
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

    [Fact]
    public async Task UploadAsync_WithForeignNoteId_ThrowsKeyNotFoundException()
    {
        var (sut, db) = BuildService(nameof(UploadAsync_WithForeignNoteId_ThrowsKeyNotFoundException));
        var foreignNote = new Note { Id = Guid.NewGuid(), UserId = OtherUserId, Title = "Secret" };
        db.Notes.Add(foreignNote);
        await db.SaveChangesAsync();

        var act = () => sut.UploadAsync(CreateUpload(noteId: foreignNote.Id));

        await act.Should().ThrowAsync<KeyNotFoundException>();
        (await db.NoteImages.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task UploadAsync_WithOwnedNoteId_PersistsNoteLink()
    {
        var (sut, db) = BuildService(nameof(UploadAsync_WithOwnedNoteId_PersistsNoteLink));
        var note = new Note { Id = Guid.NewGuid(), UserId = DefaultUserId, Title = "Owned" };
        db.Notes.Add(note);
        await db.SaveChangesAsync();

        var result = await sut.UploadAsync(CreateUpload(noteId: note.Id));

        result.NoteId.Should().Be(note.Id);
    }

    [Fact]
    public async Task UploadAsync_WhenUserQuotaWouldBeExceeded_ThrowsInvalidOperationException()
    {
        var (sut, db) = BuildService(nameof(UploadAsync_WhenUserQuotaWouldBeExceeded_ThrowsInvalidOperationException));
        db.NoteImages.Add(new NoteImage
        {
            Id = Guid.NewGuid(),
            UserId = DefaultUserId,
            FileName = "existing.png",
            ContentType = "image/png",
            SizeBytes = INoteImageService.MaxUserStorageBytes,
            Data = CreateValidImageData("image/png")
        });
        await db.SaveChangesAsync();

        var act = () => sut.UploadAsync(CreateUpload());

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*storage quota exceeded*");
        (await db.NoteImages.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task UploadAsync_DoesNotCountOtherUsersStorageAgainstQuota()
    {
        var (sut, db) = BuildService(nameof(UploadAsync_DoesNotCountOtherUsersStorageAgainstQuota));
        db.NoteImages.Add(new NoteImage
        {
            Id = Guid.NewGuid(),
            UserId = OtherUserId,
            FileName = "foreign.png",
            ContentType = "image/png",
            SizeBytes = INoteImageService.MaxUserStorageBytes,
            Data = CreateValidImageData("image/png")
        });
        await db.SaveChangesAsync();

        await sut.UploadAsync(CreateUpload());

        (await db.NoteImages.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task UploadAsync_ConcurrentUploads_CannotTogetherExceedQuota()
    {
        var dbName = nameof(UploadAsync_ConcurrentUploads_CannotTogetherExceedQuota);
        var (firstService, db) = BuildService(dbName);
        var (secondService, _) = BuildService(dbName);
        db.NoteImages.Add(new NoteImage
        {
            Id = Guid.NewGuid(),
            UserId = DefaultUserId,
            FileName = "existing.png",
            ContentType = "image/png",
            SizeBytes = INoteImageService.MaxUserStorageBytes - 8,
            Data = CreateValidImageData("image/png")
        });
        await db.SaveChangesAsync();

        static async Task<bool> TryUploadAsync(INoteImageService service)
        {
            try
            {
                await service.UploadAsync(CreateUpload());
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        var results = await Task.WhenAll(
            TryUploadAsync(firstService),
            TryUploadAsync(secondService));

        results.Should().ContainSingle(success => success);
        (await db.NoteImages.AsNoTracking().SumAsync(image => image.SizeBytes))
            .Should().Be(INoteImageService.MaxUserStorageBytes);
    }

    [Fact]
    public async Task UploadAsync_RemovesOnlyExpiredUnattachedImagesForCurrentUser()
    {
        var (sut, db) = BuildService(nameof(UploadAsync_RemovesOnlyExpiredUnattachedImagesForCurrentUser));
        var note = new Note { Id = Guid.NewGuid(), UserId = DefaultUserId, Title = "Owned" };
        var expiredPending = CreateStoredImage(DefaultUserId);
        var expiredAttached = CreateStoredImage(DefaultUserId, note.Id);
        var expiredForeign = CreateStoredImage(OtherUserId);
        db.AddRange(note, expiredPending, expiredAttached, expiredForeign);
        await db.SaveChangesAsync();

        var expiredAt = DateTime.UtcNow - INoteImageService.UnattachedRetention - TimeSpan.FromHours(1);
        expiredPending.CreatedAtUtc = expiredAt;
        expiredAttached.CreatedAtUtc = expiredAt;
        expiredForeign.CreatedAtUtc = expiredAt;
        await db.SaveChangesAsync();

        var uploaded = await sut.UploadAsync(CreateUpload());

        var remainingIds = await db.NoteImages.AsNoTracking().Select(image => image.Id).ToListAsync();
        remainingIds.Should().BeEquivalentTo(new[] { expiredAttached.Id, expiredForeign.Id, uploaded.Id });
    }

    // ── GetContentAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetContentAsync_WithOwnedImage_ReturnsContent()
    {
        var (sut, _) = BuildService(nameof(GetContentAsync_WithOwnedImage_ReturnsContent));
        var uploaded = await sut.UploadAsync(CreateUpload());

        var result = await sut.GetContentAsync(uploaded.Id, DefaultUserId);

        result.Should().NotBeNull();
        result!.Data.Should().Equal(CreateValidImageData("image/png"));
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

    [Fact]
    public async Task AssociateWithNoteAsync_WithForeignNoteId_ThrowsKeyNotFoundException()
    {
        var (sut, db) = BuildService(nameof(AssociateWithNoteAsync_WithForeignNoteId_ThrowsKeyNotFoundException));
        var image = await sut.UploadAsync(CreateUpload());
        var foreignNote = new Note { Id = Guid.NewGuid(), UserId = OtherUserId, Title = "Secret" };
        db.Notes.Add(foreignNote);
        await db.SaveChangesAsync();

        var act = () => sut.AssociateWithNoteAsync(foreignNote.Id, [image.Id]);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ── DeletePendingAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task DeletePendingAsync_DeletesOnlyCurrentUsersUnattachedImages()
    {
        var (sut, db) = BuildService(nameof(DeletePendingAsync_DeletesOnlyCurrentUsersUnattachedImages));
        var note = new Note { Id = Guid.NewGuid(), UserId = DefaultUserId, Title = "Owned" };
        var ownedPending = CreateStoredImage(DefaultUserId);
        var ownedAttached = CreateStoredImage(DefaultUserId, note.Id);
        var foreignPending = CreateStoredImage(OtherUserId);
        db.AddRange(note, ownedPending, ownedAttached, foreignPending);
        await db.SaveChangesAsync();

        var deleted = await sut.DeletePendingAsync([ownedPending.Id, ownedAttached.Id, foreignPending.Id]);

        deleted.Should().Be(1);
        var remainingIds = await db.NoteImages.AsNoTracking().Select(image => image.Id).ToListAsync();
        remainingIds.Should().BeEquivalentTo(new[] { ownedAttached.Id, foreignPending.Id });
    }

    [Fact]
    public async Task DeletePendingAsync_WithEmptyIds_ReturnsZero()
    {
        var (sut, _) = BuildService(nameof(DeletePendingAsync_WithEmptyIds_ReturnsZero));

        (await sut.DeletePendingAsync([])).Should().Be(0);
    }

    [Fact]
    public async Task DeletePendingAsync_WithNullIds_ThrowsArgumentNullException()
    {
        var (sut, _) = BuildService(nameof(DeletePendingAsync_WithNullIds_ThrowsArgumentNullException));

        var act = () => sut.DeletePendingAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    private static NoteImage CreateStoredImage(string userId, Guid? noteId = null) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        NoteId = noteId,
        FileName = "stored.png",
        ContentType = "image/png",
        SizeBytes = 8,
        Data = CreateValidImageData("image/png")
    };
}
