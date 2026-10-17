using JuDesk.Domain.Scheduling;
using static JuDesk.Tests.Unit.TestData;

namespace JuDesk.Tests.Unit;

public class AssignmentRulesTests
{
    [Fact]
    public void Qualified_free_technician_passes()
    {
        var tech = Tech("Giulia", skills: Electrical);
        var shift = ShiftAt(0, 8, 8, Electrical);

        var violations = AssignmentRules.Check(tech, shift, []);

        Assert.Empty(violations);
    }

    [Fact]
    public void Missing_skill_is_reported()
    {
        var tech = Tech("Marco", skills: Hvac);
        var shift = ShiftAt(0, 8, 8, Electrical);

        var violations = AssignmentRules.Check(tech, shift, []);

        var v = Assert.Single(violations);
        Assert.Equal(ViolationCode.MissingSkill, v.Code);
        Assert.Contains("Electrical", v.Message);
    }

    [Fact]
    public void Inactive_technician_is_reported()
    {
        var tech = Tech("Andrea", active: false, skills: Electrical);
        var shift = ShiftAt(0, 8, 8, Electrical);

        var violations = AssignmentRules.Check(tech, shift, []);

        Assert.Contains(violations, v => v.Code == ViolationCode.TechnicianInactive);
    }

    [Fact]
    public void Overlapping_shift_is_reported_with_the_conflicting_id()
    {
        var tech = Tech("Giulia", skills: Electrical);
        var existing = ShiftAt(0, 6, 8, Electrical).Assigned(tech);   // 06:00-14:00
        var target = ShiftAt(0, 13, 8, Electrical);                    // 13:00-21:00

        var violations = AssignmentRules.Check(tech, target, [existing]);

        var v = Assert.Single(violations);
        Assert.Equal(ViolationCode.Overlap, v.Code);
        Assert.Equal(existing.Id, v.ConflictingShiftId);
    }

    [Fact]
    public void Back_to_back_shifts_do_not_overlap()
    {
        var tech = Tech("Giulia", skills: Electrical);
        var existing = ShiftAt(0, 6, 8, Electrical).Assigned(tech);   // 06:00-14:00
        var target = ShiftAt(0, 14, 8, Electrical);                    // 14:00-22:00

        Assert.Empty(AssignmentRules.Check(tech, target, [existing]));
    }

    [Fact]
    public void Weekly_hours_limit_counts_existing_shifts()
    {
        var tech = Tech("Sara", maxHours: 20, skills: Electrical);
        var day1 = ShiftAt(0, 8, 8, Electrical).Assigned(tech);
        var day2 = ShiftAt(1, 8, 8, Electrical).Assigned(tech);
        var day3 = ShiftAt(2, 8, 8, Electrical);                       // would make 24 h

        var violations = AssignmentRules.Check(tech, day3, [day1, day2]);

        var v = Assert.Single(violations);
        Assert.Equal(ViolationCode.MaxHoursExceeded, v.Code);
        Assert.Contains("24 h", v.Message);
    }

    [Fact]
    public void Reaching_the_limit_exactly_is_allowed()
    {
        var tech = Tech("Sara", maxHours: 16, skills: Electrical);
        var day1 = ShiftAt(0, 8, 8, Electrical).Assigned(tech);
        var day2 = ShiftAt(1, 8, 8, Electrical);

        Assert.Empty(AssignmentRules.Check(tech, day2, [day1]));
    }

    [Fact]
    public void Full_shift_and_double_booking_are_reported()
    {
        var giulia = Tech("Giulia", skills: Electrical);
        var chiara = Tech("Chiara", skills: Electrical);
        var shift = ShiftAt(0, 8, 8, Electrical, headcount: 1).Assigned(giulia);

        Assert.Contains(AssignmentRules.Check(chiara, shift, []), v => v.Code == ViolationCode.ShiftFull);
        Assert.Contains(AssignmentRules.Check(giulia, shift, [shift]), v => v.Code == ViolationCode.AlreadyAssigned);
    }

    [Fact]
    public void Several_violations_are_reported_together()
    {
        var tech = Tech("Marco", maxHours: 4, active: false, skills: Hvac);
        var shift = ShiftAt(0, 8, 8, Electrical);

        var codes = AssignmentRules.Check(tech, shift, []).Select(v => v.Code).ToList();

        Assert.Equal([ViolationCode.TechnicianInactive, ViolationCode.MissingSkill, ViolationCode.MaxHoursExceeded], codes);
    }
}
