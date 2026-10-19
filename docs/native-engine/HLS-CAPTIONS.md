# Native HLS subtitles

HLS masters now expose subtitle groups, rendition languages/default/forced flags, closed-caption declarations and segment durations. The selected video variant determines which subtitle group is eligible. The existing language selector chooses tracks; provider-supplied captions retain priority for duplicate languages. Unselected or automatic-only requests do not fetch authored HLS tracks.

## Implemented behavior

- Complete WebVTT rendition playlists, including initialization headers, byte-range segments and AES-128 header/segment encryption with explicit or media-sequence IVs.
- Timestamp maps in either attribute order, the required zero-map default when absent, 33-bit MPEG timestamp wraparound and a measured media start time.
- Cue IDs, settings, payloads, stable styles/regions, clipping to the media duration, exact duplicates, continuous cues and overlapping boundary-clipped repeats.
- Bounded caption input/output, key cleanup, range/entity checks, cancellation, and explicit errors for live/discontinuous or unsupported in-band caption workflows.
- Native subtitle saving and ffmpeg embedding, including explicit HLS video/audio pairs. A transport-stream input with automatic container selection is remuxed to MP4 when embedding subtitles.

ffprobe is already included and verified by the app's ffmpeg installer. It reads the downloaded source's start time before merge/remux removes the original clock. HLS rendition synchronization requires that tool; direct caption documents and C# DASH WebVTT conversion retain their existing independent saving paths. The WebVTT joiner quantizes the clock mapping to milliseconds so probe decimal rounding cannot add a millisecond to every cue.

Current bounds are 256 declared renditions, 10,000 subtitle segments, 8 MiB of caption input and 2 MiB of output. Extremely long individual segment/cue spans that make timestamp rollover ambiguous are refused. Styles that change after cues begin are also refused rather than applied retroactively.

## Verification

Nine deterministic checks cover group/duration parsing, timestamp maps, wrapping and negative media origins, initialization/style handling, duplicated cues, unsupported selections, missing timing tools and cancellation. Four generated-media checks cover ranged subtitles, encrypted initialization and segments, explicit audio/video pairs, and failure recovery. They verify that saved and embedded captions retain their expected timing and that failed captions do not publish or record a successful download.

On October 8, 2026, [the public Angel One HLS sample](https://storage.googleapis.com/shaka-demo-assets/angel-one-hls/hls.m3u8) downloaded through the native engine with English and French subtitles. A repeated French cue was shortened at a segment boundary; the native joiner merged the overlap. Both languages produced 16 cues and embedded as `eng`/`fra` `mov_text`. Extracted text matched the saved VTT for each language.

The complete output was 3,993,805 bytes, 320×240 H.264/AAC and 60 seconds. ffprobe counted 1,500 video frames and 2,813 audio frames, and full decoding passed.

## Remaining work

In-band CEA captions, other HLS subtitle containers/codecs, live updates, discontinuities, changing style timelines, advanced rendition preferences, automatic-caption identification and broader timing/container combinations remain pending. Full site coverage, production cutover and complete subtitle-option parity are still unfinished.

Protocol reference: [RFC 8216 WebVTT timing and rendition rules](https://datatracker.ietf.org/doc/html/rfc8216#section-3.5). The parser, caption joining and download coordination are original C# implementations.
