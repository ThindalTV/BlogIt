namespace BlogIt.Shared.DTOs;

public record LoginRequest(string Username, string Password);

public record LoginResponse(string Token, string Username, string DisplayName, DateTime ExpiresAt);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
