using System.Net;
using System.Net.Sockets;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Services.BotKun;

/// <summary>
/// SSRF guard for server-side fetches to client-configured AI provider URLs.
/// Policy: only absolute http(s) URLs without embedded credentials are allowed,
/// and link-local (incl. cloud metadata 169.254.169.254), multicast, unspecified
/// and broadcast literal addresses are blocked. Loopback/private hosts are
/// intentionally allowed — local providers (Ollama, LM Studio, vLLM) are the
/// primary use case. (DNS rebinding is out of scope for this self-hosted tool.)
/// IPv4-mapped-IPv6 (::ffff:0:0/96) and NAT64 (64:ff9b::/96) literals are mapped
/// to their embedded IPv4 address before the checks, so they cannot smuggle a
/// blocked IPv4 address past the guard in disguise.
/// </summary>
public static class ProviderUrlGuard
{
    /// <summary>Result-typed wrapper around <see cref="IsAllowed"/> for railway-style callers.</summary>
    public static Result Validate(string? raw)
    {
        // Empty means "unset" — clearing the setting is always allowed; the
        // execution paths treat an unset endpoint as "not configured".
        if (string.IsNullOrWhiteSpace(raw))
            return Result.Success();

        return IsAllowed(raw, out var reason)
            ? Result.Success()
            : Result.Failure(new Error(Constants.Errors.InvalidBotKunProviderUrl, $"Provider URL rejected: {reason}"));
    }

    /// <summary>
    /// Validates a provider base URL for server-side fetching (SSRF guard).
    /// </summary>
    public static bool IsAllowed(string raw, out string reason)
    {
        reason = string.Empty;
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            reason = "only absolute http/https URLs are allowed";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            reason = "embedded credentials are not allowed";
            return false;
        }

        // Uri.Host may keep the brackets of IPv6 literals — strip them (and any
        // zone/scope id) so the address parses as an IPAddress.
        var host = uri.Host;
        if (host.StartsWith('[') && host.EndsWith(']'))
            host = host[1..^1];
        var zoneIndex = host.IndexOf('%');
        if (zoneIndex >= 0)
            host = host[..zoneIndex];

        if (IPAddress.TryParse(host, out var ip))
        {
            // IPv4-mapped-IPv6 (::ffff:0:0/96) and NAT64 (64:ff9b::/96) both embed
            // a plain IPv4 address — evaluate the embedded address under the IPv4
            // rules instead of the (more permissive) IPv6 rules.
            if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            {
                if (ip.IsIPv4MappedToIPv6)
                    ip = ip.MapToIPv4();
                else if (TryGetNat64EmbeddedIPv4(ip, out var embedded))
                    ip = embedded;
            }

            var bytes = ip.GetAddressBytes();
            bool blocked;
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                blocked = bytes[0] == 0                                   // 0.0.0.0/8 unspecified
                          || (bytes[0] == 169 && bytes[1] == 254)         // link-local incl. cloud metadata
                          || bytes[0] >= 224;                             // multicast, reserved, broadcast
            }
            else
            {
                blocked = ip.Equals(IPAddress.IPv6Any)                    // ::
                          || bytes[0] == 0xff                             // multicast ff00::/8
                          || (bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80); // link-local fe80::/10
            }

            if (blocked)
            {
                reason = "the address is link-local, multicast or unspecified";
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Extracts the embedded IPv4 address from a well-known NAT64 literal
    /// (64:ff9b::/96, RFC 6052) where the low 32 bits carry the IPv4 address.
    /// </summary>
    private static bool TryGetNat64EmbeddedIPv4(IPAddress ip, out IPAddress embedded)
    {
        embedded = IPAddress.None;
        var bytes = ip.GetAddressBytes();
        if (bytes.Length != 16 ||
            bytes[0] != 0x00 || bytes[1] != 0x64 || bytes[2] != 0xff || bytes[3] != 0x9b)
        {
            return false;
        }

        for (var i = 4; i < 12; i++)
        {
            if (bytes[i] != 0)
                return false;
        }

        embedded = new IPAddress(bytes[12..16]);
        return true;
    }
}
