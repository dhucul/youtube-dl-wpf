# Native Pinterest board collections

Engine 0.73.0 adds independently implemented C# public-board discovery, complete-inventory checks and selected-pin downloads. Full PinterestCollection parity and account access remain unfinished. Stable in-memory inspection/download selection was subsequently added in 0.74; see PLAYLIST-SNAPSHOTS.md. The production WPF backend still awaits the full replacement gate.

## Board discovery and pagination

The provider reads the public page's bounded `__PWS_INITIAL_PROPS__` JSON without executing scripts. Resource options identify the requested owner/slug, and board data must confirm the canonical URL, native/global IDs, owner, public visibility, declared pin count and order revision. Unknown feed filters/options, unfinished state and currently unverified section layouts fail explicitly.

The board's offered BoardFeedResource cursor drives read-only pagination. Its handler/response headers are replayed within the owned public session. Board ID and requested feed options must remain unchanged in the response. Repeated pins/cursors, invalid rows, empty continuation pages, oversized inventories and changed board revisions/counts fail. A paginated result rechecks the same board before returning its inventory. No failed pagination result is presented as a complete playlist.

Known related-interest UI modules are excluded from pin positions. Pins retain their supplied order and logical source positions; hidden/deleted entries retain unavailable positions. Grid data can omit external player details, so an omitted video object is not treated as proof of a still image. Only selected pins are resolved freshly through the individual provider. Known hosted videos supply expected archive identities, allowing a recorded selection to skip before fresh pin/media requests. Ordinary linked players retain their original provider identities.

Public/regional/tracking and short aliases retain board identity. Official `embedBoard` anchor widgets delegate to the collection provider; ordinary board links do not become invented media. Profile tabs, board sections, additional query layouts and nested board composition remain pending.

Protocol evidence comes from current public board pages and their offered read-only resource responses. Pinterest's [board documentation](https://help.pinterest.com/en/article/boards), [section documentation](https://help.pinterest.com/en/article/create-a-board-section) and [website widgets](https://help.pinterest.com/en/business/article/build-a-website-widget) describe the user-facing structures. The observed resource protocol is not presented as a separately documented API contract.

## Live verification on October 9, 2026

The public [videos board](https://www.pinterest.com/gs2134189/videos/) confirmed board `626281960621547297` and all 21 declared pins. Eight cached rows exposed hosted video; other rows require fresh selected-pin discovery to distinguish images, linked players and unsupported stories.

Pinterest changed the returned order between anonymous requests. The verification harness therefore retained each automatically fetched complete inventory while selecting known pin `626281891952504847` by identity. That live snapshot assigned it position 20, selected-job number 1, count 21. Its individual playback metadata was still fetched freshly. Production positional downloads currently use the fresh listing for that job; stable selection from an earlier inspection remains required work.

| Offered path | Published files | Strict full decoding | Publication and repeat |
| --- | --- | --- | --- |
| `v720P`, no height ceiling | 1,503,547-byte processed MP4; 1,503,402-byte original MP4 | H.264 504×760, 180 video frames; AAC 127 audio frames; 6.000000 s | Position 20/number 1/count 21, thumbnail/info JSON, two callbacks; zero repeat callbacks |
| `vHLSV4`, maximum height 360 | 283,325-byte processed MP4; 283,254-byte original MP4 | H.264 234×354, 180 video frames; silent; 6.000000 s | Same position metadata, thumbnail/info JSON, two callbacks; zero repeat callbacks |

All four saved media files fully decoded. This legacy HLS master advertises only AVC video codecs, with no audio rendition; it is not claimed equivalent to the audio-bearing MP4. Combining the offered progressive audio with silent adaptive video remains a compatibility gap. No Python, yt-dlp, personal cookies or manually supplied capture was used.

Other live inventories ended before their declared totals: 24/137, 17/20 and 22/27. The 22/27 case supplied a cursor, but its public pagination response returned zero pins and an end marker. The native provider refuses these as incomplete, preserving the unresolved access/stale-count boundary rather than silently returning fewer entries. Public API calls with unsupported/terminal request state also returned HTTP 403 during investigation.

Story records can expose different partial page/block projections without shared child IDs. Their presence now produces the explicit unsupported-composition boundary, rather than attempting to merge unbound blocks or reporting a misleading metadata conflict. Actual story/carousel composition remains pending.

Story video assets were subsequently added in 0.75.0, with identity-checked projections and live MP4/HLS verification. See [Pinterest stories](PINTEREST-STORIES.md). Full page composition, images/carousels and a multi-video story nested inside a board still need work.

Thirteen deterministic checks cover order/UI modules, offered pagination/rechecks, total gaps, owner/native/global/resource identities, private/pending/section boundaries, repeated pins/cursors, changed revisions/scopes, limits, aliases/widgets, unavailable positions, archive pre-skips and empty-versus-refused inventories. Two generated-media checks verify selected-only MP4/HLS publication, complete frames/audio, captions, retained originals, source-position metadata and archive repeats.

Stable in-memory snapshot selection across inspection/download and native queue handoff were added in 0.74.0; see [stable playlist selections](PLAYLIST-SNAPSHOTS.md) for normal-engine live verification. This replaces the special verification wrapper described above. Restart persistence, observed count/access gaps, larger authenticated live pagination, profiles/sections, story/carousel composition, silent-HLS audio parity, more layouts/metadata and complete Pinterest/PinterestCollection behavior remain pending. All-site/options coverage, removal of the old runtime, packaged verification and WPF cutover remain required.
