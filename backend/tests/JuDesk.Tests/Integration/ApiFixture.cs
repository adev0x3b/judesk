using System.Net.Http.Json;
using JuDesk.Api.Contracts;
using JuDesk.Api.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace JuDesk.Tests.Integration;

/// <summary>
/// One real PostgreSQL (Testcontainers) and one in-process API for the whole integration collection.
/// Migrations run at startup, exactly as in production; the demo seed is off so tests own their data.
/// The admin is created the way a user does it: with the setup code, through the setup endpoint.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    public const string AdminEmail = "admin@test.local";
    public const string AdminPassword = "Test-Password-1";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("judesk_test")
        .WithUsername("judesk")
        .WithPassword("judesk")
        .Build();

    private WebApplicationFactory<Program>? _factory;

    public WebApplicationFactory<Program> Factory => _factory ?? throw new InvalidOperationException("Fixture not initialised.");

    /// <summary>Connection string of the shared container, for tests that want a database of their own.</summary>
    public string ConnectionString => _postgres.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        _factory = CreateFactory(ConnectionString);
        await CompleteSetupAsync(_factory, AdminEmail, AdminPassword);
    }

    public static WebApplicationFactory<Program> CreateFactory(string connectionString) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", connectionString);
            builder.UseSetting("Seed:Demo", "false");
            // The scheduling tests are written in UTC; TimeZoneTests runs its own factory on Europe/Rome.
            builder.UseSetting("Schedule:TimeZone", "UTC");
            // Every test logs in from the same loopback address; keep the brute-force limiter out of the way.
            builder.UseSetting("Auth:LoginAttemptsPerMinute", "100000");
            builder.UseSetting("RateLimits:RequestsPerMinute", "100000");
        });

    /// <summary>Issues a setup code the way a restart or the regen script does, and creates the admin with it.</summary>
    public static async Task CompleteSetupAsync(WebApplicationFactory<Program> factory, string email, string password)
    {
        using var client = factory.CreateClient();
        (await client.GetAsync("/health")).EnsureSuccessStatusCode(); // migrations + code issued at startup
        var code = await IssueSetupCodeAsync(factory) ?? throw new InvalidOperationException("No setup code was issued.");
        var response = await client.PostAsJsonAsync("/api/setup", new SetupRequest(code, email, "Admin", password));
        response.EnsureSuccessStatusCode();
    }

    /// <summary>A fresh code (the old one stops working), or null once setup is complete.</summary>
    public static async Task<string?> IssueSetupCodeAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<SetupService>().IssueCodeIfNeededAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
        await _postgres.DisposeAsync();
    }

    public HttpClient AnonymousClient() => Factory.CreateClient();

    /// <summary>A client already logged in as the seeded admin (cookie jar handled by the test client).</summary>
    public Task<HttpClient> AdminClientAsync() => LoginAsync(AdminEmail, AdminPassword);

    public async Task<HttpClient> LoginAsync(string email, string password)
    {
        var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        response.EnsureSuccessStatusCode();
        return client;
    }

    /// <summary>Creates a planner through the admin API and returns it with a password that passes the policy.</summary>
    public async Task<(UserDto User, string Password)> CreatePlannerAsync(string? displayName = null)
    {
        const string password = "Willow-Ferry-Ninety-3";
        var admin = await AdminClientAsync();
        var email = $"planner-{Guid.NewGuid():N}@test.local";
        var response = await admin.PostAsJsonAsync("/api/users", new CreateUserRequest(email, displayName ?? "Pat Planner", password, Domain.Entities.UserRole.Planner));
        response.EnsureSuccessStatusCode();
        return ((await response.Content.ReadFromJsonAsync<UserDto>())!, password);
    }

    /// <summary>The raw auth cookie (name=value) a login response set, to replay it from a client without a cookie jar.</summary>
    public static string AuthCookie(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("judesk.auth=", StringComparison.Ordinal)).Split(';')[0];
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}
