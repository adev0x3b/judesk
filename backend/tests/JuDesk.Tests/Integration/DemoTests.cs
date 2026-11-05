using System.Net;
using System.Net.Http.Json;
using JuDesk.Api.Contracts;
using JuDesk.Api.Services;
using JuDesk.Domain.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace JuDesk.Tests.Integration;

/// <summary>Public demo mode on a database of its own; the shared fixture runs with the demo off.</summary>
[Collection(ApiCollection.Name)]
public class DemoTests(ApiFixture fixture) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        var cs = fixture.ConnectionString.Replace("Database=judesk_test", $"Database=demo_{Guid.NewGuid():N}");
        _factory = ApiFixture.CreateFactory(cs).WithWebHostBuilder(b =>
        {
            b.UseSetting("Demo:Mode", "true");
            b.UseSetting("Demo:ResetMinutes", "60");
            b.UseSetting("Seed:Demo", "true");
            b.UseSetting("Demo:MaxCatalogRows", "6"); // the seed has 5 skills
        });
        var client = _factory.CreateClient();
        (await client.GetAsync("/health")).EnsureSuccessStatusCode();

        // Until the owner has created the admin, the demo login is refused: otherwise the planner it
        // creates would count as "an account exists" and lock the setup page forever.
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync("/api/auth/demo", null)).StatusCode);
        await ApiFixture.CompleteSetupAsync(_factory, "owner@test.local", "Maple-Station-62");
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Demo_is_off_by_default()
    {
        var client = fixture.AnonymousClient();

        var status = await client.GetFromJsonAsync<DemoStatusDto>("/api/demo");
        Assert.False(status!.Enabled);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/api/auth/demo", null)).StatusCode);
    }

    [Fact]
    public async Task Visitors_sign_in_as_a_planner_without_a_password()
    {
        var client = _factory.CreateClient();

        var status = await client.GetFromJsonAsync<DemoStatusDto>("/api/demo");
        Assert.True(status!.Enabled);
        Assert.Equal(60, status.ResetMinutes);
        Assert.NotNull(status.NextResetAt);

        var response = await client.PostAsync("/api/auth/demo", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = await response.Content.ReadFromJsonAsync<UserDto>();
        Assert.Equal(UserRole.Planner, user!.Role);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/technicians")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/users")).StatusCode);

        // The same account every time, so sessions and the reset do not pile up users.
        var again = await (await client.PostAsync("/api/auth/demo", null)).Content.ReadFromJsonAsync<UserDto>();
        Assert.Equal(user.Id, again!.Id);
    }

    [Fact]
    public async Task Demo_tables_are_capped()
    {
        var client = _factory.CreateClient();
        (await client.PostAsync("/api/auth/demo", null)).EnsureSuccessStatusCode();

        await client.CreateSkillAsync("Sixth");
        var seventh = await client.PostAsJsonAsync("/api/skills", new SkillRequest("Seventh"));

        Assert.Equal(HttpStatusCode.Conflict, seventh.StatusCode);
        Assert.Contains("at most 6", await seventh.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Reset_puts_the_fleet_back_and_keeps_the_accounts()
    {
        var client = _factory.CreateClient();
        (await client.PostAsync("/api/auth/demo", null)).EnsureSuccessStatusCode();
        var skill = await client.CreateSkillAsync("Welding");
        var before = await client.GetFromJsonAsync<List<TechnicianDto>>("/api/technicians");
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/technicians/{before![0].Id}")).StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<DemoService>().ResetAsync();
        }

        var technicians = await client.GetFromJsonAsync<List<TechnicianDto>>("/api/technicians");
        Assert.Equal(8, technicians!.Count);
        var skills = await client.GetFromJsonAsync<List<SkillDto>>("/api/skills");
        Assert.DoesNotContain(skills!, s => s.Id == skill.Id);
        // Still signed in: the demo planner survived the reset.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
        var login = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest("owner@test.local", "Maple-Station-62"));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }
}
