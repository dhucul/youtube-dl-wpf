# Native PBS public videos

Native engine 0.81 adds C# extraction for public PBS video pages and players. It reads literal page/player data and the publisher's public encoding resolver, with no Python, yt-dlp, JavaScript execution, private API credentials, browser capture or login. ffmpeg remains for media verification and processing.

## Implemented behavior

- Central PBS slug video pages, legacy numeric video links, portal/partner/viral/widget player aliases, including widget/partnerplayer routes. HTTP aliases normalize to the authoritative HTTPS player.
- Current public page data is read from bounded literal Next page-data strings. The requested slug, canonical content record, content GUID and legacy numeric ID are bound before opening its player. Related or suggested videos do not become requested assets. Symbolic/computed/executable layouts require additional support.
- The exact `window.videoBridge` record is parsed as literal JSON assignments, with unique player/content identity checks and explicit availability, playability, Passport and DRM state. Related player records/encodings are excluded. Unavailable, gated, protected, missing-state and live layouts fail before encoding/media requests.
- Each supplied public encoding is resolved through its exact URS identity with `format=json`. Failed or redirected metadata is refused. Exposed signed/session URLs are kept literally, with current and observed legacy PBS media origins and content-ID checks where the URI declares an asset. No resolver IDs, media filenames, geolocation or playback authorization are guessed or changed.
- Offered MP4 and static HLS use the shared native transport and full decoding/duration checks. Explicit encoded MP4 height is retained; source/master folder dimensions are not copied to the output. Legacy dimensionless MP4s remain unknown. Adaptive quality limits use actual manifest facts.
- Exposed multi-language VTT, SRT and DFXP/TTML profiles are selectable through the existing caption format/language preferences. Same-language conflicting formats fail explicitly; legacy caption dictionaries are supported with the platform's English fallback. Original captions, native conversion/embedding, artwork, description, info JSON and originals can be saved.
- Video archives use the original PBS numeric ID across aliases. Known numeric archives skip discovery; slug requests bind the ID freshly and skip media if archived. Caller cookies/authorization are isolated from the public metadata and playback session.
- Malformed/repeated/oversized data, mismatched page/player/resolver assets, invalid origins, cancelled requests and incomplete files remain explicit failures. Prior output and archive state are preserved on failed verification.

## Verification on October 9, 2026

Fourteen deterministic PBS checks cover current page records, literal player parsing, identity/related-video scope, aliases, access state, encoding resolution, original rendition facts, caption profiles/languages/format selection, public credential isolation, archives, cancellation and denied responses. Six generated-media checks verify MP4, HLS, SRT, DFXP/TTML, numeric archive behavior and incomplete-source rejection. Every successful output and original component is fully decoded.

Live normal `NativeEngine.DownloadAsync` workflows in the existing Canadian public session:

| Requested public video/workflow | Processed / original bytes | Verified complete playback |
| --- | ---: | --- |
| [Our New World preview](https://www.pbs.org/video/preview-ldzkws/), offered MP4 | 10,871,099 / 10,869,824 | H.264 1280×720; all 900 video / 1,407 AAC audio frames; 30.030000 seconds |
| Same preview, HLS with maximum height 480 | 5,204,799 / 5,203,599 merged original | H.264 768×432; all 900 video / 1,408 AAC audio frames; 30.037333 seconds |
| Same preview, offered SRT | 10,871,099 / 10,869,824 | Same full MP4 playback and complete embedded caption text |
| Same preview, offered DFXP | 10,871,099 / 10,869,824 | Same full MP4 playback and complete native TTML conversion |
| [Nature Season 29 preview](https://www.pbs.org/video/nature-preview-season-29/), numeric player `1558498609` | 20,602,055 / 41,869,879 | H.264 512×288; all 4,307 video / 12,366 AAC audio frames; processed 287.147869 seconds, original container 287.133333 seconds |

All **12** saved media files passed strict full decoding. HLS retained separate 4,435,620-byte video and 766,679-byte audio components. The current preview saved/embedded all **11** caption cues. VTT and SRT complete caption text matched extracted embedded text; independent parsing of all 11 original DFXP paragraphs also matched the complete embedded text. Artwork and info JSON were saved. MP4 workflows reported two media completions; HLS reported four. Every immediate archive retry reported zero completions.

The older source contains two RTP hints and two MPEG-4 Systems descriptor tracks in addition to its playable video/audio. Initially ffmpeg refused to copy these during metadata processing; the identified-track fix below resolved this failure. Every encoded video/audio packet payload, packet order, PTS/DTS and duration matched the original (4,307 video packets, 12,366 audio packets; zero clock difference). The retained original contains all six tracks and remains untouched. The processed movie carries its playable video/audio.

An additional Nature Season 40 player returned an unavailable/error page without a supported video record and was refused. The private content-service endpoint exposed in page metadata returned 401 during read-only investigation; it is not used by the native provider. These results do not establish general Passport, regional or full-site access.

## Identified legacy movie tracks

Metadata/remux processing now inspects actual bounded movie track IDs and handler types before excluding incompatible legacy data. Only data streams whose codec tag and movie handler agree on RTP hints (`rtp ` / `hint`) or MPEG-4 Systems descriptors (`mp4s` / `odsm` or `sdsm`) are excluded from the processed movie. Unknown data, timecode, chapters, captions and playable media are not classified by this rule. Existing original retention preserves the complete source file.

Four parser checks verify identity rather than stream ordering, exact classification boundaries, malformed/duplicate/oversized handlers, trailing movie headers and cancellation. Two generated RTP-hint movies verify remux and metadata processing with unchanged original bytes, all video frames, identical encoded video/audio payloads/order and strict decoding. Existing timecode/chapter/cover checks remain part of the full regression suite. Interactive MPEG-4 scene rendering, other ancillary layouts and broader processing containers still need work.

## Remaining scope

This is partial coverage of the catalog's `pbs` key. PBS Kids now has a [separate partial provider](PBS-KIDS.md). Station/custom-domain and article layouts, show/season/collection pagination, renamed/other aliases and page layouts, Passport/account/member access, protected/live/geographic workflows, audio/described-video alternatives, SAMI and other caption variants/provenance, chapters/richer metadata, expiring-link/resume cases and full PBS parity remain pending. Current public previews do not prove restricted full-episode access.

Production WPF still uses yt-dlp. Full catalog/option coverage, native cutover, removal of the old dependency and standalone packaging remain unfinished.

## Sources and local evidence

- [Current PBS public preview page](https://www.pbs.org/video/preview-ldzkws/)
- [Publisher's public player for the current preview](https://player.pbs.org/portalplayer/3108688573/)
- [Older PBS preview page](https://www.pbs.org/video/nature-preview-season-29/)
- Publisher-hosted public player script and URS responses observed directly, without changing location/access parameters.
- [FFmpeg MOV/MP4 format documentation](https://ffmpeg.org/ffmpeg-formats.html#mov_002c-mp4_002c-ismv)

Implementation: `src/YoutubeDl.Engine/Providers/Pbs/`; legacy processing in `FfmpegTool.LegacyTracks.cs`, `FfmpegTool.cs` and `Mp4ChapterReferences.cs`. Tests: `PbsChecks.cs`, `FfmpegChecks.Pbs.cs`, `MovieAncillaryChecks.cs` and `FfmpegChecks.LegacyTracks.cs`. Local evidence: `work/pbs-probe/live-probe.log`, `live-legacy.log`, `legacy-packet-verification.json`, verification JSON and saved media in the probe Release output. Scratch public responses/scripts/media are not repository fixtures or runtime dependencies.
