using System.Text.Json.Serialization;

namespace JuDesk.Domain.Scheduling;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ViolationCode
{
    TechnicianInactive,
    MissingSkill,
    AlreadyAssigned,
    ShiftFull,
    Overlap,
    MaxHoursExceeded,
}

/// <summary>Why a technician cannot take a shift. A rule check returns zero or more of these.</summary>
public sealed record Violation(ViolationCode Code, string Message, Guid? ConflictingShiftId = null);
