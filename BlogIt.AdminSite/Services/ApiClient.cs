using System.Net.Http.Headers;
using System.Net.Http.Json;
using BlogIt.Shared.DTOs;

namespace BlogIt.AdminSite.Services;

public class ApiClient(HttpClient http, LocalStorageService localStorage)
{
    private async Task PrepareAuthAsync()
    {
        var token = await localStorage.GetAsync("blogit_token");
        http.DefaultRequestHeaders.Authorization = string.IsNullOrWhiteSpace(token)
            ? null
            : new AuthenticationHeaderValue("Bearer", token);
    }

    // ── Auth ────────────────────────────────────────────────────────────────

    public async Task<LoginResponse?> LoginAsync(LoginRequest request)
    {
        var resp = await http.PostAsJsonAsync("api/auth/login", request);
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<LoginResponse>();
    }

    public async Task ChangePasswordAsync(ChangePasswordRequest request)
    {
        await PrepareAuthAsync();
        var resp = await http.PostAsJsonAsync("api/auth/change-password", request);
        resp.EnsureSuccessStatusCode();
    }

    // ── Setup ───────────────────────────────────────────────────────────────

    public async Task<SetupStatusResponse?> GetSetupStatusAsync()
    {
        return await http.GetFromJsonAsync<SetupStatusResponse>("api/setup/status");
    }

    public async Task InitializeAsync(SetupInitializeRequest request)
    {
        var resp = await http.PostAsJsonAsync("api/setup/initialize", request);
        resp.EnsureSuccessStatusCode();
    }

    // ── Posts ───────────────────────────────────────────────────────────────

    public async Task<PagedResult<BlogPostSummaryDto>?> GetPostsAsync(string? q = null, int page = 1, string? status = null)
    {
        await PrepareAuthAsync();
        var url = $"api/posts?page={page}&pageSize=20";
        if (!string.IsNullOrWhiteSpace(q)) url += $"&q={Uri.EscapeDataString(q)}";
        if (!string.IsNullOrWhiteSpace(status)) url += $"&status={status}";
        return await http.GetFromJsonAsync<PagedResult<BlogPostSummaryDto>>(url);
    }

    public async Task<BlogPostDetailDto?> GetPostAsync(Guid id)
    {
        await PrepareAuthAsync();
        return await http.GetFromJsonAsync<BlogPostDetailDto>($"api/posts/{id}");
    }

    public async Task<BlogPostDetailDto?> CreatePostAsync(CreateBlogPostRequest request)
    {
        await PrepareAuthAsync();
        var resp = await http.PostAsJsonAsync("api/posts", request);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<BlogPostDetailDto>();
    }

    public async Task UpdatePostAsync(Guid id, UpdateBlogPostRequest request)
    {
        await PrepareAuthAsync();
        var resp = await http.PutAsJsonAsync($"api/posts/{id}", request);
        resp.EnsureSuccessStatusCode();
    }

    public async Task DeletePostAsync(Guid id)
    {
        await PrepareAuthAsync();
        var resp = await http.DeleteAsync($"api/posts/{id}");
        resp.EnsureSuccessStatusCode();
    }

    public async Task PublishPostAsync(Guid id)
    {
        await PrepareAuthAsync();
        var resp = await http.PostAsync($"api/posts/{id}/publish", null);
        resp.EnsureSuccessStatusCode();
    }

    public async Task UnpublishPostAsync(Guid id)
    {
        await PrepareAuthAsync();
        var resp = await http.PostAsync($"api/posts/{id}/unpublish", null);
        resp.EnsureSuccessStatusCode();
    }

    public async Task UpdatePostScheduleAsync(Guid id, UpdatePublicationScheduleRequest request)
    {
        await PrepareAuthAsync();
        var resp = await http.PutAsJsonAsync($"api/posts/{id}/schedule", request);
        resp.EnsureSuccessStatusCode();
    }

