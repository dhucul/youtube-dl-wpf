using System.Net;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Media;

namespace YoutubeDl.Engine;

public sealed partial class NativeEngine
{
    private sealed record ProcessedMedia(string Path, IReadOnlyList<string> Sidecars, bool Modified = false, bool TimelineEdited = false);
    private static async Task<ProcessedMedia> PostProcessAsync(
        NativeDownloadRequest request, PageMetadata page, string input, MediaHttp http, CancellationToken ct, bool? sourceHasAudio = null, AudioOutputPlan? audioPlan = null, Action<string>? log = null)
    {
        var ffmpeg = request.FfmpegPath;
        var modified = false; var timelineEdited = false;
        var fixup = NativeProcessingOptions.Fixup(request.Fixup);
        if (fixup is not ("" or "never"))
        {
            if (string.IsNullOrWhiteSpace(ffmpeg)) throw new InvalidOperationException("ffmpeg is required for native fixup detection.");
            var needed = await FfmpegTool.NeedsContainerFixupAsync(ffmpeg, input, ct).ConfigureAwait(false);
            if (needed || fixup == "force")
            {
                if (fixup == "warn") { if (!request.NoWarnings) log?.Invoke("[native] The source container does not match its file extension; fixup was requested in warning mode."); }
                else
                {
                    await ProcessReplaceAsync(input, temporary => FfmpegTool.FixContainerAsync(ffmpeg, input, temporary, ct), ct).ConfigureAwait(false);
                    modified = true;
                }
            }
        }
        if (request.SponsorEdits is { } sponsor)
        {
            if (sponsor.RequestsCuts(request))
            {
                sponsor = await FfmpegTool.BindSponsorSourceAsync(ffmpeg!, input, sponsor, ct).ConfigureAwait(false);
                request = request with { SponsorEdits = sponsor };
                if (sponsor.Removes(request))
                {
                    await ProcessReplaceAsync(input, temporary => FfmpegTool.RemoveSponsorSegmentsAsync(ffmpeg!, input, temporary, sponsor, request, ct), ct).ConfigureAwait(false);
                    audioPlan = null; modified = true; timelineEdited = true;
                }
            }
            page = sponsor.Metadata(page, request);
        }
        var sidecars = await SidecarWriter.WriteAsync(request, page, input, http, ct).ConfigureAwait(false);
        var thumbnail = sidecars.ThumbnailPath;
        using var subtitles = await SubtitleWriter.WriteAsync(request, page, input, http, ct).ConfigureAwait(false);
        var saved = sidecars.Files.Concat(subtitles.SavedFiles).ToArray();
        if (string.IsNullOrWhiteSpace(ffmpeg))
        {
            if (request.AudioOnly || request.EmbedSubs || request.EmbedThumbnail || request.AddMetadata || request.EmbedChapters && page.Chapters.Count > 0 || !string.IsNullOrWhiteSpace(request.Container) || !string.IsNullOrWhiteSpace(request.RecodeVideo))
                throw new InvalidOperationException("ffmpeg is required for the requested media processing.");
            return new(input, saved);
        }
        if (request.AudioOnly)
        {
            audioPlan ??= await FfmpegTool.PlanAudioAsync(ffmpeg, input, request.AudioFormat, request.AudioQuality, ct).ConfigureAwait(false);
            var audio = Path.ChangeExtension(input, audioPlan.Extension);
            await ProcessReplaceAsync(audio, temporary => FfmpegTool.ExtractAudioAsync(
                ffmpeg, input, temporary, audioPlan, ct), ct).ConfigureAwait(false);
            if (audio != input) TryDelete(input);
            input = audio;
            modified = true;
        }
        else if (request.VideoOnly && sourceHasAudio != false)
        {
            await ProcessReplaceAsync(input, temporary => FfmpegTool.ExtractVideoAsync(ffmpeg, input, temporary, ct), ct).ConfigureAwait(false);
            modified = true;
        }
        if (!request.AudioOnly && VideoRecodeProfile.Parse(request.RecodeVideo) is { } recode && !input.EndsWith("." + recode.Extension, StringComparison.OrdinalIgnoreCase))
        {
            var converted = Path.ChangeExtension(input, recode.Extension);
            await ProcessReplaceAsync(converted, temporary => FfmpegTool.RecodeVideoAsync(ffmpeg, input, temporary, recode, ct), ct).ConfigureAwait(false);
            TryDelete(input); input = converted; modified = true;
        }
        if (!request.AudioOnly && request.EmbedThumbnail && thumbnail is not null && string.IsNullOrWhiteSpace(request.Container) && string.IsNullOrWhiteSpace(request.RecodeVideo))
            request = Path.GetExtension(input).ToLowerInvariant() switch
            {
                ".ts" => request with { Container = "mp4" },
                ".webm" => request with { Container = "mkv" },
                _ => request
            };
        if (!request.AudioOnly && request.EmbedSubs && subtitles.Tracks.Count > 0 && string.IsNullOrWhiteSpace(request.Container) && string.IsNullOrWhiteSpace(request.RecodeVideo) &&
            Path.GetExtension(input).Equals(".ts", StringComparison.OrdinalIgnoreCase))
            request = request with { Container = "mp4" };
        if (!request.AudioOnly && !string.IsNullOrWhiteSpace(request.Container) &&
            !input.EndsWith("." + request.Container, StringComparison.OrdinalIgnoreCase))
        {
            var remuxed = Path.ChangeExtension(input, request.Container);
            await ProcessReplaceAsync(remuxed, temporary => FfmpegTool.RemuxAsync(ffmpeg, input, temporary, ct), ct).ConfigureAwait(false);
            TryDelete(input);
            input = remuxed;
            modified = true;
        }

        if (!request.EmbedThumbnail) thumbnail = null;
        var metadata = request.AddMetadata
            ? NativeMetadataParser.EmbeddedTags(request, page)
            : null;
        var chapters = request.EmbedChapters || NativeSponsorBlock.Enabled(request) ? page.Chapters : [];
        if (request.EmbedSubs && subtitles.Tracks.Count > 0 || thumbnail is not null || metadata is not null || chapters.Count > 0)
        {
            await ProcessReplaceAsync(input, embedded => FfmpegTool.EmbedAsync(ffmpeg, input, embedded,
                request.EmbedSubs ? subtitles.Tracks : [], thumbnail, metadata, ct, chapters), ct).ConfigureAwait(false);
            modified = true;
        }

        return new(input, saved, modified, timelineEdited);
    }

