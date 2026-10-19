# Native YouTube channel uploads

The C# YouTube provider now reads public channel upload tabs directly from the site's page JSON and browse responses. It does not execute page scripts or use an external extraction library.

## Implemented scope

- HTTPS-normalized handle, `/channel/UC...`, `/user/...` and `/c/...` aliases on the standard, www and mobile YouTube hosts. Unicode handles are covered by fixtures. IDs, path lengths, decoded path segments and ambiguous queries are validated.
- Explicit `/videos`, `/shorts` and `/streams` tabs, with server-confirmed channel metadata and selected-tab checks. Requests for absent tabs fail instead of silently downloading another feed. Other tabs, sorting/search query options and unsupported URL shapes remain explicit limitations.
- Bare channel URLs enumerate the offered Videos, Shorts and Live tabs in that order. Videos repeated across tabs or pagination appear once. Original enumeration positions remain stable for selection, filenames, reverse/random order and failure reporting; this combined order is not a chronological interleaving of tabs.
- Current video lockup cards, current Shorts cards including wrapped thumbnails, and standalone legacy video/grid/reel cards. Titles, declared durations, artwork and unavailable items are retained. Shorts cards without a duration leave it unknown. Nested menus, recommendations and other tabs are excluded.
- Bounded, complete pagination using the client version supplied by the page. Continuations must append to the exact selected feed target. Repeated tokens, feeds without progress, mismatched channels, excessive items/pages, missing arrays, unknown feed items and inaccessible/consent pages and availability messages fail explicitly. The configured entry limit never turns an incomplete inventory into a successful truncated list.
- Existing per-video extraction, playlist selection, output processing, archive identities, cancellation and completion callbacks. Channel discovery never substitutes the channel identity for an individual video's archive key. An explicitly empty supported tab can be inspected as an empty playlist.

The engine's usual inventory limit is 1,000 entries; explicit callers may raise it to 10,000. A tab also has a 1,001-page safety bound. Unsupported rich shelves, collection cards and older nested layouts require additional parsing rather than recursive guessing.

## Verification on October 8, 2026

Native inspection of [Blender Studio](https://www.youtube.com/@BlenderStudio) enumerated all offered upload tabs to their terminal continuation:

| Tab | Entries | Requests for that tab | Entries with artwork | Entries with declared duration |
| --- | ---: | ---: | ---: | ---: |
| Videos | 136 | 5 | 136 | 136 |
| Shorts | 88 | 2 | 88 | 0 |
| Live | 61 | 3 | 61 | 61 |

The combined channel operation made one additional root-page request and returned 285 unique videos with contiguous positions. Independent enumeration of the three explicit tabs produced the exact same ordered union. After adapting the observed nested Shorts thumbnail wrapper, all 285 entries retained artwork. This channel is distinct from the previously verified Blender uploads playlist containing 1,584 items.

Native inspection of [jawed's Videos tab](https://www.youtube.com/@jawed/videos) found `jNQXAC9IVRw` at position 1. A channel download selected that position and completed through the existing native IOS/SABR path, producing `001 - Me at the zoo.mp4`: 744,478 bytes, H.264 320x240, 284 decoded video frames, 821 AAC frames, and 19.063583 seconds. Full ffmpeg decoding passed. The operation emitted one completion callback and recorded `youtube jNQXAC9IVRw` in its archive. Its metadata options differ from earlier samples, so its file size differs without a change in media frame counts.

Nine deterministic checks exercise current/legacy cards, nested-menu exclusion, thumbnail/duration facts, continuation target/context validation, page-boundary deduplication, all-tab aggregation, aliases and Unicode paths, unsupported query semantics, channel identity changes, consent refusal, absent/malformed/empty feeds and access messages, bounded/repeated pagination, cancellation, selected-only media transfer and archived retries.

## Remaining work

Channel search/sorting, playlists/collection tabs, featured shelves, community content, additional layouts, account/restricted channel workflows, channel-name resolution outside the supported forms, unknown future pagination shapes, broader live verification and complete `youtube:tab` / `youtube:user` parity remain pending. Enumerating the Live tab does not implement active livestream recording. Sustained YouTube playback authorization beyond the verified short-video workflow also remains unfinished.

Primary references: [YouTube channel URL forms](https://support.google.com/youtube/answer/6180214), [YouTube handles](https://support.google.com/youtube/answer/11585688), and the public channel pages linked above. Current card and pagination fields were observed directly from those public responses. Repository fixtures use invented identifiers and continuation values, with no real session tokens or player URLs.
