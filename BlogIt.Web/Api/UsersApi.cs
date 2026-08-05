using BlogIt.Shared.Data;
using BlogIt.Shared.DTOs;
using BlogIt.Shared.Entities;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BlogIt.Web.Api;

public static class UsersApi
{
    public static IEndpointRouteBuilder MapUsersApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users")
            .WithTags("Users")
            .RequireAuthorization();

        group.MapGet("/", GetUsers);
        group.MapPost("/", CreateUser);
        group.MapDelete("/{id:guid}", DeleteUser);

        return app;
    }

    private static async Task<IResult> GetUsers(BlogItDbContext db)
    {
        var users = await db.Users
            .OrderBy(u => u.Username)
            .ToListAsync();

        return Results.Ok(users.Select(ToDto).ToList());
    }

    private static async Task<IResult> CreateUser(CreateUserRequest req, BlogItDbContext db)
    {
        if (await db.Users.AnyAsync(u => u.Username == req.Username))
            return Results.Conflict("Username already exists.");

        var user = new AppUser
        {
            Username = req.Username,
            DisplayName = req.DisplayName,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
            CreatedAt = DateTime.UtcNow,
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();
        return Results.Created($"/api/users/{user.Id}", ToDto(user));
    }

    private static async Task<IResult> DeleteUser(
        Guid id,
        BlogItDbContext db,
        ClaimsPrincipal user)
    {
        var currentUserId = Guid.Parse(user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (id == currentUserId)
            return Results.BadRequest("Cannot delete your own account.");

        var entity = await db.Users.FindAsync(id);
        if (entity is null) return Results.NotFound();

        db.Users.Remove(entity);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static AppUserDto ToDto(AppUser u) => new(u.Id, u.Username, u.DisplayName, u.CreatedAt);
}
