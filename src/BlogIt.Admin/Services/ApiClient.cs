using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BlogIt.Shared.DTOs;

namespace BlogIt.Admin.Services;

public class ApiClient(HttpClient http, LocalStorageService localStorage)
{
    private async Task PrepareAuthAsync()
    {
        var token = await localStorage.GetAsync("blogit_token");
        http.DefaultRequestHeaders.Authorization = string.IsNullOrWhiteSpace(token)
            ? null
            : new AuthenticationHeaderValue("Bearer", token);
    }

    // Server endpoints report failures as a plain JSON string (Results.BadRequest(string)/
    // Results.Conflict(string)) or as an RFC7807 ValidationProblem (Results.ValidationProblem)
    // with per-field messages under "errors". Surface the actual reason instead of the default
    // "net_http_message_not_success_statuscode_reason" text EnsureSuccessStatusCode() would throw.
    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync();
        var message = ExtractErrorMessage(body);

        throw new HttpRequestException(
            string.IsNullOrWhiteSpace(message) ? $"Request failed ({(int)response.StatusCode})." : message,
            null,
            response.StatusCode);
    }

    private static string? ExtractErrorMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.String)
                return root.GetString();

            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("errors", out var errors)
                && errors.ValueKind == JsonValueKind.Object)
            {
                foreach (var field in errors.EnumerateObject())
                {
                    if (field.Value.ValueKind == JsonValueKind.Array && field.Value.GetArrayLength() > 0)
                        return field.Value[0].GetString();
                }
            }

            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("detail", out var detail)
                && detail.ValueKind == JsonValueKind.String)
            {
                return detail.GetString();
            }

            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("title", out var title)
                && title.ValueKind == JsonValueKind.String)
            {
                return title.GetString();
            }

            return body;
        }
        catch (JsonException)
        {
            return body;
        }
    }

    // ── Auth ────────────────────────────────────────────────────────────────

    public async Task<LoginResponse?> LoginAsync(LoginRequest request)
    {
        var resp = await http.PostAsJsonAsync("auth/login", request);
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<LoginResponse>();
    }

    public async Task ChangePasswordAsync(ChangePasswordRequest request)
    {
        await PrepareAuthAsync();
        var resp = await http.PostAsJsonAsync("auth/change-password", request);
        await EnsureSuccessAsync(resp);
    }

    // ── Setup ───────────────────────────────────────────────────────────────

    public async Task<SetupStatusResponse?> GetSetupStatusAsync()
    {
        return await http.GetFromJsonAsync<SetupStatusResponse>("setup/status");
    }

    public async Task InitializeAsync(SetupInitializeRequest request)
    {
        var resp = await http.PostAsJsonAsync("setup/initialize", request);
        await EnsureSuccessAsync(resp);
    }

    // ── Posts ───────────────────────────────────────────────────────────────

    public async Task<PagedResult<BlogPostSummaryDto>?> GetPostsAsync(string? q = null, int page = 1, string? status = null)
    {
        await PrepareAuthAsync();
        var url = $"posts?page={page}&pageSize=20";
        if (!string.IsNullOrWhiteSpace(q)) url += $"&q={Uri.EscapeDataString(q)}";
        if (!string.IsNullOrWhiteSpace(status)) url += $"&status={status}";
        return await http.GetFromJsonAsync<PagedResult<BlogPostSummaryDto>>(url);
    }

    public async Task<BlogPostDetailDto?> GetPostAsync(Guid id)
    {
        await PrepareAuthAsync();
        return await http.GetFromJsonAsync<BlogPostDetailDto>($"posts/{id}");
    }

    public async Task<BlogPostDetailDto?> CreatePostAsync(CreateBlogPostRequest request)
    {
        await PrepareAuthAsync();
        var resp = await http.PostAsJsonAsync("posts", request);
        await EnsureSuccessAsync(resp);
        return await resp.Content.ReadFromJsonAsync<BlogPostDetailDto>();
    }

    public async Task UpdatePostAsync(Guid id, UpdateBlogPostRequest request)
    {
        await PrepareAuthAsync();
        var resp = await http.PutAsJsonAsync($"posts/{id}", request);
        await EnsureSuccessAsync(resp);
    }

    public async Task DeletePostAsync(Guid id)
    {
        await PrepareAuthAsync();
        var resp = await http.DeleteAsync($"posts/{id}");
        resp.EnsureSuccessStatusCode();
    }

    public async Task PublishPostAsync(Guid id)
    {
        await PrepareAuthAsync();
        var resp = await http.PostAsync($"posts/{id}/publish", null);
        resp.EnsureSuccessStatusCode();
    }

    public async Task UnpublishPostAsync(Guid id)
    {
        await PrepareAuthAsync();
        var resp = await http.PostAsync($"posts/{id}/unpublish", null);
        resp.EnsureSuccessStatusCode();
    }

    public async Task UpdatePostScheduleAsync(Guid id, UpdatePublicationScheduleRequest request)
    {
        await PrepareAuthAsync();
        var resp = await http.PutAsJsonAsync($"posts/{id}/schedule", request);
        await EnsureSuccessAsync(resp);
    }

    public async Task<PreviewLinkResponse?> CreatePostPreviewAsync(Guid id)
    {
        await PrepareAuthAsync();
        var response = await http.PostAsync($"previews/posts/{id}", null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PreviewLinkResponse>();
    }

    // ── Pages ───────────────────────────────────────────────────────────────

    public async Task<PagedResult<PageDto>?> GetPagesAsync()
    {
        await PrepareAuthAsync();
        return await http.GetFromJsonAsync<PagedResult<PageDto>>("pages");
    }

    public async Task<PageDto?> GetPageAsync(Guid id)
    {
        await PrepareAuthAsync();
        return await http.GetFromJsonAsync<PageDto>($"pages/{id}");
    }

    public async Task<PageDto?> CreatePageAsync(CreatePageRequest request)
    {
        await PrepareAuthAsync();
        var resp = await http.PostAsJsonAsync("pages", request);
        await EnsureSuccessAsync(resp);
        return await resp.Content.ReadFromJsonAsync<PageDto>();
    }

    public async Task UpdatePageAsync(Guid id, UpdatePageRequest request)
    {
        await PrepareAuthAsync();
        var resp = await http.PutAsJsonAsync($"pages/{id}", request);
        await EnsureSuccessAsync(resp);
    }

    public async Task DeletePageAsync(Guid id)
    {
        await PrepareAuthAsync();
        var resp = await http.DeleteAsync($"pages/{id}");
        resp.EnsureSuccessStatusCode();
    }

    public async Task UpdatePageScheduleAsync(Guid id, UpdatePublicationScheduleRequest request)
    {
        await PrepareAuthAsync();
        var resp = await http.PutAsJsonAsync($"pages/{id}/schedule", request);
        await EnsureSuccessAsync(resp);
    }

    public async Task<PreviewLinkResponse?> CreatePagePreviewAsync(Guid id)
    {
        await PrepareAuthAsync();
        var response = await http.PostAsync($"previews/pages/{id}", null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PreviewLinkResponse>();
    }

    // ── Media ───────────────────────────────────────────────────────────────

    public async Task<PagedResult<MediaFileDto>?> GetMediaAsync(string? q = null, int page = 1)
    {
        await PrepareAuthAsync();
        var url = $"media?page={page}&pageSize=20";
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
        var resp = await http.PostAsync("media/upload", content);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<MediaFileDto>();
    }

    public async Task DeleteMediaAsync(Guid id)
    {
        await PrepareAuthAsync();
        var resp = await http.DeleteAsync($"media/{id}");
        resp.EnsureSuccessStatusCode();
    }

    // ── Users ───────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<AppUserDto>?> GetUsersAsync()
    {
        await PrepareAuthAsync();
        return await http.GetFromJsonAsync<IReadOnlyList<AppUserDto>>("users");
    }

    public async Task<AppUserDto?> CreateUserAsync(CreateUserRequest request)
    {
        await PrepareAuthAsync();
        var resp = await http.PostAsJsonAsync("users", request);
        await EnsureSuccessAsync(resp);
        return await resp.Content.ReadFromJsonAsync<AppUserDto>();
    }

    public async Task DeleteUserAsync(Guid id)
    {
        await PrepareAuthAsync();
        var resp = await http.DeleteAsync($"users/{id}");
        resp.EnsureSuccessStatusCode();
    }

    // ── Redirects ───────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<UrlRedirectDto>?> GetRedirectsAsync()
    {
        await PrepareAuthAsync();
        return await http.GetFromJsonAsync<IReadOnlyList<UrlRedirectDto>>("redirects");
    }

    public async Task<UrlRedirectDto?> CreateRedirectAsync(SaveUrlRedirectRequest request)
    {
        await PrepareAuthAsync();
        var response = await http.PostAsJsonAsync("redirects", request);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<UrlRedirectDto>();
    }

    public async Task<UrlRedirectDto?> UpdateRedirectAsync(
        Guid id,
        SaveUrlRedirectRequest request)
    {
        await PrepareAuthAsync();
        var response = await http.PutAsJsonAsync($"redirects/{id}", request);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<UrlRedirectDto>();
    }

    public async Task DeleteRedirectAsync(Guid id)
    {
        await PrepareAuthAsync();
        var response = await http.DeleteAsync($"redirects/{id}");
        response.EnsureSuccessStatusCode();
    }

    // ── Settings ────────────────────────────────────────────────────────────

    public async Task<Dictionary<string, string>?> GetSettingsAsync()
    {
        await PrepareAuthAsync();
        return await http.GetFromJsonAsync<Dictionary<string, string>>("settings");
    }

    public async Task UpdateSettingsAsync(SiteSettingsUpdateRequest settings)
    {
        await PrepareAuthAsync();
        var resp = await http.PutAsJsonAsync("settings", settings);
        await EnsureSuccessAsync(resp);
    }

    /// <summary>
    /// Replaces the JWT signing secret server-side. Invalidates every existing token including
    /// this client's own, so the caller must send the user back to the login screen.
    /// </summary>
    public async Task RotateJwtSecretAsync()
    {
        await PrepareAuthAsync();
        var resp = await http.PostAsync("settings/jwt-secret/rotate", null);
        await EnsureSuccessAsync(resp);
    }

    public async Task<AiProviderInfoDto?> GetAiProviderInfoAsync()
    {
        await PrepareAuthAsync();
        return await http.GetFromJsonAsync<AiProviderInfoDto>("settings/ai-provider");
    }

    // ── AI ──────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<AiConversationSummaryDto>?> GetConversationsAsync()
    {
        await PrepareAuthAsync();
        return await http.GetFromJsonAsync<IReadOnlyList<AiConversationSummaryDto>>("ai/conversations");
    }

    public async Task<AiConversationDetailDto?> GetConversationAsync(Guid id)
    {
        await PrepareAuthAsync();
        return await http.GetFromJsonAsync<AiConversationDetailDto>($"ai/conversations/{id}");
    }

    public async Task<AiConversationDetailDto?> CreateConversationAsync(CreateAiConversationRequest request)
    {
        await PrepareAuthAsync();
        var resp = await http.PostAsJsonAsync("ai/conversations", request);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<AiConversationDetailDto>();
    }

    public async Task<AiConversationDetailDto?> SendMessageAsync(Guid conversationId, SendAiMessageRequest request)
    {
        await PrepareAuthAsync();
        var resp = await http.PostAsJsonAsync($"ai/conversations/{conversationId}/messages", request);
        await EnsureSuccessAsync(resp);
        return await resp.Content.ReadFromJsonAsync<AiConversationDetailDto>();
    }

    public async Task<ExportAiConversationResponse?> ExportDraftAsync(
        Guid conversationId,
        ExportAiConversationRequest request)
    {
        await PrepareAuthAsync();
        var resp = await http.PostAsJsonAsync(
            $"ai/conversations/{conversationId}/export-draft",
            request);
        await EnsureSuccessAsync(resp);
        return await resp.Content.ReadFromJsonAsync<ExportAiConversationResponse>();
    }

    public async Task DeleteConversationAsync(Guid id)
    {
        await PrepareAuthAsync();
        var resp = await http.DeleteAsync($"ai/conversations/{id}");
        resp.EnsureSuccessStatusCode();
    }

    // ── Analytics ───────────────────────────────────────────────────────────

    public async Task<AnalyticsSummaryDto?> GetAnalyticsSummaryAsync(DateTime startDate, DateTime endDate)
    {
        await PrepareAuthAsync();
        var url = $"analytics/summary?startDate={startDate:yyyy-MM-dd}&endDate={endDate:yyyy-MM-dd}";
        var response = await http.GetAsync(url);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AnalyticsSummaryDto>();
    }
}
