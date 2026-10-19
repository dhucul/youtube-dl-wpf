# Practical native downloader release

Version 1.1.6, native engine 0.228.0. This release defaults to the best available video and original audio without re-encoding, and includes the 1.1.5 reliability fixes. The earlier YouTube, public Facebook and common video-page evidence is preserved below; these quality changes do not add new live-site or private-account coverage.

## 1.1.6 quality defaults

New downloads use unrestricted Best video, any codec, no frame-rate cap, automatic containers and Best audio. Best video compares resolution, frame rate and full bitrate. Best audio retains the default language and prefers available lossless sources. Pairing no longer lowers audio quality to keep the video container: mixed codecs can use Matroska while copying both streams. Audio extraction copies the source codec, including unusual PCM layouts, without silently re-encoding.

The former shipped MP3/192K preset migrates once when saved settings load. Explicit custom conversion choices remain available and later saves preserve them. Existing queued jobs retain their chosen options.

## 1.1.5 reliability fixes

Queue and preview fixes keep progress reports ordered, reject errors from canceled requests, validate speed limits with the native parser, and preserve total hours in long durations and ETAs. Process deadlines and cancellation cover output-pipe drainage and descendants whose parent has exited.

Completion and recovery now coordinate with overlapping publishers before validating shared output files. Resume receipts tolerate short Windows sharing/delete-pending conflicts, honor cancellation and preserve read-only files. Recovery-test workers have bounded cleanup and descendant ownership. Media-test tool output uses explicit UTF-8 decoding, including under hidden Windows launchers.

## Retained 1.1.4 end-to-end checks

The current batch verifies 19 complete downloads and one expected silent-audio refusal through the native app adapter with the normal metadata and chapter settings enabled. Every successful job also verifies archive and no-overwrite repeats, unchanged output hashes and no duplicate completion callbacks. Public non-explicit adult-site samples are used; the silent sample must stop without publishing an MP3 or an archive record.

| Site | Current workflows |
| --- | --- |
| YouTube | Lowest/best quality, explicit VP9/Opus, MP3, and complete 720p/1080p sources, approximately 635 seconds for the longer source. |
| Facebook | Public HD, SD and MP3 downloads through common video/reel/watch link shapes. |
| XVideos | Complete video and MP3; canonical video identity remains stable when signed delivery URLs change. |
| XNXX | Complete video and MP3 with the same stable archive/no-overwrite identity behavior. |
| xHamster | Lowest/best HLS video and an expected, clear no-audio result for the silent clip. |
| Pornhub | Complete video and three consecutive MP3 workflows after refreshing anonymous issuer context on link renewal. |

The fixes stay within the simple downloader scope. XVideos/XNXX archive IDs and output IDs now use recognized selected-video page IDs, independent of signed CDN addresses, language host, slug or tracking query. A redirect to a different selected video is rejected, and unrelated generic identities retain their existing behavior. Archived videos can be skipped before opening their page or CDN. Unknown/unsupported URL shapes keep ordinary generic handling.

HLS/DASH audio verification now checks for an actual audio stream before requesting a decoded audio map. A silent source reports that it has no audio track and publishes no fabricated/empty audio. Pornhub renewal can discard the rejected anonymous issuer context's routing cookies and begin fresh public navigation; selected-resource identity checks, concurrency control and retry limits remain in force. No saved account/profile is inspected.

All 62 reference/source/output files pass strict full decoding, and 52 independent coded-packet or decoded-audio comparisons pass. Publisher reference files from earlier independent fetches are reused after verifying their exact hashes; YouTube retained tracks are compared independently with the processed output. The final Pornhub repeat run recovered from 3 real CDN expiry responses. Earlier failed probes are retained. The final Pornhub context change was tested afterward with video and three MP3 jobs; the unaffected site's successful cases retain their prior candidate hashes. The end-to-end report records the tested binary for each workflow rather than presenting the entire batch as a single uninterrupted all-green run.

## YouTube quality fixes

The downloader preserves the observed browser's codec capabilities, playback cookie and outgoing contexts. Selected dimensions and playback clocks are applied without discarding unknown client fields. Server context removals remain removed until a new session is established. Captured state and proof tokens stay out of serialized reports.

Audio selection now respects the session's advertised codecs and channel layouts. Unsupported surround tracks no longer displace usable stereo. DRC, voice boost and language selection follow the chosen audio track, and voice-boost renditions retain distinct IDs through session binding/renewal. Opaque legacy tags remain preserved when their optional metadata cannot be decoded.

Preview analysis reads the web inventory without starting playback or switching to mobile-client format IDs. It labels deferred choices “Checked on download”; fresh playback permissions and links are acquired during downloading. Restricted and missing formats remain unavailable. The public mobile fallback retains its narrower WebM limitation; browser/permission failures and some mobile high-frame-rate formats can still be unavailable. Universal YouTube format support is not claimed.

The preceding 1.1.1 release verified six actual native app preview/download/archive workflows: best and lowest quality, explicit VP9/Opus, MP3, and automatic 720p and 1080p. Both high-resolution workflows save the complete approximately 634-second source at the requested height. Earlier failing probes remain recorded separately. Fresh short browser-profile locations were used after deeply nested test locations caused acquisition failures; the normal app profile location is already short.

## Common adult-site checks

