using BlogIt.Shared.Data;
using BlogIt.Shared.DTOs;
using BlogIt.Shared.Entities;
using BlogIt.Shared.Helpers;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BlogIt.Api;

// INTENTIONAL: there is no role/permission tier here — every AppUser is fully privileged and
// there's no ownership check on Posts/Pages/Media (any authenticated user can edit/delete
// content created by any other user). Creating a second user account is equivalent to granting
// full site control, by design, for the same reason MarkdownHelper.cs's HTML passthrough is
// unsanitized: BlogIt currently assumes every author is as trusted as the site owner. See
// AUDIT_REPORT.md finding #6. [[roles-and-permissions]] tracks the plan to add roles later —
// the codebase already has the ownership FKs (BlogPost.AuthorId, MediaFile.UploadedByUserId)
// needed to build that without a rework.
public static class UsersApi
{
    public static IEndpointRouteBuilder MapUsersApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/users")
            .WithTags("Users")
            .RequireAuthorization(BlogItDefaults.AdminAuthorizationPolicy);

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

    private static async Task<IResult> CreateUser(
        CreateUserRequest req,
        BlogItDbContext db,
        BlogItOptions options)
    {
        if (await db.Users.AnyAsync(u => u.Username == req.Username))
            return Results.Conflict("Username already exists.");

        if (PasswordPolicy.Validate(req.Password) is string passwordError)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["password"] = [passwordError]
            });
        }

        var user = new AppUser
        {
            Username = req.Username,
            DisplayName = req.DisplayName,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
            CreatedAt = DateTime.UtcNow,
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();
        return Results.Created(
            BlogItPath.Combine(options.ApiPath, $"users/{user.Id}"),
            ToDto(user));
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

        // BlogPost.AuthorId, MediaFile.UploadedByUserId and AiConversation.CreatedByUserId are all
        // DeleteBehavior.Restrict, so the database refuses this and EF throws. Checking first turns
        // an unhandled DbUpdateException — a bare 500 — into a 409 that says what is in the way and
        // what to do about it. The counts are deliberately in the message: "reassign their posts"
        // is not actionable without knowing there are eleven of them.
        var authoredPosts = await db.BlogPosts.CountAsync(post => post.AuthorId == id);
        var uploadedMedia = await db.MediaFiles.CountAsync(media => media.UploadedByUserId == id);
        var conversations = await db.AiConversations.CountAsync(item => item.CreatedByUserId == id);

        if (authoredPosts + uploadedMedia + conversations > 0)
        {
            var owned = new List<string>();
            if (authoredPosts > 0) owned.Add($"{authoredPosts} post{(authoredPosts == 1 ? "" : "s")}");
            if (uploadedMedia > 0) owned.Add($"{uploadedMedia} media file{(uploadedMedia == 1 ? "" : "s")}");
            if (conversations > 0) owned.Add($"{conversations} AI conversation{(conversations == 1 ? "" : "s")}");

            return Results.Conflict(
                $"This user still owns {string.Join(", ", owned)}. Reassign or delete that content first.");
        }

        db.Users.Remove(entity);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static AppUserDto ToDto(AppUser u) => new(u.Id, u.Username, u.DisplayName, u.CreatedAt);
}
