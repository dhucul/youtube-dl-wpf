using YoutubeDlModern.Models;
using YoutubeDl.Engine;

namespace YoutubeDlModern.Services;

public sealed partial class NativeEngineAdapter
{
    private static void ValidateJobOptions(DownloadOptions options)
    {
        if (!new[] { "video-audio", "video-only", "audio-only" }.Contains(options.DownloadMode, StringComparer.OrdinalIgnoreCase))
            throw new NativeEngineUnsupportedException("Choose video with audio, video only or audio only.");
        if (!new[] { "best", "worst", "audio", "custom" }.Contains(options.QualityPreset, StringComparer.OrdinalIgnoreCase) &&
            !(int.TryParse(options.QualityPreset, out var height) && height > 0))
            throw new NativeEngineUnsupportedException("Choose a supported quality preset or a positive maximum video height.");
        if (options.QualityPreset.Equals("custom", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(options.CustomFormat))
            throw new NativeEngineUnsupportedException("Enter a format selector for custom quality.");
    }

    private void ValidateAnalysisSettings(AppSettings settings,string url)
    {
        var unsupported = new List<string>();
        CollectAuthentication(settings.Auth, unsupported, Uri.TryCreate(url,UriKind.Absolute,out var authUrl) ? _engine.GetAuthenticationCapabilities(authUrl) : NativeAuthenticationCapabilities.None);
        CollectConnection(settings.Network, unsupported, authUrl is not null && _engine.SupportsGeoVerificationProxy(authUrl));
        RejectUnsupported(unsupported);
    }

    private void ValidateDownloadSettings(AppSettings settings,string url)
    {
        var unsupported = new List<string>();
        CollectAuthentication(settings.Auth, unsupported, Uri.TryCreate(url,UriKind.Absolute,out var authUrl) ? _engine.GetAuthenticationCapabilities(authUrl) : NativeAuthenticationCapabilities.None);
        CollectConnection(settings.Network, unsupported, authUrl is not null && _engine.SupportsGeoVerificationProxy(authUrl));

        try { NativeExternalDownloader.Validate(settings.Network.ExternalDownloader, Text(settings.Network.ExternalDownloaderArgs) ? [settings.Network.ExternalDownloaderArgs] : null); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        { Add(Text(settings.Network.ExternalDownloader), "Network.ExternalDownloader"); Add(Text(settings.Network.ExternalDownloaderArgs), "Network.ExternalDownloaderArgs"); }
        try { NativeProcessingOptions.Validate(settings.PostProcess.Fixup, null); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { Add(true, "PostProcess.Fixup"); }
        try { NativeProcessingOptions.Validate(null, Text(settings.PostProcess.PostprocessorArgs) ? [settings.PostProcess.PostprocessorArgs] : null); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { Add(true, "PostProcess.PostprocessorArgs"); }
        try { NativeExec.Validate(Text(settings.PostProcess.ExecCommand) ? [settings.PostProcess.ExecCommand] : null); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { Add(true, "PostProcess.ExecCommand"); }
        Add(Text(settings.PostProcess.SponsorBlock) && !new[] { "none", "mark", "remove" }.Contains(settings.PostProcess.SponsorBlock, StringComparer.OrdinalIgnoreCase), "PostProcess.SponsorBlock");
        RejectUnsupported(unsupported);

        void Add(bool active, string option) { if (active) unsupported.Add(option); }
    }

    private static void CollectAuthentication(AuthSettings auth, List<string> unsupported, NativeAuthenticationCapabilities capabilities)
    {
        Add(Text(auth.Username) && !capabilities.HasFlag(NativeAuthenticationCapabilities.UserPassword), "Auth.Username");
        Add(Text(auth.Password) && !capabilities.HasFlag(NativeAuthenticationCapabilities.UserPassword), "Auth.Password");
        Add(Text(auth.TwoFactor) && !capabilities.HasFlag(NativeAuthenticationCapabilities.TwoFactor), "Auth.TwoFactor");
        Add(auth.UseNetrc && !capabilities.HasFlag(NativeAuthenticationCapabilities.UserPassword), "Auth.UseNetrc");
        Add(Text(auth.VideoPassword) && !capabilities.HasFlag(NativeAuthenticationCapabilities.VideoPassword), "Auth.VideoPassword");
        Add(Text(auth.ApMso), "Auth.ApMso");
        Add(Text(auth.ApUsername), "Auth.ApUsername");
        Add(Text(auth.ApPassword), "Auth.ApPassword");
        void Add(bool active, string option) { if (active) unsupported.Add(option); }
    }

    private static void CollectConnection(NetworkSettings network, List<string> unsupported, bool verificationProxy)
    {
        if (Text(network.GeoVerificationProxy))
        {
            try { NativeGeoVerification.Validate(new() { Proxy = network.GeoVerificationProxy }); if (!verificationProxy) unsupported.Add("Network.GeoVerificationProxy"); }
            catch (ArgumentException) { unsupported.Add("Network.GeoVerificationProxy"); }
        }
        try { NativeGeoVerification.FromLegacy(network.GeoVerificationProxy, network.GeoBypass, network.GeoBypassCountry, network.GeoBypassIpBlock); }
        catch (ArgumentException) { if (Text(network.GeoBypassCountry)) unsupported.Add("Network.GeoBypassCountry"); if (Text(network.GeoBypassIpBlock)) unsupported.Add("Network.GeoBypassIpBlock"); }
    }

    private static bool Text(string? value) => !string.IsNullOrWhiteSpace(value);

    private static void RejectUnsupported(List<string> options)
    {
        if (options.Count > 0)
            throw new NativeEngineUnsupportedException("Native support is not implemented for these configured settings: " + string.Join(", ", options) + ".");
    }
}
