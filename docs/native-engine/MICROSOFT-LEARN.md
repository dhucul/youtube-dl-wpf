# Native Microsoft Learn public videos

Native engine 0.79 adds independently implemented C# discovery for current Microsoft Learn public show episodes, event sessions, video entry players and complete show inventories. It uses public page metadata and the same public data endpoints used by Microsoft's current player, without Python, yt-dlp, copied extraction code, browser capture or login. ffmpeg remains for merging, verification and processing.

## Implemented behavior

- Current HTTPS `learn.microsoft.com` show episode/show and event-session routes, older `docs.microsoft.com` aliases, `learn-video.azurefd.net/vod/player` show/episode or event/session links, and current/legacy Learn theme player links with a public GUID entry ID.
- Canonical page, locale, show/schema and bounded authoring metadata checks, followed by exact public API entry-ID binding. Older authoring UIDs can differ from their page slug; the canonical page and video GUID remain the identity anchors. Public externally hosted entries delegate the exposed original player through the native provider registry.
- Offered high/medium/low MP4, explicit HLS/DASH and MP3 or MP4/M4A audio resources. HTTPS encoding origins and `/public-GUID/` asset paths are bound to the requested video; literal offered queries remain unchanged. Legacy dimensionless MP4s retain unknown pixel dimensions. Their names do not imply a resolution.
- The current identified One Stream encoding layout supplies separate video/audio tracks. The video filename contains an `AACAudio` marker but does not contain audio. This layout is explicitly classified as video-only and paired with its offered audio for ordinary/literal-format downloads. An explicit single-format selector still requests only that track. Encoded dimensions are retained for this verified layout, supporting quality/height selection without using poster dimensions.
- Public VTT language inventories, artwork variants, descriptions, original retention, info JSON, audio-only output, native title/quality settings and video-GUID archives. Captions are exposed language tracks; the API does not establish authored/automatic/translated provenance.
- Full show pagination using the service's maximum 30 rows per page, stable declared totals, scoped continuation origin/path/locale/order/offset and repeated-result detection. Show order is newest-first; series order is oldest-first. Hidden positions remain unavailable; selected episodes resolve freshly and have expected video archive identities. Unfiltered show queries are supported; filtered/search/paged/preview requests fail explicitly.
- Inspected show selections use the existing native snapshot contract and retain original episode positions. Known archived episode GUIDs skip selected media discovery and transfer.
- Malformed/repeated metadata, conflicting identities, invalid public resources, incomplete/changed inventories, denied API requests and cancellation remain explicit. Complete decoding and declared duration checks protect progressive/HLS publication.
- Provider opt-in now also applies complete local decoding, requested media roles and declared duration checks to DASH output before processing/publication. MP4/M4A/MOV and Matroska/WebM outputs are supported by that check. Failed verification leaves previous files and archives intact. Generic DASH without provider opt-in is unchanged.

## Verification on October 9, 2026

Fourteen deterministic provider checks cover current/older metadata, aliases, event-session and direct-entry parsing, assets, split-track classification, captions, external players, pagination/order/limits, unavailable positions, anonymous sessions, archives, cancellation and API failures. Seven generated-media checks verify complete legacy MP4, current progressive video/audio pairing, HLS, DASH, MP4 audio, selected show episodes and incomplete DASH rejection. Every successful generated file and retained component passes strict full decoding.

Live public inventories: Azure Friday enumerated all **516** episodes across scoped continuations; `.NET for Beginners` enumerated all **3** episodes. The older Azure Friday episode `99fd9a1d-1f74-4a1e-a2c1-fed80118ea77` exposed 692-second metadata, six media resources and 14 caption languages; its legacy media has not been downloaded in this stage.

Live downloads used `NativeEngine.DownloadAsync` on [Adding a NuGet Package](https://learn.microsoft.com/en-us/shows/dotnet-for-beginners/adding-a-nuget-package-dotnet-for-beginners), entry `e7b5263c-3c86-4482-abc0-7cb8e7adb928`:

| Workflow | Processed / original merged bytes | Complete playback |
| --- | ---: | --- |
| Current low MP4 plus offered MP4 audio | 10,508,220 / 10,503,257 | H.264 640×360; 5,627 video / 8,802 AAC audio frames; 187.754667 seconds |
| HLS with maximum height 360 | 9,006,018 / 9,001,055 | Same dimensions, frame counts and duration |
| DASH with maximum height 360 | 9,006,018 / 9,001,055 | Same dimensions, frame counts and duration |
| Offered audio, retained as M4A | 4,542,772 / 4,542,702 | AAC; all 8,802 audio frames; no video; 187.754667 seconds |
| Inspected show entry 3, selected low MP4/audio | 10,508,220 / 10,503,257 | Same complete video/audio, original source position 3 |

Each video workflow preserved **74 caption cues** and identical complete saved/embedded caption text. Artwork and info JSON were saved. Current progressive and selected-episode workflows retained separate 5,862,082-byte video and 4,542,702-byte audio sources. HLS retained 5,861,666-byte video and 3,044,631-byte audio components; DASH retained 5,862,082-byte video and 3,045,047-byte audio components. All **18** files across these five workflows passed strict full decoding. Video workflows reported four unique media completions; audio reported two. Immediate archive repeats reported zero.

An initial diagnostic download of the raw low MP4 proved it was video-only (5,627 frames, 187.754233 seconds). It prompted the split-track classification fix; the final progressive and selected-show results above include audio. That diagnostic is not evidence of a complete default movie download.

## Remaining coverage

This is partial coverage of `MicrosoftLearnEpisode`, `MicrosoftLearnPlaylist` and `MicrosoftLearnSession`. Live event-session playback is not established; the attempted retired Build session redirected to the general shows page and was refused. Generic Learn iframe routing is available through the existing native embed discovery.

`MicrosoftBuild` and `MicrosoftMedius` remain pending. The separate `MicrosoftEmbed` videoplayer platform now has partial supplied-resource and mapped Cascade support; see [its verified scope](MICROSOFT-EMBED.md). Retired Channel 9 layouts, other public encoding/CDN and legacy AMS/Smooth Streaming layouts, account/protected/live playback, event collections, filtered/search listings, translations/provenance, chapters, richer metadata, expired-link recovery and broader quality/container workflows need further implementation and verification. A filtered request is not returned as an unfiltered complete collection.

The production WPF app still uses its existing yt-dlp backend. Full extractor/option parity, cutover, dependency removal and standalone packaging remain unfinished.

## Primary sources and evidence

- [Microsoft Learn Azure Friday show](https://learn.microsoft.com/en-us/shows/azure-friday/)
- [Older public episode](https://learn.microsoft.com/en-us/shows/azure-friday/azure-app-service-diagnostic-and-troubleshooting-experience)
- [Current public episode](https://learn.microsoft.com/en-us/shows/dotnet-for-beginners/adding-a-nuget-package-dotnet-for-beginners)
- Current Microsoft-hosted page/player scripts and their public video/search responses, inspected directly. These are runtime protocol observations, not a documented API stability guarantee.

Implementation: `src/YoutubeDl.Engine/Providers/MicrosoftLearn/`; DASH verification integration in `NativeEngine.cs`. Checks: `MicrosoftLearnChecks.cs` and `FfmpegChecks.MicrosoftLearn.cs`. Local live evidence: `work/microsoft-probe/azure-inspect.log`, `live-probe.log`, `live-merged.log` and verification JSON/media in that probe's Release output. Scratch responses and downloads are not runtime dependencies or checked-in fixtures.
