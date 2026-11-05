using System.Net;
using System.Net.Http.Json;
using JuDesk.Api.Contracts;
using JuDesk.Api.Data;
using JuDesk.Api.Services;
using JuDesk.Domain.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JuDesk.Tests.Integration;

/// <summary>
/// First-run flow on a database of its own (same container, different database name), because the
/// shared fixture has already completed setup.
/// </summary>
[Collection(ApiCollection.Name)]
public class SetupTests(ApiFixture fixture) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        var cs = fixture.ConnectionString.Replace("Database=judesk_test", $"Database=setup_{Guid.NewGuid():N}");
        _factory = ApiFixture.CreateFactory(cs);
        (await _factory.CreateClient().GetAsync("/health")).EnsureSuccessStatusCode();
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Fresh_install_needs_setup_and_has_a_code_but_no_accounts()
    {
        var client = _factory.CreateClient();

        var status = await client.GetFromJsonAsync<SetupStatusDto>("/api/setup/status");
        Assert.True(status!.NeedsSetup);

        var code = await ApiFixture.IssueSetupCodeAsync(_factory);
        Assert.Matches("^[A-HJ-NP-Z2-9]{4}-[A-HJ-NP-Z2-9]{4}-[A-HJ-NP-Z2-9]{4}$", code);

        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("admin@judesk.local", "ChangeMe123!"));
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/technicians")).StatusCode);
    }

    [Fact]
    public async Task Wrong_code_is_refused_and_creates_nothing()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/setup", new SetupRequest("AAAA-AAAA-AAAA", "boss@test.local", "Boss", "Lantern-Quiet-41"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.True((await client.GetFromJsonAsync<SetupStatusDto>("/api/setup/status"))!.NeedsSetup);
    }

    [Fact]
    public async Task Too_many_wrong_guesses_burn_the_code_until_a_new_one_is_issued()
    {
        var client = _factory.CreateClient();
        var code = await ApiFixture.IssueSetupCodeAsync(_factory);

        for (var i = 0; i < SetupCode.MaxFailedAttempts; i++)
        {
            var guess = await client.PostAsJsonAsync("/api/setup", new SetupRequest("ZZZZ-ZZZZ-ZZZZ", "boss@test.local", "Boss", "Lantern-Quiet-41"));
            Assert.Equal(HttpStatusCode.Forbidden, guess.StatusCode);
        }

        // The right code no longer works either: the row is gone, not just locked.
        var burnt = await client.PostAsJsonAsync("/api/setup", new SetupRequest(code, "boss@test.local", "Boss", "Lantern-Quiet-41"));
        Assert.Equal(HttpStatusCode.Forbidden, burnt.StatusCode);
        Assert.Contains("No setup code", await burnt.Content.ReadAsStringAsync());
        Assert.True((await client.GetFromJsonAsync<SetupStatusDto>("/api/setup/status"))!.NeedsSetup);

        var fresh = await ApiFixture.IssueSetupCodeAsync(_factory);
        var ok = await client.PostAsJsonAsync("/api/setup", new SetupRequest(fresh!, "boss@test.local", "Boss", "Lantern-Quiet-41"));
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
    }

    [Fact]
    public async Task Only_the_hash_of_the_code_is_stored()
    {
        var code = await ApiFixture.IssueSetupCodeAsync(_factory);

        using var scope = _factory.Services.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<AppDbContext>().SetupCodes.SingleAsync();

        Assert.DoesNotContain(code!.Replace("-", ""), row.CodeHash);
        Assert.Equal(64, row.CodeHash.Length);
        Assert.Equal(0, row.FailedAttempts);
    }

    [Fact]
    public async Task Regenerating_retires_the_old_code()
    {
        var client = _factory.CreateClient();
        var old = await ApiFixture.IssueSetupCodeAsync(_factory);

        string fresh;
        using (var scope = _factory.Services.CreateScope())
        {
            fresh = await scope.ServiceProvider.GetRequiredService<SetupService>().IssueCodeAsync();
        }
        Assert.NotEqual(old, fresh);

        var stale = await client.PostAsJsonAsync("/api/setup", new SetupRequest(old, "boss@test.local", "Boss", "Lantern-Quiet-41"));
        Assert.Equal(HttpStatusCode.Forbidden, stale.StatusCode);

        var ok = await client.PostAsJsonAsync("/api/setup", new SetupRequest(fresh!.ToLowerInvariant(), "boss@test.local", "Boss", "Lantern-Quiet-41"));
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
    }

    [Fact]
    public async Task Right_code_creates_the_admin_signs_in_and_closes_setup()
    {
        var client = _factory.CreateClient();
        var code = await ApiFixture.IssueSetupCodeAsync(_factory);

        var response = await client.PostAsJsonAsync("/api/setup", new SetupRequest(code, "Boss@Test.local", "Boss", "Lantern-Quiet-41"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var admin = await response.Content.ReadFromJsonAsync<UserDto>();
        Assert.Equal("boss@test.local", admin!.Email);
        Assert.Equal(UserRole.Admin, admin.Role);

        // Signed in by the setup call itself.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/technicians")).StatusCode);
        Assert.False((await client.GetFromJsonAsync<SetupStatusDto>("/api/setup/status"))!.NeedsSetup);
        Assert.Null(await ApiFixture.IssueSetupCodeAsync(_factory));

        // A second setup, even with the right code, is refused: there is only ever one first admin.
        var again = await client.PostAsJsonAsync("/api/setup", new SetupRequest(code, "other@test.local", "Other", "Harbor-Violet-58"));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        var anonymous = _factory.CreateClient();
        var login = await anonymous.PostAsJsonAsync("/api/auth/login", new LoginRequest("boss@test.local", "Lantern-Quiet-41"));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task Setup_request_is_validated()
    {
        var client = _factory.CreateClient();
        var code = await ApiFixture.IssueSetupCodeAsync(_factory);

        var response = await client.PostAsJsonAsync("/api/setup", new SetupRequest(code, "not-an-email", "", "short"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("email", body);
        Assert.Contains("displayName", body);
        Assert.Contains("password", body);
    }
}
