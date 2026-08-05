using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;

namespace BlogIt.AdminSite.Services;

public class AuthStateProvider(LocalStorageService localStorage) : AuthenticationStateProvider
{
    private const string TokenKey = "blogit_token";
    private static readonly AuthenticationState Anonymous = new(new ClaimsPrincipal(new ClaimsIdentity()));

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var token = await localStorage.GetAsync(TokenKey);
        if (string.IsNullOrWhiteSpace(token))
            return Anonymous;

        var claims = ParseClaimsFromJwt(token);
        if (claims is null)
            return Anonymous;

        // Check expiry
        var expClaim = claims.FirstOrDefault(c => c.Type == "exp");
        if (expClaim != null && long.TryParse(expClaim.Value, out var exp))
        {
            var expiry = DateTimeOffset.FromUnixTimeSeconds(exp);
            if (expiry < DateTimeOffset.UtcNow)
            {
                await localStorage.RemoveAsync(TokenKey);
                return Anonymous;
            }
        }

        var identity = new ClaimsIdentity(claims, "jwt");
        return new AuthenticationState(new ClaimsPrincipal(identity));
    }

    public async Task MarkUserAsLoggedIn(string token)
    {
        await localStorage.SetAsync(TokenKey, token);
        var claims = ParseClaimsFromJwt(token) ?? [];
        var identity = new ClaimsIdentity(claims, "jwt");
        var user = new ClaimsPrincipal(identity);
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(user)));
    }

    public async Task MarkUserAsLoggedOut()
    {
        await localStorage.RemoveAsync(TokenKey);
        NotifyAuthenticationStateChanged(Task.FromResult(Anonymous));
    }

    public async Task<string?> GetTokenAsync() => await localStorage.GetAsync(TokenKey);

    private static IEnumerable<Claim>? ParseClaimsFromJwt(string jwt)
    {
        try
        {
            var parts = jwt.Split('.');
            if (parts.Length != 3) return null;

            var payload = parts[1];
            // Fix base64 padding
            var mod4 = payload.Length % 4;
            if (mod4 != 0) payload += new string('=', 4 - mod4);
            payload = payload.Replace('-', '+').Replace('_', '/');

            var bytes = Convert.FromBase64String(payload);
            var json = Encoding.UTF8.GetString(bytes);
            var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
            if (dict is null) return null;

            var claims = new List<Claim>();
            foreach (var (key, value) in dict)
            {
                if (value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in value.EnumerateArray())
                        claims.Add(new Claim(key, item.GetString() ?? ""));
                }
                else
                {
                    claims.Add(new Claim(key, value.ToString()));
                }
            }
            return claims;
        }
        catch
        {
            return null;
        }
    }
}
