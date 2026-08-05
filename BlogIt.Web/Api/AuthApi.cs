using System.Security.Claims;
using BlogIt.Shared.DTOs;
using BlogIt.Web.Services;

namespace BlogIt.Web.Api;

public static class AuthApi
{
    public static IEndpointRouteBuilder MapAuthApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/login", async (LoginRequest request, IAuthService authService) =>
        {
            var response = await authService.LoginAsync(request);
            if (response is null)
                return Results.Unauthorized();
            return Results.Ok(response);
        });

        group.MapPost("/change-password", async (
            ChangePasswordRequest request,
            ClaimsPrincipal user,
            IAuthService authService) =>
        {
            var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? user.FindFirstValue("sub")!);

            var success = await authService.ChangePasswordAsync(userId, request);
            if (!success)
                return Results.BadRequest("Current password is incorrect.");

            return Results.Ok(new { message = "Password changed successfully." });
        }).RequireAuthorization();

        return app;
    }
}
