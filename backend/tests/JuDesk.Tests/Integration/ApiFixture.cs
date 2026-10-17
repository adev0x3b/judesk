using System.Net.Http.Json;
using JuDesk.Api.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace JuDesk.Tests.Integration;

/// <summary>
/// One real PostgreSQL (Testcontainers) and one in-process API for the whole integration collection.
/// Migrations run at startup, exactly as in production; the demo seed is off so tests own their data.
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

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", _postgres.GetConnectionString());
            builder.UseSetting("Seed:Demo", "false");
            builder.UseSetting("Seed:AdminEmail", AdminEmail);
            builder.UseSetting("Seed:AdminPassword", AdminPassword);
            // Every test logs in from the same loopback address; keep the brute-force limiter out of the way.
            builder.UseSetting("Auth:LoginAttemptsPerMinute", "100000");
        });
        // Touch the server so migrations and the admin seed run before the first test.
        using var client = _factory.CreateClient();
        (await client.GetAsync("/health")).EnsureSuccessStatusCode();
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
