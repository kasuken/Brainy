using System.Security.Claims;
using AwesomeAssertions;
using Brainy.Data;
using Brainy.Data.Identity;
using Brainy.Domain.Entities;
using Brainy.Web.Identity;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Sdk;

namespace Brainy.Web.Tests.Identity;

public sealed class AccountDeletionServiceTests
{
    [Fact]
    public async Task DeleteCurrentUserAsync_WithValidProof_DeletesAccountGraphAndPreservesOtherUser()
    {
        await using var fixture = await AccountDeletionFixture.CreateAsync();
        await fixture.SeedCompleteOwnedGraphAsync();

        var result = await fixture.Service.DeleteCurrentUserAsync(
            AccountDeletionFixture.Password,
            AccountDeletionService.ConfirmationPhrase);

        result.Should().Be(AccountDeletionResult.Succeeded);
        await fixture.AssertDeletedUserGraphIsGoneAsync();
        (await fixture.Context.Users.AnyAsync(user => user.Id == fixture.OtherUserId)).Should().BeTrue();
        (await fixture.Context.Areas.AnyAsync(area => area.UserId == fixture.OtherUserId)).Should().BeTrue();
        (await fixture.Context.UserClaims.AnyAsync(claim => claim.UserId == fixture.CurrentUserId)).Should().BeFalse();
        (await fixture.Context.UserLogins.AnyAsync(login => login.UserId == fixture.CurrentUserId)).Should().BeFalse();
        (await fixture.Context.UserTokens.AnyAsync(token => token.UserId == fixture.CurrentUserId)).Should().BeFalse();
        (await fixture.Context.UserRoles.AnyAsync(role => role.UserId == fixture.CurrentUserId)).Should().BeFalse();
        (await fixture.Context.Roles.AnyAsync(role => role.Name == "Reader")).Should().BeTrue();
    }

    [Fact]
    public async Task DeleteCurrentUserAsync_WithWrongPassword_PreservesAccountAndData()
    {
        await using var fixture = await AccountDeletionFixture.CreateAsync();
        await fixture.SeedCompleteOwnedGraphAsync();

        var result = await fixture.Service.DeleteCurrentUserAsync(
            "not-the-password",
            AccountDeletionService.ConfirmationPhrase);

        result.Should().Be(AccountDeletionResult.InvalidPassword);
        (await fixture.Context.Users.AnyAsync(user => user.Id == fixture.CurrentUserId)).Should().BeTrue();
        (await fixture.Context.Notes.AnyAsync(note => note.UserId == fixture.CurrentUserId)).Should().BeTrue();
    }

    [Fact]
    public async Task DeleteCurrentUserAsync_WithoutExactConfirmation_PreservesAccountAndData()
    {
        await using var fixture = await AccountDeletionFixture.CreateAsync();
        await fixture.SeedCompleteOwnedGraphAsync();

        var result = await fixture.Service.DeleteCurrentUserAsync(
            AccountDeletionFixture.Password,
            "delete my account");

        result.Should().Be(AccountDeletionResult.InvalidConfirmation);
        (await fixture.Context.Users.AnyAsync(user => user.Id == fixture.CurrentUserId)).Should().BeTrue();
    }

    [Fact]
    public async Task DeleteCurrentUserAsync_WithCrossUserReference_RollsBackWithoutDeletingAnything()
    {
        await using var fixture = await AccountDeletionFixture.CreateAsync();
        await fixture.SeedCompleteOwnedGraphAsync();
        var ownedAreaId = await fixture.Context.Areas
            .Where(area => area.UserId == fixture.CurrentUserId)
            .Select(area => area.Id)
            .FirstAsync();
        fixture.Context.Notes.Add(new Note
        {
            Id = Guid.NewGuid(),
            UserId = fixture.OtherUserId,
            AreaId = ownedAreaId,
            Title = "Corrupt cross-user link",
            Content = "Must stop erasure."
        });
        await fixture.Context.SaveChangesAsync();

        var act = () => fixture.Service.DeleteCurrentUserAsync(
            AccountDeletionFixture.Password,
            AccountDeletionService.ConfirmationPhrase);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*cross-account relationships*");
        (await fixture.Context.Users.AnyAsync(user => user.Id == fixture.CurrentUserId)).Should().BeTrue();
        (await fixture.Context.Notes.AnyAsync(note => note.UserId == fixture.CurrentUserId)).Should().BeTrue();
    }

