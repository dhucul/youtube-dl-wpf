# Synchronized live HLS, dynamic DASH and playback renewal

Version 0.124.0 adds live HLS/DASH caption capture, conversion/saving/embedding and supported decoded DASH period/window clipping. All 1,887 native and 74 app checks passed. Two actual public paired recordings crossed two periods, saved/embedded 72 and 12 cues and produced 12 fully decoded media/source files; all main/original coded packet inventories matched. Generated clipping preserves exact decoded video pixels and lossless audio samples. AAC priming/delays survive final remux. Missing-clock HLS captions, other clipped codecs/advanced transport and broader private/provider/full parity remain pending. See [live captions and clipping verification](LIVE-CAPTIONS-CLIPPING.md).

Version 0.122.0 adds compatible multi-period clear MP4 live DASH, timed/ranged SegmentList, shifted fixed-template origins and announced empty timelines. All1,851 native and74app checks passed. Actual public adapter capture spanned2periods: two72s/2,160-frame files fully decoded, with every coded video packet retained in order. Separate track transitions are generated-media verified; overlapping boundary clipping and active captions remain pending. See [DASH continuity verification](DASH-CONTINUITY.md).

Version 0.120.0, verified October 11, 2026. All previous working changes remain uncommitted. These are shared native pipeline improvements; full extractor, option and production-release parity remain unfinished.

## Implemented behavior

Separate live HLS audio/video now capture concurrently against one shared program-time window and one byte-rate budget. The parser retains program-date-time anchors and extrapolates within each discontinuity period. Track sequence counters may differ; they are not treated as a shared clock. Missing/inconsistent clocks, missing intervals, changed identities, unmatched discontinuities and changing cross-period clock relationships fail explicitly. When one capture fails, the other is cancelled and drained before cleanup. Ended events close the common window, and retained source components remain separate from the merged publication.

The merger preserves the declared audio/video delay and verifies both roles and their coverage. An offset test exposed AAC priming that became audible when a positive audio delay made its first coded packet nonnegative. The native MP4 edit-list repair retains the original coded packet and restores the source priming trim without re-encoding; movie size and media offsets remain unchanged.

Dynamic clear MP4 DASH now uses the same capture and synchronization path. The parser supports inherited SegmentTemplate, finite SegmentTimeline, availability-bounded negative repeats, fixed-duration Number/Time templates, distinct adaptation/representation identities, presentation offsets for timeline addressing, a bounded DVR window and full-MPD refresh. Only complete segments inside the published availability window are offered. The selected tracks keep stable identities and initialization across refreshes. A transition to a compatible static ending snapshot closes the recording; required tracks cannot silently disappear or become a successful single-track substitute.

Private playback authorization is renewable and job-owned. PeerTube is the first consumer. Before each scoped resource read, an expired/near-expired token is renewed. A 401/403 triggers at most one retry for that request. Concurrent rejections of one token generation share one renewal. Account refresh remains separate and bounded. Token URLs are canonicalized before they enter refreshed manifests or metadata; recognized older tokens can be replaced, while foreign/conflicting tokens are refused. Read-only headers, including ranges, remain attached on retry. Tokens cannot cross their exact resource/video-directory scope or follow redirects, and token state is cleared with the owning job.

The runtime remains independent C# with ffmpeg for local processing. Protocol references: [HLS timing/synchronization](https://www.rfc-editor.org/rfc/rfc8216.html), [DASH-IF timing model](https://dashif.org/Guidelines-TimingModel/), [PeerTube API](https://docs.joinpeertube.org/api/rest-getting-started).

## Verification

The Release solution build passed with zero warnings/errors. All 1,821 native checks passed: 1,345 core and 476 media checks. All 74 existing app checks passed: 70 regular and four browser checks. This batch adds 14 protocol/core checks and seven generated-media checks. The focused batch passed all 28 checks including seven common foundation checks.

Coverage includes program-time extrapolation, unequal sequence counters, absent/unrelated clocks, DASH availability/addressing/repeats/identity/protection boundaries, actual concurrent rejected-token generations, expiry, bounded denial, redirection, cancellation and secret-free diagnostics. Generated complete media cover paired HLS, a declared audio delay, ended events, dynamic DASH, missing audio intervals, cancellation of both workers and private paired playback with one token renewal. Output/archive/callback protection and retained components are verified.

Four external native-adapter workflows used the publisher's [documented public live sources](https://docs.unified-streaming.com/best-practice/dai.html). Each requested 12 seconds and recorded a 13.44-second complete-segment window. HLS sequence counters and DASH publish times advanced over repeated reads. All archive repeats skipped before opening HTTP and emitted no callbacks.

| Workflow | Main output bytes | Retained video / audio bytes | Main decoded frames |
| --- | ---: | ---: | --- |
| Separate fMP4 HLS audio/video | 964,655 | 836,314 / 127,462 | 336 video, 630 audio |
| Dynamic DASH audio/video | 970,371 | 851,747 / 118,473 | 336 video, 630 audio |
| Dynamic DASH audio conversion | 149,648 | — / 126,579 | 630 audio |
| Dynamic DASH video only | 849,904 | — | 336 video |

All nine main/source files independently fully decoded without diagnostics. For both paired recordings, independent packet inventories proved exact ordered equality of all 336 video and 630 audio payloads between each retained source and merged output. Retained original fMP4 may carry an absolute source clock; actual decoded spans and frame counts verify the recording window rather than its reported container end time alone.

Real private-account playback remains unverified by the user's prior explicit choice. Private renewal is protocol- and generated-media-verified; public live results are not counted as private-account evidence.

## Remaining work

- Live DASH multi-period continuity, SegmentList/SegmentBase, non-MP4 and low-latency incomplete/partial segments; advanced availability-offset and shifted fixed-duration modes.
- Active live captions, missing-clock HLS alignment, changed codec/initialization/signature identities and more discontinuity layouts.
- More providers using the renewal contract, real private-account evidence, cross-origin/custom-port private resources and protected caption/artwork scopes.
- Complete long-recording, limits, resume/date/option and provider interactions.
- Full 1,731-key/126-app-option/276-long-option coverage, native production startup, yt-dlp removal and clean packaged verification.

See [complete coverage backlog](COVERAGE-BACKLOG.md). The next grouped workstreams are broader provider account/playback refresh, live period/caption continuity and the YouTube/Vimeo access/playback matrix.
