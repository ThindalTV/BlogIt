using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace BlogIt.Admin.Services;

/// <summary>
/// Composition of the admin's client-side services.
/// </summary>
/// <remarks>
/// Kept out of <c>Program.cs</c> so the composition itself is testable — specifically that every
/// <see cref="ApiClient"/> request runs through <see cref="AdminAuthMessageHandler"/>. Inline in
/// Program.cs that could only be confirmed by loading the app in a browser, and a handler that is
/// registered but not actually in the pipeline fails silently and looks exactly like the bug it was
/// added to fix.
/// </remarks>
public static class AdminServiceRegistration
{
    /// <param name="apiBaseAddress">Absolute base address of the BlogIt API, with a trailing slash.</param>
    /// <param name="createTerminalHandler">
    /// Builds the innermost handler of the HTTP pipeline. The app passes
    /// <see cref="HttpClientHandler"/>, which is the browser fetch handler under WebAssembly; tests
    /// pass a recorder so they can assert on what left the pipeline.
    /// </param>
    public static IServiceCollection AddBlogItAdminServices(
        this IServiceCollection services,
        Uri apiBaseAddress,
        Func<HttpMessageHandler> createTerminalHandler)
    {
        services.AddScoped<LocalStorageService>();
        services.AddScoped<AuthStateProvider>();
        // Same instance under both types: AuthorizeView resolves the base type while the pages cast
        // to the concrete one, and two instances would mean a login that never reaches the sidebar.
        services.AddScoped<AuthenticationStateProvider>(
            sp => sp.GetRequiredService<AuthStateProvider>());
        services.AddScoped<AdminAuthMessageHandler>();

        // Wired by hand rather than through AddHttpClient: IHttpClientFactory would pull
        // Microsoft.Extensions.Http into the WASM payload for the sake of one client, and a
        // WebAssembly app has a single scope anyway, so the factory's lifetime management and
        // handler pooling buy nothing here.
        services.AddScoped(sp =>
        {
            var authHandler = sp.GetRequiredService<AdminAuthMessageHandler>();
            authHandler.InnerHandler = createTerminalHandler();
            return new HttpClient(authHandler) { BaseAddress = apiBaseAddress };
        });
        services.AddScoped<ApiClient>();

        services.AddAuthorizationCore();
        return services;
    }
}
