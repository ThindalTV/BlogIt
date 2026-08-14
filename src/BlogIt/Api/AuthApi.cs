using System.Security.Claims;
using BlogIt.Shared.DTOs;
using BlogIt.Shared.Helpers;
using BlogIt.Services;

namespace BlogIt.Api;

public static class AuthApi
{
    public static IEndpointRouteBuilder MapAuthApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").WithTags("Auth");

        group.MapPost("/login", async (LoginRequest request, IAuthService authService) =>
        {
            var response = await authService.LoginAsync(request);
            if (response is null)
                return Results.Unauthorized();
            return Results.Ok(response);
        })
            .AllowAnonymous()
            .RequireRateLimiting(BlogItDefaults.LoginRateLimiterPolicy);

        group.MapPost("/change-password", async (
            ChangePasswordRequest request,
            ClaimsPrincipal user,
            IAuthService authService) =>
        {
            if (PasswordPolicy.Validate(request.NewPassword) is string passwordError)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["newPassword"] = [passwordError]
                });
            }

            var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? user.FindFirstValue("sub")!);

            var success = await authService.ChangePasswordAsync(userId, request);
            if (!success)
                return Results.BadRequest("Current password is incorrect.");

            return Results.Ok(new { message = "Password changed successfully." });
        }).RequireAuthorization(BlogItDefaults.AdminAuthorizationPolicy);

        return app;
    }
}
