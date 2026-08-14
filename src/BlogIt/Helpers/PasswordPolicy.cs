namespace BlogIt.Shared.Helpers;

public static class PasswordPolicy
{
    public const int MinLength = 8;

    /// <summary>Returns null if <paramref name="password"/> satisfies the policy, or a
    /// user-facing error message describing the first unmet rule.</summary>
    public static string? Validate(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinLength)
            return $"Password must be at least {MinLength} characters long.";

        if (!password.Any(char.IsUpper))
            return "Password must contain at least one uppercase letter.";

        if (!password.Any(char.IsLower))
            return "Password must contain at least one lowercase letter.";

        if (!password.Any(char.IsDigit))
            return "Password must contain at least one digit.";

        return null;
    }
}
