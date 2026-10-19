# Native Loom public recordings

Engine 0.69.0 adds independently implemented C# Loom share/embed/community recording extraction. Older MP4 playback and a modern separate-audio HLS recording are verified live. The main application still uses its original backend pending complete site/option coverage and packaging.

## Metadata and offered playback

The provider reads the public page's bounded Apollo JSON without executing its scripts. The requested video ID, normalized `getVideo` reference and returned video record must agree. Only complete, available public recordings are accepted in this first implementation. Password/account access, archived/incomplete states and edit-timeline composition are explicit remaining boundaries.

The page can preload adaptive playback. A read-only `getVideo` GraphQL operation requesting the offered MP4 URL also handles older recordings with no adaptive preload. It is not a download mutation and does not guess file extensions or use downloader/extractor code from yt-dlp. API IDs and resource paths are checked before media transfer. Supported resources are supplied Loom CDN MP4s or video/revision-bound Luna adaptive URLs.

Share, embedded-player and community aliases resolve to the same recording identity. Generic embedded HTML delegates to the provider. Native title/description/uploader, known progressive dimensions, thumbnails, caption inventories and `loom <video-ID>` archive keys are mapped. Adaptive master dimensions remain unset until manifest selection; the original recording dimensions do not exclude a lower selected variant.

Transcript metadata must belong to the requested video, and its supplied VTT resource must be video-bound. Recognized declared transcription services mark automatic captions. An undeclared caption language remains `und`; it is not inferred from page locale or transcript text. Original-language, translated-language and service/layout coverage still need expansion. Chapter-bearing layouts fail explicitly until their actual fields and timelines are verified.

The public samples come from Loom's [embed SDK example](https://developers.loom.com/docs/embed-sdk/getting-started) and [getting-started tutorial](https://www.loom.com/community/d97180be7d674f4fbf57744365457162-pg). Public page/player state and the observed read-only API supplied the protocol evidence. Python and yt-dlp are not used.

## Scoped adaptive query access

The player supplies signed query parameters for a specific Luna video/revision resource directory. Relative HLS child playlists, audio renditions, initialization/key resources and fragments need the same parameters. The engine replays those offered encoded pairs through an owned media HTTP session only within that exact HTTPS origin, port and resource-directory prefix.

Existing identical parameters are retained, missing parameters are appended without altering encoded values, and duplicates/conflicts fail. Another host, recording, revision, protocol, port or sibling prefix does not receive them. Header/anonymous-session copies retain the intended scope while the original owning session remains unchanged. The rule is excluded from metadata JSON and its diagnostic string is redacted. Signed media URLs themselves remain ordinary requested resources; no authorization values are logged by the provider.

## Independently numbered HLS transport packets

Loom's MPEG-TS video segments independently reset continuity counters. Direct concatenation produced strict demux errors in the retained video component even though the merged MP4 retained all frames. Loom's provider now opts into streaming counter alignment before the assembled stream is used or retained.

Only each packet header's four-bit continuity counter can change. PID, payload, adaptation fields, PCR clocks and timestamps remain unchanged. Payload increments, adaptation-only packets, exact duplicates, declared discontinuities and null PIDs are handled explicitly. Invalid synchronization, alignment, transport-error indicators or adaptation lengths fail while the data is staged. Fragmented MP4 is not rewritten. Other providers retain their previous byte-handling policy.

On the live recording, all 8,419 video packet payload hashes, PTS, DTS and duration values matched the pre-alignment stream exactly. The corrected retained video stream then passed strict full decoding. Its transport container bytes are canonicalized; progressive MP4 originals are retained byte-for-byte.

## Live verification on October 9, 2026

| Recording | Saved media | Verified playback | Related files and repeat |
| --- | --- | --- | --- |
| `0281766fa2d04bb788eaf19e65135184` | 163,720-byte processed MP4; 163,624-byte original MP4 | H.264 1280×800, 626 video frames, silent; 20.866667 s | Thumbnail/info JSON; two callbacks; zero archive-repeat callbacks |
| `d97180be7d674f4fbf57744365457162`, HLS maximum 720 | 46,539,429-byte processed MP4; 46,532,067-byte merged original; 45,121,504-byte video TS; 7,103,016-byte audio TS | H.264 1280×720, 8,419 video frames; AAC 13,157 frames; 280.682667 s merged | 77 VTT/embedded caption cues, thumbnail/info JSON; four callbacks; zero archive-repeat callbacks |

All six saved media files fully decoded after counter alignment. The silent older clip does not invent an audio track. The HLS job honors the maximum-height choice while retaining both source components and the merged original. Embedded and saved caption cue counts match. No personal browser cookies or manually provided capture was used.

Nine provider checks cover metadata/identity, formats/scopes, aliases, access/edit boundaries, legacy playback, caption binding/language, JSON-only parsing, archives and embedded-page routing. Five HTTP-rule checks cover encoded parameters, scope boundaries, conflicts, session copies and redaction. Three transport checks verify header-only changes, packet semantics and malformed-input refusal. Three generated-media checks cover MP4/HLS publication, complete frames, originals, captions, archive repeats and incomplete-media rejection.

Remaining Loom work includes folders (the captured upstream folder key is marked broken and has not been counted as replaced), accounts/passwords/email gates, edited/personalized/live layouts, chapter fields, additional caption languages/services, DASH/live verification, more CDN/storage/revision layouts and complete format/metadata parity. Remaining sites and app options, dependency removal, packaged verification and the WPF switch are still required.