    private sealed class AccountDeletionFixture : IAsyncDisposable
    {
        public const string Password = "Correct-password-42!";

        private readonly string _masterConnectionString;
        private readonly string _databaseName;
        private readonly ServiceProvider _provider;
        private readonly AsyncServiceScope _scope;

        private AccountDeletionFixture(
            string masterConnectionString,
            string databaseName,
            ServiceProvider provider,
            AsyncServiceScope scope,
            string currentUserId,
            string otherUserId)
        {
            _masterConnectionString = masterConnectionString;
            _databaseName = databaseName;
            _provider = provider;
            _scope = scope;
            CurrentUserId = currentUserId;
            OtherUserId = otherUserId;
        }

        public string CurrentUserId { get; }
        public string OtherUserId { get; }
        public BrainyDbContext Context => _scope.ServiceProvider.GetRequiredService<BrainyDbContext>();
        public IAccountDeletionService Service => _scope.ServiceProvider.GetRequiredService<IAccountDeletionService>();

        public static async Task<AccountDeletionFixture> CreateAsync()
        {
            var currentUserId = Guid.NewGuid().ToString();
            var otherUserId = Guid.NewGuid().ToString();
            var configuredConnection = Environment.GetEnvironmentVariable("BRAINY_TEST_SQL_CONNECTIONSTRING");
            var isExplicitlyConfigured = !string.IsNullOrWhiteSpace(configuredConnection);
            if (!isExplicitlyConfigured && OperatingSystem.IsWindows())
            {
                configuredConnection =
                    "Server=(localdb)\\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true";
            }

            if (string.IsNullOrWhiteSpace(configuredConnection))
                throw SkipException.ForSkip("Set BRAINY_TEST_SQL_CONNECTIONSTRING to run SQL Server account-deletion tests.");

            var databaseName = $"BrainyAccountDeletion_{Guid.NewGuid():N}";
            var master = new SqlConnectionStringBuilder(configuredConnection)
            {
                InitialCatalog = "master",
                TrustServerCertificate = true
            };
            var application = new SqlConnectionStringBuilder(master.ConnectionString)
            {
                InitialCatalog = databaseName
            };

            try
            {
                await ExecuteMasterCommandAsync(master.ConnectionString, $"CREATE DATABASE [{databaseName}]");
            }
            catch (Exception ex) when (!isExplicitlyConfigured && ex is SqlException or InvalidOperationException)
            {
                throw SkipException.ForSkip("SQL Server LocalDB is unavailable and BRAINY_TEST_SQL_CONNECTIONSTRING is not set.");
            }

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<AuthenticationStateProvider>(new FixedAuthenticationStateProvider(currentUserId));
            services.AddDbContext<BrainyDbContext>(options => options.UseSqlServer(application.ConnectionString));
            services.AddIdentityCore<ApplicationUser>()
                .AddEntityFrameworkStores<BrainyDbContext>();
            services.AddScoped<IAccountDeletionService, AccountDeletionService>();

            var provider = services.BuildServiceProvider();
            var scope = provider.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<BrainyDbContext>();
            await context.Database.MigrateAsync();

            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var currentResult = await userManager.CreateAsync(new ApplicationUser
            {
                Id = currentUserId,
                UserName = "current@example.test",
                Email = "current@example.test"
            }, Password);
            currentResult.Succeeded.Should().BeTrue(string.Join("; ", currentResult.Errors.Select(error => error.Description)));

            var otherResult = await userManager.CreateAsync(new ApplicationUser
            {
                Id = otherUserId,
                UserName = "other@example.test",
                Email = "other@example.test"
            }, Password);
            otherResult.Succeeded.Should().BeTrue(string.Join("; ", otherResult.Errors.Select(error => error.Description)));

            return new AccountDeletionFixture(
                master.ConnectionString, databaseName, provider, scope, currentUserId, otherUserId);
        }

