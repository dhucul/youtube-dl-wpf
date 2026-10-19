using System.Text;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;

namespace YoutubeDl.Engine.Media;

public sealed record EmbeddedSubtitle(string Path, string Language);

public sealed class SubtitleFiles : IDisposable
{
    private readonly List<string> _temporary = [];
    public List<EmbeddedSubtitle> Tracks { get; } = [];
    public List<string> SavedFiles { get; } = [];
    internal void Add(string path, string language, bool temporary, bool embed = true)
    { if (embed) Tracks.Add(new(path, language)); if (temporary) _temporary.Add(path); else SavedFiles.Add(path); }
    public void Dispose() { foreach (var path in _temporary) if (File.Exists(path)) File.Delete(path); }
}

public static class SubtitleWriter
{
    public static async Task<SubtitleFiles> WriteAsync(NativeDownloadRequest request, PageMetadata page,
        string mediaPath, MediaHttp http, CancellationToken ct)
    {
        var result = new SubtitleFiles();
        try
        {
            var stem = Path.Combine(Path.GetDirectoryName(mediaPath)!, Path.GetFileNameWithoutExtension(mediaPath));
            var selected = SubtitleSelection.Select(page.Subtitles, request);
            SubtitleFormats.ValidateSelection(request, selected);
            var target = SubtitleFormats.ConversionTarget(request.ConvertSubs);
            foreach (var track in selected)
            {
                var language = string.IsNullOrWhiteSpace(track.Language) ? "und" : track.Language;
                if (language.Length > 64 || !Regex.IsMatch(language, @"\A[A-Za-z0-9][A-Za-z0-9_-]*(?:\.[A-Za-z0-9_-]+)*\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
                    throw new InvalidDataException("The provider returned an invalid caption language identifier.");
                var extension = SubtitleFormats.Normalize(track.Extension);
                ct.ThrowIfCancellationRequested();
                if (track.Content is { } content && Encoding.UTF8.GetByteCount(content) > 2 * 1024 * 1024) throw new InvalidDataException("The resolved caption exceeds its size limit.");
                if (track.Content is null) await NativeSubtitleDelay.WaitAsync(request, ct).ConfigureAwait(false);
                var bytes = track.Content is { } resolved ? Encoding.UTF8.GetBytes(resolved) : await http.GetBytesAsync(track.Url, 2 * 1024 * 1024, ct).ConfigureAwait(false);
                var text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF', ' ', '\r', '\n', '\t');
                if (text.Length == 0 || text.StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase) ||
                    text.StartsWith("<html", StringComparison.OrdinalIgnoreCase) || extension == "vtt" && !text.StartsWith("WEBVTT", StringComparison.Ordinal))
                    throw new InvalidDataException("The caption endpoint did not return the expected subtitle document.");
                if (request.SponsorEdits is { } sponsor && sponsor.Removes(request))
                {
                    if (extension is "ttml" or "dfxp" && target.Length == 0 && (track.Automatic ? request.WriteAutoSubs : request.WriteSubs)) throw new NotSupportedException("Saving edited TTML requires an explicit VTT or SRT conversion target.");
                    var editedText = sponsor.Captions(text, extension, request, ct);
                    if (!editedText.Contains("-->", StringComparison.Ordinal)) continue;
                    bytes = Encoding.UTF8.GetBytes(editedText);
                    if (extension is "ttml" or "dfxp") extension = "vtt";
                }
                var original = bytes;
                var originalExtension = extension;
                if (extension is "ttml" or "dfxp" && (target.Length > 0 || request.EmbedSubs))
                {
                    var timed = TtmlReader.Read(text, ct);
                    original = Encoding.UTF8.GetBytes(timed.Render("vtt"));
                    originalExtension = "vtt";
                    if (target is "srt" or "vtt") { bytes = Encoding.UTF8.GetBytes(timed.Render(target)); extension = target; }
                    else if (target.Length > 0)
                    {
                        bytes = await FfmpegTool.ConvertSubtitleAsync(request.FfmpegPath!, original, "vtt", target, Path.GetDirectoryName(mediaPath)!, ct).ConfigureAwait(false);
                        extension = target;
                    }
                }
                else if (target.Length > 0 && target != extension)
                {
                    bytes = await FfmpegTool.ConvertSubtitleAsync(request.FfmpegPath!, bytes, extension, target, Path.GetDirectoryName(mediaPath)!, ct).ConfigureAwait(false);
                    extension = target;
                }
                var save = track.Automatic ? request.WriteAutoSubs : request.WriteSubs;
                var path = save ? stem + "." + language + "." + extension : Path.Combine(Path.GetDirectoryName(mediaPath)!, ".caption-" + Guid.NewGuid().ToString("N") + "." + extension);
                // Track temporary ownership before writing, so failures and cancellation clean it up.
                // LRC records cue starts but cannot retain explicit end timestamps.
                // Keep the full source timing for embedding while saving requested LRC text.
                var embedOriginal = request.EmbedSubs && (extension is "ttml" or "dfxp" || extension == "lrc" && originalExtension != "lrc");
                result.Add(path, language, temporary: !save, embed: !embedOriginal);
                await AtomicFile.WriteAsync(path, bytes, ct).ConfigureAwait(false);
                if (embedOriginal)
                {
                    var embedding = Path.Combine(Path.GetDirectoryName(mediaPath)!, ".caption-" + Guid.NewGuid().ToString("N") + "." + originalExtension);
                    result.Add(embedding, language, temporary: true);
                    await AtomicFile.WriteAsync(embedding, original, ct).ConfigureAwait(false);
                }
            }
            return result;
        }
        catch { result.Dispose(); throw; }
    }
}
