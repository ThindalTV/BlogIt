using System.Net;
using BlogIt.Admin.Services;
using BlogIt.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace BlogIt.Tests.Unit;

/// <summary>
/// Asserts the admin's client-side composition end to end. Without this the one thing that makes
/// <see cref="AdminAuthMessageHandler"/> work at all — that the <see cref="ApiClient"/>'s HttpClient
/// is actually built on it — would only ever be checked by running the app in a browser.
/// </summary>
public class AdminServiceRegistrationTests
{
    private const string TokenKey = "blogit_token";
    private static readonly Uri ApiBase = new("https://blog.example/api/");

    private static (IServiceScope Scope,
                    RecordingHttpMessageHandler Http,
                    FakeBrowserJsRuntime Js,
                    RecordingNavigationManager Nav,
                    ServiceProvider Provider) Build(string? storedToken = "jwt-abc")
    {
        var js = new FakeBrowserJsRuntime();
        if (storedToken is not null)
            js.Storage[TokenKey] = storedToken;
        var http = new RecordingHttpMessageHandler();
        var nav = new RecordingNavigationManager("pages");

        var services = new ServiceCollection();
        services.AddSingleton<IJSRuntime>(js);
        services.AddSingleton<NavigationManager>(nav);
        services.AddBlogItAdminServices(ApiBase, () => http);

        var provider = services.BuildServiceProvider();
        return (provider.CreateScope(), http, js, nav, provider);
    }

    [Fact]
    public async Task ApiClientRequests_RunThroughTheAuthHandler()
    {
        var (scope, http, _, _, provider) = Build();
        using (provider)
        using (scope)
        {
            var api = scope.ServiceProvider.GetRequiredService<ApiClient>();

            await api.GetPagesAsync();

            http.SingleRequest.RequestUri.Should()
                .Be(new Uri(ApiBase, "pages?page=1&pageSize=20"));
            http.SingleRequest.Headers.Authorization!.Parameter.Should().Be("jwt-abc");
        }
    }

    [Fact]
    public async Task A401OnAnyApiClientCall_SignsTheUserOutAndRedirects()
    {
        var (scope, http, js, nav, provider) = Build();
        using (provider)
        using (scope)
        {
            http.Respond(HttpStatusCode.Unauthorized);
            var api = scope.ServiceProvider.GetRequiredService<ApiClient>();

            var act = async () => await api.GetPagesAsync();

            // GetFromJsonAsync still throws — the point is what happened around it.
            await act.Should().ThrowAsync<HttpRequestException>();
            js.Storage.Should().NotContainKey(TokenKey);
            nav.Navigations.Should().ContainSingle().Which.Should().StartWith("login?returnUrl=");
        }
    }

    [Fact]
    public void AuthenticationStateProvider_ResolvesToTheSameInstanceAsAuthStateProvider()
    {
        // AuthorizeView resolves the base type while the pages cast to the concrete one; two
        // instances would mean a login that never reaches the sidebar.
        var (scope, _, _, _, provider) = Build();
        using (provider)
        using (scope)
        {
            scope.ServiceProvider.GetRequiredService<AuthenticationStateProvider>()
                .Should().BeSameAs(scope.ServiceProvider.GetRequiredService<AuthStateProvider>());
        }
    }
}
