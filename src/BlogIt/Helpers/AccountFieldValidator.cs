namespace BlogIt.Shared.Helpers;

/// <summary>
/// Validates the two text fields every <c>AppUser</c> is created from, against the widths
/// <c>BlogItDbContext</c> declares for them.
/// </summary>
/// <remarks>
/// Shared because an account is created down two separate paths — <c>UsersApi</c> and
/// <c>SetupApi</c>'s first-run bootstrap — and neither checked anything, so an over-long username
/// was a 500 from either. Kept as one helper rather than two copies of the same two lines for the
/// reason <see cref="SeoLimits"/> exists: the copies drift, and the one in the less-travelled path
/// is the one that rots.
/// </remarks>
public static class AccountFieldValidator
{
    /// <summary>
    /// Returns the field errors for a new account, empty when both fields are acceptable.
    /// </summary>
    public static Dictionary<string, string[]> Validate(string? username, string? displayName)
    {
        var errors = new Dictionary<string, string[]>();
        TextFieldValidator.CheckRequired(
            errors, "username", "Username", username, ContentLimits.UsernameLength);
        TextFieldValidator.CheckRequired(
            errors, "displayName", "Display name", displayName, ContentLimits.DisplayNameLength);
        return errors;
    }
}
