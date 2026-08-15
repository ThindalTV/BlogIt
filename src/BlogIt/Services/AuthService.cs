using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using BlogIt.Shared;
using BlogIt.Shared.Data;
using BlogIt.Shared.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace BlogIt.Services;

public class AuthService(BlogItDbContext db, ISettingsService settings) : IAuthService
{
    // Verified against whenever the username isn't found, so a nonexistent username takes the
    // same BCrypt-verify time as a real one and can't be distinguished by response timing.
    private static readonly string DummyPasswordHash =
        BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString());

    public async Task<LoginResponse?> LoginAsync(LoginRequest request)
    {
        var user = await db.Users
            .FirstOrDefaultAsync(u => u.Username == request.Username);

        var passwordValid = BCrypt.Net.BCrypt.Verify(request.Password, user?.PasswordHash ?? DummyPasswordHash);
        if (user is null || !passwordValid)
            return null;

        var secret = await settings.GetAsync(SettingKeys.JwtSecret) ?? string.Empty;
        var expiryStr = await settings.GetAsync(SettingKeys.JwtExpiryMinutes) ?? "60";
        var expiry = int.TryParse(expiryStr, out var m) ? m : 60;

        var expiresAt = DateTime.UtcNow.AddMinutes(expiry);
        var token = GenerateToken(
            user.Id,
            user.Username,
            user.DisplayName,
            user.SecurityStamp,
            secret,
            expiry);
        return new LoginResponse(token, user.Username, user.DisplayName, expiresAt);
    }

    public async Task<bool> ChangePasswordAsync(Guid userId, ChangePasswordRequest request)
    {
        var user = await db.Users.FindAsync(userId);
        if (user is null || !BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
            return false;

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        // Moving the stamp is what makes the change take effect now rather than whenever the
        // existing tokens happen to expire: every one of them carries the old value and stops
        // validating on the next request. Includes the session doing the change, which is why
        // clients re-authenticate after a successful password change.
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        await db.SaveChangesAsync();
        return true;
    }

    public string GenerateToken(
        Guid userId,
        string username,
        string displayName,
        string securityStamp,
        string secret,
        int expiryMinutes)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.UniqueName, username),
            new Claim("displayName", displayName),
            new Claim(BlogItClaimTypes.SecurityStamp, securityStamp),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expiryMinutes),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
