using System.ComponentModel.DataAnnotations;

namespace BlogIt.Shared.DTOs;

public record AppUserDto(Guid Id, string Username, string DisplayName, DateTime CreatedAt);

/// <remarks>
/// <c>Password</c> is marked required but deliberately carries no length or complexity attribute.
/// The engine's <c>PasswordPolicy</c> is the authority for those, it lives in an assembly contracts
/// cannot reference, and restating its minimum here would be a copied number that goes stale the
/// first time the policy is tightened — with the copy still telling clients the old rule.
/// </remarks>
public record CreateUserRequest(
    [property: Required][property: StringLength(ContentLimits.UsernameLength)] string Username,
    [property: Required][property: StringLength(ContentLimits.DisplayNameLength)] string DisplayName,
    [property: Required] string Password);
