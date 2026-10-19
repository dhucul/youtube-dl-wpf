# Native Pinterest public video pins

Engine 0.72.0 adds independently implemented C# extraction of public hosted-video pins and supplied external player embeds. MP4 and HLS playback with automatic captions are verified live. The production app still uses its original backend pending full replacement coverage and packaging.

## Current public page protocol

The public page streams literal `__PWS_RELAY_REGISTER_COMPLETED_REQUEST__` declarations. Each contains a URL-encoded JSON query identity and a separate JSON response. The C# reader parses those two arguments without executing JavaScript and combines consistent partial records for the requested pin. It does not use the older PinResource API, which returned HTTP 403 during this verification.

The query pin ID, native entity ID, typed global ID and returned `Pin` record must agree. Conflicting declarations fail. Non-final responses, unavailable/hidden pins, malformed JSON, extra executable arguments and excessive inventories remain explicit failures. Templates/comments do not become public response declarations. No Python, yt-dlp code or JavaScript runtime is required for this provider.

Hosted playback comes from the declared `videos` object. The observed pin's separate `isVideo` flag was false even though it offered complete hosted video, so that flag does not override the verified media record. Offered MP4/HLS formats must share one supplied asset, with matching resource shards/type and complete duration. HTTPS video origins, literal encoded queries and progressive dimensions are preserved. HLS dimensions are selected from its manifest, allowing a lower variant than the original pin dimensions.

Native public, regional, descriptive-slug, embedded-player and short-link routing retain pin identity. Short links must resolve to an actual supported pin; unresolved links and another-ID redirects fail. Official `data-pin-do="embedPin"` anchor widgets and pin iframes delegate through generic HTML discovery. Ordinary pin links and board widgets do not become invented media.

Title/description/creator, supplied posters and video-scoped archives are mapped. Anonymous discovery/playback avoids sending unrelated account authorization. Video posters and caption files are bound to the verified asset; independently supplied pin cover images are bound to their declared post record and image origin. Duplicate caption-language resources are collapsed only when their addresses agree. Pinterest's automatic-caption provenance is retained, with saved/embedded captions using their original timing.

Pins with an offered external player delegate to that native provider and its original video/archive identity, preserving the actual pin-page referrer. An image or unsupported link-only pin retains metadata and a warning instead of pretending to expose video. Multi-page stories, carousels and boards currently fail explicitly until their complete inventories/composition are implemented.

Primary references include the observed public page's streamed query/response declarations and offered media, Pinterest's [pin types](https://help.pinterest.com/en/article/types-of-pins-on-pinterest), [caption behavior](https://help.pinterest.com/en-gb/article/interact-with-pins), and [website widgets](https://help.pinterest.com/en/business/article/build-a-website-widget). Public PinResource/Relay layouts are observed website protocols, not a claim of a separately documented API contract.

## Live verification on October 9, 2026

The public [pin 871728071625487182](https://www.pinterest.com/pin/871728071625487182/) is attributed by its metadata to NextHorizonOrbit. A mention of NASA in its text is not treated as proof of NASA ownership. Native inspection returned three offered formats, 5.1-second declared duration and one automatic English caption track. The [embed alias](https://assets.pinterest.com/ext/embed.html?id=871728071625487182) returned the same identity/inventory through canonical native routing.

| Path | Saved media | Strict full decoding | Captions and repeat |
| --- | --- | --- | --- |
| `v720P`, maximum 720 | 583,921-byte processed MP4; 583,016-byte original MP4 | H.264 720×720, 152 video frames; AAC 112 audio frames; 5.179002 s | 201-byte VTT, four matching saved/embedded cues; two callbacks, zero archive-repeat callbacks |
| `vHLSV4`, maximum 360 | 275,075-byte processed MP4; 274,170-byte merged original; 228,572-byte video component; 44,433-byte audio component | H.264 360×360, 152 video frames; AAC 112 audio frames; 5.179002 s processed | Same caption text/timing and cue count; four callbacks, zero archive-repeat callbacks |

All six saved media files fully decoded without diagnostics. Thumbnail/info JSON publication and archive repeats passed. Fresh public page metadata and supplied resources were obtained autonomously, without personal cookies or a manually supplied capture.

Fifteen deterministic checks cover streamed-record merging, metadata, aliases/widgets, query/native/global identities, conflicts, literal JSON-only parsing, access/composition boundaries, origins/resource shards/durations/dimensions, captions/posters, external delegation, image metadata, archives and limits. Four generated-media checks cover complete MP4/HLS frames and audio, literal child queries, captions with original timing, byte-identical MP4 originals, archive repeats and incomplete-source refusal while preserving existing output.

Public board inventory/pagination and selected-pin downloads were added in 0.73.0; see [Pinterest collections](PINTEREST-COLLECTIONS.md) for live evidence and count/order/audio boundaries. Remaining Pinterest work includes full `PinterestCollection` parity, profiles/sections/accounts, stable selection from earlier inspections, multi-page stories/carousels, more page/resource/regional/short layouts, older state/API formats, richer metadata and full upstream parity. Remaining sites and app options, removal of the old runtime, packaged verification and WPF cutover remain required.

Story video asset extraction and additional resource families were added in 0.75.0; see [Pinterest stories](PINTEREST-STORIES.md) for live evidence, multi-asset selection and remaining rendering/nested-layout work.