        public async Task SeedCompleteOwnedGraphAsync()
        {
            var role = new IdentityRole
            {
                Id = Guid.NewGuid().ToString(),
                Name = "Reader",
                NormalizedName = "READER"
            };
            var area = new Area { Id = Guid.NewGuid(), UserId = CurrentUserId, Name = "Area" };
            var goal = new Goal { Id = Guid.NewGuid(), UserId = CurrentUserId, Title = "Goal", Area = area };
            var project = new Project { Id = Guid.NewGuid(), UserId = CurrentUserId, Name = "Project", Area = area, Goal = goal };
            var parentTask = new TaskItem { Id = Guid.NewGuid(), UserId = CurrentUserId, Project = project, Title = "Parent" };
            var childTask = new TaskItem
            {
                Id = Guid.NewGuid(), UserId = CurrentUserId, Project = project,
                ParentTask = parentTask, Title = "Child"
            };
            var source = new Source { Id = Guid.NewGuid(), UserId = CurrentUserId, Title = "Source" };
            var resource = new Resource { Id = Guid.NewGuid(), UserId = CurrentUserId, Name = "Resource", Area = area };
            var tag = new Tag { Id = Guid.NewGuid(), UserId = CurrentUserId, Name = "tag" };
            var sourceNote = new Note
            {
                Id = Guid.NewGuid(), UserId = CurrentUserId, Title = "Source note", Content = "Content",
                Area = area, Project = project, Resource = resource, Source = source
            };
            var targetNote = new Note
            {
                Id = Guid.NewGuid(), UserId = CurrentUserId, Title = "Target note", Content = "Content"
            };
            sourceNote.Tags.Add(tag);
            resource.Tags.Add(tag);
            var output = new Output
            {
                Id = Guid.NewGuid(), UserId = CurrentUserId, Title = "Output", Content = "Content",
                Area = area, Project = project, Goal = goal
            };
            output.SourceNotes.Add(sourceNote);

            Context.AddRange(
                role,
                new IdentityUserClaim<string> { UserId = CurrentUserId, ClaimType = "scope", ClaimValue = "brainy" },
                new IdentityUserLogin<string>
                {
                    LoginProvider = "test", ProviderKey = "current", ProviderDisplayName = "Test", UserId = CurrentUserId
                },
                new IdentityUserToken<string>
                {
                    LoginProvider = "test", Name = "token", Value = "secret", UserId = CurrentUserId
                },
                new IdentityUserRole<string> { UserId = CurrentUserId, RoleId = role.Id },
                area, goal, project, parentTask, childTask, source, resource, tag, sourceNote, targetNote, output,
                new GoalMilestone { Id = Guid.NewGuid(), Goal = goal, Title = "Milestone" },
                new GoalActivity { Id = Guid.NewGuid(), Goal = goal, Description = "Created" },
                new TaskDependency { Id = Guid.NewGuid(), Task = childTask, DependsOnTask = parentTask },
                new WeeklyTaskSelection
                {
                    Id = Guid.NewGuid(),
                    UserId = CurrentUserId,
                    Task = parentTask,
                    WeekStartDate = new DateTime(2026, 6, 15)
                },
                new NoteRelationship
                {
                    Id = Guid.NewGuid(), SourceNote = sourceNote, TargetNote = targetNote
                },
                new Highlight { Id = Guid.NewGuid(), Note = sourceNote, Text = "Highlight" },
                new Summary { Id = Guid.NewGuid(), Note = sourceNote, Content = "Summary" },
                new NoteImage
                {
                    Id = Guid.NewGuid(), UserId = CurrentUserId, Note = sourceNote,
                    FileName = "image.png", ContentType = "image/png", Data = [1, 2, 3], SizeBytes = 3
                },
                new ActionItem { Id = Guid.NewGuid(), UserId = CurrentUserId, Title = "Action", Note = sourceNote, TaskItem = childTask },
                new Idea { Id = Guid.NewGuid(), UserId = CurrentUserId, Title = "Idea", Area = area, CommittedProjectId = project.Id },
                new ArchiveRetentionRule { Id = Guid.NewGuid(), UserId = CurrentUserId, EntityType = "Note" },
                new UserDashboardPreference { Id = Guid.NewGuid(), UserId = CurrentUserId },
                new LifecycleActivity
                {
                    Id = Guid.NewGuid(), UserId = CurrentUserId, EntityId = sourceNote.Id,
                    Title = "Captured"
                },
                new Area { Id = Guid.NewGuid(), UserId = OtherUserId, Name = "Other area" });

            await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();
        }

