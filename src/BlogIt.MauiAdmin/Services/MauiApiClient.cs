using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BlogIt.MauiAdmin.Models;
using BlogIt.Shared.DTOs;

namespace BlogIt.MauiAdmin.Services;

/// <summary>
/// API client that always operates against the currently active site profile (a single
/// deliberate simplification — this is a one-workspace-at-a-time app, like a chat
/// client's account switcher, not a client that fans calls out across sites at once).
/// Every call returns an <see cref="ApiResult"/>/<see cref="ApiResult{T}"/> whose error,
/// when present, has already been parsed by <see cref="ApiResponseParser"/> from
/// whatever shape the server actually returned — callers should show
/// <c>result.Error.Message</c> directly rather than a generic failure string.
/// </summary>
public class MauiApiClient(IHttpClientFactory httpClientFactory, SiteProfileService profileService)
{
    /// <summary>
    /// Resolves the active site and configures BaseAddress/auth on a freshly-created
    /// client instance before any request is sent on it. This has to happen here,
    /// not inside <see cref="ActiveSiteHttpMessageHandler"/>: <see cref="HttpClient"/>
    /// validates/combines a relative RequestUri against BaseAddress itself, inside
    /// HttpClient.SendAsync, before the request ever reaches a DelegatingHandler's
    /// SendAsync override — so a handler can never fix up a relative URI in time.
    /// IHttpClientFactory.CreateClient(name) returns a new HttpClient instance per
    /// call (only the underlying handler is pooled), so mutating BaseAddress/headers
    /// on this particular instance is safe and doesn't affect other callers.
    /// </summary>
    private async Task<HttpClient> CreateActiveSiteClientAsync()
    {
        var client = httpClientFactory.CreateClient("BlogIt");
        var profile = await profileService.GetActiveProfileAsync()
            ?? throw new InvalidOperationException("No active site profile. Please add a site first.");

        client.BaseAddress = new Uri(profile.BaseUri, profile.ApiPath.TrimStart('/') + "/");

        var token = await profileService.GetTokenAsync(profile.Id);
        if (!string.IsNullOrEmpty(token) && profile.IsTokenValid)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    // ── Auth ────────────────────────────────────────────────────────────
    // Login precedes "active", so it builds its own request against the given
    // profile directly rather than going through the active-site handler.
    public async Task<ApiResult<LoginResponse>> LoginAsync(string profileId, string username, string password)
    {
        var profile = (await profileService.GetProfilesAsync()).FirstOrDefault(p => p.Id == profileId)
            ?? throw new InvalidOperationException("Profile not found.");

        try
        {
            using var client = httpClientFactory.CreateClient();
            var apiBase = new Uri(profile.BaseUri, profile.ApiPath.TrimStart('/') + "/");
            var url = new Uri(apiBase, "auth/login");
            using var response = await client.PostAsJsonAsync(url, new LoginRequest(username, password), BlogItJson.Options);

            if (!response.IsSuccessStatusCode)
                return ApiResult<LoginResponse>.Fail(await ApiResponseParser.ParseErrorAsync(response));

            var result = await response.Content.ReadFromJsonAsync<LoginResponse>(BlogItJson.Options);
            if (result is null)
                return ApiResult<LoginResponse>.Fail(new ApiError(response.StatusCode, "The server returned an unexpected response."));

            await profileService.SaveTokenAsync(profileId, result.Token, result.ExpiresAt, result.Username, result.DisplayName);
            return ApiResult<LoginResponse>.Ok(result);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return ApiResult<LoginResponse>.Fail(Unreachable());
        }
    }

    public Task<ApiResult> ChangePasswordAsync(ChangePasswordRequest request) => PostAsync("auth/change-password", request);

    // ── Setup ───────────────────────────────────────────────────────────
    public Task<ApiResult<SetupStatusResponse>> GetSetupStatusAsync() => GetAsync<SetupStatusResponse>("setup/status");

    // ── Posts ───────────────────────────────────────────────────────────
    public Task<ApiResult<PagedResult<BlogPostSummaryDto>>> GetPostsAsync(string? q = null, int page = 1, int pageSize = 20, string status = "all") =>
        GetAsync<PagedResult<BlogPostSummaryDto>>($"posts?q={Uri.EscapeDataString(q ?? "")}&page={page}&pageSize={pageSize}&status={status}");

    public Task<ApiResult<BlogPostDetailDto>> GetPostAsync(Guid id) => GetAsync<BlogPostDetailDto>($"posts/{id}");

    public Task<ApiResult<BlogPostDetailDto>> CreatePostAsync(CreateBlogPostRequest request) => PostAsync<BlogPostDetailDto>("posts", request);

    public Task<ApiResult<BlogPostDetailDto>> UpdatePostAsync(Guid id, UpdateBlogPostRequest request) => PutAsync<BlogPostDetailDto>($"posts/{id}", request);

    public Task<ApiResult> DeletePostAsync(Guid id) => DeleteAsync($"posts/{id}");

    public Task<ApiResult<BlogPostDetailDto>> PublishPostAsync(Guid id) => PostAsync<BlogPostDetailDto>($"posts/{id}/publish", null);

    public Task<ApiResult<BlogPostDetailDto>> UnpublishPostAsync(Guid id) => PostAsync<BlogPostDetailDto>($"posts/{id}/unpublish", null);

    public Task<ApiResult<BlogPostDetailDto>> UpdatePostScheduleAsync(Guid id, UpdatePublicationScheduleRequest request) =>
        PutAsync<BlogPostDetailDto>($"posts/{id}/schedule", request);

    public Task<ApiResult<PreviewLinkResponse>> CreatePostPreviewAsync(Guid id) => PostAsync<PreviewLinkResponse>($"previews/posts/{id}", null);

    // ── Pages ───────────────────────────────────────────────────────────
    public Task<ApiResult<PagedResult<PageDto>>> GetPagesAsync(string? q = null, int page = 1, int pageSize = 20) =>
        GetAsync<PagedResult<PageDto>>($"pages?q={Uri.EscapeDataString(q ?? "")}&page={page}&pageSize={pageSize}");

    public Task<ApiResult<PageDto>> GetPageAsync(Guid id) => GetAsync<PageDto>($"pages/{id}");

    public Task<ApiResult<PageDto>> CreatePageAsync(CreatePageRequest request) => PostAsync<PageDto>("pages", request);

    public Task<ApiResult<PageDto>> UpdatePageAsync(Guid id, UpdatePageRequest request) => PutAsync<PageDto>($"pages/{id}", request);

    public Task<ApiResult> DeletePageAsync(Guid id) => DeleteAsync($"pages/{id}");

    public Task<ApiResult<PageDto>> UpdatePageScheduleAsync(Guid id, UpdatePublicationScheduleRequest request) =>
        PutAsync<PageDto>($"pages/{id}/schedule", request);

    public Task<ApiResult<PreviewLinkResponse>> CreatePagePreviewAsync(Guid id) => PostAsync<PreviewLinkResponse>($"previews/pages/{id}", null);

    // ── Media ───────────────────────────────────────────────────────────
    public Task<ApiResult<PagedResult<MediaFileDto>>> GetMediaAsync(string? q = null, int page = 1, int pageSize = 20) =>
        GetAsync<PagedResult<MediaFileDto>>($"media?q={Uri.EscapeDataString(q ?? "")}&page={page}&pageSize={pageSize}");

    public async Task<ApiResult<MediaFileDto>> UploadMediaAsync(string title, Stream data, string fileName, string contentType)
    {
        try
        {
            using var client = await CreateActiveSiteClientAsync();
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(title), "title");
            var fileContent = new StreamContent(data);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            form.Add(fileContent, "file", fileName);

            using var response = await client.PostAsync("media/upload", form);
            if (!response.IsSuccessStatusCode)
                return ApiResult<MediaFileDto>.Fail(await ApiResponseParser.ParseErrorAsync(response));

            var value = await response.Content.ReadFromJsonAsync<MediaFileDto>(BlogItJson.Options);
            return ApiResult<MediaFileDto>.Ok(value!);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return ApiResult<MediaFileDto>.Fail(Unreachable());
        }
    }

