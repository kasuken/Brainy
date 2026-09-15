using System.Text;
using Brainy.Application.DTOs.DataImport;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Application.Tests.Fakes;
using Brainy.Data;
using Brainy.Domain.Enums;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Brainy.Application.Tests.Services.ExternalImport;

/// <summary>Covers the Evernote <c>.enex</c> path of <see cref="IExternalImportService"/>.</summary>
public sealed class ExternalImportServiceEvernoteTests
{
    private const string UserId = "evernote-user";

    private static (IExternalImportService Sut, BrainyDbContext Db) BuildService(
        string databaseName, InMemoryDatabaseRoot? root = null)
    {
        var services = new ServiceCollection();
        services.AddDbContext<BrainyDbContext>(options =>
        {
            if (root is null) options.UseInMemoryDatabase(databaseName);
            else options.UseInMemoryDatabase(databaseName, root);
        });
        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<BrainyDbContext>());
        services.AddSingleton<ICurrentUserService>(new FakeCurrentUserService(UserId));
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddBrainyApplication();

        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IExternalImportService>(), provider.GetRequiredService<BrainyDbContext>());
    }

    private static MemoryStream Enex(string innerXml) =>
        new(Encoding.UTF8.GetBytes($"""<?xml version="1.0" encoding="UTF-8"?><en-export>{innerXml}</en-export>"""));

    [Fact]
    public async Task ImportCurrentUserAsync_ConvertsEnmlToTextAndPreservesTags()
    {
        var (sut, db) = BuildService(nameof(ImportCurrentUserAsync_ConvertsEnmlToTextAndPreservesTags));
        var enex = Enex("""
            <note>
              <title>Grocery list</title>
              <tag>errand</tag>
              <tag>home</tag>
              <content><![CDATA[<en-note><div>Buy <b>milk</b> and <i>eggs</i>.</div><div><a href="https://example.test">recipe link</a></div></en-note>]]></content>
            </note>
            """);

        var result = await sut.ImportCurrentUserAsync(ExternalImportSourceFormat.Evernote, enex);

        result.SourceFormat.Should().Be("Evernote export");
        var note = await db.Notes.Include(n => n.Tags).SingleAsync();
        note.Title.Should().Be("Grocery list");
        note.Content.Should().Contain("**milk**").And.Contain("_eggs_").And.Contain("[recipe link](https://example.test)");
        note.Tags.Select(t => t.Name).Should().BeEquivalentTo(["errand", "home"]);
        note.Status.Should().Be(NoteStatus.Inbox);
        note.ParaCategory.Should().Be(ParaCategory.Project);
    }

    [Fact]
    public async Task ImportCurrentUserAsync_ImportsInlineResourceAndRewritesReference()
    {
        var (sut, db) = BuildService(nameof(ImportCurrentUserAsync_ImportsInlineResourceAndRewritesReference));
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 10, 20, 30 };
        var hash = Convert.ToHexString(System.Security.Cryptography.MD5.HashData(png)).ToLowerInvariant();
        var base64 = Convert.ToBase64String(png);

        var enex = Enex($"""
            <note>
              <title>Photo note</title>
              <content><![CDATA[<en-note><div>Look:</div><en-media hash="{hash}" type="image/png"/></en-note>]]></content>
              <resource>
                <data encoding="base64">{base64}</data>
                <mime>image/png</mime>
                <resource-attributes><file-name>photo.png</file-name></resource-attributes>
              </resource>
            </note>
            """);

        var result = await sut.ImportCurrentUserAsync(ExternalImportSourceFormat.Evernote, enex);

        result.EntityOutcomes.Single(o => o.EntityType == "Note images").Created.Should().Be(1);
        var image = await db.NoteImages.SingleAsync();
        image.FileName.Should().Be("photo.png");
        image.Data.Should().BeEquivalentTo(png);

        var note = await db.Notes.SingleAsync();
        note.Content.Should().Contain($"/api/note-images/{image.Id}");
        note.Content.Should().NotContain("enmedia:");
    }

    [Fact]
    public async Task ImportCurrentUserAsync_WithUnreferencedResource_AppendsItAsAnOrphanAttachment()
    {
        var (sut, db) = BuildService(nameof(ImportCurrentUserAsync_WithUnreferencedResource_AppendsItAsAnOrphanAttachment));
        var png = new byte[] { 1, 2, 3, 4, 5 };
        var base64 = Convert.ToBase64String(png);

        var enex = Enex($"""
            <note>
              <title>Note with orphan attachment</title>
              <content><![CDATA[<en-note><div>No inline reference to the resource below.</div></en-note>]]></content>
              <resource>
                <data encoding="base64">{base64}</data>
                <mime>image/png</mime>
              </resource>
            </note>
            """);

        var result = await sut.ImportCurrentUserAsync(ExternalImportSourceFormat.Evernote, enex);

        result.EntityOutcomes.Single(o => o.EntityType == "Note images").Created.Should().Be(1);
        var image = await db.NoteImages.SingleAsync();
        image.Data.Should().BeEquivalentTo(png);
        image.ContentType.Should().Be("image/png");
    }

    [Fact]
    public async Task ImportCurrentUserAsync_WithMalformedXml_ThrowsAClearError()
    {
        var (sut, _) = BuildService(nameof(ImportCurrentUserAsync_WithMalformedXml_ThrowsAClearError));
        var malformed = new MemoryStream(Encoding.UTF8.GetBytes("<en-export><note><title>Oops</note></en-export>"));

        var act = () => sut.ImportCurrentUserAsync(ExternalImportSourceFormat.Evernote, malformed);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not a valid Evernote*");
    }

    [Fact]
    public async Task ImportCurrentUserAsync_WithDoctype_IsRejectedRatherThanResolvingExternalEntities()
    {
        var (sut, _) = BuildService(nameof(ImportCurrentUserAsync_WithDoctype_IsRejectedRatherThanResolvingExternalEntities));
        var xxe = new MemoryStream(Encoding.UTF8.GetBytes(
            """<?xml version="1.0"?><!DOCTYPE en-export [<!ENTITY xxe SYSTEM "file:///etc/passwd">]><en-export><note><title>&xxe;</title><content><![CDATA[<en-note>x</en-note>]]></content></note></en-export>"""));

        var act = () => sut.ImportCurrentUserAsync(ExternalImportSourceFormat.Evernote, xxe);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ImportCurrentUserAsync_ReimportingTheSameEnex_IsANoOp()
    {
        var databaseName = nameof(ImportCurrentUserAsync_ReimportingTheSameEnex_IsANoOp);
        var root = new InMemoryDatabaseRoot();
        MemoryStream BuildEnex() => Enex("""
            <note>
              <title>Idea</title>
              <content><![CDATA[<en-note>Some idea body.</en-note>]]></content>
            </note>
            """);

        var (sut1, _) = BuildService(databaseName, root);
        var first = await sut1.ImportCurrentUserAsync(ExternalImportSourceFormat.Evernote, BuildEnex());
        first.EntityOutcomes.Single(o => o.EntityType == "Notes").Created.Should().Be(1);

        var (sut2, db) = BuildService(databaseName, root);
        var second = await sut2.ImportCurrentUserAsync(ExternalImportSourceFormat.Evernote, BuildEnex());

        second.EntityOutcomes.Single(o => o.EntityType == "Notes").Created.Should().Be(0);
        second.EntityOutcomes.Single(o => o.EntityType == "Notes").Reused.Should().Be(1);
        (await db.Notes.CountAsync()).Should().Be(1);
    }
}
