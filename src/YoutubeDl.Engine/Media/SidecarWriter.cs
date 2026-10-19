using System.Text;
using System.Text.Json;
using System.Buffers.Binary;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;

namespace YoutubeDl.Engine.Media;

public sealed record WrittenSidecars(string? ThumbnailPath, IReadOnlyList<string> Files);

/// <summary>Writes the description, thumbnail, and info JSON beside a finished download.</summary>
public static class SidecarWriter
{
    public static async Task<WrittenSidecars> WriteAsync(
        NativeDownloadRequest request,
        PageMetadata page,
        string mediaPath,
        MediaHttp http,
        CancellationToken ct)
    {
        page = ThumbnailCatalog.Prepare(page);
        var stem = Path.Combine(Path.GetDirectoryName(mediaPath)!, Path.GetFileNameWithoutExtension(mediaPath));
        var files = new List<string>();
        if (request.WriteDescription && !string.IsNullOrWhiteSpace(page.Description))
        {
            await AtomicFile.WriteAsync(stem + ".description", Encoding.UTF8.GetBytes(page.Description), ct).ConfigureAwait(false);
            files.Add(stem + ".description");
        }
        if (request.WriteInfoJson)
        {
            var data = JsonSerializer.SerializeToNode(new
            {
                id = page.Id,
                title = page.Title,
                description = page.Description,
                thumbnail = page.ThumbnailUrl,
                thumbnails = page.Thumbnails.Select(item => new { id = item.Id, url = item.Url.AbsoluteUri, width = item.Width, height = item.Height }),
                uploader = page.Uploader,
                webpage_url = request.Url.AbsoluteUri,
                extractor = page.Extractor,
                duration = page.DurationSeconds,
                upload_date = (page.UploadDate ?? MetadataFacts.Day(page.UploadTimestamp))?.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture),
                timestamp = page.UploadTimestamp?.ToUnixTimeMilliseconds() / 1000m,
                view_count = page.ViewCount,
                age_limit = page.AgeLimit,
                is_live = page.IsLive,
                playlist_id = request.PlaylistId,
                playlist_title = request.PlaylistTitle,
                playlist_index = request.PlaylistIndex,
                playlist_autonumber = request.PlaylistAutoNumber,
                n_entries = request.PlaylistCount,
                playlist_parents = request.PlaylistParents.Count > 0 ? request.PlaylistParents.Select(parent => new {
                    id = parent.Id, title = parent.Title, playlist_index = parent.Index, playlist_autonumber = parent.AutoNumber, n_entries = parent.Count }).ToArray() : null,
                chapters = page.Chapters.Select(chapter => new { start_time = chapter.StartSeconds, end_time = chapter.EndSeconds, title = chapter.Title })
            })!.AsObject();
            foreach (var item in page.ParsedMetadata) data[item.Key] = System.Text.Json.Nodes.JsonValue.Create(item.Value);
            await AtomicFile.WriteAsync(stem + ".info.json", Encoding.UTF8.GetBytes(data.ToJsonString()), ct).ConfigureAwait(false);
            files.Add(stem + ".info.json");
        }

        string? preferredPath = null;
        if (request.WriteThumbnail || request.WriteAllThumbnails || request.EmbedThumbnail)
        {
            var selected = request.WriteAllThumbnails ? page.Thumbnails : page.Thumbnails.Where(item => item.Url.AbsoluteUri == page.ThumbnailUrl).ToArray();
            long received = 0;
            foreach (var item in selected)
            {
                ct.ThrowIfCancellationRequested();
                var remaining = 64L * 1024 * 1024 - received;
                if (remaining <= 0) throw new InvalidDataException("The thumbnail set exceeds its total byte limit.");
                var bytes = await http.GetBytesAsync(item.Url, Math.Min(8 * 1024 * 1024, remaining), ct).ConfigureAwait(false);
                received += bytes.Length;
                var path = stem + (request.WriteAllThumbnails ? "." + item.Id : "") + ImageExtension(bytes);
                await AtomicFile.WriteAsync(path, bytes, ct).ConfigureAwait(false);
                if (request.WriteThumbnail || request.WriteAllThumbnails) files.Add(path);
                if (item.Url.AbsoluteUri == page.ThumbnailUrl) preferredPath = path;
            }
        }
        return new(preferredPath, files);
    }
    private static string ImageExtension(byte[] bytes) =>
        bytes.Length >= 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff ? ".jpg" :
        bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ? ".png" :
        bytes.Length >= 12 && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8) ? ".webp" :
        bytes.AsSpan().StartsWith("GIF87a"u8) || bytes.AsSpan().StartsWith("GIF89a"u8) ? ".gif" :
        bytes.Length >= 14 && bytes[0] == 'B' && bytes[1] == 'M' ? ".bmp" :
        IsAvif(bytes) ? ".avif" :
        throw new InvalidDataException("The thumbnail endpoint did not return a supported image.");
    private static bool IsAvif(byte[] bytes)
    {
        if (bytes.Length < 16 || !bytes.AsSpan(4, 4).SequenceEqual("ftyp"u8)) return false;
        var length = BinaryPrimitives.ReadUInt32BigEndian(bytes);
        if (length is < 16 or > 4096 || length > bytes.Length || length % 4 != 0) return false;
        for (var offset = 8; offset < length; offset += 4)
        {
            if (offset == 12) continue;
            if (bytes.AsSpan(offset, 4).SequenceEqual("avif"u8) || bytes.AsSpan(offset, 4).SequenceEqual("avis"u8)) return true;
        }
        return false;
    }
}
