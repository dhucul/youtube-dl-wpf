# Native Panopto folders and playlists

Engine 0.68.0 adds Panopto folder/public-library inventories and ordered playlists to the independent C# engine. Public collection inspection and selected complete MP4 downloads are verified. This does not establish full Panopto or all-site parity; the production WPF backend remains unchanged.

## Folder and library inventories

`/Panopto/Pages/Sessions/List.aspx` URLs may identify a folder through the browser fragment, including `#folderID=%22<GUID>%22`. Without a folder, the inventory covers the accessible library on the supplied server. Supported URL state includes search text, sort column/direction, presentation view and a bounded page size. Unsupported filters, repeated parameters, invalid identities and nonzero starting pages fail explicitly. Recording positions are selected through the existing playlist settings.

The current site's player/list scripts are the protocol reference. The provider uses bounded `GetFolderInfo` and `GetSessions` requests against that server's `Data.svc`. It checks the folder identity, each listed recording's folder/server/viewer identity, stable advertised total, page sizes, duplicate identities and completion. It reads the full bounded inventory before selected playback. A short/repeated/changing response cannot become a successful truncated collection. The configured inventory limit is retained.

When the folder declares a custom recording order and the URL does not override it, the declared `OrderSort` values determine the final sequence. Search/sort state is sent through typed requests. Source positions are assigned after the complete final ordering; selected autonumbers retain their existing separate meaning.

## Ordered playlists

Viewer/embed URLs with `pid=<GUID>` use the playlist metadata and its bound session-list ID. The playlist ID, repository key, session-list ID and recording viewer addresses must agree. Legacy expanded-item responses preserve order, repeated recordings and unavailable positions. An unavailable entry remains in the inventory rather than shifting later source indexes.

The legacy player requests at most 500 expanded items. The native engine requests a sentinel above a smaller configured limit and does not interpret a full-cap response as complete. At the legacy boundary it uses the documented v1 playlist-session endpoint with `sortField=Order`, ascending order and successive page numbers until an empty page. The original playlist/list identity and list update revision are rechecked after pagination. Repeated pages, changed revisions and limit excesses fail before playback. The paged path is covered by deterministic protocol tests, including 501 entries, but authenticated live verification remains pending. The public tutorial server returned HTTP 401 for that endpoint; the engine reports missing access rather than publishing the first 500 items.

When a playlist link includes a displayed recording ID and `DownloadPlaylist` is false, only that recording is resolved. A playlist link without a displayed recording, or a folder URL, uses the existing first-entry selection behavior when playlists are disabled.

Archive keys remain recording-specific: `panopto:delivery <server>/<recording-GUID>`. Listing entries carry those expected identities, so archived selected recordings can skip watch-page and playback requests. Unselected entries are never resolved for media transfer. One owned discovery session is shared through the job; signed CDN playback retains the existing anonymous-session policy.

## Chapter gap preservation

The live folder test exposed a recording whose first named chapter begins at 0.57 seconds. QuickTime's consecutive chapter samples had previously moved it to zero. Movie chapter output now represents unnamed leading and internal gaps explicitly, preserving the named intervals. Metadata still lists the provider's named chapters; the embedded movie may include additional empty-title gap entries. This normalization applies to movie containers, leaving other container behavior unchanged. Chapter titles/times and valid references are checked before publication.

A generated regression checks both a leading gap and an internal gap, then preserves the same ranges through later metadata processing and MOV remuxing. Existing packet-hash, frame-count, subtitle and decoding checks still apply.

## Live verification on October 9, 2026

* The public [What's New folder](https://demo.hosted.panopto.com/Panopto/Pages/Sessions/List.aspx#folderID=%22d6c8ecc2-ad9b-4426-8e74-affc01828f45%22) returned six recordings. Page size two produced three complete pages with the same total and order.
* The public [Featured Video Tutorials playlist](https://howtovideos.hosted.panopto.com/Panopto/Pages/Viewer.aspx?pid=f3b39fcf-882f-4849-93d6-a9f401236d36) returned nineteen ordered recordings.

| Selected source position | Saved files | Verified media | Captions and chapters |
| --- | --- | --- | --- |
| Folder position 4; selected autonumber 1 | 5,699,223-byte processed MP4 and 5,685,322-byte original | H.264 1280×720, 971 video and 1,665 AAC frames; 38.736 s processed | Five caption cues; unnamed 0–0.57 s gap, then the two named intervals at 0.57–9.12 and 9.12–38.696 s |
| Playlist position 3; selected autonumber 1 | 22,721,604-byte processed MP4 and 22,676,621-byte original | H.264 1920×1080, 2,707 video and 3,882 AAC frames; 90.203322 s | Twenty-one caption cues; this recording supplies no chapter inventory |

All four saved files fully decoded. Both jobs saved captions, a thumbnail and info JSON, emitted two media callbacks, and recorded the selected recording's server-scoped archive key. Repeating each job emitted zero new media callbacks. Filename and info-JSON positions/autonumbers matched selection. No Python, yt-dlp, personal account cookies or manually supplied playback capture was used.

Eleven deterministic checks cover pagination, specified/explicit order, changed/repeated/cross-folder responses, search/library state, intentional playlist duplicates and unavailable positions, no-playlist routing, resource binding, authenticated paging/revision checks, selected-only resolution and archive skips, and input validation. Two generated-media checks cover selected folder/playlist downloads, original bytes, full decoding, filename/info metadata and repeat skips. The movie-gap regression is additional shared media coverage.

Remaining work includes authenticated live v1 pagination, additional URL filters and layouts, nested playlists/reference copies, invitations/SSO/account coverage, richer folder/playlist metadata and alternate/on-premises APIs. The HLS source-timestamp failures recorded in [PANOPTO.md](PANOPTO.md) remain unresolved. The captured Panopto extractor keys have working tested subsets; they are not claimed as fully replaced. All remaining sites/options, dependency removal, packaging and final WPF cutover remain required.
