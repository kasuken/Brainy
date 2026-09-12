using AwesomeAssertions;
using Brainy.Application;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Data;
using Brainy.Data.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Stripe;
using Xunit;
using PlanTier = Brainy.Domain.Enums.PlanTier;

namespace Brainy.Data.IntegrationTests;

/// <summary>
/// Proves issue #308's security-sensitive acceptance criteria against a real SQL Server
/// database (not EF InMemory) end to end through the real DI-selected <c>StripeBillingProvider</c>
/// and <c>BillingWebhookProcessor</c>: a forged webhook payload is rejected and changes nothing,
/// and a replayed valid event is idempotent thanks to <c>ProcessedWebhookEvent</c>'s unique-index
/// constraint (EF InMemory enforces unique indexes too, but this is the real SQL Server
/// constraint/behavior AGENTS.md asks security-sensitive flows to be proven against). Never
/// calls the real Stripe API: <c>VerifyWebhookSignatureAsync</c>/<c>ParseWebhookEventAsync</c>
/// are pure local signature math and JSON parsing.
/// </summary>
public sealed class BillingWebhookSqlIntegrationTests
{
    private const string WebhookSecret = "whsec_integration_test_secret";
    private const string UserId = "sql-webhook-user";

    [Fact]
    public async Task ForgedWebhookPayload_IsRejectedAndAppliesNothing()
    {
        await using var fixture = await Fixture.CreateAsync();
        var payload = fixture.CheckoutCompletedPayload("evt_forged_1");
        var validSignature = EventUtility.GenerateSignatureHeader(payload, WebhookSecret);

        // Forged: the attacker captured a valid signature but tampered with the event id/body.
        var tamperedPayload = fixture.CheckoutCompletedPayload("evt_forged_1_but_different");

        var result = await fixture.Processor.ProcessAsync(tamperedPayload, validSignature);

        result.Accepted.Should().BeFalse();
        (await fixture.Db.UserPlans.AnyAsync(p => p.UserId == UserId)).Should().BeFalse();
        (await fixture.Db.ProcessedWebhookEvents.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ValidCheckoutWebhook_UpgradesUserToProAndReplaysIdempotently()
    {
        await using var fixture = await Fixture.CreateAsync();
        var payload = fixture.CheckoutCompletedPayload("evt_sql_checkout_1");
        var signature = EventUtility.GenerateSignatureHeader(payload, WebhookSecret);

        var first = await fixture.Processor.ProcessAsync(payload, signature);
        first.Accepted.Should().BeTrue();
        first.Reason.Should().Be("applied");

        var userPlan = await fixture.Db.UserPlans.SingleAsync(p => p.UserId == UserId);
        userPlan.Tier.Should().Be(PlanTier.Pro);
        userPlan.BillingProviderCustomerId.Should().Be("cus_sql_1");
        (await fixture.Db.ProcessedWebhookEvents.CountAsync()).Should().Be(1);

        // Demote directly, bypassing the processor, so a second "applied" (a bug) is observable.
        userPlan.Tier = PlanTier.Starter;
        await fixture.Db.SaveChangesAsync();

        var replay = await fixture.Processor.ProcessAsync(payload, signature);

        replay.Accepted.Should().BeTrue();
        replay.Reason.Should().Be("already_processed");
        (await fixture.Db.ProcessedWebhookEvents.CountAsync()).Should().Be(1);
        (await fixture.Db.UserPlans.SingleAsync(p => p.UserId == UserId)).Tier.Should().Be(PlanTier.Starter);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _masterConnectionString;
        private readonly string _databaseName;
        private readonly ServiceProvider _provider;
        private readonly AsyncServiceScope _scope;

        private Fixture(string masterConnectionString, string databaseName, ServiceProvider provider, AsyncServiceScope scope)
        {
            _masterConnectionString = masterConnectionString;
            _databaseName = databaseName;
            _provider = provider;
            _scope = scope;
        }

        public BrainyDbContext Db => _scope.ServiceProvider.GetRequiredService<BrainyDbContext>();
        public IBillingWebhookProcessor Processor => _scope.ServiceProvider.GetRequiredService<IBillingWebhookProcessor>();

        public string CheckoutCompletedPayload(string eventId) =>
            $$"""
            {
              "id": "{{eventId}}",
              "object": "event",
              "api_version": "{{ApiVersion}}",
              "type": "checkout.session.completed",
              "data": {
                "object": {
                  "id": "cs_sql_1",
                  "object": "checkout.session",
                  "mode": "subscription",
                  "client_reference_id": "{{UserId}}",
                  "customer": "cus_sql_1",
                  "subscription": "sub_sql_1"
                }
              }
            }
            """;

        private static readonly string ApiVersion = (string)typeof(StripeConfiguration).Assembly
            .GetType("Stripe.ApiVersion")!
            .GetField("Current", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(null)!;

        public static async Task<Fixture> CreateAsync()
        {
            var configuredConnection = Environment.GetEnvironmentVariable("BRAINY_TEST_SQL_CONNECTIONSTRING");
            if (string.IsNullOrWhiteSpace(configuredConnection))
                throw Xunit.Sdk.SkipException.ForSkip("Set BRAINY_TEST_SQL_CONNECTIONSTRING to run SQL Server billing-webhook tests.");

            var databaseName = $"BrainyBillingWebhook_{Guid.NewGuid():N}";
            var master = new SqlConnectionStringBuilder(configuredConnection)
            {
                InitialCatalog = "master",
                TrustServerCertificate = true
            };
            var application = new SqlConnectionStringBuilder(master.ConnectionString)
            {
                InitialCatalog = databaseName
            };

            await ExecuteMasterCommandAsync(master.ConnectionString, $"CREATE DATABASE [{databaseName}]");

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Billing:Provider"] = "Stripe",
                    ["Billing:ApiKey"] = "sk_test_fake_never_called",
                    ["Billing:WebhookSigningSecret"] = WebhookSecret,
                    ["Billing:ProPriceId"] = "price_fake",
                    ["Billing:AppBaseUrl"] = "https://app.example.test",
                })
                .Build();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<ICurrentUserService>(new FixedCurrentUserService(UserId));
            services.AddDbContext<BrainyDbContext>(options => options.UseSqlServer(application.ConnectionString));
            services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<BrainyDbContext>());
            services.AddBrainyApplication();
            services.AddBilling(configuration);

            var provider = services.BuildServiceProvider();
            var scope = provider.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<BrainyDbContext>();
            await context.Database.MigrateAsync();

            // UserPlan has a real FK to AspNetUsers on SQL Server (unlike EF InMemory, which
            // does not enforce it) — a checkout webhook only ever names an already-registered
            // Brainy user, so the fixture seeds one to match.
            context.Users.Add(new ApplicationUser
            {
                Id = UserId,
                UserName = "sql-webhook-user@example.test",
                NormalizedUserName = "SQL-WEBHOOK-USER@EXAMPLE.TEST",
                Email = "sql-webhook-user@example.test",
                NormalizedEmail = "SQL-WEBHOOK-USER@EXAMPLE.TEST"
            });
            await context.SaveChangesAsync();

            return new Fixture(master.ConnectionString, databaseName, provider, scope);
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

        private sealed class FixedCurrentUserService(string userId) : ICurrentUserService
        {
            public Task<string?> GetUserIdAsync(CancellationToken cancellationToken = default) =>
                Task.FromResult<string?>(userId);

            public Task<string> GetRequiredUserIdAsync(CancellationToken cancellationToken = default) =>
                Task.FromResult(userId);
        }
    }
}
