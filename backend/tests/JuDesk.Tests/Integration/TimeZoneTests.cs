using System.Net;
using System.Net.Http.Json;
using JuDesk.Api.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;

namespace JuDesk.Tests.Integration;

/// <summary>An installation in Europe/Rome, on a database of its own: weeks, days and the CSV follow Italian time.</summary>
[Collection(ApiCollection.Name)]
public class TimeZoneTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string Password = "Olive-Tramway-Thirty-5";
    private WebApplicationFactory<Program> _factory = null!;

    public async ValueTask InitializeAsync()
    {
        var cs = fixture.ConnectionString.Replace("Database=judesk_test", $"Database=rome_{Guid.NewGuid():N}");
        _factory = ApiFixture.CreateFactory(cs).WithWebHostBuilder(b => b.UseSetting("Schedule:TimeZone", "Europe/Rome"));
        await ApiFixture.CompleteSetupAsync(_factory, "rome@test.local", Password);
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private async Task<HttpClient> LoginAsync()
    {
        var client = _factory.CreateClient();
        (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("rome@test.local", Password))).EnsureSuccessStatusCode();
        return client;
    }

    [Fact]
    public async Task Config_names_the_zone()
    {
        var config = await _factory.CreateClient().GetFromJsonAsync<ConfigDto>("/api/config");
        Assert.Equal("Europe/Rome", config!.TimeZone);
    }

    [Fact]
    public async Task A_shift_at_half_past_midnight_on_monday_belongs_to_that_week_in_rome()
    {
        var client = await LoginAsync();
        var skill = await client.CreateSkillAsync();
        var site = await client.CreateSiteAsync();
        // Monday 3 Nov 2031 00:30 in Rome (UTC+1) is still Sunday 23:30 in UTC.
        var shift = await client.CreateShiftAsync(site.Id, skill.Id, new DateTime(2031, 11, 2, 23, 30, 0, DateTimeKind.Utc), 4);

        Assert.Contains((await client.WeekAsync(new DateOnly(2031, 11, 3))).Shifts, s => s.Id == shift.Id);
        Assert.DoesNotContain((await client.WeekAsync(new DateOnly(2031, 10, 27))).Shifts, s => s.Id == shift.Id);

        var csv = await client.GetStringAsync("/api/schedule/export?weekStart=2031-11-03");
        Assert.Contains($"2031-11-03,00:30,04:30,4,{site.Name}", csv);
    }

    [Fact]
    public async Task A_night_shift_across_the_autumn_clock_change_counts_its_real_hours()
    {
        var client = await LoginAsync();
        var skill = await client.CreateSkillAsync();
        var site = await client.CreateSiteAsync();
        var tech = await client.CreateTechnicianAsync("Night Owl", 8, true, skill.Id);
        // Saturday 25 Oct 2031 22:00 CEST to Sunday 06:00 CET: the clocks go back at 03:00, so it lasts 9 hours.
        var shift = await client.CreateShiftAsync(site.Id, skill.Id, new DateTime(2031, 10, 25, 20, 0, 0, DateTimeKind.Utc), 9);

        Assert.Equal(9, shift.Hours);
        var rejected = await client.AssignAsync(shift.Id, tech.Id);
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Contains("MaxHoursExceeded", await rejected.Content.ReadAsStringAsync());

        var csv = await client.GetStringAsync("/api/schedule/export?weekStart=2031-10-20");
        Assert.Contains("2031-10-25,22:00,06:00,9,", csv);
    }
}
