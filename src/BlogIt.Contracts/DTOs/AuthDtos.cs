using System.ComponentModel.DataAnnotations;

namespace BlogIt.Shared.DTOs;

public record LoginRequest(
    [property: Required][property: StringLength(ContentLimits.UsernameLength)] string Username,
    [property: Required] string Password);

public record LoginResponse(string Token, string Username, string DisplayName, DateTime ExpiresAt);

/// <remarks>
/// No length or complexity attribute on either password — see <see cref="CreateUserRequest"/>.
/// </remarks>
public record ChangePasswordRequest(
    [property: Required] string CurrentPassword,
    [property: Required] string NewPassword);
