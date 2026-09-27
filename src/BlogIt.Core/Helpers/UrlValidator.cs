using System.Net;
using System.Net.Sockets;

namespace BlogIt.Shared.Helpers;

public static class UrlValidator
{
    /// <summary>
    /// Hostname suffixes that can only name something inside the deployment. <c>.local</c> is mDNS,
    /// <c>.home.arpa</c> is the RFC 8375 home-network zone, <c>.internal</c> is the convention every
    /// major cloud uses for its private zones, and <c>.localhost</c> is loopback by RFC 6761.
    /// </summary>
    private static readonly string[] PrivateHostSuffixes =
    [
        ".localhost",
        ".local",
        ".internal",
        ".home.arpa"
    ];

    /// <summary>True if <paramref name="value"/> is a non-empty, absolute http(s) URL.</summary>
    public static bool IsValidAbsoluteHttpUrl(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    /// <summary>
    /// True if <paramref name="value"/> is an absolute URL whose host is loopback, link-local, or in
    /// a private/non-globally-routable range — an address only something inside the deployment can
    /// reach.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Answers only "where does this point"; scheme and shape are
    /// <see cref="IsValidAbsoluteHttpUrl"/>'s question, so a value this cannot parse is
    /// <see langword="false"/> here rather than an error of its own.
    /// </para>
    /// <para>
    /// Literal-only by design. A public DNS name that resolves into private space passes: resolving
    /// at validation time answers about one moment and one resolver, and DNS rebinding defeats it
    /// outright, so a lookup here would buy confidence it cannot support. This is a guard on what an
    /// operator can configure, not a substitute for egress rules.
    /// </para>
    /// </remarks>
    public static bool IsPrivateOrLocalHttpUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return false;
        }

        // Covers "localhost", 127.0.0.0/8 and [::1] in one, including the spellings Uri canonicalises.
        if (uri.IsLoopback)
            return true;

        var host = uri.Host.Trim('[', ']');

        if (IPAddress.TryParse(host, out var address))
            return IsPrivateAddress(address);

        // A name with no dot at all cannot be a public FQDN — it is a container or LAN name.
        return !host.Contains('.')
            || PrivateHostSuffixes.Any(suffix =>
                host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPrivateAddress(IPAddress address)
    {
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv4MappedToIPv6)
                return IsPrivateAddress(address.MapToIPv4());

            return IPAddress.IsLoopback(address)
                || address.IsIPv6LinkLocal
                || address.IsIPv6UniqueLocal
                || address.IsIPv6SiteLocal
                || address.Equals(IPAddress.IPv6Any);
        }

        var octets = address.GetAddressBytes();
        return octets[0] switch
        {
            0 => true,                                       // 0.0.0.0/8, "this network"
            10 => true,                                      // RFC 1918
            127 => true,                                     // loopback
            100 => octets[1] >= 64 && octets[1] <= 127,      // RFC 6598 carrier-grade NAT
            169 => octets[1] == 254,                         // link-local, incl. cloud metadata
            172 => octets[1] >= 16 && octets[1] <= 31,       // RFC 1918
            192 => octets[1] == 168,                         // RFC 1918
            _ => false
        };
    }
}
