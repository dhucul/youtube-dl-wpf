using System.IO;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;
using YoutubeDlModern.Services;

internal static class QualityPreviewChecks
{
    internal static IEnumerable<(string Name, Func<Task> Test)> All => [("YouTube quality preview keeps web inventory IDs without acquiring playback", Preview)];
    private static async Task Preview()
    {
        var provider = new Fixture(); var adapter = new NativeEngineAdapter(new NativeEngine([provider], _ => new MediaHttp()));
        var info = await adapter.GetVideoInfoAsync("https://www.youtube.com/watch?v=abcdefghijk", default);
        if (provider.Request is not { SkipDownload: true, MetadataInventoryOnly: true } || info.Formats?.Count != 1 || info.Formats[0].FormatId != "web-video" || !info.Formats[0].FormatNote!.Contains("Checked on download", StringComparison.Ordinal)) throw new Exception("The preview required a signed URL, switched client IDs, or exposed a protected rendition.");
    }
    private sealed class Fixture : IMediaProvider
    {
        internal NativeDownloadRequest? Request;
        public MediaProviderInfo Info => new("youtube", "Owned preview", 100);
        public bool CanHandle(Uri uri) => true;
        public Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
        {
            Request = request;
            return Task.FromResult(new PageMetadata { Extractor = "youtube", Id = "abcdefghijk", Title = "Owned preview", ReportedFormats = [
                new() { Id = "web-video", Extension = "webm", Height = 1080, VideoCodec = "vp9", HasVideo = true, HasAudio = false, Availability = FormatAvailability.AdaptiveTransportRequired },
                new() { Id = "protected", Availability = FormatAvailability.DrmProtected }, new() { Id = "missing", Availability = FormatAvailability.MissingUrl }] });
        }
    }
}
