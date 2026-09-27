using System.Net;
using System.Net.Http.Json;
using BlogIt.Shared;
using BlogIt.Shared.DTOs;

namespace BlogIt.MauiAdmin.Services;

public enum SiteProbeStatus { ReachableSetupComplete, ReachableSetupIncomplete, NotFound, Unreachable }

public record SiteProbeResult(SiteProbeStatus Status, string? ResolvedApiPath = null);

/// <summary>
/// Probes a candidate site before it is added to the profile list. Kept separate from
/// <see cref="SiteProfileService"/> (pure persistence) and <see cref="MauiApiClient"/>
/// (assumes an already-active profile), since probing happens before a site exists in
/// either. Uses the server's existing anonymous "setup/status" endpoint as both a
/// reachability check and a first-run-setup-complete check.
/// </summary>
public class SiteProbeService(IHttpClientFactory httpClientFactory)
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);

    public async Task<SiteProbeResult> ProbeAsync(Uri baseUri, string apiPath, CancellationToken ct = default)
    {
        var (outcome, isComplete) = await ProbeSetupStatusAsync(baseUri, apiPath, ct);
        if (outcome == ProbeOutcome.Ok)
            return new SiteProbeResult(ToStatus(isComplete), apiPath);
        if (outcome == ProbeOutcome.Unreachable)
            return new SiteProbeResult(SiteProbeStatus.Unreachable);

        // outcome == NotFound: apiPath itself may be wrong (a customized deployment).
        // Fall back to the admin bootstrap-config endpoint at the default admin path
        // to discover the real one.
        var discoveredApiPath = await TryDiscoverApiPathAsync(baseUri, ct);
        if (discoveredApiPath is not null)
        {
            var (retryOutcome, retryComplete) = await ProbeSetupStatusAsync(baseUri, discoveredApiPath, ct);
            if (retryOutcome == ProbeOutcome.Ok)
                return new SiteProbeResult(ToStatus(retryComplete), discoveredApiPath);
        }

        return new SiteProbeResult(SiteProbeStatus.NotFound);
    }

    private static SiteProbeStatus ToStatus(bool isComplete) =>
        isComplete ? SiteProbeStatus.ReachableSetupComplete : SiteProbeStatus.ReachableSetupIncomplete;

    private enum ProbeOutcome { Ok, NotFound, Unreachable }

    private async Task<(ProbeOutcome Outcome, bool IsComplete)> ProbeSetupStatusAsync(
        Uri baseUri, string apiPath, CancellationToken ct)
    {
        try
        {
            using var client = httpClientFactory.CreateClient();
            client.Timeout = ProbeTimeout;
            var url = new Uri(baseUri, apiPath.TrimStart('/') + "/setup/status");
            using var response = await client.GetAsync(url, ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return (ProbeOutcome.NotFound, false);
            if (!response.IsSuccessStatusCode)
                return (ProbeOutcome.Unreachable, false);

            var status = await response.Content.ReadFromJsonAsync<SetupStatusResponse>(cancellationToken: ct);
            return (ProbeOutcome.Ok, status?.IsComplete ?? false);
        }
        catch
        {
            return (ProbeOutcome.Unreachable, false);
        }
    }

    private async Task<string?> TryDiscoverApiPathAsync(Uri baseUri, CancellationToken ct)
    {
        try
        {
            using var client = httpClientFactory.CreateClient();
            client.Timeout = ProbeTimeout;
            var url = new Uri(baseUri, "blogit/" + BlogItAdminBootstrapConfig.RelativePath);
            using var response = await client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return null;

            var config = await response.Content.ReadFromJsonAsync<BlogItAdminBootstrapConfig>(cancellationToken: ct);
            return config?.ApiPath;
        }
        catch
        {
            return null;
        }
    }
}
