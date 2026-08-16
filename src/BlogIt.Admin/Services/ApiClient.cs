using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BlogIt.Shared.DTOs;

namespace BlogIt.Admin.Services;

/// <summary>
/// Typed wrapper over the BlogIt admin API. Authentication is not its concern: the injected
/// <see cref="HttpClient"/> is built on <see cref="AdminAuthMessageHandler"/>, which attaches the
/// bearer token and handles a rejected one. The per-method <c>PrepareAuthAsync</c> this class used
/// to call is gone — a request could not be sent unauthenticated by omission any more.
/// </summary>
public class ApiClient(HttpClient http)
{
    /// <summary>
    /// Rows requested per list call. Matches the server's own default; the server clamps anything
    /// above 100. Sent explicitly rather than relying on that default so the value the UI pages
    /// with and the value the server slices by can never drift apart.
    /// </summary>
    public const int DefaultPageSize = 20;

    // Server endpoints report failures as a plain JSON string (Results.BadRequest(string)/
    // Results.Conflict(string)) or as an RFC7807 ValidationProblem (Results.ValidationProblem)
    // with per-field messages under "errors". Surface the actual reason instead of the default
    // "net_http_message_not_success_statuscode_reason" text EnsureSuccessStatusCode() would throw.
    //
    // Every mutating call must route through here. Several of these refusals are the only thing
    // telling the operator what to do next — a 409 from ConcurrencyGuard says to reload before
    // reapplying, and a refused user delete names the content still blocking it — and the pages
    // show ex.Message verbatim, so a raw EnsureSuccessStatusCode() discards exactly the sentence
    // that mattered.
    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;

        var message = ExtractErrorMessage(await response.Content.ReadAsStringAsync());

