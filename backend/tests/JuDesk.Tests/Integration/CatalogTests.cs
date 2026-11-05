using System.Net;
using System.Net.Http.Json;
using JuDesk.Api.Contracts;

namespace JuDesk.Tests.Integration;

[Collection(ApiCollection.Name)]
public class CatalogTests(ApiFixture fixture)
{
    [Fact]
    public async Task Technician_crud_round_trips_skills()
    {
        var client = await fixture.AdminClientAsync();
        var electrical = await client.CreateSkillAsync();
        var hvac = await client.CreateSkillAsync();

        var created = await client.CreateTechnicianAsync("Giulia Ferri", 32, true, electrical.Id);
        Assert.Equal(32, created.MaxWeeklyHours);
        Assert.Equal([electrical.Id], created.Skills.Select(s => s.Id));

        var update = await client.PutAsJsonAsync($"/api/technicians/{created.Id}",
            new TechnicianRequest("Giulia Ferri", "g@test.local", null, 40, false, [electrical.Id, hvac.Id]));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var fetched = await client.GetFromJsonAsync<TechnicianDto>($"/api/technicians/{created.Id}");
        Assert.False(fetched!.Active);
        Assert.Equal(2, fetched.Skills.Count);
        Assert.Equal("g@test.local", fetched.Email);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/technicians/{created.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/technicians/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task A_skill_still_needed_by_an_upcoming_assignment_cannot_be_removed()
    {
        var client = await fixture.AdminClientAsync();
        var skill = await client.CreateSkillAsync();
        var site = await client.CreateSiteAsync();
        var tech = await client.CreateTechnicianAsync("Marco Neri", 40, true, skill.Id);
        var shift = await client.CreateShiftAsync(site.Id, skill.Id, new DateTime(2035, 3, 5, 8, 0, 0, DateTimeKind.Utc), 8);
        var assignment = await (await client.AssignAsync(shift.Id, tech.Id)).Content.ReadFromJsonAsync<AssignmentDto>();

        var strip = await client.PutAsJsonAsync($"/api/technicians/{tech.Id}", new TechnicianRequest("Marco Neri", null, null, 40, true, []));
        Assert.Equal(HttpStatusCode.Conflict, strip.StatusCode);
        Assert.Contains(skill.Name, await strip.Content.ReadAsStringAsync());

        // Keeping the skill while changing anything else is fine.
        var rename = await client.PutAsJsonAsync($"/api/technicians/{tech.Id}", new TechnicianRequest("Marco Neri Jr", null, null, 40, true, [skill.Id]));
        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/shifts/{shift.Id}/assignments/{assignment!.Id}")).StatusCode);
        var now = await client.PutAsJsonAsync($"/api/technicians/{tech.Id}", new TechnicianRequest("Marco Neri", null, null, 40, true, []));
        Assert.Equal(HttpStatusCode.OK, now.StatusCode);
    }

    [Fact]
    public async Task Invalid_technician_returns_field_errors()
    {
        var client = await fixture.AdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/technicians", new TechnicianRequest("", null, null, 0, true, [Guid.NewGuid()]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblem>();
        Assert.Contains("fullName", problem!.Errors.Keys);
        Assert.Contains("maxWeeklyHours", problem.Errors.Keys);
        Assert.Contains("skillIds", problem.Errors.Keys);
    }

    [Fact]
    public async Task Skill_in_use_cannot_be_deleted()
    {
        var client = await fixture.AdminClientAsync();
        var skill = await client.CreateSkillAsync();
        var site = await client.CreateSiteAsync();
        await client.CreateShiftAsync(site.Id, skill.Id, new DateTime(2030, 1, 7, 8, 0, 0, DateTimeKind.Utc), 8);

        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/skills/{skill.Id}")).StatusCode);
    }

    [Fact]
    public async Task Sites_and_technicians_with_upcoming_work_cannot_be_deleted()
    {
        var client = await fixture.AdminClientAsync();
        var skill = await client.CreateSkillAsync();
        var site = await client.CreateSiteAsync();
        var tech = await client.CreateTechnicianAsync("Busy Bee", 40, true, skill.Id);
        var shift = await client.CreateShiftAsync(site.Id, skill.Id, new DateTime(2032, 5, 3, 8, 0, 0, DateTimeKind.Utc), 8);
        var assignment = await (await client.AssignAsync(shift.Id, tech.Id)).Content.ReadFromJsonAsync<AssignmentDto>();

        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/sites/{site.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/technicians/{tech.Id}")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/shifts/{shift.Id}/assignments/{assignment!.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/technicians/{tech.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/shifts/{shift.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/sites/{site.Id}")).StatusCode);
    }

    [Fact]
    public async Task Site_colour_is_validated()
    {
        var client = await fixture.AdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/sites", new SiteRequest("Bad colour", null, "blue"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // .NET's `$` matches before a trailing newline; the colour ends up in a style attribute, so be strict.
        var sneaky = await client.PostAsJsonAsync("/api/sites", new SiteRequest("Sneaky colour", null, "#2563eb\n"));
        Assert.Equal(HttpStatusCode.BadRequest, sneaky.StatusCode);
    }

[Fact]
    public async Task Technician_edits_cannot_break_upcoming_assignments()
    {
        var client = await fixture.AdminClientAsync();
        var skill = await client.CreateSkillAsync();
        var site = await client.CreateSiteAsync();
        var tech = await client.CreateTechnicianAsync("Booked Solid", 40, true, skill.Id);
        var monday = new DateTime(2033, 3, 7, 0, 0, 0, DateTimeKind.Utc);
        foreach (var day in new[] { 0, 1, 2 })
        {
            var shift = await client.CreateShiftAsync(site.Id, skill.Id, monday.AddDays(day).AddHours(8), 8);
            (await client.AssignAsync(shift.Id, tech.Id)).EnsureSuccessStatusCode();
        }
        TechnicianRequest Edit(int maxHours = 40, bool active = true) => new(tech.FullName, null, null, maxHours, active, [skill.Id]);

        var deactivate = await client.PutAsJsonAsync($"/api/technicians/{tech.Id}", Edit(active: false));
        Assert.Equal(HttpStatusCode.Conflict, deactivate.StatusCode);
        Assert.Contains("deactivating", await deactivate.Content.ReadAsStringAsync());

        var cut = await client.PutAsJsonAsync($"/api/technicians/{tech.Id}", Edit(maxHours: 20));
        Assert.Equal(HttpStatusCode.Conflict, cut.StatusCode);
        Assert.Contains("24 h", await cut.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/technicians/{tech.Id}", Edit(maxHours: 24))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/technicians/{tech.Id}", Edit(maxHours: 60))).StatusCode);
    }

        private sealed record ValidationProblem(Dictionary<string, string[]> Errors);
}
