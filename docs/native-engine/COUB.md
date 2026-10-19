# Native Coub public loops

Native engine 0.83 adds independently implemented C# extraction for public Coub view/embed links. It uses the current public metadata API and supplied media URLs, without Python, yt-dlp, JavaScript execution, browser capture or login. ffmpeg remains for native bounded rendering/verification.

## Implemented behavior

- View/embed, `www` and HTTP aliases normalize to the exact requested public API permalink. Public numeric identity, supported simple-record type, completed/published/banned/visibility and explicit age state are checked. The current nullable admin restriction field is accepted only when explicitly absent/null; missing unsupported access state is not silently treated as public.
- Offered HTML5 high/medium/low video and audio inventories, source byte counts, supplied dimensions, public channel/title/artwork, unchanged media query values and permalink archives. Unsupported types, origins, containers, dimensions, sizes, private/unfinished/banned/access-gated records, malformed/repeated JSON and redirected API identities fail explicitly.
- Default/literal paired sound downloads repeat the supplied video cycle through one complete audio track. The finite export stops after the longer source timeline, so shorter audio repeats through at least one full video cycle. Sources are streamed/copied locally without inventing media or external URLs. The renderer bounds source durations to 600-second video / 3,600-second audio and at most 1,000 cycles in either direction; unsupported or unmeasurable layouts fail before publication.
- This finite export preserves the site's observed independently looping video/audio behavior for one complete source timeline. It does not attempt an infinite interactive playback file. The reported API duration remains the original video-cycle duration; the combined file can be longer.
- Explicit video-only or single-video selectors retain one source cycle. Audio-only saves the complete offered audio track. Silent clips retain their normal video duration with no fabricated soundtrack. Explicit paired quality selection uses the same bounded loop policy.
- Native quality/height constraints, compatible merge/container selection, metadata/artwork/info JSON, full local decoding/duration/role checks, exact original video/audio retention, atomic publication and stable archive retries. Known permalink archives skip all discovery and transfer. Anonymous public sessions isolate caller cookies/authorization.
- The provider never derives source links from templates or guesses mobile audio chunks/share variants. Current public share MP4s inspected here are video-only, so they are not misrepresented as complete audio/video files.

## Verification on October 9, 2026

Thirteen deterministic provider checks cover canonical aliases, identity/access/age state, malformed and repeated metadata, exact descriptors/dimensions/origins, literal queries, normal/explicit/single/audio/silent selection, credential isolation, archives, cancellation and API denial/redirects. Eight generated-media checks verify default and explicit pairs, shorter-audio looping, video-only/single-video/audio-only/silent output and changed-source-size rejection. Successful generated outputs/originals fully decode; exact source bytes and every copied/repeated encoded video/audio payload and order are verified. Archive repeats request no old media or new completions.

Live normal `NativeEngine.DownloadAsync` on [public Coub `2zrym`](https://coub.com/view/2zrym):

| Workflow | Processed / original bytes | Complete playback |
| --- | ---: | --- |
| Default full sound/loop movie | 22,315,232 / 22,315,161 merged original | Matroska H.264 1280×960; 3,010 repeated video frames, 4,611 decoded MP3 audio frames; 120.477000 seconds |
| Video-only | 1,611,356 / 1,610,676 | H.264 1280×960; all 250 original video frames; silent, 10.000000 seconds |
| Audio-only, best/copy | 2,892,187 / 2,892,067 | Complete MP3; all 4,611 decoded audio frames; 120.425556 seconds |

The combined workflow separately retained the original 1,610,676-byte ten-second video and 2,892,067-byte complete audio. All **8** saved media files passed strict full decoding. Independent packet inspection confirmed all 3,010 output video packets repeat the original 250-packet cycle exactly, and all 4,612 encoded audio packets retain their original payloads/order. Artwork and info JSON were saved. Combined output reported four completions; each single-track output reported two. Every immediate archive retry reported zero and required no discovery.

The first live probe returned transient I/O errors without detailed diagnostics. A subsequent diagnostic retry completed all three workflows and the verification above. This does not establish a cause for the earlier errors or complete output-recovery coverage.

The publisher's inspected page, API and player scripts establish the separate ten-second video / approximately 120-second audio source layout and independent loops. Both its API share URL and the page's published share endpoint returned a silent 250-frame 640×480 MP4 in the initial inspection; those files are source-layout evidence, not a complete default movie.

## Remaining coverage

This is partial coverage of the catalog's `Coub` key. Recoubs/remixes/edit layouts, mobile/segmented sources, additional hosting/encoding and current media-block/source shapes, private/account/age/live access, longer/other timelines and loop synchronization, collections/channels/search, original-sound/source composition semantics, captions/chapters/richer metadata, broader containers, expiring-link/resume and output-recovery workflows still need implementation and verification.

No working 9GAG or Newgrounds workflow was established in this stage: their tested public pages returned HTTP 403. Those catalog providers remain pending. Production WPF still uses yt-dlp; full catalog/option parity, native cutover, old dependency removal and standalone packaging remain unfinished.

## Primary references and local evidence

- [Public Coub page](https://coub.com/view/2zrym)
- [Public permalink metadata](https://coub.com/api/v2/coubs/2zrym)
- Current Coub-hosted player scripts inspected directly for supplied-track selection and independent loop behavior.
- [FFmpeg stream-loop, duration and stream-copy documentation](https://ffmpeg.org/ffmpeg.html)

Implementation: `src/YoutubeDl.Engine/Providers/Coub/`, internal provider loop opt-in in `MediaCandidate.cs`, paired download dispatch in `NativeEngine.cs`, and bounded local renderer in `FfmpegTool.Loops.cs`. Tests: `CoubChecks.cs` and `FfmpegChecks.Coub.cs`. Local live evidence: `work/coub-probe/live-probe.log`, `packet-verification.json` and saved media/verification JSON in its Release output. Scratch public responses/scripts/reference downloads are not repository fixtures or runtime dependencies.