        public async Task AssertDeletedUserGraphIsGoneAsync()
        {
            (await Context.Users.AnyAsync(user => user.Id == CurrentUserId)).Should().BeFalse();
            (await Context.Areas.AnyAsync(entity => entity.UserId == CurrentUserId)).Should().BeFalse();
            (await Context.Goals.AnyAsync(entity => entity.UserId == CurrentUserId)).Should().BeFalse();
            (await Context.Projects.AnyAsync(entity => entity.UserId == CurrentUserId)).Should().BeFalse();
            (await Context.Tasks.AnyAsync(entity => entity.UserId == CurrentUserId)).Should().BeFalse();
            (await Context.Resources.AnyAsync(entity => entity.UserId == CurrentUserId)).Should().BeFalse();
            (await Context.Sources.AnyAsync(entity => entity.UserId == CurrentUserId)).Should().BeFalse();
            (await Context.Tags.AnyAsync(entity => entity.UserId == CurrentUserId)).Should().BeFalse();
            (await Context.Notes.AnyAsync(entity => entity.UserId == CurrentUserId)).Should().BeFalse();
            (await Context.NoteImages.AnyAsync(entity => entity.UserId == CurrentUserId)).Should().BeFalse();
            (await Context.Outputs.AnyAsync(entity => entity.UserId == CurrentUserId)).Should().BeFalse();
            (await Context.Ideas.AnyAsync(entity => entity.UserId == CurrentUserId)).Should().BeFalse();
            (await Context.ActionItems.AnyAsync(entity => entity.UserId == CurrentUserId)).Should().BeFalse();
            (await Context.WeeklyTaskSelections.AnyAsync(entity => entity.UserId == CurrentUserId)).Should().BeFalse();
            (await Context.ArchiveRetentionRules.AnyAsync(entity => entity.UserId == CurrentUserId)).Should().BeFalse();
            (await Context.DashboardPreferences.AnyAsync(entity => entity.UserId == CurrentUserId)).Should().BeFalse();
            (await Context.LifecycleActivities.AnyAsync(entity => entity.UserId == CurrentUserId)).Should().BeFalse();
            (await Context.GoalMilestones.AnyAsync()).Should().BeFalse();
            (await Context.GoalActivities.AnyAsync()).Should().BeFalse();
            (await Context.TaskDependencies.AnyAsync()).Should().BeFalse();
            (await Context.NoteRelationships.AnyAsync()).Should().BeFalse();
            (await Context.Highlights.AnyAsync()).Should().BeFalse();
            (await Context.Summaries.AnyAsync()).Should().BeFalse();
        }

        public async ValueTask DisposeAsync()
        {
            await _scope.DisposeAsync();
            await _provider.DisposeAsync();
            await ExecuteMasterCommandAsync(
                _masterConnectionString,
                $"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_databaseName}]");
        }

        private static async Task ExecuteMasterCommandAsync(string connectionString, string commandText)
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = commandText;
            await command.ExecuteNonQueryAsync();
        }
    }

    private sealed class FixedAuthenticationStateProvider(string userId) : AuthenticationStateProvider
    {
        private readonly AuthenticationState _state = new(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId)],
            IdentityConstants.ApplicationScheme)));

        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(_state);
    }
}
