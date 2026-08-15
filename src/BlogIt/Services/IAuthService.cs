using BlogIt.Shared.DTOs;

namespace BlogIt.Services;

public interface IAuthService
{
    Task<LoginResponse?> LoginAsync(LoginRequest request);
    Task<bool> ChangePasswordAsync(Guid userId, ChangePasswordRequest request);
    string GenerateToken(
        Guid userId,
        string username,
        string displayName,
        string securityStamp,
        string secret,
        int expiryMinutes);
}
