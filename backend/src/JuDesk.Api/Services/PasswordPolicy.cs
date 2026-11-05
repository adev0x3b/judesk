namespace JuDesk.Api.Services;

/// <summary>
/// What a password must look like: a length range and nothing else. Length beats composition rules
/// (NIST SP 800-63B); online guessing is handled by the login rate limit and the per-email lockout.
/// </summary>
public static class PasswordPolicy
{
    public const int MinLength = 12;
    public const int MaxLength = 128;

    /// <summary>Adds the reason <paramref name="password"/> is not acceptable to <paramref name="v"/>, under <paramref name="field"/>.</summary>
    public static Validation Check(this Validation v, string field, string? password) =>
        v.Must(field, (password?.Length ?? 0) >= MinLength, $"Must be at least {MinLength} characters.")
         .Must(field, (password?.Length ?? 0) <= MaxLength, $"Must be at most {MaxLength} characters.");
}