    public async Task<PreviewLinkResponse?> CreatePostPreviewAsync(Guid id)
    {
        await PrepareAuthAsync();
        var response = await http.PostAsync($"api/previews/posts/{id}", null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PreviewLinkResponse>();
    }

    // ── Pages ───────────────────────────────────────────────────────────────

    public async Task<PagedResult<PageDto>?> GetPagesAsync()
    {
        await PrepareAuthAsync();
        return await http.GetFromJsonAsync<PagedResult<PageDto>>("api/pages");
    }

    public async Task<PageDto?> GetPageAsync(Guid id)
    {
        await PrepareAuthAsync();
        return await http.GetFromJsonAsync<PageDto>($"api/pages/{id}");
    }

    public async Task<PageDto?> CreatePageAsync(CreatePageRequest request)
    {
        await PrepareAuthAsync();
        var resp = await http.PostAsJsonAsync("api/pages", request);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<PageDto>();
    }

    public async Task UpdatePageAsync(Guid id, UpdatePageRequest request)
    {
        await PrepareAuthAsync();
        var resp = await http.PutAsJsonAsync($"api/pages/{id}", request);
        resp.EnsureSuccessStatusCode();
    }

    public async Task DeletePageAsync(Guid id)
    {
        await PrepareAuthAsync();
        var resp = await http.DeleteAsync($"api/pages/{id}");
        resp.EnsureSuccessStatusCode();
    }

    public async Task UpdatePageScheduleAsync(Guid id, UpdatePublicationScheduleRequest request)
    {
        await PrepareAuthAsync();
        var resp = await http.PutAsJsonAsync($"api/pages/{id}/schedule", request);
        resp.EnsureSuccessStatusCode();
    }

    public async Task<PreviewLinkResponse?> CreatePagePreviewAsync(Guid id)
    {
        await PrepareAuthAsync();
        var response = await http.PostAsync($"api/previews/pages/{id}", null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PreviewLinkResponse>();
    }

    // ── Media ───────────────────────────────────────────────────────────────

    public async Task<PagedResult<MediaFileDto>?> GetMediaAsync(string? q = null, int page = 1)
    {
        await PrepareAuthAsync();
        var url = $"api/media?page={page}&pageSize=20";
        if (!string.IsNullOrWhiteSpace(q)) url += $"&q={Uri.EscapeDataString(q)}";
        return await http.GetFromJsonAsync<PagedResult<MediaFileDto>>(url);
    }

    public async Task<MediaFileDto?> UploadMediaAsync(string title, Stream fileStream, string fileName, string contentType)
    {
        await PrepareAuthAsync();
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(title), "title");
        var streamContent = new StreamContent(fileStream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(streamContent, "file", fileName);
        var resp = await http.PostAsync("api/media/upload", content);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<MediaFileDto>();
    }

    public async Task DeleteMediaAsync(Guid id)
    {
        await PrepareAuthAsync();
        var resp = await http.DeleteAsync($"api/media/{id}");
        resp.EnsureSuccessStatusCode();
    }

    // ── Users ───────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<AppUserDto>?> GetUsersAsync()
    {
        await PrepareAuthAsync();
        return await http.GetFromJsonAsync<IReadOnlyList<AppUserDto>>("api/users");
    }

    public async Task<AppUserDto?> CreateUserAsync(CreateUserRequest request)
    {
        await PrepareAuthAsync();
        var resp = await http.PostAsJsonAsync("api/users", request);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<AppUserDto>();
    }

    public async Task DeleteUserAsync(Guid id)
    {
        await PrepareAuthAsync();
        var resp = await http.DeleteAsync($"api/users/{id}");
        resp.EnsureSuccessStatusCode();
    }

    // ── Redirects ───────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<UrlRedirectDto>?> GetRedirectsAsync()
    {
        await PrepareAuthAsync();
        return await http.GetFromJsonAsync<IReadOnlyList<UrlRedirectDto>>("api/redirects");
    }

    public async Task<UrlRedirectDto?> CreateRedirectAsync(SaveUrlRedirectRequest request)
    {
        await PrepareAuthAsync();
        var response = await http.PostAsJsonAsync("api/redirects", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<UrlRedirectDto>();
    }

    public async Task<UrlRedirectDto?> UpdateRedirectAsync(
        Guid id,
        SaveUrlRedirectRequest request)
    {
        await PrepareAuthAsync();
        var response = await http.PutAsJsonAsync($"api/redirects/{id}", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<UrlRedirectDto>();
    }

    public async Task DeleteRedirectAsync(Guid id)
    {
        await PrepareAuthAsync();
        var response = await http.DeleteAsync($"api/redirects/{id}");
        response.EnsureSuccessStatusCode();
    }

    // ── Settings ────────────────────────────────────────────────────────────

    public async Task<Dictionary<string, string>?> GetSettingsAsync()
    {
        await PrepareAuthAsync();
        return await http.GetFromJsonAsync<Dictionary<string, string>>("api/settings");
    }

    public async Task UpdateSettingsAsync(Dictionary<string, string> settings)
    {
        await PrepareAuthAsync();
        var resp = await http.PutAsJsonAsync("api/settings", settings);
        resp.EnsureSuccessStatusCode();
    }

    public async Task<AiProviderInfoDto?> GetAiProviderInfoAsync()
    {
        await PrepareAuthAsync();
        return await http.GetFromJsonAsync<AiProviderInfoDto>("api/settings/ai-provider");
    }

    // ── AI ──────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<AiConversationSummaryDto>?> GetConversationsAsync()
    {
        await PrepareAuthAsync();
        return await http.GetFromJsonAsync<IReadOnlyList<AiConversationSummaryDto>>("api/ai/conversations");
    }

    public async Task<AiConversationDetailDto?> GetConversationAsync(Guid id)
    {
        await PrepareAuthAsync();
        return await http.GetFromJsonAsync<AiConversationDetailDto>($"api/ai/conversations/{id}");
    }

    public async Task<AiConversationDetailDto?> CreateConversationAsync(CreateAiConversationRequest request)
    {
        await PrepareAuthAsync();
        var resp = await http.PostAsJsonAsync("api/ai/conversations", request);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<AiConversationDetailDto>();
    }

    public async Task<AiConversationDetailDto?> SendMessageAsync(Guid conversationId, SendAiMessageRequest request)
    {
        await PrepareAuthAsync();
        var resp = await http.PostAsJsonAsync($"api/ai/conversations/{conversationId}/messages", request);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<AiConversationDetailDto>();
    }

    public async Task<ExportAiConversationResponse?> ExportDraftAsync(
        Guid conversationId,
        ExportAiConversationRequest request)
    {
        await PrepareAuthAsync();
        var resp = await http.PostAsJsonAsync(
            $"api/ai/conversations/{conversationId}/export-draft",
            request);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<ExportAiConversationResponse>();
    }

    public async Task DeleteConversationAsync(Guid id)
    {
        await PrepareAuthAsync();
        var resp = await http.DeleteAsync($"api/ai/conversations/{id}");
        resp.EnsureSuccessStatusCode();
    }

    // ── Analytics ───────────────────────────────────────────────────────────

    public async Task<AnalyticsSummaryDto?> GetAnalyticsSummaryAsync(DateTime startDate, DateTime endDate)
    {
        await PrepareAuthAsync();
        var url = $"api/analytics/summary?startDate={startDate:yyyy-MM-dd}&endDate={endDate:yyyy-MM-dd}";
        var response = await http.GetAsync(url);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AnalyticsSummaryDto>();
    }
}
