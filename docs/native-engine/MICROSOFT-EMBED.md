# Native Microsoft Universal Media Player

Native engine 0.80 adds a C# provider for the Microsoft `videoplayer/embed` platform. It reads literal player URL/JSON settings, public PowerCMS mappings and the current Cascade playback-info endpoint. No Python, yt-dlp, copied extractor code, JavaScript execution, browser capture or login is used. ffmpeg remains for verification and media processing.

## Implemented scope

- HTTPS `www.microsoft.com`/`microsoft.com` embed URLs, optional locale prefixes, legacy path IDs, `powerCmsVideoId`, direct Adobe/Cascade group/video settings, and literal JSON PowerCMS plugin settings.
- Current flat `mp4=QUALITY,URL`, `hls=URL`, `dash=URL`, `vtt=LOCALE,URL` and `ttml=LOCALE,URL` options; repeated MP4/caption fields; literal `options` JSON sources, captions, title and poster. Form decoding preserves the original nested URL query escapes.
- PowerCMS mapping lookup followed by the exact declared operating-group/video Cascade endpoint. Redirected identities, conflicting IDs, malformed/repeated JSON fields, invalid object/array shapes, excessive inventories and incompatible mixed CMS/direct-source settings fail explicitly. Missing mappings do not trigger alternate playback or guesses.
- Current explicit MP4/HLS/DASH types. `HD`, `HQ`, `SD` and `LO` labels have a bounded provider ordering without inventing dimensions or bandwidth. Unknown pixel facts remain unknown; maximum-height constraints fail on unmeasured progressive files while adaptive manifests supply their actual rendition facts.
- Public VTT/TTML caption tracks, VTT preference for duplicate language/type offerings, TTML-only language retention, supplied title/artwork and caption overrides on CMS discovery, original captions, native conversion/embedding, retained originals, info JSON and stable archives. Ambiguous preferred caption resources fail explicitly.
- Anonymous public metadata and playback sessions isolate caller cookies/authorization. Supplied public HTTP/HTTPS resources are used literally; file/blob/credential-bearing/fragment-bearing media addresses are rejected. Recognized protection/age settings require additional native support rather than being bypassed.
- Progressive/HLS/DASH verification applies full local decoding and available provider duration/media-role checks before publication. Failure preserves previous files and archives. Known PowerCMS archives skip metadata; supplied-source archive IDs derive from the source inventory rather than display titles. Generic native iframe routing reaches the provider.

## Verification on October 9, 2026

Fourteen deterministic provider checks cover literal/flat settings, named quality ordering, mapped/direct Cascade identity, aliases, URL/resource/schema boundaries, protection/age and redirected API responses, caption choice/overrides, stable archives, anonymous sessions, cancellation and native iframe dispatch. Six generated-media checks verify MP4, flat TTML conversion/embedding, HLS, DASH, mapped Cascade MP4 and incomplete-source rejection. All successful generated outputs and original components are fully decoded.

Live media verification used current Microsoft player configurations **constructed from media URLs published by Microsoft's public Surface page and Learn API**. These are real network downloads through `NativeEngine.DownloadAsync`; no metadata responses or media bodies were substituted. They prove the supplied-resource configuration path, not live PowerCMS/Cascade discovery. The constructed MP4 player URL itself returned HTTP 200, retained its MP4 configuration and served the current `index.mjs` player module.

| Configured workflow | Processed / merged-original bytes | Complete playback |
| --- | ---: | --- |
| Public Surface hero MP4 | 3,374,902 / 3,375,166 | H.264 1920×1080, all 240 video frames, silent, 8.000 seconds; data stream present in both files |
| Public Learn HLS, maximum height 360 | 9,006,004 / 9,001,055 | H.264 640×360, all 5,627 video / 8,802 AAC audio frames, 187.754667 seconds |
| Public Learn DASH, maximum height 360 | 9,006,005 / 9,001,055 | Same dimensions, frame counts and duration |

All **10** files passed strict full decoding. HLS retained 5,861,666-byte video / 3,044,631-byte audio components; DASH retained 5,862,082-byte video / 3,045,047-byte audio components. Adaptive workflows saved and embedded all **74** caption cues with identical complete caption text. MP4 reported two media completions; each adaptive workflow reported four. All immediate archive retries reported zero completions.

The mapping endpoint returned HTTP 404 for public older examples `RE1ZWJz`, `RE201WL`, `RE4bOeh`, `RW10Cyz` and `RE4yiO5`. Their legacy embed pages redirected to the current player shell, but that shell is not playback evidence. Live PowerCMS/Cascade media playback remains **unverified**; the implemented flow is covered by deterministic metadata and generated media checks. No retired-platform or broad Microsoft parity claim follows from these samples.

## Remaining work

This is partial coverage of the frozen catalog's `MicrosoftEmbed` key. Working live PowerCMS/Cascade examples, additional mappings/API layouts, downloadable-files menus/audio/transcript variants, Smooth Streaming/MSS, other source/quality types, richer metadata/chapters, source refresh, advanced plugins/options, account/protected/live workflows and broad alias/embedding compatibility remain pending. Arbitrary JavaScript player construction is not executed.

`MicrosoftBuild` and `MicrosoftMedius` remain pending; Microsoft Learn has its separate partial provider. Production WPF still uses yt-dlp. Full catalog/option coverage, native cutover, old dependency removal and standalone packaging remain unfinished.

## Primary references and local evidence

- [Current Microsoft embed player module](https://www.microsoft.com/videoplayer/embed/index.mjs)
- [Current Microsoft Universal Media Player implementation](https://www.microsoft.com/videoplayer/ump.mjs)
- [Public Surface product page](https://www.microsoft.com/en-us/surface/devices/surface-pro-11th-edition)
- [Public Learn episode supplying the adaptive resources](https://learn.microsoft.com/en-us/shows/dotnet-for-beginners/adding-a-nuget-package-dotnet-for-beginners)

These Microsoft-hosted scripts/responses establish current observed protocol behavior, not an API stability guarantee. Implementation: `src/YoutubeDl.Engine/Providers/MicrosoftEmbed/`; named provider quality ordering in `MediaCandidate.cs`. Checks: `MicrosoftEmbedChecks.cs` and `FfmpegChecks.MicrosoftEmbed.cs`. Local live evidence: `work/microsoft-embed-probe/live-probe.log`, configured player HTML, and saved verification JSON/media in its Release output. Scratch scripts, responses and downloads are not repository fixtures or runtime dependencies.
