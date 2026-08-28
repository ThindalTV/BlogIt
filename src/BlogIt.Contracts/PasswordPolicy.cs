namespace BlogIt.Shared.Helpers;

/// <summary>
/// The single definition of what BlogIt accepts as a password. Applied by setup, user creation and
/// change-password, and — since it is the last line of defence for an embedder calling
/// <c>IAuthService</c> directly — by <c>AuthService.ChangePasswordAsync</c> itself.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="MaxLength"/> is deliberately far above BCrypt's 72-byte input ceiling. BCrypt ignores
/// everything past that, so two accepted passwords sharing a 72-byte prefix hash identically; the
/// cap does not fix that, it only stops unbounded input reaching the hasher and turns a silently
/// truncated password into an explicit rejection at a length nobody types by accident.
/// </para>
/// <para>
/// Pre-hashing the password (SHA-384 then BCrypt, which is what BCrypt.Net's <c>Enhanced*</c>
/// helpers do) would make the whole password count and was considered and rejected: a stored
/// BCrypt hash does not record whether it was produced that way, so switching would invalidate
/// every existing hash and lock every existing user out of their site with no reset flow to
/// recover through. Capping at 72 instead was rejected for the mirror-image reason — it would
/// reject passphrases people are already signing in with.
/// </para>
/// </remarks>
public static class PasswordPolicy
{
    /// <summary>Shortest accepted password, in characters.</summary>
    public const int MinLength = 8;

    /// <summary>
    /// Longest accepted password, in characters. Generous on purpose — see the remarks on
    /// <see cref="PasswordPolicy"/> for why this is not BCrypt's 72-byte figure.
    /// </summary>
    public const int MaxLength = 128;

    /// <summary>Returns null if <paramref name="password"/> satisfies the policy, or a
    /// user-facing error message describing the first unmet rule.</summary>
    public static string? Validate(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinLength)
            return $"Password must be at least {MinLength} characters long.";

        // Checked before the character-class rules so an oversized value is rejected on its length
        // rather than on some incidental missing digit 200 characters in.
        if (password.Length > MaxLength)
            return $"Password must be at most {MaxLength} characters long.";

        if (!password.Any(char.IsUpper))
            return "Password must contain at least one uppercase letter.";

        if (!password.Any(char.IsLower))
            return "Password must contain at least one lowercase letter.";

        if (!password.Any(char.IsDigit))
            return "Password must contain at least one digit.";

        return null;
    }
}
