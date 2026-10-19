# Live overlap, period timing and provider sessions — 0.164.0

This batch extends the independent C# engine. Full replacement acceptance remains open, and production startup continues to use YtDlpEngine.

## Period and renewal behavior

Adaptive renewal now prepares every refreshed graph before committing it. Root aliases are validated first; graph and root mappings become visible under the same owner lock. Failed issuer reads, invalid root collisions, cancellation, disposal and stale staged graphs preserve the previous usable generation. Concurrent rejections still share one renewal, including when the issuer returns the same URL.

Live DASH retains exact complete-source presentation timing alongside its visible clipped interval. A period closing inside a source segment can shorten that visible interval while renewal keeps the full signed source identity. Known starts/closing durations, numbered/time-addressed slots, MIME/codec/range facts and the publish clock still prevent changed or backwards timelines. Period history stays bounded at 1,000 entries. Ended single-period snapshots retain their prior live availability context.

Time-addressed sliding timelines can reset implicit ordinal numbers. Their declared time/timescale address supplies the slot identity; `$Number$` templates retain number-based checks. The initial validated binding of a physical graph resource remains stable within the job when another period references the same resource. This prevents a legitimate period closure from invalidating an already captured media or initialization receipt. Cross-session replay of renewed shared graphs remains a separate open contract.

These choices follow the distinction between source samples and visible period intervals in the [DASH-IF timing model](https://dashif.org/Guidelines-TimingModel/). Representation renaming is still conservative: the model requires identical representation sets for signaled connected adaptation sets, so the batch does not infer continuity merely from similar codecs.

## Decoded clipping

The clear single-track MP4 path now accepts H.264, HEVC, VP9 and AV1 planar 8-bit/10-bit YUV video, and AAC, ALAC, FLAC and Opus audio. Compatible dimensions, pixel formats, channels, channel layouts, sample rates and sample precision are checked across periods. Unsupported formats, 12-bit video and lossless audio above 24-bit precision remain explicit refusals.

Video is decoded into lossless FFV1 intermediates and emitted as lossless H.264 with the source pixel format. FLAC and ALAC use lossless ALAC; AAC and Opus use one final AAC encode at 256 kb/s after trimming. This changes the output codec for newly supported sources; complete encoded source periods remain retained when requested. Clipped intervals go directly through decoded joining rather than first concatenating encoded periods through the stricter stream-copy path. Unclipped joining retains its existing decoder-configuration checks. The decoded path returns original period media clocks for caption alignment.

## Longer provider sessions

Shared renewable leases now allow up to 256 renewal attempts per job. Other resource, alias, graph and lifetime bounds remain active and can be reached sooner. Each refused HTTP request still permits only one automatic replay. Deterministic fixtures exercise 24 successive generations and concurrent rejection for Wistia, JW, Brightcove and Kaltura consumers, plus HLS, DASH and progressive shared leases. Failed generations retain valid prior mappings. This extends local session behavior; it does not establish a private issuer/account workflow.

## Verification

All **2,870 native checks** (2,009 core + 861 media), **76 app checks** and **61 focused checks** passed. Release build has zero warnings/errors. The batch adds 32 core and22 media checks. Fresh public DASH/HLS recordings each survive24 preliminary fixture renewals and one injected authorization refusal/renewal, then repeat their archives with zero HTTP/callbacks. Ten files independently fully decode, and four exact ordered main/original packet inventories match. Public/final engine SHA values match.

The batch adds 32 core and 22 generated-media checks. They cover staged/atomic renewal, period closure/removal/end, exact fractional clocks, true numbered-source mutation versus time-addressed ordinal resets, shared recovery bindings, range/codec/origin/timing refusal, long generation suppression, cancellation/disposal/waiters and the 256-attempt bound. Six HEVC/VP9/AV1 8/10-bit cases compare all 40 decoded source frames exactly; 24-bit FLAC compares all decoded source samples exactly. Native paired HEVC/Opus and 10-bit VP9/FLAC jobs renew across a 1.5-second period boundary, retain complete source periods, fully decode and preserve all 40 video frames. Archive repeats issue no HTTP or new callbacks. Rejected layouts preserve prior output files.

Fresh public native DASH and HLS captures each pass 24 preliminary fixture renewals, one injected media rejection/renewal and a zero-HTTP archive repeat. The DASH capture uses 360 periods per hour (10-second periods) and captures at least 12 seconds. Its configuration follows the simulator's [published URL parameters](https://github.com/Dash-Industry-Forum/livesim2/wiki/URL-Parameters). Ten published/original/component/source-period files independently fully decode; four exact ordered main/original video/audio packet inventories match. Those packet comparisons verify metadata preservation of already assembled/clipped media. The controlled issuer and authorization refusal are fixtures; no real account, saved credentials or private issuer is accessed.

Evidence lives in `work/live-period-session-batch` and `work/live-period-session-probe/bin/Release/net10.0/verified-0.164.0-final`. Public proof and final repository engine hashes must match. Earlier failed probe captures and diagnostic logs are preserved. The public sliding-time test exposed the implicit-number regression before final acceptance; generated fixtures now cover it.

## Remaining work

Selected representation renaming/period connectivity, advanced decoder/color changes, additional codecs/containers, indexed/non-MP4/low-latency/advanced availability, missing-clock captions, cross-session renewed-graph recovery and provider live-ended metadata transitions remain open. Broader real issuer/session/player/proof/signing contracts, defaults/locale and full catalog/options/packaged-production acceptance follow. Inventory lineage/limits, legacy changed anchors and cross-source/storage interleavings stay on the backlog. Real-account verification remains user-deferred. Frozen scope stays 1,731 extractor keys, 126 app options and 276 long-option names, with no new full-parity claim.
