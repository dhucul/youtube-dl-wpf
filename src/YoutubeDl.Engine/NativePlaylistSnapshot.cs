using YoutubeDl.Engine.Discovery;

namespace YoutubeDl.Engine;

/// <summary>Immutable inspected playlist identities/order; contains no captured playback resources or session credentials.</summary>
public sealed class NativePlaylistSnapshot
{
    internal sealed record Item(NativePlaylistEntry Entry, bool RefreshParent, string? ExpectedMediaId, string? Referer);
    internal Uri RequestUrl { get; }
    internal string ProviderId { get; }
    internal PageMetadata Metadata { get; }
    internal IReadOnlyList<Item> Items { get; }
    public int Count => Items.Count;
    internal NativePlaylistSnapshot(Uri url, string providerId, PageMetadata metadata, IReadOnlyList<Item> items)
    {
        RequestUrl = url; ProviderId = providerId;
        Metadata = new() { IsPlaylist = true, Id = metadata.Id, PlaylistId = metadata.PlaylistId, Extractor = metadata.Extractor,
            Title = metadata.Title, Description = metadata.Description, Uploader = metadata.Uploader, SourcePageUrl = metadata.SourcePageUrl,RevalidateCollectionMembership=metadata.RevalidateCollectionMembership };
        Items = Array.AsReadOnly(items.ToArray());
    }
    public override string ToString() => "Inspected playlist selection (" + Count + " positions).";
}