| Site | Result |
| --- | --- |
| XVideos | Complete publisher-marked non-explicit sample, approximately 1,883 seconds; native preview, download, archive repeat, strict full decode and independent publisher-byte comparison pass. |
| XNXX | Complete publisher-marked non-explicit sample, approximately 667 seconds; the same checks pass. |
| xHamster | Native decoding of seven address versions and wrapped path segments. Complete publisher-listed SFW clip, approximately 22 seconds, passes best/lowest HLS download, archive/no-overwrite, full decode and independent publisher-byte comparison. The sample has no audio; no synthetic audio is added. Direct MP4 alternatives returned 403 and remain fallbacks. |
| Pornhub | Neutral sample from the publisher’s /sfw inventory, approximately 577 seconds, passes complete video and MP3 downloads, archive/no-overwrite checks, full decode and independent coded-audio/reference-MP3 comparisons. |

No sample media is included in the app package. These checks do not establish support for every adult site, login/age gates, paid media or DRM.

The common reader handles selected JW/Video.js/flashvars/html5player sources and xplayer H.264/HLS groups. The xplayer address decoder is C#, using bounded unsigned 32-bit operations and fixed independent vectors for seven versions/four edge seeds. It accepts full wrappers and the first wrapped path segment only, requires the publisher’s HTTPS CDN for decoded addresses, and never executes page scripts. Automatic xplayer selection follows the player’s HLS preference. Video sources do not invent an audio track; declared durations are verified before publication. Metadata-only JSON-LD can use its selected page player while retaining duration/title facts. Existing declared sources, unresolved main entities, restricted/live/clipped declarations and preview/protected players retain their boundaries. Invalid/unsupported encoded addresses never become relative-page media requests or advertising fallbacks. The two signed player pages request fresh HTML and omit an automatically invented self-referrer; explicit caller referrers and the media-delivery referrer remain intact.

## Verification and packaging

For the 1.1.6 quality changes, all 3,616 core native engine checks and all 204 application regression checks pass. A targeted native/media run passes 20 checks, including compressed-packet preservation for default AAC, Opus, FLAC and H.264/Opus output; 25 focused Best-selection and audio extraction checks also pass, including decoded-sample comparisons. The Release build has zero warnings and errors.

The preceding 1.1.5 full optional native engine/media run passed all 5,022 checks: 3,607 core checks and 1,415 generated-media checks, including the catalog and HDR cases. Its 200 application regression checks also passed. Seven added engine regressions cover publication overlap, receipt sharing/delete-pending conflicts, cancellation, read-only protection, worker drainage and console-independent UTF-8 decoding. The original intermittent cases also pass repeated focused runs. These generated and fixture-based checks do not expand the earlier live-site or real-account claims.

The preceding 0.224 two-site batch strictly decodes nine complete reference/source/output files and passes eleven independent comparisons across four successful workflows. Both xHamster qualities and the Pornhub video match independently fetched full publisher bytes and coded pictures. Pornhub’s kept high-resolution audio packets match the independently fetched rendition’s complete audio; MP3 packets and PCM match an independent 192-kbit conversion reference. MP3 conversion is not described as lossless. No preview or partial source counts as success.

The preceding 0.224 two-site results span separate runs, with earlier Pornhub 410/412 failures retained. Version 0.226 adds automatic renewal for these public Pornhub players: HTTP 401/403/410/412 can trigger a fresh read of the same public watch page, short cancellable backoff, and replay of the failed read. Renewal uses cache directives and a fresh page query; publisher redirects back to the canonical video remain supported. CDN hosts/signatures may change only within the publisher CDN. The video asset path, selected rendition, duration and adaptive segment identities must remain consistent.

Each resource permits at most six replays, with twelve issuer refreshes across the job. Ordinary 404 responses and unrelated provider statuses keep their existing behavior. An observed adaptive graph is validated before the renewed mappings commit; concurrent expired requests share one generation, and already completed media segments retain their checkpoint identity. Revoked access, changed videos/quality/timing, previews, protected sources and foreign redirects fail without publishing partial output.

The preceding 0.226 normal app-networking matrix verified complete public non-explicit video and MP3 downloads with archive/no-overwrite repeats. Five source/reference/output files pass strict full decoding and seven independent comparisons. The publisher reference from the preceding batch is reused only after verifying its exact preserved hash; coded video/audio and independently converted MP3 output match. A separate traced run recovered from 6 actual CDN expiry responses. Owned media tests cover master expiry, converted audio and expiry after a completed HLS prefix; contract tests cover child manifests, keys/maps, simultaneous failures, stale refreshed manifests, cancellation and the retry bounds. Additional public fault injection exhausted its bound when the real CDN continued rejecting links; that failure is retained. Availability of every issued CDN address is not guaranteed. Prior YouTube/site evidence remains preserved separately.

The self-contained Windows package is checked with a fresh isolated data folder and only Windows directories on PATH. Native readiness, empty history and the rendered main screen are verified. No yt-dlp or Python installation is needed. ffmpeg remains the media-processing tool, and YouTube browser acquisition uses Microsoft Edge WebView2. Existing settings/history remain in their normal locations. Earlier installer packages are preserved.

The earlier live-site batch audited pre-existing uncommitted files against its start-of-batch hashes and retained exact original snapshots. Its detailed evidence remains in the workspace's work/simple-e2e-batch folder. The 1.1.6 quality and packaging updates preserve existing work. Earlier verification remains historical evidence for its recorded release.