    private static string ProcessedExtension(NativeDownloadRequest request, PageMetadata page, string rawExtension, AudioOutputPlan? audioPlan)
    {
        if (request.AudioOnly) return audioPlan?.Extension ?? AudioOutputPlan.ExtensionFor(request.AudioFormat) ?? throw new InvalidOperationException("The automatic audio output has not been resolved.");
        if (VideoRecodeProfile.Parse(request.RecodeVideo) is { } recode) return recode.Extension;
        if (!string.IsNullOrWhiteSpace(request.Container)) return request.Container;
        if (request.EmbedThumbnail && !string.IsNullOrWhiteSpace(page.ThumbnailUrl))
        {
            if (rawExtension == "ts") return "mp4";
            if (rawExtension == "webm") return "mkv";
        }
        if (rawExtension == "ts" && request.EmbedSubs && SubtitleSelection.Select(page.Subtitles, request).Count > 0) return "mp4";
        return rawExtension;
    }
    private static bool WouldModify(NativeDownloadRequest request, PageMetadata page, string rawExtension, string finalExtension, bool? hasAudio) =>
        NativeSponsorBlock.Enabled(request) || NativeProcessingOptions.Fixup(request.Fixup) is "force" or "detect_or_fix" || request.AudioOnly || request.VideoOnly && hasAudio != false || rawExtension != finalExtension || request.AddMetadata ||
        request.EmbedThumbnail && !string.IsNullOrWhiteSpace(page.ThumbnailUrl) || request.EmbedChapters && page.Chapters.Count > 0 ||
        request.EmbedSubs && SubtitleSelection.Select(page.Subtitles, request).Count > 0;

