using System.Security.Claims;
using System.Text.Encodings.Web;
using AwesomeAssertions;
using Brainy.Application.Interfaces.Services;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Data;
using Brainy.Domain.Entities;
using Brainy.Domain.Enums;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Brainy.Web.Tests.ProductionSurface;

public sealed class WeekPageRenderTests
{
    [Fact]
    public async Task AuthenticatedWeekPage_RendersOnlyCurrentUsersPlanningData()
    {
        await using var factory = new AuthenticatedWeekPageFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        await factory.SeedAsync();

        using var response = await client.GetAsync("/today/week");
        var content = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        content.Should().Contain("Week");
        content.Should().Contain("Owner project");
        content.Should().Contain("Owner selected task");
        content.Should().NotContain("Other project");
        content.Should().NotContain("Other selected task");
    }

    [Fact]
    public async Task AuthenticatedTodayPage_RendersCurrentUsersPlannedWeekAboveProjectWork()
    {
        await using var factory = new AuthenticatedWeekPageFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        await factory.SeedAsync();

        using var response = await client.GetAsync("/today");
        var content = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        content.Should().Contain("Planned this week");
        content.Should().Contain("Owner selected task");
        content.Should().NotContain("Other selected task");
        content.IndexOf("Planned this week", StringComparison.Ordinal)
            .Should().BeLessThan(content.IndexOf("Priority Projects", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AuthenticatedTodayPage_WhenPlannedWeekIsDisabled_DoesNotRenderIt()
    {
        await using var factory = new AuthenticatedWeekPageFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        await factory.SeedAsync();
        await factory.SetWidgetOrderAsync("[\"current-focus\",\"in-progress\",\"priority-projects\"]");

        using var response = await client.GetAsync("/today");
        var content = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        content.Should().NotContain("Planned this week");
    }

    private sealed class AuthenticatedWeekPageFactory : WebApplicationFactory<Program>
    {
        private const string DatabaseName = "WeekPageRenderTests";
        public const string UserId = "week-web-user";
        public const string OtherUserId = "week-web-other";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("https_port", "443");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:ApplyMigrationsOnStartup"] = "false",
                    ["ConnectionStrings:DefaultConnection"] =
                        "Server=127.0.0.1,1;Database=BrainyTests;User Id=test;******;TrustServerCertificate=true;Connect Timeout=1"
                });
            });
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<BrainyDbContext>>();
                services.RemoveAll<BrainyDbContext>();
                services.RemoveAll<IApplicationDbContext>();

                services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = TestAuthHandler.Scheme;
                        options.DefaultChallengeScheme = TestAuthHandler.Scheme;
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.Scheme, _ => { });

                var efProvider = new ServiceCollection()
                    .AddEntityFrameworkInMemoryDatabase()
                    .BuildServiceProvider();

                services.AddDbContext<BrainyDbContext>(options =>
                    options.UseInMemoryDatabase(DatabaseName)
                        .UseInternalServiceProvider(efProvider));
                services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<BrainyDbContext>());
                services.AddSingleton<TimeProvider>(new TimeProviderStub(new DateTimeOffset(2026, 6, 17, 12, 0, 0, TimeSpan.Zero)));
                services.AddSingleton<IUserTimeZoneService>(new FixedUserTimeZoneService(new DateTime(2026, 6, 17)));
            });
        }

        public async Task SeedAsync()
        {
            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<BrainyDbContext>();
            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();

            var ownerProject = new Project
            {
                Id = Guid.NewGuid(),
                UserId = UserId,
                Name = "Owner project",
                Status = ProjectStatus.Active,
                Priority = ProjectPriority.High
            };
            var otherProject = new Project
            {
                Id = Guid.NewGuid(),
                UserId = OtherUserId,
                Name = "Other project",
                Status = ProjectStatus.Active,
                Priority = ProjectPriority.High
            };
            var ownerTask = new TaskItem
            {
                Id = Guid.NewGuid(),
                UserId = UserId,
                Project = ownerProject,
                Title = "Owner selected task",
                Status = TaskItemStatus.InProgress
            };
            var otherTask = new TaskItem
            {
                Id = Guid.NewGuid(),
                UserId = OtherUserId,
                Project = otherProject,
                Title = "Other selected task",
                Status = TaskItemStatus.Todo
            };

            db.AddRange(
                ownerProject,
                otherProject,
                ownerTask,
                otherTask,
                new WeeklyTaskSelection
                {
                    Id = Guid.NewGuid(),
                    UserId = UserId,
                    Task = ownerTask,
                    WeekStartDate = new DateTime(2026, 6, 15)
                },
                new WeeklyTaskSelection
                {
                    Id = Guid.NewGuid(),
                    UserId = OtherUserId,
                    Task = otherTask,
                    WeekStartDate = new DateTime(2026, 6, 15)
                });

            await db.SaveChangesAsync();
        }

        public async Task SetWidgetOrderAsync(string widgetOrder)
        {
            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<BrainyDbContext>();
            db.DashboardPreferences.Add(new UserDashboardPreference
            {
                Id = Guid.NewGuid(),
                UserId = UserId,
                WidgetOrder = widgetOrder
            });
            await db.SaveChangesAsync();
        }

        private sealed class FixedUserTimeZoneService(DateTime today) : IUserTimeZoneService
        {
            public Task<string> GetTimeZoneIdAsync(CancellationToken cancellationToken = default) => Task.FromResult("UTC");
            public Task<TimeZoneInfo> GetTimeZoneAsync(CancellationToken cancellationToken = default) => Task.FromResult(TimeZoneInfo.Utc);
            public Task<DateTime> GetUserTodayAsync(CancellationToken cancellationToken = default) => Task.FromResult(today);
            public Task SetTimeZoneIdAsync(string timeZoneId, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task<string?> GetTimeZoneOverrideIdAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
            public Task SetTimeZoneOverrideAsync(string timeZoneId, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task<(DateTime StartUtc, DateTime EndUtc)> GetUtcRangeAsync(DateTime localStartDate, DateTime localEndDate, CancellationToken cancellationToken = default)
                => Task.FromResult((localStartDate.Date, localEndDate.Date.AddDays(1)));
        }

        private sealed class TimeProviderStub(DateTimeOffset utcNow) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => utcNow;
        }
    }

    private sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public new const string Scheme = "TestAuth";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, AuthenticatedWeekPageFactory.UserId)],
                Scheme);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
