using BlogIt.Shared.DTOs;

namespace BlogIt.Services;

/// <summary>
/// Authentication operations, as the endpoints and any embedding host use them.
/// </summary>
/// <remarks>
/// Deliberately does not expose token minting. <c>GenerateToken(userId, …)</c> used to sit here and
/// was called from exactly one place — inside <see cref="LoginAsync"/> — which meant any host code
/// that could resolve this service could mint a valid token for an arbitrary user id without
/// presenting a password. It is now a private detail of <c>AuthService</c>.
/// <para>
/// This is narrower than the entity types and <c>BlogItDbContext</c>, which stay public because a
/// host writing a custom database provider genuinely needs them (see "the data model is part of the
/// public API" in <c>docs/technical-guide.md</c>). That reasoning does not reach here: no provider,
/// theme or extension point needs to sign a token.
/// </para>
/// </remarks>
public interface IAuthService
{
    /// <summary>
    /// Verifies a username and password and returns a freshly signed token, or
    /// <see langword="null"/> when the credentials do not match.
    /// </summary>
    Task<LoginResponse?> LoginAsync(LoginRequest request);

    /// <summary>
    /// Replaces the user's password after verifying the current one, and rotates their security
    /// stamp so every existing token for the account stops validating.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when the user does not exist or the current password is wrong.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// The new password does not satisfy <c>PasswordPolicy</c>. Thrown rather than returned as
    /// <see langword="false"/> because the two cases need different answers: a wrong current
    /// password is a normal, retryable outcome, whereas a policy violation is a programming error at
    /// the call site — the API layer validates first and turns it into a 400.
    /// </exception>
    Task<bool> ChangePasswordAsync(Guid userId, ChangePasswordRequest request);
}
