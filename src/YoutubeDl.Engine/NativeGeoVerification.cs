using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using YoutubeDl.Engine.Providers;

namespace YoutubeDl.Engine;

/// <summary>Separate verification connection and explicit source forwarded-IP header policy.</summary>
public sealed record NativeGeoVerificationOptions
{
    [JsonIgnore] public string Proxy { get; init; } = "";
    [JsonIgnore] public string ForwardedForMode { get; init; } = "default";
    [JsonIgnore] internal string? SelectedAddress { get; init; }
    public override string ToString() => "Native geographic request options [redacted]";
}

public static class NativeGeoVerification
{
    public static void Validate(NativeGeoVerificationOptions? options)
    {
        if (options is null) return;
        if (options.Proxy is null || options.ForwardedForMode is null) throw new ArgumentException("Geographic request options cannot be null.");
        if (options.Proxy.Length > 0) _ = ProxyValue(options.Proxy);
        _ = Mode(options.ForwardedForMode);
        if (options.SelectedAddress is { } address && (!IPAddress.TryParse(address, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork)) throw new ArgumentException("The forwarded address must be one IPv4 address.");
    }
    public static string ProxyValue(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 4096 || text.Any(char.IsControl) || !Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.Host.Length == 0 || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.AbsolutePath != "/")
            throw new ArgumentException("The geo verification proxy must be an HTTP/HTTPS server without credentials, a path, query or fragment.");
        return text;
    }
    public static NativeGeoVerificationOptions? FromLegacy(string? proxy, bool enabled, string? country, string? block)
    {
        // The classic builder emits country and then IP block after the enable flag.
        var mode = !string.IsNullOrWhiteSpace(block) ? IpBlock(block) : !string.IsNullOrWhiteSpace(country) ? Country(country) : enabled ? "default" : "never";
        var options = new NativeGeoVerificationOptions { Proxy = string.IsNullOrWhiteSpace(proxy) ? "" : ProxyValue(proxy), ForwardedForMode = mode };
        return options.Proxy.Length == 0 && mode == "default" ? null : options;
    }
    internal static NativeGeoVerificationOptions WithMode(NativeGeoVerificationOptions? current, string mode) => (current ?? new()) with { ForwardedForMode = Mode(mode), SelectedAddress = null };
    internal static string Country(string value) => value.Length == 2 && GeoCountryRanges.Blocks.ContainsKey(value) ? value.ToUpperInvariant() : throw new ArgumentException("Choose a supported two-letter country code for the forwarded-IP header.");
    internal static string IpBlock(string value) { _ = Range(value); return value; }
    private static string Mode(string value) => value.Equals("default", StringComparison.OrdinalIgnoreCase) ? "default" : value.Equals("never", StringComparison.OrdinalIgnoreCase) ? "never" : value.Length == 2 ? Country(value) : IpBlock(value);
    internal static NativeDownloadRequest Prepare(NativeDownloadRequest request)
    {
        var options = request.GeoVerification; Validate(options); if (options is null || options.SelectedAddress is not null) return request;
        var mode = Mode(options.ForwardedForMode); if (mode is "default" or "never") return request;
        var block = mode.Length == 2 ? GeoCountryRanges.Blocks[mode] : mode; var (network, mask) = Range(block);
        Span<byte> bytes = stackalloc byte[4]; RandomNumberGenerator.Fill(bytes); var selected = network | BinaryPrimitives.ReadUInt32BigEndian(bytes) & ~mask; BinaryPrimitives.WriteUInt32BigEndian(bytes, selected);
        return request with { GeoVerification = options with { SelectedAddress = new IPAddress(bytes).ToString() } };
    }
    private static (uint Network, uint Mask) Range(string value)
    {
        if (value.Length > 32 || value.Any(char.IsWhiteSpace) || value.Any(char.IsControl)) throw new ArgumentException("Choose an IPv4 block in CIDR notation for the forwarded-IP header.");
        var parts = value.Split('/');
        if (parts.Length != 2 || parts[0].Split('.').Length != 4 || parts[0].Split('.').Any(item => !byte.TryParse(item, NumberStyles.None, CultureInfo.InvariantCulture, out _)) || !IPAddress.TryParse(parts[0], out var address) || address.AddressFamily != AddressFamily.InterNetwork || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var bits) || bits is < 0 or > 32)
            throw new ArgumentException("Choose an IPv4 block in CIDR notation for the forwarded-IP header.");
        var mask = bits == 0 ? 0u : uint.MaxValue << (32 - bits); return (BinaryPrimitives.ReadUInt32BigEndian(address.GetAddressBytes()) & mask, mask);
    }
    internal static void ValidateProvider(NativeDownloadRequest request, IMediaProvider provider)
    {
        Validate(request.GeoVerification);
        if (request.GeoVerification is { Proxy.Length: > 0 } && !provider.SupportsGeoVerificationProxy) throw new NotSupportedException("The selected native provider does not declare a geo verification proxy route.");
    }
}
