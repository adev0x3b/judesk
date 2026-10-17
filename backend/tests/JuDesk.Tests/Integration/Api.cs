using System.Net.Http.Json;
using JuDesk.Api.Contracts;

namespace JuDesk.Tests.Integration;

/// <summary>Typed helpers so tests read like a planner's workflow.</summary>
internal static class Api
{
    private static string Unique(string prefix) => $"{prefix} {Guid.NewGuid():N}"[..Math.Min(prefix.Length + 9, 40)];

    public static async Task<SkillDto> CreateSkillAsync(this HttpClient client, string? name = null)
    {
        var response = await client.PostAsJsonAsync("/api/skills", new SkillRequest(name ?? Unique("Skill")));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SkillDto>())!;
    }

    public static async Task<SiteDto> CreateSiteAsync(this HttpClient client, string? name = null)
    {
        var response = await client.PostAsJsonAsync("/api/sites", new SiteRequest(name ?? Unique("Site"), "Somewhere 1", "#2563eb"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SiteDto>())!;
    }

    public static async Task<TechnicianDto> CreateTechnicianAsync(this HttpClient client, string name, int maxWeeklyHours = 40, bool active = true, params Guid[] skillIds)
    {
        var response = await client.PostAsJsonAsync("/api/technicians", new TechnicianRequest(name, null, null, maxWeeklyHours, active, skillIds));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TechnicianDto>())!;
    }

    public static async Task<ShiftDto> CreateShiftAsync(this HttpClient client, Guid siteId, Guid skillId, DateTime startsAt, int hours, int headcount = 1)
    {
        var response = await client.PostAsJsonAsync("/api/shifts", new ShiftRequest(siteId, startsAt, startsAt.AddHours(hours), skillId, headcount, null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ShiftDto>())!;
    }

    public static Task<HttpResponseMessage> AssignAsync(this HttpClient client, Guid shiftId, Guid technicianId, bool overrideHours = false) =>
        client.PostAsJsonAsync($"/api/shifts/{shiftId}/assignments", new AssignRequest(technicianId, overrideHours));

    public static async Task<WeekScheduleDto> WeekAsync(this HttpClient client, DateOnly weekStart) =>
        (await client.GetFromJsonAsync<WeekScheduleDto>($"/api/schedule/week?weekStart={weekStart:yyyy-MM-dd}"))!;
}
