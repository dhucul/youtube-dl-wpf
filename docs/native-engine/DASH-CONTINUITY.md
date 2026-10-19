# Native live DASH continuity

Version 0.166.0 selects unique compatible renamed DASH representations across periods while pinning accepted IDs within each period and retaining width/language/role/channel facts. Shared renewal protects used resources, retires unavailable unused resources, and accepts ended metadata only after fresh complete adaptive graphs and declared DASH end coverage. Literal outer DASH IDs no longer leak into inner selection. All 2,936 native checks (2,053 core + 883 media), 76 app and 73 focused checks passed; Release build has zero warnings/errors. Controlled fresh public DASH/HLS workflows verify per-period renaming, unused-format retirement and complete-snapshot ending with zero-HTTP archive repeats. Full replacement remains open. See [representation and provider contracts](REPRESENTATION-CONTRACTS.md).

Version 0.124.0 adds live HLS/DASH caption capture, conversion/saving/embedding and supported decoded DASH period/window clipping. All 1,887 native and 74 app checks passed. Two actual public paired recordings crossed two periods, saved/embedded 72 and 12 cues and produced 12 fully decoded media/source files; all main/original coded packet inventories matched. Generated clipping preserves exact decoded video pixels and lossless audio samples. AAC priming/delays survive final remux. Missing-clock HLS captions, other clipped codecs/advanced transport and broader private/provider/full parity remain pending. See [live captions and clipping verification](LIVE-CAPTIONS-CLIPPING.md).

Version 0.122.0, October 11, 2026. Existing working changes remain uncommitted. This checkpoint expands the shared transport pipeline; the complete native replacement and production cutover remain unfinished.

## Implemented scope

Clear MP4 live DASH can now span multiple contiguous periods. Period starts and ends resolve from explicit timing, the preceding duration or the next start. The availability window applies across the presentation, including periods with no currently available segments. Announced periods may contain an empty pending timeline. Expired periods can disappear from refreshed manifests without renumbering captured segments or discontinuities.

Selected adaptation/representation identities remain stable across periods. A new period may provide a different initialization resource and reset its sample clock. Each period retains its own initialization, and local joining verifies actual codec, dimensions, channel layout, time base and decoder configuration before publication. Initialization changes within a period, mutated declared timing, reused period identities, missing selected roles, gaps, overlaps and incompatible decoder changes fail. Separate audio/video retain the shared capture window, synchronized period boundaries, cancellation/draining, retained components and transactional publication introduced in the preceding batch.

Live SegmentList supports inherited initialization, media and initialization byte ranges, finite or bounded repeated timelines, fixed durations, presentation offsets and start numbers. Timeline and URL counts must agree. Range reads retain existing exact-response/validator checks. Fixed-duration SegmentTemplate now supports shifted media origins with Number and Time URL templates. Representation, period, segment, aggregate and session limits bound processing.

The timing implementation follows the [DASH-IF timing model](https://dashif.org/Guidelines-TimingModel/). The runtime uses independent C# and ffmpeg for local inspection, joining and decoding.

## Verification

The Release solution build passed with zero warnings or errors. All 1,851 native checks passed: 1,362 core and 489 media checks. All 74 app checks passed, including four browser checks. This batch adds 17 protocol/core checks and 13 generated-media checks; the focused run passed 37 checks including seven common foundation checks.

Generated complete media verify period clock resets, separate audio/video, timeline and fixed-template addressing, explicit lists and ranged combined objects, late entry after the presentation beginning, audio-only/video-only selection, ended snapshots with expired periods removed, retained sources, callbacks and archive repeats. Successful two-period videos contain every one of their 200 source frames. Failure cases verify unchanged prior output and archives, zero completion callbacks and cleanup for changed codec, missing audio, a period gap, changed initialization, incompatible actual decoder configuration and cancellation.

The actual native WPF adapter recorded a [DASH-IF public multi-period test stream](https://livesim2.dashif.org/livesim2/periods_60/segtimeline_1/mup_1/testpic_2s/Manifest.mpd), using the publisher's [documented simulator parameters](https://github.com/Dash-Industry-Forum/livesim2/wiki/URL-Parameters). It captured 36 video segments across periods P29862574 and P29862575, with 48 manifest reads. Both the published MP4 and retained original are 1,397,800 bytes and independently fully decode to 72 seconds and 2,160 video frames. Their ordered inventories contain exactly the same 2,160 coded video packet payloads. An archive repeat opened no HTTP session and emitted no callbacks.

The external result establishes multi-period video playback. Separate audio/video transitions are generated-media verified. Some real audio layouts overlap a period boundary and require clipping or duplicate-boundary handling; this batch does not establish those layouts. Real private-account verification remains deferred by the user's prior decision.

## Remaining work

- Period-boundary sample/segment overlap and clipping, period connectivity, selected representation renaming and actual codec conversion.
- Active live HLS/DASH captions and complete caption continuity across periods.
- Dynamic SegmentBase/indexed addressing, static multi-period files outside the live-session projection, non-MP4 media, low-latency partial segments, advanced availability offsets and remote periods.
- Broader provider account/password/playback renewal, especially YouTube and Vimeo, and full option/default/provider interaction evidence.
- Full frozen 1,731-key, 126-app-option and 276-long-option coverage, native WPF startup, yt-dlp removal and clean standalone packaging.

See the [complete coverage backlog](COVERAGE-BACKLOG.md). No extractor or option gained a full-parity claim from this shared batch.
