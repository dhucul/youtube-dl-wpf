# Native Medal HLS service

Native engine 0.85 extends the existing Medal provider to the supplied public `/api/hls/CLIP/master.m3u8` service. It preserves the opaque `bebit` value and all publisher-provided child/segment authorization URLs, without decoding or generating the service token, Python, yt-dlp, JavaScript execution, browser capture or login. ffmpeg remains for media verification and processing.

## Implemented scope

- Exact HTTPS Medal host/default-port, requested clip path and master playlist identity. The current service descriptor requires one bounded opaque `bebit` field; mismatched clip/host/path, repeated fields, malformed token shape and credential/fragment-bearing URLs fail before playback. These are route/structure checks, not cryptographic token validation.
- HLS candidates retain unknown rendition width/height until the manifest declares them. The original MP4 dimensions do not become adaptive rendition facts. Ready HLS formats are reported with their actual protocol alongside supplied MP4 and publisher-marked missing renditions.
- Shared static HLS master/media/segment transfer, actual manifest quality/height/codec/frame-rate selection, full local decoding, role/duration checks, metadata/artwork/info JSON, originals and video/audio-only processing. Parent and child authorization values remain literal; no unavailable rendition or source MP4 is silently substituted for an explicit HLS request.
- Caller cookies/authorization remain isolated in the public session. Denied or incomplete HLS cannot replace previous files or record archive success. Known clip archives skip all metadata/manifest/media discovery and repeat no completions.

Two new provider checks verify exact service binding and rejection of mismatched/duplicated/malformed descriptors. Six generated-media checks cover complete HLS, video-only, audio-only, silent sources, incomplete-source rejection and denied-access preservation. Every successful file/original fully decodes and retains all 48 video frames. The existing 13 Medal source/provider checks and six MP4 media checks still apply.

## Live verification on October 9, 2026

Normal `NativeEngine.DownloadAsync` fetched fresh clip metadata and used its supplied HLS URI:

| Workflow | Processed / original bytes | Complete playback |
| --- | ---: | --- |
| [`1HnFOn78iK1FqT`](https://medal.tv/games/minecraft/clips/1HnFOn78iK1FqT), HLS | 3,788,388 / 3,771,092 TS | H.264 1920×1080; all 291 video / 226 AAC audio frames; 4.871333 seconds |
| Same clip, video-only HLS | 3,704,164 / 3,771,092 retained full TS | All 291 video frames; no processed audio; 4.850000 seconds |
| Same clip, best/copy audio | 79,203 M4A / 3,771,092 retained full TS | All 226 AAC audio frames; no processed video; 4.821333 seconds |
| [`lPCKMyghU8rF3EPqL`](https://medal.tv/games/minecraft/clips/lPCKMyghU8rF3EPqL), HLS | 6,287,096 / 6,236,900 TS | H.264 1280×720; all 392 video / 613 AAC audio frames; 13.088001 seconds |

All **8** saved media files passed strict full decoding. Original TS video/audio encoded packet payloads/order matched processed TS and selected video-only output. Extracted M4A changes AAC transport framing, so raw TS/M4A packet hashes differ; independent decoded PCM hashes matched exactly. Artwork and info JSON were saved. Each workflow reported two unique media completions; every immediate archive retry reported zero without any discovery.

The first master exposed one actual 1080p/60-fps variant and its complete two-segment VOD media playlist. A normal HLS download requesting maximum height 720 failed with zero completions and no archive entry. It did not pretend a missing lower rendition existed or fall back to the source MP4.

## Remaining scope

This is additional partial `MedalTV` coverage, not complete site parity. Other service/token shapes, lower/ready transcoded variants, live/dynamic timelines, captions, extra audio layouts, legacy/download/trailing aliases, feeds/profile/game/tag/collection/search inventories, account/private/age/archived workflows, expiring-link/resume and richer metadata still need implementation and verification.

The service returns authorized URLs that may expire. Native extraction obtains fresh public page data per new job; broader mid-transfer refresh/recovery is not established. Production WPF still uses yt-dlp. Full catalog/option coverage, cutover, old dependency removal and standalone packaging remain unfinished.

## Primary sources and local evidence

- [First public Medal clip](https://medal.tv/games/minecraft/clips/1HnFOn78iK1FqT)
- [Second public Medal clip](https://medal.tv/games/minecraft/clips/lPCKMyghU8rF3EPqL)
- Their publisher-provided service descriptors/master/media/segment responses, inspected directly. These are current protocol observations, not an API stability guarantee.

Implementation: `MedalProvider.cs` plus existing native HLS transport. Checks: `MedalChecks.cs` and `FfmpegChecks.MedalHls.cs`. Local evidence: `work/medal-hls-probe/live-probe.log`, `packet-verification.json`, `audio-verification.json`, verification JSON/media under its Release output. Signed public snapshots/tokens are not printed in documentation, checked-in fixtures or runtime dependencies.
