namespace JuDesk.Api.Services;

/// <summary>
/// What a password must look like. Length beats composition rules (NIST SP 800-63B), so the policy is a
/// minimum length, a list of passwords attackers try first, and a few checks against the obvious:
/// one repeated character, a keyboard or digit run, or the account's own name or email.
/// </summary>
public static class PasswordPolicy
{
    public const int MinLength = 12;
    public const int MaxLength = 128;

    // Long enough to pass the length check, yet near the top of every breach list.
    private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
    {
        "123456789012", "1234567890123", "12345678901234", "123456123456", "123123123123", "111111111111",
        "000000000000", "121212121212", "112233445566", "987654321098", "qwertyuiop12", "qwertyuiop123",
        "qwertyuiopasdf", "1qaz2wsx3edc", "1q2w3e4r5t6y", "q1w2e3r4t5y6", "zaq12wsxcde3", "asdfghjkl123",
        "password1234", "password12345", "password123456", "passwordpassword", "password!123", "p@ssw0rd1234",
        "p@ssword1234", "iloveyou1234", "letmein12345", "welcome12345", "welcome123456", "administrator",
        "administrator1", "admin1234567", "adminadmin123", "changeme1234", "trustno11234", "football1234",
        "baseball1234", "princess1234", "sunshine1234", "superman1234", "starwars1234", "dragon123456",
        "monkey123456", "master123456", "abcdefghijkl", "abcdefgh1234", "abc123abc123", "abcabcabcabc",
        "correcthorsebatterystaple", "mypassword123", "secretpassword", "qwerty123456", "qwertyqwerty",
        "1234qwerasdf", "zxcvbnm12345", "michael12345", "jessica12345", "ciao12345678", "password2026",
        "judesk123456", "judeskjudesk", "juventus1234", "napoli123456",
    };

    /// <summary>Adds the reasons <paramref name="password"/> is not acceptable to <paramref name="v"/>, under <paramref name="field"/>.</summary>
    public static Validation Check(this Validation v, string field, string? password, params string?[] personal)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinLength)
        {
            return v.Must(field, false, $"Must be at least {MinLength} characters.");
        }
        if (password.Length > MaxLength)
        {
            return v.Must(field, false, $"Must be at most {MaxLength} characters.");
        }
        var plain = password.Trim();
        v.Must(field, !Common.Contains(plain), "Is one of the passwords attackers try first. Pick another.");
        v.Must(field, plain.Distinct().Count() > 3, "Uses too few different characters.");
        v.Must(field, !IsRun(plain), "Is a keyboard or number sequence.");
        foreach (var word in personal.SelectMany(Words))
        {
            if (plain.Contains(word, StringComparison.OrdinalIgnoreCase))
            {
                v.Must(field, false, "Must not contain your name or email.");
                break;
            }
        }
        return v;
    }

    /// <summary>The words of a name or of an email's local part that are long enough to matter ("Anna", not "Al").</summary>
    private static IEnumerable<string> Words(string? value)
    {
        var text = value?.Trim() ?? string.Empty;
        var at = text.IndexOf('@');
        if (at > 0)
        {
            text = text[..at];
        }
        return text.Split((char[])[' ', '.', '-', '_', '+', '\'', ','], StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 4);
    }

    /// <summary>True when every character follows the previous one by the same step (abcdef..., 13579..., 987654...).</summary>
    private static bool IsRun(string s)
    {
        var step = s[1] - s[0];
        if (Math.Abs(step) > 2)
        {
            return false;
        }
        for (var i = 2; i < s.Length; i++)
        {
            if (s[i] - s[i - 1] != step)
            {
                return false;
            }
        }
        return true;
    }
}
