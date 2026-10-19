# Native YouTube search and Vimeo collection inventories — 0.190.0

This batch advances the core-platform backlog after the HDR work. It adds a native YouTube search URL inventory and six Vimeo collection families using the existing scoped authentication and playback paths. Seven previously unevidenced frozen extractor rows now have implemented-subset evidence. Full parity and production cutover remain open.

## Supported routes

| Frozen key | Implemented subset |
| --- | --- |
| `youtube:search_url` | Bounded `youtube.com`, `www.youtube.com` and mobile Web `/results?search_query=...` inventories, optional bounded Web `sp` filter, and OAuth Data API video search for plain queries |
| `vimeo:album` | Account-owned `/showcase/{id}` and `/album/{id}` through `/me/albums/{id}`; explicit owner-qualified `https://api.vimeo.com/users/{user}/albums/{id}` inventories |
| `vimeo:channel` | Channel numeric/custom-name roots and `/videos` aliases through the selected API channel connection |
| `vimeo:group` | Group roots and `/videos` aliases through the selected API group connection |
| `vimeo:user` | Numeric `user{id}` and supported bounded vanity user roots, with optional `/videos` |
| `vimeo:likes` | Supported user `/likes` inventories through their declared API connection |
| `vimeo:watchlater` | `/watchlater` for the selected account and supported user `/watchlater` routes |

Vimeo inventory requests require a supplied API token or Vimeo OAuth session. Web showcase IDs use the authorized account's owned-showcase API, rather than guessing an owner or extracting an unrelated embedded video. An owner-qualified API URL supplies an explicit alternative. Anonymous public showcase owner discovery and other Vimeo collection routes remain pending.

## Inventory and selection behavior

Inventories contain member identities, source positions, titles, duration/artwork facts and normal video URLs. They do not retain the response's playback links or embed resolved media snapshots. Only selected members obtain fresh playback through the normal YouTube/Vimeo video provider. Archive records use the underlying video identity, rather than a synthetic collection identity. Vimeo unlisted hashes survive into the selected member's API/player context.

Both new inventory families participate in the existing inspected-playlist membership refresh. Reordered members retain their inspected selection identity and original output index. A removed member, changed collection identity or changed occurrence set fails before selected playback. Previous outputs, archives and callbacks remain protected. Archived selected members still allow required parent membership checks, but make zero playback/media requests and create no new callbacks.

The shared channel video-card reader is reused only after the search parser selects primary-result objects. Search reads explicit item sections, legacy video cards and supported video lockups. Known channel/playlist cards, advertisements, shelves and secondary recommendations are excluded. It does not recursively collect video-looking objects from menus or sidebars. Unknown primary layouts and availability messages are refused rather than reported as a complete empty/partial inventory. An explicit empty primary array remains an empty inventory.

Web pagination uses the page-declared client version and the existing selected account context. Continuations must target the selected primary feed exactly once. Repeated tokens, excessive counts, missing client context and nonprogressing pages fail explicitly. Video duplicates are collapsed in first-seen order. Request limits are safety bounds; inventories are not silently truncated to them.

With a supplied Google OAuth grant, plain-query search uses the documented Data API `search` resource with `type=video`. It validates resource kinds and identifiers and follows bounded page tokens, including the existing same-account token renewal. Opaque Web filters are refused on this route before requests. API sign-in does not imply private Web playback access; the existing separate cookie requirement remains.

Vimeo metadata must bind the requested numeric identity or supported alias. Self-scoped routes additionally bind the returned owner to the API account identity. The advertised connection must belong to that exact metadata resource. Every page must retain its declared total, page number and page size, and explicitly advertise completion. Duplicate video IDs, short inventories, changed counts, foreign connections and altered/repeated page queries fail before complete inventory publication. Pagination links are validated and reconstructed within the selected connection; they are not used as arbitrary authenticated request targets.

## Authentication boundaries

