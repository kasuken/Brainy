using AwesomeAssertions;
using Brainy.Application;
using Brainy.Application.DTOs.Email;
using Brainy.Application.Interfaces.Email;
using Brainy.Data;
using Brainy.Data.Identity;
using Brainy.Web.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Sdk;

namespace Brainy.Web.Tests.Identity;

/// <summary>
/// Wires <see cref="BrainyIdentityEmailSender"/> — the adapter between ASP.NET Core
/// Identity's <c>IEmailSender&lt;ApplicationUser&gt;</c> and the application-layer email
/// abstraction — through real <c>UserManager</c> token generation/confirmation against SQL
/// Server, proving issue #307's "Identity:RequireConfirmedAccount=true produces a working
/// registration flow" and "send failures surface as errors" acceptance criteria end to end.
/// </summary>
public sealed class EmailConfirmationFlowTests
{
    private const string Password = "Correct-password-42!";

    [Fact]
    public async Task GenerateTokenThenConfirm_WithTheDefaultNullEmailSender_DoesNotThrowAndConfirmsTheAccount()
    {
        await using var fixture = await EmailFlowFixture.CreateAsync(emailProvider: "None");
        var user = await fixture.CreateUserAsync("confirm-null@example.test");

        var code = await fixture.UserManager.GenerateEmailConfirmationTokenAsync(user);

        var act = () => fixture.EmailSender.SendConfirmationLinkAsync(
            user, user.Email!, "https://www.brainy-me.com/Account/ConfirmEmail?code=" + code);
        await act.Should().NotThrowAsync();

        var result = await fixture.UserManager.ConfirmEmailAsync(user, code);
        result.Succeeded.Should().BeTrue();
        (await fixture.UserManager.IsEmailConfirmedAsync(user)).Should().BeTrue();
    }

    [Fact]
    public async Task GenerateTokenThenConfirm_ForPasswordReset_ProducesAWorkingResetToken()
    {
        await using var fixture = await EmailFlowFixture.CreateAsync(emailProvider: "None");
        var user = await fixture.CreateUserAsync("reset-null@example.test");

        var code = await fixture.UserManager.GeneratePasswordResetTokenAsync(user);

        await fixture.EmailSender.SendPasswordResetLinkAsync(
            user, user.Email!, "https://www.brainy-me.com/Account/ResetPassword?code=" + code);

        var result = await fixture.UserManager.ResetPasswordAsync(user, code, "New-password-99!");
        result.Succeeded.Should().BeTrue();
        (await fixture.UserManager.CheckPasswordAsync(user, "New-password-99!")).Should().BeTrue();
    }

    [Fact]
    public async Task SendConfirmationLinkAsync_WhenTheTransportFails_PropagatesTheFailureThroughTheIdentityAdapter()
    {
        await using var fixture = await EmailFlowFixture.CreateAsync(
            emailProvider: "None",
            overrideEmailSender: new ThrowingEmailSender());
        var user = await fixture.CreateUserAsync("confirm-fails@example.test");

        var act = () => fixture.EmailSender.SendConfirmationLinkAsync(user, user.Email!, "https://example.test/confirm");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("simulated SMTP outage");
    }

    private sealed class ThrowingEmailSender : IEmailSender
    {
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("simulated SMTP outage");
    }

    private sealed class EmailFlowFixture : IAsyncDisposable
    {
        private readonly string _masterConnectionString;
        private readonly string _databaseName;
        private readonly ServiceProvider _provider;
        private readonly AsyncServiceScope _scope;

        private EmailFlowFixture(
            string masterConnectionString, string databaseName, ServiceProvider provider, AsyncServiceScope scope)
        {
            _masterConnectionString = masterConnectionString;
            _databaseName = databaseName;
            _provider = provider;
            _scope = scope;
        }

        public UserManager<ApplicationUser> UserManager => _scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        public IEmailSender<ApplicationUser> EmailSender => _scope.ServiceProvider.GetRequiredService<IEmailSender<ApplicationUser>>();

        public async Task<ApplicationUser> CreateUserAsync(string email)
        {
            var user = new ApplicationUser { Id = Guid.NewGuid().ToString(), UserName = email, Email = email };
            var result = await UserManager.CreateAsync(user, Password);
            result.Succeeded.Should().BeTrue(string.Join("; ", result.Errors.Select(error => error.Description)));
            return user;
        }

        public static async Task<EmailFlowFixture> CreateAsync(string emailProvider, IEmailSender? overrideEmailSender = null)
        {
            var configuredConnection = Environment.GetEnvironmentVariable("BRAINY_TEST_SQL_CONNECTIONSTRING");
            var isExplicitlyConfigured = !string.IsNullOrWhiteSpace(configuredConnection);
            if (!isExplicitlyConfigured && OperatingSystem.IsWindows())
            {
                configuredConnection =
                    "Server=(localdb)\\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true";
            }

            if (string.IsNullOrWhiteSpace(configuredConnection))
                throw SkipException.ForSkip("Set BRAINY_TEST_SQL_CONNECTIONSTRING to run SQL Server email-flow tests.");

            var databaseName = $"BrainyEmailFlow_{Guid.NewGuid():N}";
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

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Email:Provider"] = emailProvider,
                })
                .Build();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDataProtection();
            services.AddDbContext<BrainyDbContext>(options => options.UseSqlServer(application.ConnectionString));
            services.AddIdentityCore<ApplicationUser>()
                .AddEntityFrameworkStores<BrainyDbContext>()
                .AddDefaultTokenProviders();
            services.AddBrainyApplication();

            // Mirrors Program.cs: Provider=None (the default) registers NullEmailSender so the
            // real Identity/token flow works without a mail account configured.
            services.AddEmail(configuration);
            services.AddScoped<IEmailSender<ApplicationUser>, BrainyIdentityEmailSender>();

            if (overrideEmailSender is not null)
            {
                // Registered after AddEmail: the container resolves the last registration for
                // a service type, so this substitutes the transport AccountEmailService (and
                // therefore BrainyIdentityEmailSender) actually calls, without needing an
                // internal type or a real SMTP failure to prove propagation.
                services.AddSingleton(overrideEmailSender);
            }

            var provider = services.BuildServiceProvider();
            var scope = provider.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<BrainyDbContext>();
            await context.Database.MigrateAsync();

            return new EmailFlowFixture(master.ConnectionString, databaseName, provider, scope);
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
}
