using System.Net.Http.Headers;
using System.Net.Http.Json;
using BlogIt.MauiAdmin.Models;
using BlogIt.Shared.DTOs;

namespace BlogIt.MauiAdmin.Services;

/// <summary>
/// API client that always targets the currently active site profile.
/// </summary>
public class MauiApiClient(SiteProfileService profileService)
{
    private async Task<HttpClient> CreateClientAsync()
    {
        var profile = await profileService.GetActiveProfileAsync()
            ?? throw new InvalidOperationException("No active site profile. Please add a site first.");

        var client = new HttpClient { BaseAddress = new Uri(profile.ApiBaseUrl.TrimEnd('/') + "/") };

        if (profile.IsTokenValid)
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", profile.JwtToken);

        return client;
    }

    // ── Auth ────────────────────────────────────────────────────────────
    public async Task<LoginResponse?> LoginAsync(string profileId, string username, string password)
    {
        var profile = (await profileService.GetProfilesAsync())
            .FirstOrDefault(p => p.Id == profileId)
            ?? throw new InvalidOperationException("Profile not found.");

        using var client = new HttpClient { BaseAddress = new Uri(profile.ApiBaseUrl.TrimEnd('/') + "/") };
        var response = await client.PostAsJsonAsync("api/auth/login", new LoginRequest(username, password));
        if (!response.IsSuccessStatusCode) return null;

        var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
        if (result is not null)
            await profileService.SaveTokenAsync(profileId, result.Token, result.ExpiresAt,
                result.Username, result.DisplayName);

        return result;
    }

    public async Task<bool> ChangePasswordAsync(ChangePasswordRequest request)
    {
        using var client = await CreateClientAsync();
        var response = await client.PostAsJsonAsync("api/auth/change-password", request);
        return response.IsSuccessStatusCode;
    }

    // ── Setup ───────────────────────────────────────────────────────────
    public async Task<SetupStatusResponse?> GetSetupStatusAsync()
    {
        using var client = await CreateClientAsync();
        return await client.GetFromJsonAsync<SetupStatusResponse>("api/setup/status");
    }

    public async Task<bool> InitializeAsync(SetupInitializeRequest request)
    {
        using var client = await CreateClientAsync();
        var response = await client.PostAsJsonAsync("api/setup/initialize", request);
        return response.IsSuccessStatusCode;
    }

    // ── Posts ───────────────────────────────────────────────────────────
    public async Task<PagedResult<BlogPostSummaryDto>?> GetPostsAsync(string? q = null, int page = 1, string status = "all")
    {
        using var client = await CreateClientAsync();
        return await client.GetFromJsonAsync<PagedResult<BlogPostSummaryDto>>(
            $"api/posts?q={Uri.EscapeDataString(q ?? "")}&page={page}&status={status}");
    }

    public async Task<BlogPostDetailDto?> GetPostAsync(Guid id)
    {
        using var client = await CreateClientAsync();
        return await client.GetFromJsonAsync<BlogPostDetailDto>($"api/posts/{id}");
    }

    public async Task<BlogPostDetailDto?> CreatePostAsync(CreateBlogPostRequest request)
    {
        using var client = await CreateClientAsync();
        var response = await client.PostAsJsonAsync("api/posts", request);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<BlogPostDetailDto>()
            : null;
    }

    public async Task<BlogPostDetailDto?> UpdatePostAsync(Guid id, UpdateBlogPostRequest request)
    {
        using var client = await CreateClientAsync();
        var response = await client.PutAsJsonAsync($"api/posts/{id}", request);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<BlogPostDetailDto>()
            : null;
    }

    public async Task<bool> DeletePostAsync(Guid id)
    {
        using var client = await CreateClientAsync();
        var r = await client.DeleteAsync($"api/posts/{id}");
        return r.IsSuccessStatusCode;
    }

    public async Task<bool> PublishPostAsync(Guid id)
    {
        using var client = await CreateClientAsync();
        var r = await client.PostAsync($"api/posts/{id}/publish", null);
        return r.IsSuccessStatusCode;
    }

    public async Task<bool> UnpublishPostAsync(Guid id)
    {
        using var client = await CreateClientAsync();
        var r = await client.PostAsync($"api/posts/{id}/unpublish", null);
        return r.IsSuccessStatusCode;
    }

    // ── Pages ───────────────────────────────────────────────────────────
    public async Task<PagedResult<PageDto>?> GetPagesAsync(string? q = null, int page = 1)
    {
        using var client = await CreateClientAsync();
        return await client.GetFromJsonAsync<PagedResult<PageDto>>(
            $"api/pages?q={Uri.EscapeDataString(q ?? "")}&page={page}");
    }

    public async Task<PageDto?> GetPageAsync(Guid id)
    {
        using var client = await CreateClientAsync();
        return await client.GetFromJsonAsync<PageDto>($"api/pages/{id}");
    }

    public async Task<PageDto?> CreatePageAsync(CreatePageRequest request)
    {
        using var client = await CreateClientAsync();
        var r = await client.PostAsJsonAsync("api/pages", request);
        return r.IsSuccessStatusCode ? await r.Content.ReadFromJsonAsync<PageDto>() : null;
    }

