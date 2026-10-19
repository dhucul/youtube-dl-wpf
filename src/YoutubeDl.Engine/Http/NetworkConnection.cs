using System.Net;
using System.Net.Sockets;

namespace YoutubeDl.Engine.Http;

internal static class NetworkConnection
{
    internal static (IPAddress? Source, AddressFamily Family) Validate(string? sourceAddress, int ipVersion)
    {
        if (ipVersion is not (0 or 4 or 6)) throw new ArgumentException("Choose automatic, IPv4 or IPv6 for the network connection.");
        IPAddress? source = null;
        if (!string.IsNullOrWhiteSpace(sourceAddress))
        {
            if (!IPAddress.TryParse(sourceAddress.Trim(), out source)) throw new ArgumentException("The source address must be a local IPv4 or IPv6 address.");
            if (source.IsIPv4MappedToIPv6) source = source.MapToIPv4();
        }
        var family = ipVersion switch { 4 => AddressFamily.InterNetwork, 6 => AddressFamily.InterNetworkV6, _ => source?.AddressFamily ?? AddressFamily.Unspecified };
        if (source is not null && source.AddressFamily != family) throw new ArgumentException("The source address and selected IP version do not match.");
        return (source, family);
    }
    internal static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, IPAddress? source, AddressFamily family, CancellationToken ct)
    {
        var endpoint = context.DnsEndPoint;
        var addresses = IPAddress.TryParse(endpoint.Host, out var literal) ? [literal] : await Dns.GetHostAddressesAsync(endpoint.Host, family, ct).ConfigureAwait(false);
        Exception? failure = null;
        foreach (var address in addresses.Where(address => family == AddressFamily.Unspecified || address.AddressFamily == family).Distinct())
        {
            ct.ThrowIfCancellationRequested();
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                if (source is not null) socket.Bind(new IPEndPoint(source, 0));
                await socket.ConnectAsync(new IPEndPoint(address, endpoint.Port), ct).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException ex) { failure = ex; socket.Dispose(); }
            catch { socket.Dispose(); throw; }
        }
        ct.ThrowIfCancellationRequested();
        throw new HttpRequestException("No connection could be made using the selected IP version and source address.", failure);
    }
}
