using System.Globalization;
using System.Net;
using System.Text.Json;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Panopto;

public sealed partial class PanoptoProvider
{
    private static async Task<PageMetadata> ReadCollectionAsync(NativeDownloadRequest request, PanoptoCollectionAddress address, MediaHttp http, CancellationToken ct)
    {
        var headers = new Dictionary<string, string> { ["Referer"] = string.IsNullOrWhiteSpace(request.Referer) ? address.Page.AbsoluteUri : request.Referer,
            ["User-Agent"] = string.IsNullOrWhiteSpace(request.UserAgent) ? "YoutubeDlNative/" + NativeEngine.Version : request.UserAgent };
        using var session = http.WithHeaders(headers);
        if (address.Playlist) return await ReadOrderedPlaylistAsync(request, address, session, ct).ConfigureAwait(false);
        var title = address.Page.Host + " public library"; var specifiedOrder = false;
        if (address.FolderId is not null)
        {
            using var folder = await WcfAsync(session, address, "GetFolderInfo", new { folderID = address.FolderId }, ct).ConfigureAwait(false);
            var data = Object(folder.RootElement, "d");
            if (GuidValue(data, "ID") != address.FolderId) throw new InvalidDataException("Panopto returned another folder identity.");
            if (!string.IsNullOrEmpty(Text(data, "UnauthorizedTransferUrl"))) throw new NotSupportedException("This Panopto folder requires additional sign-in or access.");
            title = Title(data, "Name"); specifiedOrder = True(data, "DeliveriesHaveSpecifiedOrder") && !address.ExplicitSort;
        }
        if (address.Query.Length > 0) title += " — " + address.Query;
        var entries = new List<(NativePlaylistEntry Entry, double Order)>(); var seen = new HashSet<string>(StringComparer.Ordinal); int? total = null;
        for (var page = 0; ; page++)
        {
            ct.ThrowIfCancellationRequested();
            using var json = await WcfAsync(session, address, "GetSessions", new { queryParameters = new {
                folderID = address.FolderId, query = address.Query.Length == 0 ? null : address.Query, sortColumn = address.SortColumn,
                sortAscending = address.SortAscending, maxResults = address.PageSize, page, getFolderData = true,
                includeArchived = false, includeArchivedStateCount = false, searchSubfolders = false } }, ct).ConfigureAwait(false);
            var data = Object(json.RootElement, "d"); var count = Integer(data, "TotalNumber");
            if (count is null || count > request.MaximumPlaylistEntries || count > 10000) throw new NotSupportedException("The Panopto collection count is missing or exceeds the configured complete-inventory limit.");
            if (total is not null && total != count) throw new InvalidDataException("The Panopto collection changed size during pagination.");
            total = count; var rows = Array(data, "Results");
            if (rows.Length > address.PageSize || entries.Count + rows.Length > total || rows.Length == 0 && entries.Count != total ||
                rows.Length < address.PageSize && entries.Count + rows.Length != total) throw new InvalidDataException("Panopto pagination did not return a complete, consistent collection.");
            foreach (var row in rows)
            {
                var id = GuidValue(row, "DeliveryID");
                if (!seen.Add(id)) throw new InvalidDataException("Panopto pagination repeated a recording identity.");
                if (address.FolderId is not null && GuidValue(row, "FolderID") != address.FolderId) throw new InvalidDataException("A Panopto folder listing contains another folder's recording.");
                var available = Integer(row, "PlayableObjectType") == 0 && Integer(row, "Status") == 4 && True(row, "HasStreams") && !True(row, "IsArchived");
                var url = Viewer(Text(row, "ViewerUrl"), address, id);
                entries.Add((new(entries.Count + 1, id, Title(row, "SessionName"), url, Number(row, "Duration"), SafeImage(Text(row, "ThumbUrl")), available,
                    ExpectedArchiveIdentity: Identity(address, id)), specifiedOrder ? Number(row, "OrderSort") ?? throw new InvalidDataException("Panopto omitted a specified folder-order position.") : 0));
            }
            if (entries.Count == total) break;
            if (page >= 9999) throw new InvalidDataException("The Panopto collection did not complete within its page limit.");
        }
        var ordered = specifiedOrder ? entries.OrderBy(item => item.Order).Select(item => item.Entry).ToArray() : entries.Select(item => item.Entry).ToArray();
        return new() { Extractor = "panopto:folder", Id = address.FolderId ?? address.Page.Host, PlaylistId = address.FolderId ?? address.Page.Host,
            Title = title, IsPlaylist = true, Entries = ordered.Select((entry, index) => entry with { Position = index + 1 }).ToArray(), SourcePageUrl = address.Page };
    }
    private static async Task<PageMetadata> ReadOrderedPlaylistAsync(NativeDownloadRequest request, PanoptoCollectionAddress address, MediaHttp http, CancellationToken ct)
    {
        var metadataUrl = new Uri(address.Origin + "/Panopto/Api/Playlists/" + address.PlaylistId);
        using var metadata = await RestAsync(http, metadataUrl, ct).ConfigureAwait(false); var info = metadata.RootElement;
        if (GuidValue(info, "Id") != address.PlaylistId || GuidValue(info, "RepositoryKey") != address.PlaylistId || Text(info, "TypeName") != "Playlist")
            throw new InvalidDataException("Panopto returned another playlist identity or resource type.");
        var listId = GuidValue(info, "SessionListId");
        var maximum = Math.Min(500, request.MaximumPlaylistEntries + 1);
        var listUrl = new Uri(address.Origin + "/Panopto/Api/SessionLists/" + listId + "?collections%5B0%5D.maxCount=" + maximum.ToString(CultureInfo.InvariantCulture) + "&collections%5B0%5D.name=items");
        using var list = await RestAsync(http, listUrl, ct).ConfigureAwait(false); var data = list.RootElement;
        if (GuidValue(data, "Id") != listId || GuidValue(data, "RepositoryKey") != listId) throw new InvalidDataException("Panopto returned another playlist's item list.");
        var items = Array(data, "Items");
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("Items", out var itemList) || itemList.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Panopto omitted the playlist item inventory.");
        if (items.Length > request.MaximumPlaylistEntries) throw new NotSupportedException("The Panopto playlist exceeds the configured complete-inventory limit.");
        if (items.Length >= maximum)
        {
            if (maximum < 500) throw new NotSupportedException("The Panopto playlist exceeds the configured complete-inventory limit.");
            return await ReadPagedPlaylistAsync(request, address, http, info, data, metadataUrl, ct).ConfigureAwait(false);
        }
        var entries = new List<NativePlaylistEntry>();
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested(); var id = GuidValue(item, "Id");
            var available = Text(item, "TypeName") == "Session" && Integer(item, "Status") == 4 && !True(item, "IsDeleted") && !True(item, "IsPlaylist") && !True(item, "IsReferenceCopy");
            entries.Add(new(entries.Count + 1, id, Title(item, "Name"), Viewer(Text(item, "ViewerUri"), address, id), Number(item, "Duration"), SafeImage(Text(item, "ThumbUrl")), available,
                ExpectedArchiveIdentity: Identity(address, id)));
        }
        return new() { Extractor = "panopto:playlist", Id = address.PlaylistId, PlaylistId = address.PlaylistId, Title = Title(info, "Name"), Description = Text(info, "Description"),
            ThumbnailUrl = SafeImage(Text(info, "ThumbUrl")), IsPlaylist = true, Entries = entries, SourcePageUrl = address.Page };
    }
    private static async Task<PageMetadata> ReadPagedPlaylistAsync(NativeDownloadRequest request, PanoptoCollectionAddress address, MediaHttp http, JsonElement metadata, JsonElement initialList,
        Uri metadataUrl, CancellationToken ct)
    {
        var entries = new List<NativePlaylistEntry>(); var pages = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            for (var page = 0; ; page++)
            {
                ct.ThrowIfCancellationRequested();
                var endpoint = new Uri(address.Origin + "/Panopto/api/v1/playlists/" + address.PlaylistId + "/sessions?sortField=Order&sortOrder=Asc&pageNumber=" + page.ToString(CultureInfo.InvariantCulture));
                using var json = await RestAsync(http, endpoint, ct).ConfigureAwait(false);
                if (!json.RootElement.TryGetProperty("Results", out var rows) || rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() > 1000)
                    throw new InvalidDataException("Panopto omitted a supported paged playlist response.");
                if (rows.GetArrayLength() == 0) break;
                if (entries.Count + rows.GetArrayLength() > request.MaximumPlaylistEntries || page > 10000)
                    throw new NotSupportedException("The complete Panopto playlist exceeds its configured inventory limit.");
                // An identical page cannot establish progress, even when the source
                // intentionally repeats videos. Fail rather than invent positions.
                var fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rows.GetRawText())));
                if (!pages.Add(fingerprint)) throw new NotSupportedException("Panopto returned an identical playlist page. A complete ordered inventory could not be verified.");
                foreach (var item in rows.EnumerateArray())
                {
                    var id = GuidValue(item, "Id"); var urls = Object(item, "Urls");
                    entries.Add(new(entries.Count + 1, id, Title(item, "Name"), Viewer(Text(urls, "ViewerUrl"), address, id), Number(item, "Duration"), SafeImage(Text(urls, "ThumbnailUrl")),
                        Text(item, "Status") == "Complete", ExpectedArchiveIdentity: Identity(address, id)));
                }
            }
            using var current = await RestAsync(http, metadataUrl, ct).ConfigureAwait(false);
            if (GuidValue(current.RootElement, "Id") != address.PlaylistId || GuidValue(current.RootElement, "SessionListId") != GuidValue(metadata, "SessionListId"))
                throw new InvalidDataException("The Panopto playlist changed its item list while being read.");
            using var currentList = await RestAsync(http, new Uri(address.Origin + "/Panopto/Api/SessionLists/" + GuidValue(initialList, "Id")), ct).ConfigureAwait(false);
            if (Text(initialList, "UpdateDate") is not { Length: > 0 } version || Text(currentList.RootElement, "UpdateDate") != version || GuidValue(currentList.RootElement, "Id") != GuidValue(initialList, "Id"))
                throw new InvalidDataException("The Panopto playlist changed while its recording pages were read.");
            return new() { Extractor = "panopto:playlist", Id = address.PlaylistId, PlaylistId = address.PlaylistId, Title = Title(metadata, "Name"), Description = Text(metadata, "Description"),
                ThumbnailUrl = SafeImage(Text(metadata, "ThumbUrl")), IsPlaylist = true, Entries = entries, SourcePageUrl = address.Page };
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new NotSupportedException("This larger Panopto playlist needs access to the authenticated paged API. The native engine will not publish a truncated collection.");
        }
    }
    private static async Task<JsonDocument> WcfAsync(MediaHttp http, PanoptoCollectionAddress address, string method, object body, CancellationToken ct)
    {
        var url = new Uri(address.Origin + "/Panopto/Services/Data.svc/" + method);
        // Playback/session options remain shared with the owned collection job.
        using var controlled = http.AllowAutoRedirect ? http.CreateWithoutRedirects() : null;
        var text = await (controlled ?? http).PostJsonAsync(url, body, 4 * 1024 * 1024, null, ct).ConfigureAwait(false);
        return ParseDocument(text);
    }
    private static async Task<JsonDocument> RestAsync(MediaHttp http, Uri url, CancellationToken ct)
    {
        var document = await http.GetDocumentAsync(url, 4 * 1024 * 1024, ct).ConfigureAwait(false);
        if (document.Url != url) throw new NotSupportedException("Panopto redirected its collection API. Sign-in or a different layout may be required.");
        return ParseDocument(document.Text);
    }
    private static JsonDocument ParseDocument(string text)
    {
        try { return JsonDocument.Parse(text, new() { MaxDepth = 48 }); }
        catch (JsonException) { throw new NotSupportedException("Panopto returned no supported collection document. Access or a different layout may be required."); }
    }
    private static string Title(JsonElement data, string key) => Text(data, key) is { Length: > 0 and <= 4096 } title && !title.Contains('\0') ? title : throw new InvalidDataException("Panopto omitted a supported collection or recording title.");
    private static Uri Viewer(string? value, PanoptoCollectionAddress address, string id)
    {
        if (string.IsNullOrEmpty(value)) return new Uri(address.Origin + "/Panopto/Pages/Viewer.aspx?id=" + id);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var url) || url.GetLeftPart(UriPartial.Authority) != address.Origin) throw new InvalidDataException("A Panopto collection recording points to another server.");
        if (PanoptoCollectionAddress.IsPlaylist(url)) url = PanoptoCollectionAddress.Single(url);
        if (PanoptoAddress.Parse(url).Id != id) throw new InvalidDataException("A Panopto collection entry points to another recording.");
        return url;
    }
    private static string? SafeImage(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var url) && url.Scheme == "https" && url.UserInfo.Length == 0 ? url.AbsoluteUri : null;
    private static DownloadArchiveIdentity Identity(PanoptoCollectionAddress address, string id) => new("panopto:delivery", address.Page.Host + "/" + id);
}