    private static async Task ProcessReplaceAsync(string destination, Func<string, Task> process, CancellationToken ct)
    {
        var temporary = Path.Combine(Path.GetDirectoryName(destination)!, ".process-" + Guid.NewGuid().ToString("N") + Path.GetExtension(destination));
        try
        {
            await process(temporary).ConfigureAwait(false);
            if (!File.Exists(temporary) || new FileInfo(temporary).Length == 0) throw new InvalidDataException("ffmpeg did not produce a media file.");
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: true);
        }
        finally { TryDelete(temporary); }
    }

    private Task<MediaHttp> CreateSessionAsync(NativeDownloadRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_httpFactory is not null) return Task.FromResult(_httpFactory(request));
        // Browser database work must not block the WPF dispatcher while preparing an HTTP session.
        return Task.Run(() => CreateHttp(request, ct), ct);
    }
    private static MediaHttp CreateHttp(NativeDownloadRequest request, CancellationToken ct)
    {
        CookieContainer? cookies = null;
        IReadOnlyList<string> warnings = [];
        if (!string.IsNullOrWhiteSpace(request.CookieFile))
            cookies = NetscapeCookieFile.Load(request.CookieFile, ct);
        else if (!string.IsNullOrWhiteSpace(request.BrowserCookies))
        {
            var imported = BrowserCookieReader.Read(request.BrowserCookies, ct);
            cookies = imported.Cookies; warnings = imported.Warnings;
        }
        ct.ThrowIfCancellationRequested();
        var options = new MediaHttpOptions
        {
            UserAgent = string.IsNullOrWhiteSpace(request.UserAgent) ? MediaHttp.DefaultUserAgent : request.UserAgent,
            Referer = string.IsNullOrWhiteSpace(request.Referer) ? request.Url.AbsoluteUri : request.Referer,
            ForwardedFor = request.GeoVerification?.SelectedAddress,
            Proxy = request.Proxy ?? "",
            SourceAddress = request.SourceAddress ?? "",
            IpVersion = request.IpVersion,
            NoCheckCertificate = request.NoCheckCertificate,
            TimeoutSeconds = request.TimeoutSeconds,
            CookieContainer = cookies,
            SessionWarnings = warnings
        };
        foreach (var pair in request.HttpHeaders) options.Headers[pair.Key] = pair.Value;
        return new MediaHttp(options);
    }

    private static NativeMediaInfo ToInfo(Uri url, PageMetadata page, NativePlaylistSnapshot? snapshot = null) => new()
    {
        Id = page.Id ?? Sanitize(url.Host),
        Title = string.IsNullOrWhiteSpace(page.Title) ? url.Host : page.Title,
        Description = page.Description,
        ThumbnailUrl = page.ThumbnailUrl,
        Thumbnails = page.Thumbnails,
        Uploader = page.Uploader,
        Extractor = page.Extractor,
        DurationSeconds = page.DurationSeconds,
        UploadDate = page.UploadDate ?? MetadataFacts.Day(page.UploadTimestamp),
        UploadTimestamp = page.UploadTimestamp,
        ViewCount = page.ViewCount,
        AgeLimit = page.AgeLimit,
        IsLive = page.IsLive,
        Subtitles = page.Subtitles,
        Chapters = page.Chapters,
        PlayerScriptUrl = page.PlayerScriptUrl,
        PlaylistId = page.PlaylistId,
        IsPlaylist = page.IsPlaylist,
        PlaylistSnapshot = snapshot,
        Entries = page.Entries,
        Warnings = page.Warnings,
        Formats = page.ReportedFormats.Count > 0 ? page.ReportedFormats : page.Media.Select((item, index) => new NativeFormat
        {
            Id = item.Id ?? (index + 1).ToString(),
            Url = item.Kind == MediaKind.HttpSequence ? null : item.Url,
            MimeType = item.MimeType,
            Width = item.Width,
            Bandwidth = item.Bandwidth,
            Extension = item.Extension,
            Height = item.Height,
            Note = item.Label ?? item.Source,
            AudioOnly = item.IsAudioOnly,
            HasAudio = item.HasAudio,
            HasVideo = item.HasVideo,
            AudioLanguage = item.AudioLanguage,
            IsDefaultAudio = item.IsDefaultAudio,
            AudioCodec = item.AudioCodec,
            VideoCodec = item.VideoCodec,
            FramesPerSecond = item.FramesPerSecond,
            FileSize = item.FileSize,
            Protocol = item.Kind switch { MediaKind.Hls => "m3u8", MediaKind.Dash => "dash", MediaKind.Sabr => "sabr", MediaKind.HttpSequence => "http_sequence", _ => item.Url.Scheme }
        }).ToList()
    };

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return Whitespace().Replace(cleaned, " ");
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* the converted file is already the result */ }
    }

    [GeneratedRegex("\\s+")]
    private static partial Regex Whitespace();
}
