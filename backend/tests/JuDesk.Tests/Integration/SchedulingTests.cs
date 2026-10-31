using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JuDesk.Api.Contracts;
using JuDesk.Domain.Entities;
using JuDesk.Domain.Scheduling;

namespace JuDesk.Tests.Integration;

[Collection(ApiCollection.Name)]
public class SchedulingTests(ApiFixture fixture)
{
    // Each test uses its own far-future week so weeks never collide across tests.
    private static readonly object WeekLock = new();
    private static int _weekCounter;

    private static DateTime FreshMonday()
    {
        lock (WeekLock)
        {
            return new DateTime(2031, 1, 6, 0, 0, 0, DateTimeKind.Utc).AddDays(7 * _weekCounter++);
        }
    }

    [Fact]
    public async Task Assigning_a_qualified_free_technician_succeeds_and_shows_in_the_week()
    {
        var client = await fixture.AdminClientAsync();
        var monday = FreshMonday();
        var skill = await client.CreateSkillAsync();
        var site = await client.CreateSiteAsync();
        var tech = await client.CreateTechnicianAsync("Giulia", 40, true, skill.Id);
        var shift = await client.CreateShiftAsync(site.Id, skill.Id, monday.AddHours(8), 8);

        var response = await client.AssignAsync(shift.Id, tech.Id);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var week = await client.WeekAsync(DateOnly.FromDateTime(monday));
        var planned = Assert.Single(week.Shifts, s => s.Id == shift.Id);
        Assert.Equal("Giulia", Assert.Single(planned.Assignments).TechnicianName);
        Assert.Equal(8, week.Technicians.Single(t => t.Id == tech.Id).ScheduledHours);
        Assert.Equal(0, week.OpenSlots);
    }

