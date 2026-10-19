using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Coub;

public sealed class CoubProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("coub", "Coub public video loops and supplied audio", 200);
    public bool CanHandle(Uri url) => CoubAddress.Matches(url);
    public DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request) => new("coub", CoubAddress.Parse(request.Url).Id);
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var address = CoubAddress.Parse(request.Url); using var anonymous = http.CreateAnonymousSession();
        var endpoint = new Uri("https://coub.com/api/v2/coubs/" + address.Id); var response = await anonymous.GetDocumentAsync(endpoint, 4 * 1024 * 1024, ct).ConfigureAwait(false);
        if (response.Url != endpoint) throw new InvalidDataException("Coub redirected its requested metadata identity.");
        using var json = Json(response.Text); var root = json.RootElement;
        if (Text(root, "permalink") != address.Id || Long(root, "id") is not > 0 || Text(root, "type") != "Coub::Simple") throw new InvalidDataException("Coub returned another video or an unsupported recoub/edit layout.");
        if (Text(root, "visibility_type") is not ("public" or "unlisted") || !Flag(root, "published") || !Flag(root, "is_done") || Flag(root, "banned") || Text(root, "moderation_state") is "rejected" or "deleted")
            throw new NotSupportedException("This Coub is unavailable to the current public session.");
        var adminRestriction = root.TryGetProperty("age_restricted_by_admin", out var adminAge) && adminAge.ValueKind == JsonValueKind.Null ? false : Flag(root, "age_restricted_by_admin");
        if (Flag(root, "age_restricted") || adminRestriction) throw new NotSupportedException("This Coub needs additional native age/account workflow support.");
        var sound = Flag(root, "has_sound"); var duration = Number(root, "duration"); if (duration is not > 0 || duration > 600) throw new InvalidDataException("Coub returned an unsupported loop duration.");
        var html5 = Object(Object(root, "file_versions"), "html5"); var video = Object(html5, "video"); var audio = Object(html5, "audio");
        if (video.ValueKind != JsonValueKind.Object || video.EnumerateObject().Count() > 16 || sound && (audio.ValueKind != JsonValueKind.Object || audio.EnumerateObject().Count() > 16))
            throw new NotSupportedException("Coub did not expose supported complete HTML5 video/audio inventories.");
        var media = new List<MediaCandidate>(); var urls = new HashSet<string>(StringComparer.Ordinal); var dimensions = Object(root, "dimensions");
        foreach (var descriptor in video.EnumerateObject())
        {
            if (descriptor.Name is not ("high" or "med" or "low")) throw new NotSupportedException("This Coub video rendition needs additional native support.");
            var url = Resource(Text(descriptor.Value, "url"), ".mp4"); if (!urls.Add(url.AbsoluteUri)) continue;
            var dimension = Array(dimensions, descriptor.Name == "high" ? "big" : descriptor.Name == "med" ? "med" : "small");
            var width = Pixel(dimension, 0); var height = Pixel(dimension, 1); var size = Size(descriptor.Value);
            media.Add(new() { Id = "video-" + descriptor.Name, Url = url, Kind = MediaKind.Progressive, Extension = "mp4", Width = width, Height = height, ProviderQualityOrder = Order(descriptor.Name),
                HasVideo = true, HasAudio = false, IsCompleteMedia = !sound, LoopVideoToAudio = sound, Source = "coub", FileSize = size, VerificationFileSize = size,
                VerificationDemuxer = "mov", VerificationDurationSeconds = duration });
        }
        if (sound) foreach (var descriptor in audio.EnumerateObject())
        {
            if (descriptor.Name is not ("high" or "med" or "low")) throw new NotSupportedException("This Coub audio rendition needs additional native support.");
            var url = Resource(Text(descriptor.Value, "url"), ".mp3"); if (!urls.Add(url.AbsoluteUri)) continue; var size = Size(descriptor.Value);
            media.Add(new() { Id = "audio-" + descriptor.Name, Url = url, Kind = MediaKind.Progressive, Extension = "mp3", HasAudio = true, HasVideo = false, IsCompleteMedia = true,
                IsDefaultAudio = descriptor.Name == "high", ProviderQualityOrder = Order(descriptor.Name), Source = "coub", FileSize = size, VerificationFileSize = size, VerificationDemuxer = "mp3" });
        }
        if (!media.Any(item => item.HasVideo == true) || sound && !media.Any(item => item.IsAudioOnly)) throw new NotSupportedException("Coub omitted its complete requested video/audio source.");
        var image = Text(root, "picture") is { Length: > 0 } poster ? Resource(poster, null).AbsoluteUri : null;
        return new() { Id = address.Id, Extractor = "coub", ArchiveIdentity = new("coub", address.Id), Title = Text(root, "title") ?? address.Id, DurationSeconds = duration,
            Uploader = Text(Object(root, "channel"), "title") ?? "Coub", ThumbnailUrl = image, Media = media, SourcePageUrl = address.Page, AnonymousPlayback = true,
            Warnings = sound ? ["The finite native movie repeats the supplied video cycle through one complete audio track. Explicit video-only/single-video selections retain one source cycle."] : [] };
    }
    private static int Order(string quality) => quality switch { "high" => 3, "med" => 2, "low" => 1, _ => 0 };
    private static int? Pixel(JsonElement[] dimensions, int index)
    {
        if (dimensions.Length == 0) return null;
        if (dimensions.Length != 2 || dimensions[index].ValueKind != JsonValueKind.Number || !dimensions[index].TryGetInt32(out var value) || value is < 1 or > 16384) throw new InvalidDataException("Coub returned invalid supplied video dimensions.");
        return value;
    }
    private static long Size(JsonElement descriptor) => Long(descriptor, "size") is { } value && value is > 0 and <= 2000000000 ? value : throw new InvalidDataException("Coub returned an invalid exact source size.");
    private static bool Flag(JsonElement root, string key) => root.TryGetProperty(key, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.ValueKind == JsonValueKind.True :
        throw new NotSupportedException("Coub omitted a supported explicit public source state.");
    private static Uri Resource(string? raw, string? extension)
    {
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var url) || url.Scheme != "https" || !url.IsDefaultPort || url.UserInfo.Length != 0 || url.Fragment.Length != 0 ||
            url.Host is not ("attachments-cdn-s.coub.com" or "attachments-cdn.coub.com") || extension is not null && !url.AbsolutePath.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Coub returned an unverified supplied resource origin or container.");
        return url;
    }
    private static JsonDocument Json(string text)
    {
        JsonDocument value; try { value = JsonDocument.Parse(text, new() { MaxDepth = 64 }); } catch (JsonException) { throw new InvalidDataException("Coub returned malformed public metadata."); }
        try
        {
            var stack = new Stack<JsonElement>(); stack.Push(value.RootElement); var count = 0;
            while (stack.TryPop(out var node))
            {
                if (++count > 100000) throw new InvalidDataException("Coub returned excessive metadata fields.");
                if (node.ValueKind == JsonValueKind.Object) { var names = new HashSet<string>(StringComparer.Ordinal); foreach (var property in node.EnumerateObject()) { if (!names.Add(property.Name)) throw new InvalidDataException("Coub returned repeated metadata fields."); stack.Push(property.Value); } }
                else if (node.ValueKind == JsonValueKind.Array) foreach (var child in node.EnumerateArray()) stack.Push(child);
            }
            return value;
        }
        catch { value.Dispose(); throw; }
    }
}