    public async Task<PageDto?> UpdatePageAsync(Guid id, UpdatePageRequest request)
    {
        using var client = await CreateClientAsync();
        var r = await client.PutAsJsonAsync($"api/pages/{id}", request);
        return r.IsSuccessStatusCode ? await r.Content.ReadFromJsonAsync<PageDto>() : null;
    }

    public async Task<bool> DeletePageAsync(Guid id)
    {
        using var client = await CreateClientAsync();
        return (await client.DeleteAsync($"api/pages/{id}")).IsSuccessStatusCode;
    }

    // ── Media ───────────────────────────────────────────────────────────
    public async Task<PagedResult<MediaFileDto>?> GetMediaAsync(string? q = null, int page = 1)
    {
        using var client = await CreateClientAsync();
        return await client.GetFromJsonAsync<PagedResult<MediaFileDto>>(
            $"api/media?q={Uri.EscapeDataString(q ?? "")}&page={page}");
    }

    public async Task<MediaFileDto?> UploadMediaAsync(string title, Stream data, string fileName, string contentType)
    {
        using var client = await CreateClientAsync();
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(title), "title");
        var fileContent = new StreamContent(data);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(fileContent, "file", fileName);

        var r = await client.PostAsync("api/media/upload", form);
        return r.IsSuccessStatusCode ? await r.Content.ReadFromJsonAsync<MediaFileDto>() : null;
    }

    public async Task<bool> DeleteMediaAsync(Guid id)
    {
        using var client = await CreateClientAsync();
        return (await client.DeleteAsync($"api/media/{id}")).IsSuccessStatusCode;
    }

    // ── Users ───────────────────────────────────────────────────────────
    public async Task<List<AppUserDto>?> GetUsersAsync()
    {
        using var client = await CreateClientAsync();
        return await client.GetFromJsonAsync<List<AppUserDto>>("api/users");
    }

    public async Task<AppUserDto?> CreateUserAsync(CreateUserRequest request)
    {
        using var client = await CreateClientAsync();
        var r = await client.PostAsJsonAsync("api/users", request);
        return r.IsSuccessStatusCode ? await r.Content.ReadFromJsonAsync<AppUserDto>() : null;
    }

    public async Task<bool> DeleteUserAsync(Guid id)
    {
        using var client = await CreateClientAsync();
        return (await client.DeleteAsync($"api/users/{id}")).IsSuccessStatusCode;
    }

    // ── Settings ────────────────────────────────────────────────────────
    public async Task<Dictionary<string, string>?> GetSettingsAsync()
    {
        using var client = await CreateClientAsync();
        return await client.GetFromJsonAsync<Dictionary<string, string>>("api/settings");
    }

    public async Task<bool> UpdateSettingsAsync(Dictionary<string, string> settings)
    {
        using var client = await CreateClientAsync();
        return (await client.PutAsJsonAsync("api/settings", settings)).IsSuccessStatusCode;
    }

    // ── AI ──────────────────────────────────────────────────────────────
    public async Task<List<AiConversationSummaryDto>?> GetConversationsAsync()
    {
        using var client = await CreateClientAsync();
        return await client.GetFromJsonAsync<List<AiConversationSummaryDto>>("api/ai/conversations");
    }

    public async Task<AiConversationDetailDto?> GetConversationAsync(Guid id)
    {
        using var client = await CreateClientAsync();
        return await client.GetFromJsonAsync<AiConversationDetailDto>($"api/ai/conversations/{id}");
    }

    public async Task<AiConversationSummaryDto?> CreateConversationAsync(string title)
    {
        using var client = await CreateClientAsync();
        var r = await client.PostAsJsonAsync("api/ai/conversations", new CreateAiConversationRequest(title));
        return r.IsSuccessStatusCode
            ? await r.Content.ReadFromJsonAsync<AiConversationSummaryDto>()
            : null;
    }

    public async Task<AiConversationDetailDto?> SendMessageAsync(Guid conversationId, string content)
    {
        using var client = await CreateClientAsync();
        var r = await client.PostAsJsonAsync(
            $"api/ai/conversations/{conversationId}/messages",
            new SendAiMessageRequest(content));
        return r.IsSuccessStatusCode
            ? await r.Content.ReadFromJsonAsync<AiConversationDetailDto>()
            : null;
    }

    public async Task<Guid?> ExportDraftAsync(Guid conversationId, string? instructions)
    {
        using var client = await CreateClientAsync();
        var r = await client.PostAsJsonAsync(
            $"api/ai/conversations/{conversationId}/export-draft",
            new ExportAiConversationRequest(instructions));
        if (!r.IsSuccessStatusCode) return null;
        var obj = await r.Content.ReadFromJsonAsync<ExportAiConversationResponse>();
        return obj?.PostId;
    }

    public async Task<bool> DeleteConversationAsync(Guid id)
    {
        using var client = await CreateClientAsync();
        return (await client.DeleteAsync($"api/ai/conversations/{id}")).IsSuccessStatusCode;
    }

    // ── Analytics ───────────────────────────────────────────────────────
    public async Task<AnalyticsSummaryDto?> GetAnalyticsSummaryAsync(string startDate, string endDate)
    {
        using var client = await CreateClientAsync();
        return await client.GetFromJsonAsync<AnalyticsSummaryDto>(
            $"api/analytics/summary?startDate={startDate}&endDate={endDate}");
    }

}
