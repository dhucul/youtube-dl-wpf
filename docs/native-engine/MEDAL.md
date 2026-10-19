# Native Medal public clips

Native engine 0.84 added C# extraction for current Medal public clip pages and supplied MP4 sources/renditions. Version 0.85 also adds the supplied [Medal HLS service](MEDAL-HLS.md). It reads literal public page data and preserves the publisher's media authorization values. No Python, yt-dlp, JavaScript execution, browser capture or login is used. ffmpeg remains for media verification/processing.

## Implemented behavior

- Current game/clip routes, singular/plural clip paths, locale prefixes and direct clip routes, with HTTP/`www` normalization. The returned clip ID remains bound across canonical redirects. Other feed/profile/search/collection URLs are not claimed as individual clips.
- Bounded literal Next Flight parsing supports both ordinary JSON rows and UTF-8 byte-counted text records followed immediately by another row. Public preload/module rows are not executed. Invalid lengths, Unicode boundaries, malformed/repeated JSON, excessive data and executable call arguments fail explicitly.
- The exact requested `contentId` is selected from the public data, excluding related clips. Clip share/category identities, published/success/shareable state, access/processing/privacy/login and archive state are checked before exposing ready resources. Conflicting repeated requested records fail rather than selecting an arbitrary record.
- Supplied source dimensions/duration/title/description/owner/artwork and MP4 URLs. Current authorization identity payloads are checked against the requested clip and public owner; the original signature, expiry and other query values remain unchanged. This checks declared identity/structure, not the server's cryptographic signature. No signatures or tokens are generated.
- Named renditions are used only when actually provided and not marked `missing`. Publisher-marked unavailable renditions are reported separately with no downloadable URL. Their fields do not become guessed lower-quality files or resolution fallbacks. Source dimensions and provided rendition heights are retained; unprovided width/codec/frame-rate/size/captions are not invented.
- Progressive native transfer, requested format/height constraints, full local decoding and duration checks, video-only/audio-only processing, original retention, metadata/artwork/info JSON, unique completion callbacks and clip-ID archives. Known archives skip page/media discovery across supported aliases.
- Anonymous public metadata/playback sessions isolate caller cookies and authorization. Invalid origins/containers/owner or clip identities, login/private/non-ready records, denied/cancelled requests and incomplete source files remain explicit failures. Prior output/archive state is preserved on verification failure.
- The supplied Medal HLS service now has separate native transfer/verification support; see [HLS scope and evidence](MEDAL-HLS.md). Extra rendition/query or data layouts require further verification.

## Verification on October 9, 2026

Thirteen deterministic Medal checks cover requested/related records, aliases/routes, identity/access state, source facts, missing/ready renditions, signed resource identity/structure and literal queries, public credential isolation, archives, cancellation/denial and redirected identity. Four shared Flight checks verify UTF-8 byte-counted adjacent records, Unicode/truncation boundaries, literal arguments/duplicate fields and row/data limits. Six generated-media checks verify source/ready-rendition MP4, video-only/audio-only/silent output and incomplete-source rejection. Successful generated files fully decode, retain all 48 video frames and exact original bytes, and archive retries issue no new requests or callbacks.

Live ordinary `NativeEngine.DownloadAsync` workflows:

| Requested workflow | Processed / original bytes | Complete playback |
| --- | ---: | --- |
| [Public source `1HnFOn78iK1FqT`](https://medal.tv/games/minecraft/clips/1HnFOn78iK1FqT) | 3,659,557 / 3,659,502 | H.264 1920×1080, all 291 video / 226 AAC audio frames, 4.871000 seconds |
| Same clip, video-only | 3,577,355 / 3,659,502 retained full source | All 291 video frames, no audio in processed output, 4.850000 seconds |
| Same clip, best/copy audio | 79,203 M4A / 3,659,502 retained full MP4 source | All 226 AAC audio frames, no processed video, 4.821333 seconds |
| [Second public source `lPCKMyghU8rF3EPqL`](https://medal.tv/games/minecraft/clips/lPCKMyghU8rF3EPqL) | 6,058,672 / 6,058,609 | H.264 1280×720, all 392 video / 613 AAC audio frames, 13.087667 seconds |

All **8** saved media files passed strict full decoding. Independent packet inspection verified unchanged encoded video/audio payloads and order for the first normal source and its video/audio-only outputs (291 video packets and 226 audio packets). Artwork and info JSON were saved. Every workflow reported exactly two new media completions; immediate archive repeats reported zero with no discovery.

Version 0.84 native inspection of the first clip reported one ready source and six publisher-marked unavailable named renditions, not seven usable MP4 downloads. Version 0.85 also reports its verified supplied HLS service as ready. Live lower-rendition playback remains unestablished. Signed resource values were used only internally and are not documentation or runtime fixtures.

## Remaining scope

This is partial coverage of the catalog's `MedalTV` key. Additional HLS/server-generated/transcode paths, source/CDN/token shapes, legacy/download/trailing route aliases, game/profile/tag/collection/search inventories, private/account/age/archived/live workflows, captions/chapters/richer metadata, ready rendition codec/frame-rate/size facts, expiring-link/resume and full Medal parity still need implementation and verification.

The shared Flight parser is only consumed by this provider in this stage; it is not evidence of arbitrary Next site support. Production WPF still uses yt-dlp. Full catalog/option coverage, cutover, removal of the old dependency and standalone packaging remain unfinished.

## Primary sources and local evidence

- [First public Medal clip](https://medal.tv/games/minecraft/clips/1HnFOn78iK1FqT)
- [Second public Medal clip](https://medal.tv/games/minecraft/clips/lPCKMyghU8rF3EPqL)
- Their publisher-hosted page data, signed media and artwork responses inspected directly. Current protocol observations are not an API stability guarantee.

Implementation: `src/YoutubeDl.Engine/Providers/Medal/` and `Discovery/NextFlightReader.cs`. Tests: `MedalChecks.cs`, `FlightChecks.cs` and `FfmpegChecks.Medal.cs`. Local evidence: `work/medal-probe/live-probe.log`, `packet-verification.json`, verification JSON and saved media under the probe Release output. Scratch signed page/response/reference material is not checked-in fixture data or a runtime dependency.