        throw new HttpRequestException(
            string.IsNullOrWhiteSpace(message) ? $"Request failed ({(int)response.StatusCode})." : message,
            null,
            response.StatusCode);
    }

    /// <summary>
    /// The server's own words for this failure, or null when the body is not one of its error
    /// shapes and the bare status is the better thing to report.
    /// </summary>
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

            if (root.ValueKind != JsonValueKind.Object)
                return null;

            // Before "detail" and "title": on a ValidationProblem both of those hold the same
            // generic "One or more validation errors occurred.", and the field message under
            // "errors" is the only part that names what is actually wrong.
            if (root.TryGetProperty("errors", out var errors)
                && errors.ValueKind == JsonValueKind.Object)
            {
                foreach (var field in errors.EnumerateObject())
                {
                    if (field.Value.ValueKind == JsonValueKind.Array && field.Value.GetArrayLength() > 0)
                        return field.Value[0].GetString();
                }
            }

            if (root.TryGetProperty("detail", out var detail)
                && detail.ValueKind == JsonValueKind.String)
            {
                return detail.GetString();
            }

            if (root.TryGetProperty("title", out var title)
                && title.ValueKind == JsonValueKind.String)
            {
                return title.GetString();
            }

            return body;
        }
        catch (JsonException)
        {
            // Not JSON at all, so not one of this API's error shapes — something else in the
            // pipeline answered, a reverse proxy's error page or the developer exception page.
            // Returning the raw text would paste a whole HTML document into an admin alert, which
            // is worse than reporting the status alone.
            return null;
        }
    }

    // ── Auth ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Signs in, returning null only when the server actually rejected the credentials.
    /// </summary>
    /// <remarks>
    /// Any other failure throws, because the login screen renders a null as "Invalid username or
    /// password." Flattening every non-2xx to null meant a 500 from a missing signing key, or the
    /// 429 the login rate limiter returns, told the operator to check their typing — the one
    /// diagnosis that guarantees they will not look at the server.
    /// </remarks>
    public async Task<LoginResponse?> LoginAsync(LoginRequest request)
    {
        var resp = await http.PostAsJsonAsync("auth/login", request);
        if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized) return null;
        await EnsureSuccessAsync(resp);
        return await resp.Content.ReadFromJsonAsync<LoginResponse>();
    }

    public async Task ChangePasswordAsync(ChangePasswordRequest request)
    {
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

    public async Task<PagedResult<BlogPostSummaryDto>?> GetPostsAsync(
        string? q = null,
        int page = 1,
        string? status = null,
        int pageSize = DefaultPageSize)
    {
        var url = $"posts?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(q)) url += $"&q={Uri.EscapeDataString(q)}";
        if (!string.IsNullOrWhiteSpace(status)) url += $"&status={status}";
        return await http.GetFromJsonAsync<PagedResult<BlogPostSummaryDto>>(url);
    }

    public async Task<BlogPostDetailDto?> GetPostAsync(Guid id)
    {
        return await http.GetFromJsonAsync<BlogPostDetailDto>($"posts/{id}");
    }

    public async Task<BlogPostDetailDto?> CreatePostAsync(CreateBlogPostRequest request)
    {
        var resp = await http.PostAsJsonAsync("posts", request);
        await EnsureSuccessAsync(resp);
        return await resp.Content.ReadFromJsonAsync<BlogPostDetailDto>();
    }

    public async Task UpdatePostAsync(Guid id, UpdateBlogPostRequest request)
    {
        var resp = await http.PutAsJsonAsync($"posts/{id}", request);
        await EnsureSuccessAsync(resp);
    }

    public async Task DeletePostAsync(Guid id)
    {
        var resp = await http.DeleteAsync($"posts/{id}");
        await EnsureSuccessAsync(resp);
    }

    public async Task PublishPostAsync(Guid id)
    {
        var resp = await http.PostAsync($"posts/{id}/publish", null);
        await EnsureSuccessAsync(resp);
    }

    public async Task UnpublishPostAsync(Guid id)
    {
        var resp = await http.PostAsync($"posts/{id}/unpublish", null);
        await EnsureSuccessAsync(resp);
    }

    public async Task UpdatePostScheduleAsync(Guid id, UpdatePublicationScheduleRequest request)
    {
        var resp = await http.PutAsJsonAsync($"posts/{id}/schedule", request);
        await EnsureSuccessAsync(resp);
    }

    public async Task<PreviewLinkResponse?> CreatePostPreviewAsync(Guid id)
    {
        var response = await http.PostAsync($"previews/posts/{id}", null);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<PreviewLinkResponse>();
    }

    // ── Pages ───────────────────────────────────────────────────────────────

    /// <summary>
    /// One page of the page list. Used to send no parameters at all, which silently pinned the
    /// admin to the server's first 20 rows: page 21 onward was invisible and uneditable.
    /// </summary>
    /// <param name="q">Server-side title/slug search. Filtering the returned window client-side
    /// instead would only ever search the rows already on screen.</param>
    public async Task<PagedResult<PageDto>?> GetPagesAsync(
        string? q = null,
        int page = 1,
        int pageSize = DefaultPageSize)
    {
        var url = $"pages?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(q)) url += $"&q={Uri.EscapeDataString(q)}";
        return await http.GetFromJsonAsync<PagedResult<PageDto>>(url);
    }

    public async Task<PageDto?> GetPageAsync(Guid id)
    {
        return await http.GetFromJsonAsync<PageDto>($"pages/{id}");
    }

    public async Task<PageDto?> CreatePageAsync(CreatePageRequest request)
    {
        var resp = await http.PostAsJsonAsync("pages", request);
        await EnsureSuccessAsync(resp);
        return await resp.Content.ReadFromJsonAsync<PageDto>();
    }

    public async Task UpdatePageAsync(Guid id, UpdatePageRequest request)
    {
        var resp = await http.PutAsJsonAsync($"pages/{id}", request);
        await EnsureSuccessAsync(resp);
    }

    public async Task DeletePageAsync(Guid id)
    {
        var resp = await http.DeleteAsync($"pages/{id}");
        await EnsureSuccessAsync(resp);
    }

    public async Task UpdatePageScheduleAsync(Guid id, UpdatePublicationScheduleRequest request)
    {
        var resp = await http.PutAsJsonAsync($"pages/{id}/schedule", request);
        await EnsureSuccessAsync(resp);
    }

    public async Task<PreviewLinkResponse?> CreatePagePreviewAsync(Guid id)
    {
        var response = await http.PostAsync($"previews/pages/{id}", null);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<PreviewLinkResponse>();
    }

    // ── Media ───────────────────────────────────────────────────────────────

    /// <summary>
    /// One page of the media library.
    /// </summary>
    /// <param name="q">Server-side title/filename search, so a file that exists but sorts outside
    /// the current page is still findable. The media screens used to filter their own window and
    /// reported "No media files found" for anything past row 20.</param>
    public async Task<PagedResult<MediaFileDto>?> GetMediaAsync(
        string? q = null,
        int page = 1,
        int pageSize = DefaultPageSize)
    {
        var url = $"media?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(q)) url += $"&q={Uri.EscapeDataString(q)}";
        return await http.GetFromJsonAsync<PagedResult<MediaFileDto>>(url);
    }

    public async Task<MediaFileDto?> UploadMediaAsync(string title, Stream fileStream, string fileName, string contentType)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(title), "title");
        var streamContent = new StreamContent(fileStream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(streamContent, "file", fileName);
        var resp = await http.PostAsync("media/upload", content);
        await EnsureSuccessAsync(resp);
        return await resp.Content.ReadFromJsonAsync<MediaFileDto>();
    }

    public async Task DeleteMediaAsync(Guid id)
    {
        var resp = await http.DeleteAsync($"media/{id}");
        await EnsureSuccessAsync(resp);
    }

    // ── Users ───────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<AppUserDto>?> GetUsersAsync()
    {
        return await http.GetFromJsonAsync<IReadOnlyList<AppUserDto>>("users");
    }

    public async Task<AppUserDto?> CreateUserAsync(CreateUserRequest request)
    {
        var resp = await http.PostAsJsonAsync("users", request);
        await EnsureSuccessAsync(resp);
        return await resp.Content.ReadFromJsonAsync<AppUserDto>();
    }

    public async Task DeleteUserAsync(Guid id)
    {
        var resp = await http.DeleteAsync($"users/{id}");
        await EnsureSuccessAsync(resp);
    }

    // ── Redirects ───────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<UrlRedirectDto>?> GetRedirectsAsync()
    {
        return await http.GetFromJsonAsync<IReadOnlyList<UrlRedirectDto>>("redirects");
    }

    public async Task<UrlRedirectDto?> CreateRedirectAsync(SaveUrlRedirectRequest request)
    {
        var response = await http.PostAsJsonAsync("redirects", request);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<UrlRedirectDto>();
    }

    public async Task<UrlRedirectDto?> UpdateRedirectAsync(
        Guid id,
        SaveUrlRedirectRequest request)
    {
        var response = await http.PutAsJsonAsync($"redirects/{id}", request);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<UrlRedirectDto>();
    }

    public async Task DeleteRedirectAsync(Guid id)
    {
        var response = await http.DeleteAsync($"redirects/{id}");
        await EnsureSuccessAsync(response);
    }

    // ── Settings ────────────────────────────────────────────────────────────

    public async Task<Dictionary<string, string>?> GetSettingsAsync()
    {
        return await http.GetFromJsonAsync<Dictionary<string, string>>("settings");
    }

    public async Task UpdateSettingsAsync(SiteSettingsUpdateRequest settings)
    {
        var resp = await http.PutAsJsonAsync("settings", settings);
        await EnsureSuccessAsync(resp);
    }

    /// <summary>
    /// Replaces the JWT signing secret server-side. Invalidates every existing token including
    /// this client's own, so the caller must send the user back to the login screen.
    /// </summary>
    public async Task RotateJwtSecretAsync()
    {
        var resp = await http.PostAsync("settings/jwt-secret/rotate", null);
        await EnsureSuccessAsync(resp);
    }

    public async Task<AiProviderInfoDto?> GetAiProviderInfoAsync()
    {
        return await http.GetFromJsonAsync<AiProviderInfoDto>("settings/ai-provider");
    }

    // ── AI ──────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<AiConversationSummaryDto>?> GetConversationsAsync()
    {
        return await http.GetFromJsonAsync<IReadOnlyList<AiConversationSummaryDto>>("ai/conversations");
    }

    public async Task<AiConversationDetailDto?> GetConversationAsync(Guid id)
    {
        return await http.GetFromJsonAsync<AiConversationDetailDto>($"ai/conversations/{id}");
    }

    public async Task<AiConversationDetailDto?> CreateConversationAsync(CreateAiConversationRequest request)
    {
        var resp = await http.PostAsJsonAsync("ai/conversations", request);
        await EnsureSuccessAsync(resp);
        return await resp.Content.ReadFromJsonAsync<AiConversationDetailDto>();
    }

    public async Task<AiConversationDetailDto?> SendMessageAsync(Guid conversationId, SendAiMessageRequest request)
    {
        var resp = await http.PostAsJsonAsync($"ai/conversations/{conversationId}/messages", request);
        await EnsureSuccessAsync(resp);
        return await resp.Content.ReadFromJsonAsync<AiConversationDetailDto>();
    }

    public async Task<ExportAiConversationResponse?> ExportDraftAsync(
        Guid conversationId,
        ExportAiConversationRequest request)
    {
        var resp = await http.PostAsJsonAsync(
            $"ai/conversations/{conversationId}/export-draft",
            request);
        await EnsureSuccessAsync(resp);
        return await resp.Content.ReadFromJsonAsync<ExportAiConversationResponse>();
    }

    public async Task<AiConversationDetailDto?> RenameConversationAsync(Guid id, string title)
    {
        var resp = await http.PutAsJsonAsync(
            $"ai/conversations/{id}/title",
            new RenameAiConversationRequest(title));
        await EnsureSuccessAsync(resp);
        return await resp.Content.ReadFromJsonAsync<AiConversationDetailDto>();
    }

    public async Task DeleteConversationAsync(Guid id)
    {
        var resp = await http.DeleteAsync($"ai/conversations/{id}");
        await EnsureSuccessAsync(resp);
    }

    // ── Analytics ───────────────────────────────────────────────────────────

    public async Task<AnalyticsSummaryDto?> GetAnalyticsSummaryAsync(DateTime startDate, DateTime endDate)
    {
        var url = $"analytics/summary?startDate={startDate:yyyy-MM-dd}&endDate={endDate:yyyy-MM-dd}";
        var response = await http.GetAsync(url);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<AnalyticsSummaryDto>();
    }
}