    public Task<ApiResult> DeleteMediaAsync(Guid id) => DeleteAsync($"media/{id}");

    // ── Users ───────────────────────────────────────────────────────────
    public Task<ApiResult<List<AppUserDto>>> GetUsersAsync() => GetAsync<List<AppUserDto>>("users");

    public Task<ApiResult<AppUserDto>> CreateUserAsync(CreateUserRequest request) => PostAsync<AppUserDto>("users", request);

    public Task<ApiResult> DeleteUserAsync(Guid id) => DeleteAsync($"users/{id}");

    // ── Settings ────────────────────────────────────────────────────────
    public Task<ApiResult<Dictionary<string, string>>> GetSettingsAsync() => GetAsync<Dictionary<string, string>>("settings");

    public Task<ApiResult> UpdateSettingsAsync(SiteSettingsUpdateRequest settings) => PutAsync("settings", settings);

    public Task<ApiResult<AiProviderInfoDto>> GetAiProviderInfoAsync() => GetAsync<AiProviderInfoDto>("settings/ai-provider");

    // ── AI ──────────────────────────────────────────────────────────────
    public Task<ApiResult<List<AiConversationSummaryDto>>> GetConversationsAsync() => GetAsync<List<AiConversationSummaryDto>>("ai/conversations");

    public Task<ApiResult<AiConversationDetailDto>> GetConversationAsync(Guid id) => GetAsync<AiConversationDetailDto>($"ai/conversations/{id}");

