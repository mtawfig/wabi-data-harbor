using System.Net;
using System.Net.Sockets;
using WabiInsight.Core.Ai;

namespace WabiInsight.Infrastructure.Ai;

/// <summary>
/// Keeps organization-managed AI providers on the public internet (P13.25). An Organization Administrator types the
/// provider's address, and the API then sends requests there from inside the platform's network; without this
/// guard that address could be the server itself, a neighbour on the private network or the cloud metadata service.
/// Platform providers are registered by Platform Administrators, who run that network, and are not restricted.
/// The address is checked twice: its text when it is saved, and the IP address actually connected to on every
/// request (a public name can be pointed at a private address after it was saved).
/// </summary>
public static class PublicAddressGuard
{
    public const string ErrorCode = "ai_provider_endpoint_not_public";

    /// <summary>The saved form: https only, and not a name or literal that is plainly internal.</summary>
    public static bool IsAcceptableEndpoint(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (uri.Scheme != Uri.UriSchemeHttps || uri.IsLoopback || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        var host = uri.IdnHost.TrimEnd('.');
        if (IPAddress.TryParse(host.Trim('[', ']'), out var literal))
        {
            return IsPublic(literal);
        }

        // A single-label name ("intranet") or a reserved internal suffix only resolves inside a private network.
        return host.Contains('.', StringComparison.Ordinal)
            && !InternalSuffixes.Any(suffix => host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }

    private static readonly string[] InternalSuffixes = [".localhost", ".local", ".internal", ".lan", ".home.arpa", ".intranet", ".corp"];

    /// <summary>True for an address on the public internet: not loopback, private, link-local, shared, multicast or reserved.</summary>
    public static bool IsPublic(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] switch
            {
                0 or 10 or 127 => false,                       // "this" network, private, loopback
                100 when b[1] is >= 64 and <= 127 => false,    // carrier-grade NAT (shared address space)
                169 when b[1] == 254 => false,                 // link-local, cloud metadata services
                172 when b[1] is >= 16 and <= 31 => false,     // private
                192 when b[1] == 168 => false,                 // private
                192 when b[1] == 0 && b[2] == 0 => false,      // protocol assignments
                198 when b[1] is 18 or 19 => false,            // benchmarking
                >= 224 => false,                               // multicast, reserved, broadcast
                _ => true,
            };
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal || address.IsIPv6Multicast || address.Equals(IPAddress.IPv6None) || address.Equals(IPAddress.IPv6Any))
            {
                return false;
            }

            // NAT64 (64:ff9b::/96) carries an IPv4 address in its last four bytes: judge that one.
            var b = address.GetAddressBytes();
            if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xff && b[3] == 0x9b && b.AsSpan(4, 8).IndexOfAnyExcept((byte)0) < 0)
            {
                return IsPublic(new IPAddress(b.AsSpan(12, 4)));
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Connection callback of the HTTP client used for organization-managed providers: resolves the name, refuses
    /// every non-public address and connects to the address it checked, so the answer cannot change in between.
    /// Through an outbound proxy the proxy makes the connection, so the provider's own name is resolved and checked
    /// before the proxy is contacted.
    /// </summary>
    public static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var target = context.DnsEndPoint;
        var requested = context.InitialRequestMessage.RequestUri;
        var throughProxy = requested is not null && !string.Equals(requested.IdnHost, target.Host, StringComparison.OrdinalIgnoreCase);

        if (throughProxy)
        {
            await ResolvePublicAsync(requested!.IdnHost, cancellationToken);
            return await OpenAsync(new DnsEndPoint(target.Host, target.Port), cancellationToken);
        }

        var addresses = await ResolvePublicAsync(target.Host, cancellationToken);
        Exception? last = null;
        foreach (var address in addresses)
        {
            try
            {
                return await OpenAsync(new IPEndPoint(address, target.Port), cancellationToken);
            }
            catch (SocketException ex)
            {
                last = ex;
            }
        }

        throw new HttpRequestException("The AI provider could not be reached.", last);
    }

    private static async Task<IPAddress[]> ResolvePublicAsync(string host, CancellationToken cancellationToken)
    {
        IPAddress[] addresses;
        try
        {
            addresses = IPAddress.TryParse(host.Trim('[', ']'), out var literal) ? [literal] : await Dns.GetHostAddressesAsync(host, cancellationToken);
        }
        catch (SocketException ex)
        {
            throw new HttpRequestException("The AI provider's name could not be resolved.", ex);
        }

        // One internal address among several is enough to refuse: the caller cannot choose which one is used.
        if (addresses.Length == 0 || addresses.Any(a => !IsPublic(a)))
        {
            throw new AiProviderException(ErrorCode, "The AI provider's address is not on the public internet.");
        }

        return addresses;
    }

    private static async Task<Stream> OpenAsync(EndPoint endPoint, CancellationToken cancellationToken)
    {
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(endPoint, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
