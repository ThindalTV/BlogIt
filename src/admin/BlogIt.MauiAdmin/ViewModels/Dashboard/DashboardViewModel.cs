using System.Collections.ObjectModel;
using BlogIt.MauiAdmin.Services;
using BlogIt.Shared.DTOs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BlogIt.MauiAdmin.ViewModels.Dashboard;

public partial class DashboardViewModel(MauiApiClient apiClient) : ObservableObject
{
    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private int publishedCount;

    [ObservableProperty]
    private int draftCount;

    [ObservableProperty]
    private int pagesCount;

    [ObservableProperty]
    private int mediaCount;

    [ObservableProperty]
    private bool analyticsConfigured;

    [ObservableProperty]
    private long analyticsSessions;

    [ObservableProperty]
    private long analyticsUsers;

    [ObservableProperty]
    private long analyticsPageViews;

    public ObservableCollection<BlogPostSummaryDto> RecentPosts { get; } = [];

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            // Published/draft counts come from TotalCount on a status-filtered,
            // single-item page — not from counting .Items — since a site with more
            // than one page of posts would otherwise report a wrong number.
            var publishedTask = apiClient.GetPostsAsync(page: 1, pageSize: 1, status: "published");
            var draftTask = apiClient.GetPostsAsync(page: 1, pageSize: 1, status: "draft");
            var pagesTask = apiClient.GetPagesAsync(page: 1, pageSize: 1);
            var mediaTask = apiClient.GetMediaAsync(page: 1, pageSize: 1);
            var recentTask = apiClient.GetPostsAsync(page: 1, pageSize: 5, status: "all");
            var analyticsTask = apiClient.GetAnalyticsSummaryAsync("30daysAgo", "today");

            await Task.WhenAll(publishedTask, draftTask, pagesTask, mediaTask, recentTask, analyticsTask);

            var published = await publishedTask;
            var draft = await draftTask;
            var pages = await pagesTask;
            var media = await mediaTask;
            var recent = await recentTask;
            var analytics = await analyticsTask;

            PublishedCount = published.Success ? published.Value!.TotalCount : 0;
            DraftCount = draft.Success ? draft.Value!.TotalCount : 0;
            PagesCount = pages.Success ? pages.Value!.TotalCount : 0;
            MediaCount = media.Success ? media.Value!.TotalCount : 0;

            RecentPosts.Clear();
            if (recent.Success)
                foreach (var post in recent.Value!.Items)
                    RecentPosts.Add(post);

            // A 404 here means Analytics isn't configured for this site — an
            // expected empty state, not an error to surface.
            if (analytics.Success)
            {
                AnalyticsConfigured = true;
                AnalyticsSessions = analytics.Value!.Sessions;
                AnalyticsUsers = analytics.Value.Users;
                AnalyticsPageViews = analytics.Value.PageViews;
            }
            else
            {
                AnalyticsConfigured = false;
            }

            if (!published.Success || !draft.Success || !pages.Success || !media.Success || !recent.Success)
                ErrorMessage = "Some dashboard data couldn't be loaded.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
