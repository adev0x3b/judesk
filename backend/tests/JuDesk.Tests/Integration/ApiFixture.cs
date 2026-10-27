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

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("judesk_test")
        .WithUsername("judesk")
        .WithPassword("judesk")
        .Build();

    private WebApplicationFactory<Program>? _factory;

    public WebApplicationFactory<Program> Factory => _factory ?? throw new InvalidOperationException("Fixture not initialised.");

    /// <summary>Connection string of the shared container, for tests that want a database of their own.</summary>
    public string ConnectionString => _postgres.GetConnectionString();

    public async Task InitializeAsync()
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
            // Every test logs in from the same loopback address; keep the brute-force limiter out of the way.
            builder.UseSetting("Auth:LoginAttemptsPerMinute", "100000");
        });

    /// <summary>Reads the current setup code the way the log banner shows it, and creates the admin with it.</summary>
    public static async Task CompleteSetupAsync(WebApplicationFactory<Program> factory, string email, string password)
    {
        using var client = factory.CreateClient();
        (await client.GetAsync("/health")).EnsureSuccessStatusCode(); // migrations + code issued at startup
        var code = await CurrentSetupCodeAsync(factory) ?? throw new InvalidOperationException("No setup code was issued.");
        var response = await client.PostAsJsonAsync("/api/setup", new SetupRequest(code, email, "Admin", password));
        response.EnsureSuccessStatusCode();
    }

    public static async Task<string?> CurrentSetupCodeAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<SetupService>().EnsureCodeAsync();
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
        await _postgres.DisposeAsync();
    }

    public HttpClient AnonymousClient() => Factory.CreateClient();

    /// <summary>A client already logged in as the seeded admin (cookie jar handled by the test client).</summary>
    public async Task<HttpClient> AdminClientAsync()
    {
        var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(AdminEmail, AdminPassword));
        response.EnsureSuccessStatusCode();
        return client;
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}
