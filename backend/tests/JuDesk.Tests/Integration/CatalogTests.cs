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
    public async Task Site_colour_is_validated()
    {
        var client = await fixture.AdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/sites", new SiteRequest("Bad colour", null, "blue"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed record ValidationProblem(Dictionary<string, string[]> Errors);
}