Search reuses the job-owned YouTube cookie/account state and its origin-bound signing. OAuth search and Vimeo collection resources extend the existing issuer API allowlist only to supported read routes. Supplied Vimeo tokens stay on controlled `api.vimeo.com` requests; OAuth tokens stay on their declared issuer resources. Selected watch pages and media receive no inventory bearer token. Credentials do not enter inventory/snapshot JSON.

Self-scoped Vimeo metadata is checked against `/me` or the already identified OAuth account. Redirected account/resource responses, spoofed numeric identities, account changes and unauthorized resources remain failures. The existing HTTP/session owners retain cancellation and disposal behavior. No saved account, cookie profile, netrc or user settings file is accessed for verification.

## Verification

All **3,792 native checks** (2,630 core + 1,162 optional media), **143 app checks** and **80 focused checks** pass on the final candidate. Release has zero warnings/errors. The 63 new core contracts and ten generated-media workflows pass in the full run. Seven retained workflows independently verify 14 complete decodes, 28 exact coded video/audio inventories and 504 video pictures. Default policy/recovery compatibility with verified 0.188.0 passes.

New coverage comprises 63 core contracts and ten optional-media workflows. The focused invocation passes 80 checks, including the seven shared foundation checks. It exercises URL/query boundaries, legacy/current cards, section targeting, duplicate suppression, limits, empty inventories, cancellation, signed cookie search, OAuth refresh, Vimeo owner/alias binding, self-account redirects, unlisted and unavailable members, pagination attacks and metadata-only discovery.

Seven retained successful workflows cover Web/OAuth search, owned showcases, OAuth channels, likes, unlisted members and selected playback-link renewal. Their 14 published main/original files fully decode. An independent FFmpeg/ffprobe verifier compares all 28 ordered coded video/audio inventories with their generated source and verifies 504 video pictures. Archive repeats fetch no playback/media and create no callbacks. Negative workflows verify removed members and changed collection identities preserve prior output/archive files without fetching media.

Default policy JSON and video/audio recovery keys match the preserved verified 0.188.0 binary. The app regression suite remains at 143 checks; no native WPF extension is added, so the extension count stays eighteen. The broad native suite runs alone in an owned isolated temporary recovery registry. The earlier synthetic YouTube playback fixture was corrected to declare its actual source host, three-second duration and byte size; it is retained as a diagnostic rather than counted as a passed run.

On October 16, 2026, a public anonymous native search for `jNQXAC9IVRw` returned a search availability message and was refused. That observation establishes neither a complete live search inventory nor live playback. Real-account verification remains explicitly user-deferred. Deterministic scope is recorded separately from missing public/private acceptance evidence.

## Remaining full-replacement work

Search pseudo-addresses such as `ytsearch:`, music search, additional layouts/resource types, account feeds and broader filter semantics remain open. Vimeo anonymous public showcase discovery, further owner/alias layouts, On Demand/event/portfolio/review workflows and real account/token/session evidence remain open. The seven key rows are partial implementations, not complete parity.

The frozen scope remains 1,731 extractor keys, 126 classic app options and 276 captured long-option names. Implemented-subset rows increase from 107 to 114; rows without key-specific evidence decrease from 1,489 to 1,482. The 135 broken decisions, 62 provider registrations, 13 global unmapped settings, five scoped authentication settings and 113 typed long-option handlers remain unchanged. Full provider/option/default/account/transport/processing/packaged acceptance is still required before switching production startup from YtDlpEngine.

Evidence: `work/core-inventories-batch`, `work/core-inventories-probe`, `work/core-inventories-verify`, `work/core-inventories-compatibility`. All existing uncommitted work is preserved through before/after hashes.

Primary references: [YouTube Data API search](https://developers.google.com/youtube/data/reference/rest/v3/search/list), [Vimeo's official OpenAPI specification](https://github.com/vimeo/openapi), [Vimeo common API formats](https://developer.vimeo.com/api/common-formats), and [Vimeo authentication](https://developer.vimeo.com/api/authentication). Web parsing uses the project's native JSON model contracts; additional remote layouts remain unverified.
