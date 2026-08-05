namespace BlogIt.Shared.DTOs;

public record AppUserDto(Guid Id, string Username, string DisplayName, DateTime CreatedAt);

public record CreateUserRequest(string Username, string DisplayName, string Password);
