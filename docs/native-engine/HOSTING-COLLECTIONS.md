# Consumer delegation and hosting collections

Version 0.136.0 adds numeric public Nowness playlists with paged declared membership positions and explicit Brightcove/Vimeo/YouTube identities, legacy Wistia playlists with fresh selected playback and HTML delegation, and stable Wistia VTT/SRT caption tracks with saving/conversion/embedding/scoped API handling. All 2,173 native and 74 app checks passed, including 59 new checks. Four public adapter workflows produced ten fully decoded source/media files and ten exact ordered packet inventories; both caption workflows retained all 56 saved/embedded cues with exact text and timing. Two additional exact keys gained partial evidence. Private verification remains user-deferred; full replacement and production coverage remain open. See [playlist/caption verification](PLAYLIST-CAPTIONS.md).

Version 0.134.0 adds complete JW and Brightcove playlist discovery, public Wistia channel/playlist-mode inventories, and English Nowness story/series delegation. Three exact frozen keys gain partial evidence: WistiaChannel, nowness, and nowness:series. Full provider, option and replacement parity remains open.

## Complete inventories and selected playback

JW follows publisher-returned links.next with exact issuer/path, consecutive one-based offsets, preserved filters, stable final boundaries and any declared total. Brightcove uses its documented limit/offset API and completes against a stable declared count or a terminal short response. Both bound each response to 8 MiB, total inventory to 32 MiB, paging to 256 requests and membership to the configured limit. Missing pages, changed counts, foreign links, skipped offsets and malformed totals fail without returning a partial inventory.

Playlist responses preserve source positions and materialized offered formats. JW's official example repeats the same video at positions 2 and 8. Repeated JW/Brightcove members retain their distinct positions; each gets an occurrence-bound renewal context. Renewed membership must retain the selected video's occurrence count and hosted identity. Ordinary selection avoids an individual-video metadata refetch. Renewal rereads the complete parent inventory and selects the corresponding occurrence, preserving issuer credential scope.

JW publisher signing creates a fresh parameter-bound grant per page. An opaque supplied grant cannot be reused to invent another page's authorization. Brightcove's policy and scoped JWT stay on the exact Playback API. Public recordings do not establish private signing or account access.

Wistia channel JSON binds its hashed identity, declared series/section counts, explicit episode arrays and access flags. All positions, names, durations and exact media identities are captured without playback URLs. Only chosen available episodes fetch fresh Wistia media responses. Registration-required episodes retain unavailable positions and do not gain anonymous access. Missing or additional sections, incomplete episode counts, unsupported navigation/access parameters, repeated identities and configured-limit overflow fail. Channel mode=playlist is supported; the separate legacy WistiaPlaylist JSON contract is verified in0.136;additional layouts/access variants remain pending.

Nowness uses its first-party API's ordinary X-Nowness-Language: en-us, publisher Origin and referrer. Exactly one English translation must match the requested slug. A published complete video post delegates only its main video to the publisher-declared Brightcove account/player. Related media, previews, coming-soon posts, unknown hosting and external iframe workflows do not become the primary recording. Series preserve their public ordered post inventory and hosted archive identities; only selected stories are resolved. Nested series/story routes also validate series membership. Private/preview parameters cannot be silently ignored. Account tokens stay out of the publisher API.

## Verification

The batch adds 39 protocol and 11 generated-media checks. They exercise complete paging, identity/count/offset/filter boundaries, duplicates and occurrence renewal, fresh selected-only playback, hosted credentials/referrers, archive skips, unavailable members, failures and cancellation. Positive generated recordings retain all 36 video frames and every ordered coded audio/video source packet, complete originals and expected callbacks. Failed discovery and unavailable or changed selected identities preserve previous files and archives.

All **2,114 native checks** (1,548 core plus 566 media), **74 app checks** and **57 focused checks** passed. Release solution build: zero warnings and errors. Five actual public native WPF-adapter workflows produced fourteen independently fully decoded outputs/sources and thirteen nonempty exact ordered main/original/component coded-packet inventories.

| Workflow | Inventory / selected | Main bytes | Original bytes | Source seconds | Video frames | Audio frames |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| nowness-story | single story | 17469345 | 17469307 | 239.915 | 5996 | 11246 |
| nowness-series | 12 / 12 | 11308846 | 11308813 | 173.640 | 4341 | 7477 |
| wistia-channel | 205 / 33 | 8478372 | 8478513 | 241.418 | 5788 | 10397 |
| jw-playlist | 8 / 8 | 4013612 | 3770528 | 110.338 | 2647 | 4752 |
| brightcove-playlist | 4 / 4 | 267922 | 269237 | 8.809 | 264 | 0 |

The Nowness story retained 13,592,275-byte video and 3,883,746-byte audio components; the selected series story retained 8,526,484-byte video and 2,788,842-byte audio components. The Brightcove sample offers silent video; both complete video files decode with 264 frames, and no nonexistent audio inventory is counted. JW follows four actual pages and preserves its repeated video at positions 2/8. Archive repeats perform only bounded metadata requests: story3,series1,Wistia1,JW4,Brightcove2, with no media transfer or callbacks. Private account/signing and forced renewal remain controlled-test evidence, not public private-access claims.

The existing Bilibili cancellation check also now waits, within its original five-second deadline, for a readable complete atomic process-id record. A Windows file-access race exposed this test issue; cancellation still must drain the child and preserve prior publication. The affected check passed after the fix, and the final full regression verifies it again.

## Remaining work

- Broader consumer families and complete route, collection, region, account, live and dynamic embed contracts.
- Additional Nowness playlist/language/platform/season/access workflows and complete mixed-host playback.
- Additional legacy Wistia playlist/registration/incomplete/paged-section contracts,caption variants and more channel layouts.
- Additional signed playlist grants, live/mixed collections, changed membership and durable session/recording recovery.
- Real private-account/signing/remote renewal evidence remains explicitly user-deferred.
- Full option/default/catalog compatibility, native production cutover and packaged acceptance remain unfinished.

Primary references: [JW playlist paging](https://docs.jwplayer.com/platform/reference/get_v2-playlists-playlist-id), [Brightcove Playback API playlists](https://apis.support.brightcove.com/playback/getting-started/overview-playback-api.html), [Brightcove's public playlist example](https://player.support.brightcove.com/playlists/implementing-playlists.html), [Wistia channel embeds](https://docs.wistia.com/docs/channel-embeds), and [Wistia channel playlist mode](https://docs.wistia.com/docs/wistia-and-oembed). Nowness's API path, English header and player declaration were independently observed in its [first-party app bundle](https://www.nowness.com/assets/bundle.app.js) and verified through actual public API and hosted playback. No yt-dlp extraction source or Python fallback was used.