    public Task<ApiResult<AiConversationDetailDto>> CreateConversationAsync(string title) =>
        PostAsync<AiConversationDetailDto>("ai/conversations", new CreateAiConversationRequest(title));

    public Task<ApiResult<AiConversationDetailDto>> SendMessageAsync(Guid conversationId, string content) =>
        PostAsync<AiConversationDetailDto>($"ai/conversations/{conversationId}/messages", new SendAiMessageRequest(content));

    public Task<ApiResult<ExportAiConversationResponse>> ExportDraftAsync(Guid conversationId, string? instructions) =>
        PostAsync<ExportAiConversationResponse>($"ai/conversations/{conversationId}/export-draft", new ExportAiConversationRequest(instructions));

    public Task<ApiResult> DeleteConversationAsync(Guid id) => DeleteAsync($"ai/conversations/{id}");

    // ── Analytics ───────────────────────────────────────────────────────
    public Task<ApiResult<AnalyticsSummaryDto>> GetAnalyticsSummaryAsync(string startDate, string endDate) =>
        GetAsync<AnalyticsSummaryDto>($"analytics/summary?startDate={startDate}&endDate={endDate}");

    // ── Redirects ───────────────────────────────────────────────────────
    public Task<ApiResult<List<UrlRedirectDto>>> GetRedirectsAsync() => GetAsync<List<UrlRedirectDto>>("redirects");

    public Task<ApiResult<UrlRedirectDto>> CreateRedirectAsync(SaveUrlRedirectRequest request) => PostAsync<UrlRedirectDto>("redirects", request);

    public Task<ApiResult<UrlRedirectDto>> UpdateRedirectAsync(Guid id, SaveUrlRedirectRequest request) => PutAsync<UrlRedirectDto>($"redirects/{id}", request);

    public Task<ApiResult> DeleteRedirectAsync(Guid id) => DeleteAsync($"redirects/{id}");

    // ── HTTP plumbing ───────────────────────────────────────────────────

    private static ApiError Unreachable() =>
        new(HttpStatusCode.ServiceUnavailable, "Couldn't reach the server. Check your connection and try again.");

    private async Task<ApiResult<T>> GetAsync<T>(string requestUri) => await SendAsync<T>(HttpMethod.Get, requestUri, null);
    private async Task<ApiResult<T>> PostAsync<T>(string requestUri, object? body) => await SendAsync<T>(HttpMethod.Post, requestUri, body);
    private async Task<ApiResult<T>> PutAsync<T>(string requestUri, object? body) => await SendAsync<T>(HttpMethod.Put, requestUri, body);

    private async Task<ApiResult> PostAsync(string requestUri, object? body)
    {
        var result = await SendAsync<JsonElement?>(HttpMethod.Post, requestUri, body);
        return result.Success ? ApiResult.Ok() : ApiResult.Fail(result.Error!);
    }

    private async Task<ApiResult> PutAsync(string requestUri, object? body)
    {
        var result = await SendAsync<JsonElement?>(HttpMethod.Put, requestUri, body);
        return result.Success ? ApiResult.Ok() : ApiResult.Fail(result.Error!);
    }

    private async Task<ApiResult> DeleteAsync(string requestUri)
    {
        var result = await SendAsync<JsonElement?>(HttpMethod.Delete, requestUri, null);
        return result.Success ? ApiResult.Ok() : ApiResult.Fail(result.Error!);
    }

    private async Task<ApiResult<T>> SendAsync<T>(HttpMethod method, string requestUri, object? body)
    {
        try
        {
            using var client = await CreateActiveSiteClientAsync();
            using var request = new HttpRequestMessage(method, requestUri);
            if (body is not null)
                request.Content = JsonContent.Create(body, options: BlogItJson.Options);

            using var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                return ApiResult<T>.Fail(await ApiResponseParser.ParseErrorAsync(response));

            var text = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(text))
                return ApiResult<T>.Ok(default!);

            var value = JsonSerializer.Deserialize<T>(text, BlogItJson.Options);
            return ApiResult<T>.Ok(value!);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return ApiResult<T>.Fail(Unreachable());
        }
    }
}
