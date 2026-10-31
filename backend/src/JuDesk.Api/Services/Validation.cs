namespace JuDesk.Api.Services;

/// <summary>Dates the API accepts: wide enough for any real schedule, narrow enough that date arithmetic never overflows.</summary>
public static class Dates
{
    public static readonly DateOnly Min = new(2000, 1, 1);
    public static readonly DateOnly Max = new(2100, 12, 31);

    public static bool IsSane(DateOnly date) => date >= Min && date <= Max;
    public static bool IsSane(DateTime utc) => IsSane(DateOnly.FromDateTime(utc));

    public static IResult Problem(string field) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [$"Must be between {Min:yyyy-MM-dd} and {Max:yyyy-MM-dd}."] });
}

/// <summary>Tiny request validator that collects field errors into a 400 ValidationProblem.</summary>
public sealed class Validation
{
    private readonly Dictionary<string, List<string>> _errors = new(StringComparer.OrdinalIgnoreCase);

    public bool HasErrors => _errors.Count > 0;

    public Validation Require(string field, string? value, int maxLength = 200)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Add(field, "Required.");
        }
        else if (value.Length > maxLength)
        {
            Add(field, $"Must be at most {maxLength} characters.");
        }
        return this;
    }

    public Validation MaxLength(string field, string? value, int maxLength)
    {
        if (value is not null && value.Length > maxLength)
        {
            Add(field, $"Must be at most {maxLength} characters.");
        }
        return this;
    }

    public Validation Range(string field, int value, int min, int max)
    {
        if (value < min || value > max)
        {
            Add(field, $"Must be between {min} and {max}.");
        }
        return this;
    }

    public Validation SaneDate(string field, DateTime utc) => Must(field, Dates.IsSane(utc), $"Must be between {Dates.Min:yyyy-MM-dd} and {Dates.Max:yyyy-MM-dd}.");

    public Validation Must(string field, bool condition, string message)
    {
        if (!condition)
        {
            Add(field, message);
        }
        return this;
    }

    public IResult? Problem() => HasErrors
        ? Results.ValidationProblem(_errors.ToDictionary(e => e.Key, e => e.Value.ToArray()))
        : null;

    private void Add(string field, string message)
    {
        if (!_errors.TryGetValue(field, out var list))
        {
            list = [];
            _errors[field] = list;
        }
        list.Add(message);
    }
}
