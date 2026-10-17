namespace JuDesk.Api.Services;

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
