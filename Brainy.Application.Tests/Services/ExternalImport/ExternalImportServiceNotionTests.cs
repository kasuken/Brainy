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

/// <summary>Covers the Notion "Markdown &amp; CSV" export path of <see cref="IExternalImportService"/>.</summary>
public sealed class ExternalImportServiceNotionTests
{
    private const string UserId = "notion-user";

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

    [Fact]
    public async Task ImportCurrentUserAsync_StripsNotionIdSuffixAndResolvesPageLinks()
    {
        var (sut, db) = BuildService(nameof(ImportCurrentUserAsync_StripsNotionIdSuffixAndResolvesPageLinks));
        var id1 = "3f2504e04f8911d39a0c0305e82c3301";
        var id2 = "3f2504e04f8911d39a0c0305e82c3302";
        var zip = new MemoryStream(ExternalImportTestZipBuilder.Build(new Dictionary<string, string>
        {
            [$"Project Plan {id1}.md"] = $"Tags: launch, roadmap\n\n# Project Plan\n\nSee [Kickoff]({Uri.EscapeDataString($"Kickoff {id2}.md")}) for details.",
            [$"Kickoff {id2}.md"] = "# Kickoff\n\nKickoff body."
        }));

        var result = await sut.ImportCurrentUserAsync(ExternalImportSourceFormat.NotionExport, zip);

        result.SourceFormat.Should().Be("Notion export");
        var plan = await db.Notes.Include(n => n.Tags).SingleAsync(n => n.Title == "Project Plan");
        plan.Content.Should().NotContain(id1);
        plan.Tags.Select(t => t.Name).Should().BeEquivalentTo(["launch", "roadmap"]);
        plan.Content.Should().NotContain("Tags: launch, roadmap");
        plan.Status.Should().Be(NoteStatus.Inbox);

        var kickoff = await db.Notes.SingleAsync(n => n.Title == "Kickoff");
        var relationship = await db.NoteRelationships.SingleAsync();
        relationship.SourceNoteId.Should().Be(plan.Id);
        relationship.TargetNoteId.Should().Be(kickoff.Id);
    }

    [Fact]
    public async Task ImportCurrentUserAsync_DatabaseCsv_ImportsOneNotePerRowIntoInboxWithoutGuessingParaCategory()
    {
        var (sut, db) = BuildService(nameof(ImportCurrentUserAsync_DatabaseCsv_ImportsOneNotePerRowIntoInboxWithoutGuessingParaCategory));
        const string csv = "Name,Status,Tags\n\"Buy milk\",Not started,\"errand, home\"\n\"Write report\",Done,work\n";
        var zip = new MemoryStream(ExternalImportTestZipBuilder.Build(new Dictionary<string, string>
        {
            ["Tasks abcdef0123456789abcdef0123456789.csv"] = csv
        }));

        var result = await sut.ImportCurrentUserAsync(ExternalImportSourceFormat.NotionExport, zip);

        result.EntityOutcomes.Single(o => o.EntityType == "Notes").Created.Should().Be(2);
        var rows = await db.Notes.Include(n => n.Tags).ToListAsync();
        rows.Should().OnlyContain(n => n.Status == NoteStatus.Inbox && n.ParaCategory == ParaCategory.Project);

        var buyMilk = rows.Single(n => n.Title == "Buy milk");
        buyMilk.Tags.Select(t => t.Name).Should().BeEquivalentTo(["errand", "home"]);
        buyMilk.Content.Should().Contain("Status:").And.Contain("Not started");
    }

    [Fact]
    public async Task ImportCurrentUserAsync_ReimportingTheSameNotionExport_IsANoOp()
    {
        var databaseName = nameof(ImportCurrentUserAsync_ReimportingTheSameNotionExport_IsANoOp);
        var root = new InMemoryDatabaseRoot();
        var id = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        byte[] BuildZip() => ExternalImportTestZipBuilder.Build(new Dictionary<string, string>
        {
            [$"Idea {id}.md"] = "# Idea\n\nSome idea body."
        });

        var (sut1, _) = BuildService(databaseName, root);
        var first = await sut1.ImportCurrentUserAsync(ExternalImportSourceFormat.NotionExport, new MemoryStream(BuildZip()));
        first.EntityOutcomes.Single(o => o.EntityType == "Notes").Created.Should().Be(1);

        var (sut2, db) = BuildService(databaseName, root);
        var second = await sut2.ImportCurrentUserAsync(ExternalImportSourceFormat.NotionExport, new MemoryStream(BuildZip()));

        second.EntityOutcomes.Single(o => o.EntityType == "Notes").Created.Should().Be(0);
        second.EntityOutcomes.Single(o => o.EntityType == "Notes").Reused.Should().Be(1);
        (await db.Notes.CountAsync()).Should().Be(1);
    }
}
