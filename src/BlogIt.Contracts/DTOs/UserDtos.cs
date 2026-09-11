using System.ComponentModel.DataAnnotations;

namespace BlogIt.Shared.DTOs;

public record AppUserDto(Guid Id, string Username, string DisplayName, DateTimeOffset CreatedAt);

/// <remarks>
/// <c>Password</c> is marked required but deliberately carries no length or complexity attribute.
/// <see cref="BlogIt.Shared.Helpers.PasswordPolicy"/> is the single authority for those, and
/// restating its minimum here would be a copied number that goes stale the first time the policy is
/// tightened — with the copy still telling clients the old rule. Complexity cannot be expressed as a
/// validation attribute at all, so a client wanting to check before it posts calls
/// <c>PasswordPolicy.Validate</c> directly; it now lives in this assembly precisely so it can.
/// </remarks>
public record CreateUserRequest(
    [property: Required][property: StringLength(ContentLimits.UsernameLength)] string Username,
    [property: Required][property: StringLength(ContentLimits.DisplayNameLength)] string DisplayName,
    [property: Required] string Password);
