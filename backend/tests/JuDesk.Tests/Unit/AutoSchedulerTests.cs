using JuDesk.Domain.Entities;
using JuDesk.Domain.Scheduling;
using static JuDesk.Tests.Unit.TestData;

namespace JuDesk.Tests.Unit;

public class AutoSchedulerTests
{
    [Fact]
    public void Fills_every_slot_when_there_is_capacity()
    {
        var a = Tech("A", skills: Electrical);
        var b = Tech("B", skills: Electrical);
        var shifts = new[] { ShiftAt(0, 8, 8, Electrical), ShiftAt(1, 8, 8, Electrical), ShiftAt(2, 8, 8, Electrical, headcount: 2) };

        var plan = AutoScheduler.Plan(shifts, [a, b]);

        Assert.Empty(plan.Unfilled);
        Assert.Equal(4, plan.Assignments.Count);
        Assert.Equal(2, plan.Assignments.Count(p => p.TechnicianId == a.Id));
        Assert.Equal(2, plan.Assignments.Count(p => p.TechnicianId == b.Id));
    }

    [Fact]
    public void Balances_hours_by_giving_each_slot_to_the_least_loaded_technician()
    {
        var busy = Tech("Busy", skills: Electrical);
        var free = Tech("Free", skills: Electrical);
        var existing = ShiftAt(0, 8, 8, Electrical).Assigned(busy);
        var open = ShiftAt(1, 8, 8, Electrical);

        var plan = AutoScheduler.Plan([existing, open], [busy, free]);

        var p = Assert.Single(plan.Assignments);
        Assert.Equal(free.Id, p.TechnicianId);
        Assert.Equal(open.Id, p.ShiftId);
    }

    [Fact]
    public void Never_proposes_a_rule_breaking_assignment()
    {
        var tech = Tech("Only", maxHours: 10, skills: Electrical);
        var shifts = new[] { ShiftAt(0, 8, 8, Electrical), ShiftAt(0, 10, 8, Electrical), ShiftAt(1, 8, 8, Electrical), ShiftAt(2, 8, 8, Hvac) };

        var plan = AutoScheduler.Plan(shifts, [tech]);

        var p = Assert.Single(plan.Assignments);
        Assert.Equal(shifts[0].Id, p.ShiftId);
        Assert.Equal(3, plan.Unfilled.Count);
        Assert.Contains(plan.Unfilled, u => u.ShiftId == shifts[1].Id && u.Reason.Contains("overlapping"));
        Assert.Contains(plan.Unfilled, u => u.ShiftId == shifts[2].Id && u.Reason.Contains("weekly hours"));
        Assert.Contains(plan.Unfilled, u => u.ShiftId == shifts[3].Id && u.Reason.Contains("HVAC"));
    }

    [Fact]
    public void Most_constrained_shift_is_planned_first()
    {
        // Specialist is the only one who can do HVAC; the generalist can do both.
        // If the electrical shift were planned first the specialist (same hours, earlier name) could be
        // taken by it and the HVAC shift would end up unfilled.
        var generalist = Tech("Generalist", 40, true, Electrical, Hvac);
        var specialist = Tech("A-Specialist", skills: Hvac);
        var electrical = ShiftAt(0, 8, 8, Electrical);
        var hvac = ShiftAt(0, 8, 8, Hvac);

        var plan = AutoScheduler.Plan([electrical, hvac], [generalist, specialist]);

        Assert.Empty(plan.Unfilled);
        Assert.Contains(plan.Assignments, p => p.ShiftId == hvac.Id && p.TechnicianId == specialist.Id);
        Assert.Contains(plan.Assignments, p => p.ShiftId == electrical.Id && p.TechnicianId == generalist.Id);
    }

    [Fact]
    public void Ignores_inactive_technicians_and_keeps_existing_assignments()
    {
        var inactive = Tech("Inactive", active: false, skills: Electrical);
        var active = Tech("Active", skills: Electrical);
        var alreadyFull = ShiftAt(0, 8, 8, Electrical).Assigned(active);
        var open = ShiftAt(1, 8, 8, Electrical);

        var plan = AutoScheduler.Plan([alreadyFull, open], [inactive, active]);

        var p = Assert.Single(plan.Assignments);
        Assert.Equal(active.Id, p.TechnicianId);
        Assert.Equal(open.Id, p.ShiftId);
    }

    [Fact]
    public void Does_not_mutate_the_input_graph()
    {
        var tech = Tech("A", skills: Electrical);
        var shift = ShiftAt(0, 8, 8, Electrical);

        AutoScheduler.Plan([shift], [tech]);

        Assert.Empty(shift.Assignments);
        Assert.Empty(tech.Assignments);
    }

    [Fact]
    public void Is_deterministic()
    {
        var techs = Enumerable.Range(0, 6).Select(i => Tech($"T{i}", 40, true, Electrical, Hvac)).ToList();
        var shifts = Enumerable.Range(0, 7).SelectMany(d => new[] { ShiftAt(d, 6, 8, Electrical, 2), ShiftAt(d, 14, 8, Hvac) }).ToList();

        var first = AutoScheduler.Plan(shifts, techs);
        var second = AutoScheduler.Plan(shifts, techs);

        Assert.Equal(first.Assignments, second.Assignments);
        Assert.Empty(first.Unfilled);
    }

    [Fact]
    public void Empty_inputs_give_an_empty_plan()
    {
        Assert.Equal(SchedulePlan.Empty, AutoScheduler.Plan([], [Tech("A")]));
        var plan = AutoScheduler.Plan([ShiftAt(0, 8, 8, Electrical)], Array.Empty<Technician>());
        Assert.Empty(plan.Assignments);
        Assert.Single(plan.Unfilled);
    }
}
