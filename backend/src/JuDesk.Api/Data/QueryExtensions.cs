using JuDesk.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace JuDesk.Api.Data;

public static class QueryExtensions
{
    /// <summary>A shift with everything its DTO shows: site, skill, assignments and their technicians.</summary>
    public static IQueryable<Shift> WithDetails(this IQueryable<Shift> shifts) => shifts
        .Include(s => s.Site)
        .Include(s => s.RequiredSkill)
        .Include(s => s.Assignments).ThenInclude(a => a.Technician);
}
