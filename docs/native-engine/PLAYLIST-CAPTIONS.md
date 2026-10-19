# Nowness playlists, legacy Wistia playlists and captions

Version 0.138.0 adds actual-referrer Vimeo literal player retrieval and clear static JSON adaptive segments, initialization-bound graph renewal, and fresh occurrence/member checks for inspected Nowness/Wistia/JW/Brightcove collections. All 2,229 native and 74 app checks passed, including 56 new checks. Three public inspected native-adapter workflows produced ten fully decoded files and ten exact ordered packet inventories. The selected Nowness YouTube recording stopped at a server playback-authorization challenge and remains unverified. Full parity, production cutover and user-deferred private evidence remain open. See [mixed collection verification](MIXED-COLLECTIONS.md).

Version 0.136.0 adds partial evidence for the exact frozen nowness:playlist and WistiaPlaylist keys and extends Wistia caption handling. Full provider/option/replacement parity remains open.

## Playlist identity and selection

Nowness playlist URLs use a numeric ID and optional member-provided slug. The declared detail must match the requested ID/English slug and explicitly be public. Scoped hosted media credentials cannot grant a playlist inventory. The member API uses the case-sensitive PlaylistId parameter; its lowercase spelling returns unrelated general posts and is rejected by count/identity validation.

Published members are fetched in bounded pages of eight, with matching page/limit/total/page-count metadata, an 8 MiB response bound, 32 MiB aggregate bound, 256-request cap and configured item limit. Their declared zero-based membership positions reconstruct the playlist order independently of the endpoint's publication-date sorting. Missing/repeated positions or post IDs, changed counts, private state, redirects and incomplete pages fail without a partial inventory.

Each main video declares its hosting provider. Brightcove, Vimeo and YouTube identities map to their exact native player/archive identities; a numeric Vimeo ID cannot silently become Brightcove. Unknown or missing provider declarations fail. Related media and previews do not grant primary playback. Only selected available stories fetch fresh child media. Inspected selections retain hosted archive identities and source positions.

Legacy Wistia uses the first-party JSON endpoint for its declared playlist hash. Complete section arrays retain section/member IDs, names, durations and validated embedded config identities. Stale embedded playback URLs are not transferred: chosen members resolve fresh Wistia media. Missing configs and declared protected/registration members retain unavailable positions; repeated/mismatched identities, unsupported kinds and incomplete/oversized inventories fail. Media credentials do not grant collection access.

Generic HTML now distinguishes a legacy playlist class/literal Wistia.playlist declaration from a single-media async tag. Current Wistia playlist web components delegate channel playlist mode. Computed JavaScript remains unexecuted.

## Captions

Wistia's declared caption/transcript language inventory maps to stable media/language/format identities. Transcript prose does not become timed subtitles. Public VTT/SRT endpoints retain the publisher language code while declared BCP47/known ISO language mappings support native language selection. Duplicate/invalid/conflicting language declarations and insecure or ambiguous resources fail. Declared automatic tracks respect automatic-only selection.

Explicitly issued modern Wistia API caption links must match the exact media ID, language and format. The supplied media bearer remains on the exact API. Requested timed caption bodies are fetched with bounded job-owned storage and redirects disabled; unrequested captions do not transfer. Media/CDN requests receive no API bearer. Public password/protected responses never fall back to exposed assets. Real private access remains user-deferred.

Existing native caption saving, selection, conversion, embedding and renewal now consume these stable tracks. Caption-only jobs transfer no media. Changed/revoked captions, HTML, cancellation and wrong scope protect prior files and archives. Signed caption links can renew through the existing issuer-owned media lease without losing the selected identity.

## Verification

The batch adds 44 protocol and 15 generated-media checks. Positive generated recordings retain all 36 video frames and every ordered audio/video coded packet, full originals and callback/archive identity. Tests cover both collection metadata and selected/inspected downloads, mixed hosting, unavailable items, generic delegation, caption formats/provenance, exact API credential scope, renewal, conversion/embedding and protected failures.

All **2,173 native checks** (1,592 core plus 581 media), **74 app checks** and **66 focused checks** passed. Release build: zero warnings and errors. Four current public native WPF-adapter workflows produced ten fully decoded output/source files and ten nonempty exact ordered main/original/component coded-packet inventories.

| Workflow | Inventory / selected | Main bytes | Original bytes | Source seconds | Video frames | Audio frames |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| nowness-paged | 21 / 3 | 9732945 | 9732907 | 133.793 | 3343 | 5762 |
| wistia-legacy | 4 / 2 | 6577502 | 6558040 | 73.733 | 2212 | 3172 |
| wistia-caption-vtt | captioned media | 4983541 | 4979139 | 154.752 | 3710 | 6665 |
| wistia-caption-srt | captioned media | 4983541 | 4979139 | 154.752 | 3710 | 6665 |

The Nowness source retained 7,566,470-byte video and 2,171,294-byte audio components. Its complete 21-member inventory includes declared Brightcove, Vimeo and YouTube entries; this public download verifies the selected Brightcove recording, while other platform delegation identities are independently protocol-tested. The old official Wistia example retains four complete source positions and fetches only the chosen fresh media. Both Wistia caption workflows save and embed all 56 ordered cue payloads with **zero measured timing error**; the final cue ends at 146.357 seconds, inside the full video. Saved VTT is 4,090 bytes and converted SRT is 4,243 bytes. Public archive repeats make only 4/1/0/0metadata requests, zero media/caption requests or callbacks. Private modern caption/API/renewal cases remain controlled evidence; real private verification is deferred.

## Remaining work

- Complete mixed-host Nowness playback and other locale/private/preview/duplicate/unavailable-member contracts.
- More legacy Wistia layouts, gates, duplicates, scoped collection grants and current channel variants.
- Additional caption/API/automatic/live/alternate-audio layouts and sustained remote renewal.
- Real permitted account/private evidence remains explicitly user-deferred.
- Full option/default/catalog compatibility and native production/package acceptance remain unfinished.

Primary references: [Nowness's first-party app bundle](https://www.nowness.com/assets/bundle.app.js), [Nowness public playlist behavior](https://www.nowness.com/about/faq), [Wistia's original official legacy embed documentation](https://github.com/wistia/wistia-open-docs/blob/master/_posts/construct-an-embed-code.md), [Wistia's first-party legacy client](https://fast.wistia.com/assets/external/playlist-v1.js), [modern caption API formats](https://docs.wistia.com/reference/get_medias-mediahashedid-captions-languagecode), and [Wistia caption/player example](https://wistia.com/blog/video-captions). Contracts and tests are independently authored from first-party declarations and owned responses.
