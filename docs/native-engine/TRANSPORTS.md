# Native transport implementation

## Verified scope

The C# engine now implements static HLS AES-128 segment decryption, explicit and implicit byte ranges, initialization maps, key rotation, fragmented MP4 output, and separate audio renditions. It selects the default audio rendition in the chosen variant's audio group. Combined output uses ffmpeg with staged replacement; audio-only and video-only requests omit the other track's segments. Missing ffmpeg fails before media segment transfer. Compatible static MPEG-TS and fragmented-MP4 timeline periods can be joined while preserving frame-rate changes and stable separate-track offsets; see `HLS-DISCONTINUITIES.md`. Unsupported sample encryption, remaining discontinuity layouts, missing fragments, nested masters, and live recording continue to fail explicitly. Downloaded initialization/media sections are staged and the destination is replaced only after transfer completion.

Static DASH supports inherited BaseURL/SegmentTemplate settings, numbered and timestamped URL templates, finite SegmentTimeline repetitions, explicit SegmentList resources, byte ranges, initialization sections, and separate-track merging. MP4 SegmentBase/sidx addressing now supports bounded flat indexes with exact resource ranges and entity validation. WebM/nested indexes, shifted periods, protection metadata, dynamic presentations and multiple periods remain pending. URL expansion and segment counts are bounded. See `INDEXED-DASH.md` for the implemented boundaries and live verification.

Generated AES-encrypted HLS, fragmented MP4 HLS, separate HLS renditions in all three download modes, and segmented DASH video/audio passed native transfer, ffprobe inspection, and ffmpeg decoding. These tests use deterministic HTTP responses backed by generated local media. They do not establish live site coverage. Requested audio-language preference and full codec/container compatibility still need implementation.

The native PeerTube provider's live HLS path downloaded `73556243-7a9b-496c-a740-f80e42ee0ad9` from Framatube on October 7, 2026. Its complete 819,934-byte output fully decoded with H.264/AAC, at 1280×888 and a 9.237333-second container duration. This verifies one public HLS workflow independently of yt-dlp.

## YouTube SABR components

Native components now encode protobuf playback requests, decode UMP/protobuf responses, retain playback cookies and context policies, validate streaming redirects, assemble interleaved media segments, reject incomplete/retransmitted mismatches, and write ordered initialization/media into staging streams. The HTTP session loop bounds requests, redirects, stalled responses, and body-read delays. A track is complete only when its declared final media segment was received.

Initialization state is now based on bytes actually written into each selected track. Request field 2 acknowledges already received initialization; sending every selected format in a fresh request suppressed the initialization segments in a live probe. Fresh requests now leave it empty. Follow-up requests report received formats plus independent buffered start/duration and first/last segment indexes. The session derives that state from its sink rather than trusting caller-supplied progress. Tracks require separate empty staging streams and continuous, increasing timelines with one-millisecond rounding tolerance. Available declared end times are checked alongside final segment numbers.

The native YouTube provider now establishes an IOS playback context and connects its clear SABR formats to `SabrDownloader`. The same session downloads both selected tracks, then ffmpeg merges them when required. Audio-only and video-only selectors transfer only the selected track. Controlled HTTP shares cookies, proxy and configured headers with discovery but checks redirects manually. Neither an external player process nor the optional managed JavaScript interpreter is needed for the verified IOS sample.

Tick-based media headers and track duration metadata are supported alongside millisecond fields. Exact timescales and contiguous tick ranges are retained in follow-up requests; inconsistent units, discontinuities and invalid/overflowing clocks fail. Network/body interruptions discard pending buffers and retry within the configured limit, preserving completed segments. Disk write failures and three stalled responses terminate without retrying. Explicit authorization denial (status 3) always stops; the integrated session may continue temporary playback (status 2) until it completes or is denied. No new sessions are started to collect fragments after a denial.

On October 8, `NativeEngine.DownloadAsync` completed public `jNQXAC9IVRw` through this path with explicit, default and DRC selection. Its 745,215-byte metadata/chapter output fully decoded as H.264/AAC, 320×240, 19.063583 seconds, 284 video frames and 821 audio frames. A longer `aqz-KE-bpKQ` session progressed beyond 50 seconds before status 3; that incomplete transfer was not published. Full-length authorization and broader live coverage remain unfinished.

Native page/API attestation readers and interpreter fetching are implemented and tested. A scratch managed challenge experiment returned snapshot responses but no usable playback token. No browser dependency has been introduced. See `PLAYER-RUNTIME.md` for the boundary between completed URL transformations and unfinished authorization.

Earlier Web-client diagnostics received 734,588 bytes after initialization state was corrected. Assembly produced a 246,829-byte H.264/426x240 fragment (604 decoded frames, 20,133 ms received range) and a 486,669-byte AAC/44.1 kHz fragment (1,290 decoded frames, 29,954 ms received range). Headers advertised approximately 634.6 seconds and final segment counts 123/64, but only four video and three audio segments arrived. They remain diagnostic partial files. Completion requires received segment counts and available end times, irrespective of the container header's advertised duration.

## Remaining work

- Establish sustained authorized YouTube playback, finish attestation/token generation, and extend supported player layouts.
- Add session expiration/refresh, durable resumable state and broader live verification.
- Add live recording, remaining HLS rendition cases/discontinuities, WebM/nested DASH indexes, shifted/edited periods, remaining addressing modes, and multi-period handling.
- Complete channels/remaining playlists, authentication, remaining captions/post-processing options, and dedicated site providers.
- Switch the production app after compatibility and live workflows are verified; remove the old yt-dlp backend.

Wire/protocol references: [RFC 8216](https://www.rfc-editor.org/rfc/rfc8216), [DASH-IF timing guidance](https://dashif.org/Guidelines-TimingModel/), [protobuf encoding](https://protobuf.dev/programming-guides/encoding/), and the [published GoogleVideo message schemas](https://github.com/LuanRT/googlevideo/tree/main/protos/video_streaming). The runtime components are local C# implementations and have no yt-dlp or Python dependency.
