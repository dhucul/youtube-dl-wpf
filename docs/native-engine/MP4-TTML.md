# Native fragmented MP4 TTML captions

The native DASH pipeline now reads clear fragmented MP4 text samples identified as `stpp`, `stpp.ttml`, `stpp.ttml.im1t` or `stpp.ttml.im2t`. It shares MP4 box/range/timeline validation with the existing WebVTT reader and uses the independent C# TTML reader for XML interpretation. It emits standalone WebVTT without requiring ffmpeg. Other requested output conversions and media embedding use the existing subtitle pipeline.

## Implemented scope

- Single supported text tracks in complete fragmented resources, indexed SegmentBase files, SegmentTemplate sequences and explicit/ranged SegmentLists.
- Namespace lists in STPP sample entries, local data references and descriptive schema-location fields that are never fetched. Image profiles, auxiliary MIME payloads/subsamples, protected media and unsupported XML content remain explicit failures.
- Track IDs, 32/64-bit decode time, signed composition offsets, explicit/default sample sizes and durations, and relative/absolute resource positions. A missing matching `trex` is usable only when each sample obtains valid duration/size from its own fragment/run. Defaults belonging to another track are never used.
- TTML document times remain on the track timeline. Each document is intersected with its sample's composition interval before applying the DASH presentation offset and final period window. The sample extent bounds otherwise indefinite text. Valid empty documents represent caption gaps; continuing identical text can join across adjacent samples without dropping concurrent duplicates.
- Initialization validation occurs before segmented caption payload transfer. Separate caption files use bounded parallel fetching and ordered consumption. The shared byte budget is checked during reads; cancellation/failure drains workers before returning. Ranged caption sequences retain ordered entity-validator state. Converted output and temporary/index files follow existing protected publication behavior.

Inputs remain bounded to 8 MiB per selected caption track and converted output to 2 MiB. Existing sample/cue limits, TTML structural/work limits and the configured 1–32 fragment concurrency range apply. Source timestamps may use a large track epoch; output timestamps are shifted and rounded to millisecond precision.

## Verification on October 8, 2026

Eleven deterministic checks cover sample-window clipping, document-time alignment, large/signed timestamps, empty/concurrent cues, namespace lists, missing/foreign defaults, protected/invalid data, absolute offsets, codec profiles, indexed/template/list addressing, caption-only transfer, parallel ordering, failure/cancellation draining, aggregate read limits and preservation of published outputs.

A generated-media check saves and embeds sample-clipped TTML captions in MP4. It retains both expected cue intervals and text, English language metadata and all 40 generated video frames; full video decoding passes. Existing tests continue to verify that image-based subtitle profiles fail before media access when selected.

The public [DASH-IF one-hour STPP presentation](https://livesim.dashif.org/vod/testpic_2s/Manifest_stpp.mpd), listed in the [reference asset catalog](https://livesim.dashif.org/vod), completed through native caption-only downloading:

- 1 manifest + 1 initialization + 1,800 caption fragment requests, with peak concurrency 4 and no active requests on return.
- A guard refused every audio/video URL; the job produced no media output.
- One 180,008-byte WebVTT document with 3,600 cues spanning 00:00:00.000 through 01:00:00.000.
- Every cue was checked to occupy its expected one-second interval, and every generated timestamp label matched that cue's start. ffprobe independently read all 3,600 WebVTT packets.

The published initialization contains a namespace list and a default entry for a different track. Its caption fragments supply their own matching track ID, duration and size. A replay of the first two published fragments verified their four cue intervals before the complete one-hour check.

## Remaining work

Non-fragmented sample tables, complex/shifted edit lists, auxiliary/image TTML, additional profiles, full TTML visual presentation, additional source refresh/retry behavior, raw original-container retention, exact representation/format preferences, live and multi-period subtitle timelines, and broader provider verification remain pending. This verifies a complete caption-only public workflow; full media embedding is verified with generated media, not the full one-hour public presentation.

Primary references: [DASH-IF text profiles](https://dashif.org/codecs/text/), [W3C's TTML track/sample timing clarification](https://www.w3.org/2021/04/29-tt-minutes.html), and [ISO-BMFF byte streams](https://www.w3.org/TR/mse-byte-stream-format-isobmff/). No external extraction or subtitle-parser runtime was added.
