# Native Streamable public videos

The independent C# provider queries Streamable's documented read-only video API. It resolves fresh media addresses for each selected download; signed CDN URLs are not hardcoded or copied into repository fixtures. It does not use a browser, Python, yt-dlp, or a Streamable client library.

## Implemented scope

- Public watch links, current `/e/` and `/o/` embeds, and `/videos/` API links. Generic HTML iframe discovery delegates to this provider. Video identity is verified against the API's canonical address.
- Ready MP4/WebM files and recognized offered manifests, bounded file descriptors, title, artwork, duration, dimensions, frame rate, bitrate, size and declared audio presence. Only positive offered measurements become format facts; codec facts are not invented.
- Best/worst, explicit format IDs, height/frame-rate selection, preserved literal signed queries, a scoped playback referrer and shared HTTP settings/cookies.
- Processing/unavailable videos, access denial, declared private/password requirements, protected renditions and unsupported formats remain explicit. Original-file metadata without a ready download URL does not become a playable format. No alternate access route is tried after denial.
- All supported aliases share `streamable <shortcode>` archive identity and can skip recorded items before metadata access.

## Verification on October 8, 2026

The [sample linked in Streamable's API documentation](https://streamable.com/hn8hq), `hn8hq`, completed through native downloading with metadata and a 1920x1080 JPEG cover:

| Requested height | Output bytes | Video | Audio | Container duration |
| --- | ---: | --- | --- | ---: |
| 360 | 4,871,197 | H.264, 640x360, 30 fps, 720 frames | AAC stereo 44.1 kHz, 1,033 frames | 24.000000 s |
| 720 | 14,355,251 | H.264, 1280x720, 30000/1001 fps, 719 frames | AAC stereo 44.1 kHz, 1,033 frames | 23.990633 s |

Both fully decoded. The API advertised a rounded 30 fps and 24.002-second duration for both renditions; those discovery facts were preserved separately from the measured media facts. The engine did not re-encode the video to force that rounded frame rate.

Nine deterministic checks cover API metadata, signed queries, aliases, quality selection, unknown facts, generic embeds, processing/access failure, partial readiness and protection, malformed identities/resources, archives and cancellation. Live verification currently establishes progressive MP4 playback only; manifest/WebM paths need provider-specific live checks.

## Remaining work

Additional legacy links, account/password/region workflows, captions and chapters, richer metadata, original-file access, expiration refresh during transfer, broader formats/manifests, complete option semantics and production integration remain pending. The captured `Streamable` key has partial verified coverage.

Primary references: [Streamable API documentation](https://streamable-support.zendesk.com/hc/en-us/articles/35415672400916-API-Documentation) and [embed parameters](https://streamable-support.zendesk.com/hc/en-us/articles/35415648975892-Editing-the-Embed-Code). The API documentation describes usage conditions for native integrations; this implementation does not establish service approval for distribution.
