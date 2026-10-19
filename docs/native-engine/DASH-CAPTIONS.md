# Native DASH captions

The native DASH reader now retains subtitle languages and representation details. Requested tracks enter the existing language selector, sidecar publication and embedding pipeline. Existing provider-supplied captions retain priority over a duplicate manifest language. Unselected captions are not downloaded; unsupported codecs fail only when selected.

## Implemented scope

- Plain WebVTT and TTML resources declared by a static DASH manifest.
- Fragmented MP4 WebVTT (`wvtt`) as a complete resource, indexed SegmentBase resource, SegmentTemplate sequence, or explicit/ranged SegmentList.
- Clear fragmented TTML (`stpp` and supported IMSC text profiles), using the same resource modes and native sample-window conversion. See `MP4-TTML.md` for verified scope.
- Initialization metadata, fragment defaults, 32/64-bit decode times, per-sample duration/size, signed composition offsets, fragment-relative and explicit absolute data offsets.
- Cue identifiers, settings, UTF-8 payloads, overlapping cues and contiguous continuation merging. Presentation offsets and period duration clip the generated WebVTT timeline.
- Shared HTTP sessions and range/entity checks. Caption inputs are bounded to 8 MiB, converted output to 2 MiB, and sample/cue counts to 100,000.
- Resolved caption content passes through the normal subtitle writer without a redundant request for the original MP4 URL. Saving requires no external caption converter; embedding uses the retained ffmpeg pipeline.

The MP4 reader is original C# code. The installed ffmpeg build classified the public sample's raw `wvtt` track as data and could not extract it as a subtitle stream, so conversion is performed by the native reader before embedding.

## Validation

Ten deterministic checks cover cue text/settings/timing, empty and overlapping samples, clipping, wide/signed timestamps, offset bounds, protected and corrupt data, languages, unsupported selections, template/list/index paths, cancellation and preservation of previous output. A generated-media check verifies that converted captions embed as English `mov_text`, preserve their merged cue timing and decode back to the expected text.

On October 8, 2026, the native engine downloaded [the public Angel One presentation](https://storage.googleapis.com/shaka-demo-assets/angel-one/dash.mpd) with English and French subtitles. It saved two VTT files and embedded both as `mov_text` tracks tagged `eng` and `fra`. Each language had 16 cues, with its last cue clipped to 60 seconds. Extracting the embedded tracks reproduced the saved caption text for both languages. The complete video retained all 1,500 H.264 frames and 2,814 AAC frames; full decoding passed. The subtitled MP4 was 2,774,886 bytes and 60.021333 seconds.

## Remaining work

Non-fragmented MP4 sample tables, remaining TTML profiles/auxiliary data, complex edit lists, additional subtitle codecs, empty-line payload conversion, automatic-caption declarations, full representation/format preferences, transcript-only workflows, multi-period/live subtitles and broader production integration remain pending. HLS WebVTT rendition progress is documented separately in `HLS-CAPTIONS.md`. Protection metadata is refused rather than decrypted.

Container references: [W3C ISO-BMFF byte streams](https://www.w3.org/TR/mse-byte-stream-format-isobmff/) and [GPAC's documented WVTT/STPP container behavior](https://github.com/gpac/gpac/wiki/Subtitling-with-GPAC). The implementation does not depend on GPAC or its conversion code.
