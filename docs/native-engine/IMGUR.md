# Native Imgur video support

Implemented in engine 0.55.0. This is a tested subset of the captured `imgur`, `imgur:album` and `imgur:gallery` targets, not full provider parity.

## Current behavior

- Individual Imgur pages and gifv player addresses resolve the actual video resource supplied by the page. The engine does not invent an MP4 address by changing a gifv extension.
- Public album and gallery pages expose `window.postDataJSON`. The native reader decodes the double-quoted literal, including the observed JavaScript apostrophe escape, then parses bounded JSON without executing site scripts or using an API key.
- The requested post ID, unique media IDs, declared item count and offered CDN resource IDs are checked. IDs and URL query values retain their case. Incomplete or excessive inventories fail instead of appearing complete.
- Albums retain source order and unavailable positions for still images or unsupported media. A single-item album remains an album. Only selected video resources are transferred; complete supplied metadata snapshots avoid unnecessary child-page requests.
- Offered MP4/WebM videos carry their actual dimensions, size, duration and sound state. Silent animations are complete media and do not require an invented audio track. Individual Open Graph pages do not declare sound state, so it remains unknown until local media inspection.
- The observed `privacy: private` album field does not deny access by itself: a publicly accessible gallery returned that value. HTTP denials, absent public declarations and pending posts remain explicit failures.
- Completed files are fully decoded with a fixed local demuxer before publication. Album duration is checked against decoded media; retained originals remain byte-identical. Archives use `imgur <media-id>`, independently of gallery/album IDs and individual/gifv aliases.

## Verification

Eight deterministic checks cover inventories, aliases, literal queries, sound state, unavailable positions, ID/count conflicts, public declaration parsing, limits, archives, cancellation and HTTP denial. Three generated-media checks cover video with audio, silent video, and incomplete-duration rejection. Selected album position 2 retains source index 2 and autonumber 1; no unselected media is fetched. Frame counts, complete decoding, original bytes, callbacks, info JSON and archive skips are checked.

On October 9, 2026, the public gallery `fA5QRLy` and individual video `t1x21sR` were inspected through the default native registry. Native album selection downloaded its offered video using only C# and ffmpeg:

- Processed MP4: 6,359,274 bytes; original: 6,359,160 bytes.
- H.264, 854 × 480, 1,320 video frames and 2,278 AAC audio frames in both files.
- Duration 52.895011 seconds; both files fully decoded. The page's nominal duration was 52.919 seconds.
- Two completion callbacks and the individual media archive identity were preserved.

Primary evidence was the published Imgur gallery and individual HTML, not another extractor's implementation. Live multi-item and silent albums have not yet been verified; their current evidence is deterministic generated media.

## Remaining scope

Still-image/GIF downloads, thumbnail variants, additional legacy/player/embedded layouts, album fragment selection, pagination or alternate APIs, account/private workflows, resource refresh, fuller metadata and complete option/archive parity remain pending. The production WPF backend is unchanged until the broader replacement requirements are met.