    [Fact]
    public async Task Rule_violations_come_back_as_409_with_codes()
    {
        var client = await fixture.AdminClientAsync();
        var monday = FreshMonday();
        var electrical = await client.CreateSkillAsync();
        var hvac = await client.CreateSkillAsync();
        var site = await client.CreateSiteAsync();
        var tech = await client.CreateTechnicianAsync("Marco", 40, true, hvac.Id);
        var shift = await client.CreateShiftAsync(site.Id, electrical.Id, monday.AddHours(8), 8);

        var response = await client.AssignAsync(shift.Id, tech.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        var violations = problem.GetProperty("violations").EnumerateArray().Select(v => v.GetProperty("code").GetString()).ToList();
        Assert.Equal([nameof(ViolationCode.MissingSkill)], violations);
    }

    [Fact]
    public async Task Overlap_is_detected_from_the_database()
    {
        var client = await fixture.AdminClientAsync();
        var monday = FreshMonday();
        var skill = await client.CreateSkillAsync();
        var site = await client.CreateSiteAsync();
        var tech = await client.CreateTechnicianAsync("Sara", 40, true, skill.Id);
        var morning = await client.CreateShiftAsync(site.Id, skill.Id, monday.AddHours(6), 8);
        var overlapping = await client.CreateShiftAsync(site.Id, skill.Id, monday.AddHours(12), 8);

        Assert.Equal(HttpStatusCode.Created, (await client.AssignAsync(morning.Id, tech.Id)).StatusCode);
        var response = await client.AssignAsync(overlapping.Id, tech.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var candidates = await client.GetFromJsonAsync<List<CandidateDto>>($"/api/shifts/{overlapping.Id}/candidates");
        var candidate = Assert.Single(candidates!, c => c.TechnicianId == tech.Id);
        Assert.False(candidate.Eligible);
        Assert.Equal(morning.Id, Assert.Single(candidate.Violations).ConflictingShiftId);
    }

    [Fact]
    public async Task Weekly_hours_can_be_overridden_by_the_planner_but_hard_rules_cannot()
    {
        var client = await fixture.AdminClientAsync();
        var monday = FreshMonday();
        var skill = await client.CreateSkillAsync();
        var site = await client.CreateSiteAsync();
        var tech = await client.CreateTechnicianAsync("Luca", 8, true, skill.Id);
        var first = await client.CreateShiftAsync(site.Id, skill.Id, monday.AddHours(8), 8);
        var second = await client.CreateShiftAsync(site.Id, skill.Id, monday.AddDays(1).AddHours(8), 8);
        var third = await client.CreateShiftAsync(site.Id, skill.Id, monday.AddDays(1).AddHours(10), 4);

        Assert.Equal(HttpStatusCode.Created, (await client.AssignAsync(first.Id, tech.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.AssignAsync(second.Id, tech.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.AssignAsync(second.Id, tech.Id, overrideHours: true)).StatusCode);
        // Overlap stays a hard rule even with the override flag.
        Assert.Equal(HttpStatusCode.Conflict, (await client.AssignAsync(third.Id, tech.Id, overrideHours: true)).StatusCode);
    }

    [Fact]
    public async Task Double_booking_the_same_technician_is_rejected()
    {
        var client = await fixture.AdminClientAsync();
        var monday = FreshMonday();
        var skill = await client.CreateSkillAsync();
        var site = await client.CreateSiteAsync();
        var tech = await client.CreateTechnicianAsync("Elena", 40, true, skill.Id);
        var shift = await client.CreateShiftAsync(site.Id, skill.Id, monday.AddHours(8), 8, headcount: 2);

        Assert.Equal(HttpStatusCode.Created, (await client.AssignAsync(shift.Id, tech.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.AssignAsync(shift.Id, tech.Id)).StatusCode);
    }

    [Fact]
    public async Task Overlap_is_detected_across_the_week_boundary()
    {
        var client = await fixture.AdminClientAsync();
        var monday = FreshMonday();
        var skill = await client.CreateSkillAsync();
        var site = await client.CreateSiteAsync();
        var tech = await client.CreateTechnicianAsync("Night Owl", 40, true, skill.Id);
        var sundayNight = await client.CreateShiftAsync(site.Id, skill.Id, monday.AddHours(-2), 8); // Sun 22:00 - Mon 06:00
        var mondayEarly = await client.CreateShiftAsync(site.Id, skill.Id, monday, 8);              // Mon 00:00 - 08:00
        (await client.AssignAsync(sundayNight.Id, tech.Id)).EnsureSuccessStatusCode();

        var response = await client.AssignAsync(mondayEarly.Id, tech.Id);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Overlap", await response.Content.ReadAsStringAsync());

        var candidates = await client.GetFromJsonAsync<List<CandidateDto>>($"/api/shifts/{mondayEarly.Id}/candidates");
        Assert.False(candidates!.Single(c => c.TechnicianId == tech.Id).Eligible);

        var plan = await (await client.PostAsJsonAsync("/api/schedule/auto-assign", new AutoAssignRequest(DateOnly.FromDateTime(monday), true)))
            .Content.ReadFromJsonAsync<AutoAssignResultDto>();
        Assert.DoesNotContain(plan!.Assignments, p => p.ShiftId == mondayEarly.Id);
    }

    [Fact]
    public async Task Dates_outside_the_supported_window_are_rejected_not_500()
    {
        var client = await fixture.AdminClientAsync();
        var skill = await client.CreateSkillAsync();
        var site = await client.CreateSiteAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/schedule/week?weekStart=9999-12-31")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/shifts?from=9999-12-31")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/schedule/export?weekStart=1999-12-31")).StatusCode);
        var far = new DateTime(9999, 12, 31, 8, 0, 0, DateTimeKind.Utc);
        var create = await client.PostAsJsonAsync("/api/shifts", new ShiftRequest(site.Id, far, far.AddHours(4), skill.Id, 1, null));
        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
    }

    [Fact]
    public async Task Unassign_frees_the_slot()
    {
        var client = await fixture.AdminClientAsync();
        var monday = FreshMonday();
        var skill = await client.CreateSkillAsync();
        var site = await client.CreateSiteAsync();
        var tech = await client.CreateTechnicianAsync("Davide", 40, true, skill.Id);
        var shift = await client.CreateShiftAsync(site.Id, skill.Id, monday.AddHours(8), 8);
        var assignment = await (await client.AssignAsync(shift.Id, tech.Id)).Content.ReadFromJsonAsync<AssignmentDto>();

        var response = await client.DeleteAsync($"/api/shifts/{shift.Id}/assignments/{assignment!.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var week = await client.WeekAsync(DateOnly.FromDateTime(monday));
        Assert.Equal(1, week.OpenSlots);
    }

    [Fact]
    public async Task Auto_assign_fills_the_week_and_can_be_undone()
    {
        var client = await fixture.AdminClientAsync();
        var monday = FreshMonday();
        var weekStart = DateOnly.FromDateTime(monday);
        var electrical = await client.CreateSkillAsync();
        var hvac = await client.CreateSkillAsync();
        var site = await client.CreateSiteAsync();
        var a = await client.CreateTechnicianAsync("A", 40, true, electrical.Id, hvac.Id);
        var b = await client.CreateTechnicianAsync("B", 40, true, electrical.Id);
        var shifts = new List<ShiftDto>();
        for (var day = 0; day < 5; day++)
        {
            shifts.Add(await client.CreateShiftAsync(site.Id, electrical.Id, monday.AddDays(day).AddHours(8), 8));
        }
        shifts.Add(await client.CreateShiftAsync(site.Id, hvac.Id, monday.AddDays(5).AddHours(8), 8));
        var manual = shifts[0];
        Assert.Equal(HttpStatusCode.Created, (await client.AssignAsync(manual.Id, b.Id)).StatusCode);

        var dry = await (await client.PostAsJsonAsync("/api/schedule/auto-assign", new AutoAssignRequest(weekStart, DryRun: true)))
            .Content.ReadFromJsonAsync<AutoAssignResultDto>();
        Assert.True(dry!.DryRun);
        Assert.Equal(0, dry.Created);
        Assert.Equal(5, dry.Assignments.Count);
        Assert.Equal(1, (await client.WeekAsync(weekStart)).Shifts.Sum(s => s.Assignments.Count));

        var applied = await (await client.PostAsJsonAsync("/api/schedule/auto-assign", new AutoAssignRequest(weekStart)))
            .Content.ReadFromJsonAsync<AutoAssignResultDto>();
        Assert.Equal(5, applied!.Created);
        Assert.Empty(applied.Unfilled);

        var week = await client.WeekAsync(weekStart);
        Assert.Equal(0, week.OpenSlots);
        Assert.Equal(a.Id, Assert.Single(week.Shifts.Single(s => s.Id == shifts[5].Id).Assignments).TechnicianId);
        var hoursA = week.Technicians.Single(t => t.Id == a.Id).ScheduledHours;
        var hoursB = week.Technicians.Single(t => t.Id == b.Id).ScheduledHours;
        Assert.Equal(48, hoursA + hoursB);
        Assert.True(Math.Abs(hoursA - hoursB) <= 8, $"hours should be balanced: A={hoursA} B={hoursB}");

        // Running it again is a no-op; clearing removes only the auto assignments.
        var again = await (await client.PostAsJsonAsync("/api/schedule/auto-assign", new AutoAssignRequest(weekStart)))
            .Content.ReadFromJsonAsync<AutoAssignResultDto>();
        Assert.Equal(0, again!.Created);

        var cleared = await (await client.DeleteAsync($"/api/schedule/auto-assignments?weekStart={weekStart:yyyy-MM-dd}"))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(5, cleared.GetProperty("removed").GetInt32());
        week = await client.WeekAsync(weekStart);
        Assert.Equal(5, week.OpenSlots);
        Assert.Equal(AssignmentSource.Manual, Assert.Single(week.Shifts.Single(s => s.Id == manual.Id).Assignments).Source);
    }

    [Fact]
    public async Task Auto_assign_reports_slots_it_cannot_fill()
    {
        var client = await fixture.AdminClientAsync();
        var monday = FreshMonday();
        var weekStart = DateOnly.FromDateTime(monday);
        var rare = await client.CreateSkillAsync();
        var site = await client.CreateSiteAsync();
        await client.CreateTechnicianAsync("Unskilled", 40, true);
        var shift = await client.CreateShiftAsync(site.Id, rare.Id, monday.AddHours(8), 8, headcount: 2);

        var result = await (await client.PostAsJsonAsync("/api/schedule/auto-assign", new AutoAssignRequest(weekStart)))
            .Content.ReadFromJsonAsync<AutoAssignResultDto>();

        Assert.Equal(0, result!.Created);
        var unfilled = Assert.Single(result.Unfilled);
        Assert.Equal(shift.Id, unfilled.ShiftId);
        Assert.Equal(2, unfilled.Missing);
        Assert.Contains(rare.Name, unfilled.Reason);
    }

    [Fact]
    public async Task Csv_export_lists_assignments_and_unfilled_slots()
    {
        var client = await fixture.AdminClientAsync();
        var monday = FreshMonday();
        var weekStart = DateOnly.FromDateTime(monday);
        var skill = await client.CreateSkillAsync("Fire, safety");
        var site = await client.CreateSiteAsync();
        var tech = await client.CreateTechnicianAsync("Chiara Rizzo", 40, true, skill.Id);
        var shift = await client.CreateShiftAsync(site.Id, skill.Id, monday.AddHours(9), 4, headcount: 2);
        await client.AssignAsync(shift.Id, tech.Id);

        var response = await client.GetAsync($"/api/schedule/export?weekStart={weekStart:yyyy-MM-dd}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
        Assert.Contains($"judesk-week-{weekStart:yyyy-MM-dd}.csv", response.Content.Headers.ContentDisposition!.FileName);
        var lines = (await response.Content.ReadAsStringAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("date,start,end,hours,site,skill,technician,source,notes", lines[0]);
        Assert.Equal($"{weekStart:yyyy-MM-dd},09:00,13:00,4,{site.Name},\"Fire, safety\",Chiara Rizzo,Manual,", lines[1]);
        Assert.Equal($"{weekStart:yyyy-MM-dd},09:00,13:00,4,{site.Name},\"Fire, safety\",,Unfilled,", lines[2]);
    }

    [Fact]
    public async Task Shift_validation_and_edit_guards()
    {
        var client = await fixture.AdminClientAsync();
        var monday = FreshMonday();
        var skill = await client.CreateSkillAsync();
        var site = await client.CreateSiteAsync();

        var backwards = await client.PostAsJsonAsync("/api/shifts", new ShiftRequest(site.Id, monday.AddHours(10), monday.AddHours(8), skill.Id, 1, null));
        Assert.Equal(HttpStatusCode.BadRequest, backwards.StatusCode);

        var tech = await client.CreateTechnicianAsync("Giulia", 40, true, skill.Id);
        var shift = await client.CreateShiftAsync(site.Id, skill.Id, monday.AddHours(8), 8, headcount: 2);
        await client.AssignAsync(shift.Id, tech.Id);

        var shrink = await client.PutAsJsonAsync($"/api/shifts/{shift.Id}", new ShiftRequest(site.Id, shift.StartsAt, shift.EndsAt, skill.Id, 1, "fine"));
        Assert.Equal(HttpStatusCode.OK, shrink.StatusCode);

        var move = await client.PutAsJsonAsync($"/api/shifts/{shift.Id}", new ShiftRequest(site.Id, shift.StartsAt.AddHours(1), shift.EndsAt.AddHours(1), skill.Id, 1, null));
        Assert.Equal(HttpStatusCode.Conflict, move.StatusCode);

        var offsetDate = DateTime.SpecifyKind(monday.AddDays(2).AddHours(10), DateTimeKind.Local);
        var created = await client.PostAsJsonAsync("/api/shifts", new ShiftRequest(site.Id, offsetDate, offsetDate.AddHours(2), skill.Id, 1, null));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
    }
}
