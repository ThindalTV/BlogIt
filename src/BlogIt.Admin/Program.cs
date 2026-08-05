using System.Net.Http.Json;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.Components.Authorization;
using BlogIt.Admin;
using BlogIt.Admin.Services;
using BlogIt.Shared;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

using var bootstrapClient = new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
};
var bootstrap = await bootstrapClient.GetFromJsonAsync<BlogItAdminBootstrapConfig>(
    BlogItAdminBootstrapConfig.RelativePath)
    ?? throw new InvalidOperationException("BlogIt admin configuration was empty.");
var apiBaseAddress = CreateApiBaseAddress(
    new Uri(builder.HostEnvironment.BaseAddress),
    bootstrap.ApiPath);

builder.Services.AddScoped(_ => new HttpClient { BaseAddress = apiBaseAddress });
builder.Services.AddScoped<LocalStorageService>();
builder.Services.AddScoped<ApiClient>();
builder.Services.AddScoped<AuthStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(
    sp => sp.GetRequiredService<AuthStateProvider>());
builder.Services.AddAuthorizationCore();

await builder.Build().RunAsync();

static Uri CreateApiBaseAddress(Uri adminBaseAddress, string apiPath)
{
    if (!apiPath.StartsWith("/", StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            $"BlogIt admin configuration returned an invalid API path '{apiPath}'.");
    }

    var origin = new Uri($"{adminBaseAddress.Scheme}://{adminBaseAddress.Authority}/");
    return new Uri(origin, $"{apiPath.Trim('/')}/");
}
