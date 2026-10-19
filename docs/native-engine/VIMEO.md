# Native Vimeo provider

Version 0.190.0 advances core-platform inventories: native Web/OAuth YouTube search URLs and API-backed Vimeo owned showcases, channels, groups, users, likes and watch-later. Inventories contain metadata rather than captured playback links; selected members revalidate complete membership and fetch fresh ordinary video playback/renewal. Scoped credentials, owner identities, unlisted hashes, paging/count/target progress, archive identities and previous outputs/callbacks are checked. All 3,792 native checks (2,630 core + 1,162 media), 143 app and 80 focused checks pass; Release zero warnings/errors. Seven independently verified generated workflows fully decode 14 files, match 28 source video/audio inventories and 504 pictures, and repeat with zero playback/media/callback activity. Public anonymous search returned an availability message and remains unverified for live playback; real-account evidence remains user-deferred. Seven newly evidenced key subsets bring partial coverage to 114, leaving 1,482 without key-specific evidence. Full replacement remains open. See [core-platform inventories](CORE-PLATFORM-INVENTORIES.md).

Version 0.138.0 adds actual-referrer Vimeo literal player retrieval and clear static JSON adaptive segments, initialization-bound graph renewal, and fresh occurrence/member checks for inspected Nowness/Wistia/JW/Brightcove collections. All 2,229 native and 74 app checks passed, including 56 new checks. Three public inspected native-adapter workflows produced ten fully decoded files and ten exact ordered packet inventories. The selected Nowness YouTube recording stopped at a server playback-authorization challenge and remains unverified. Full parity, production cutover and user-deferred private evidence remain open. See [mixed collection verification](MIXED-COLLECTIONS.md).

Version 0.128.0 adds HLS/DASH signed-resource graph renewal, external-browser OAuth consent with Google PKCE/account refresh and Vimeo scoped code exchange, authorized YouTube API playlists and Vimeo adaptive API/captions. All 1,976 native and 74 app checks passed; 52 new checks verify the batch. Three public native-adapter workflows produced nine fully decoded files with six exact ordered source-packet inventories and network-free archives. Google API login still requires separate cookies for private playback. Real account/grant verification remains user-deferred; broader provider/layout/default/catalog and production coverage remain open. See [adaptive renewal and login verification](ADAPTIVE-ACCOUNT-RENEWAL.md).

Version 0.126.0 adds YouTube cookie-selected account/private Web playback, Vimeo video-password/API file access, progressive/caption signed-link renewal and matching SABR context renewal. All 1,924 native and 74 app checks passed; final account review passed all 1,406 core and 37 batch checks again. Public YouTube playback/archive behavior passed with three fully decoded media/source files and exact coded packet retention. Real private-account verification remains user-deferred; interactive login/OAuth grants, adaptive resource-graph renewal and wider provider parity remain pending. See [account and renewal verification](ACCOUNT-RENEWAL.md).

The C# provider resolves numeric video URLs, embedded player URLs, supported channel/group video aliases, and album/showcase video links. Unlisted hashes in paths or `h` query parameters are retained in the player configuration request, including encoded parameter names. Leading-zero IDs normalize to the same underlying video. Collection URLs are not mistaken for individual videos.

## Player metadata and embeds

Configuration parsing reads video identity, title, owner, duration, thumbnail, exposed progressive files, selected HLS/DASH manifests, and text tracks. Offered progressive frame rates, sizes and MIME codec parameters are retained. Duplicate quality labels receive distinct format IDs; fractional dimensions are not turned into integer facts.

The selected CDN's primary, AVC and fallback URLs are retained without changing signed parameters. Duplicate addresses collapse. A requested H.264 preference prioritizes an offered AVC variant, while limited fallback variants remain lower-priority choices. This does not rotate CDNs or retry through a different access route after denial.

Known protected CENC/CBCS paths, explicit protection descriptors and CENC HLS groups are reported with unavailable format status. Their addresses do not become downloadable candidates. Clear alternatives remain eligible. A protected-only configuration fails before requesting media. Unavailable player views, unexpected configuration redirects, identity mismatches, invalid URLs and size/count limits fail explicitly.

Generic HTML now recognizes `data-vimeo-id` and `data-vimeo-url` embeds in addition to iframes. Conflicting IDs are rejected, and unlisted access values are retained. Caller/embedding referrers survive playback setup. The native WPF adapter exposes ready formats as choices, keeps authored/automatic caption language groups, and reports an actual unavailability reason when no format is ready.

## Supplied video file links

Already supplied HTTPS file links support the documented `progressive_redirect/playback/.../container/...`, rendition file, `play/c/.../hls.m3u8`, `play/c/.../dash.mpd` and legacy numeric `external/...` shapes. The original URL and signed query are sent unchanged. A header-only discovery request validates the redirect and media type, then the native downloader transfers the resolved resource with the same HTTP session. HTML/JSON responses, expired links and unknown containers fail explicitly.

These links retain the underlying Vimeo video ID for archives. They do not require a second player-configuration lookup. Unknown dimensions/codecs on a bare link remain unknown rather than being inferred from its path. A supplied API token now obtains matching clear progressive links through the identified API video route. Direct supplied-file-link routes still do not regenerate links through the API. See ACCOUNT-RENEWAL.md.

## Verification

Eleven deterministic provider checks cover identity/hash aliases, current player states, format facts, protected/clear alternatives, offered manifests, declarative embeds, literal signed links, media-header validation, access failure and transfer behavior. Two generated-media checks exercise clear configuration streams and supplied file links. They retain all six generated video frames and the complete source audio frame count, fully decode, and preserve the tested caption text and title metadata.

On October 8, 2026, [Vimeo's official SDK demo](https://github.com/vimeo/player.js) supplied protected sources for `76979871?h=8272103f6e`. Native inspection retained its 62-second metadata and four caption tracks while reporting six protected formats and zero ready formats. An instrumented download returned failure without any media request or published file. The two operations made only their two player-configuration requests.

The other official example `59777392` returned an unavailable player view. Additional public sample configurations returned HTTP 403; a public watch page returned a verification page. These observations do not establish successful live Vimeo playback. No clear live Vimeo media transfer has yet been verified.

## Remaining work

Real external/private playback, remote consent and sustained renewal proof; additional account/API/domain/collection/file-link and caption defaults/layouts; advanced graph addressing/redirect/changed-track contracts; credential persistence/re-consent UX, richer metadata/chapters and complete site parity remain pending. The production application still uses its existing backend while replacement coverage is completed.

Primary references: [Vimeo Player SDK and embed attributes](https://github.com/vimeo/player.js), [official player demo](https://player.vimeo.com/api/demo), and [Vimeo video file links](https://developer.vimeo.com/api/files/video-links). Live response fields were observed directly; signed URLs and session data remain outside repository fixtures.
