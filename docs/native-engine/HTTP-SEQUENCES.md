# Ordered HTTP media files

Some providers expose one complete video as an ordered collection of standalone media files. `MediaKind.HttpSequence` keeps that collection as one downloadable format. It is distinct from a playlist of separate videos and from alternative quality renditions. Format inspection reports `http_sequence` and does not offer the first chunk as a complete direct URL or claim that source-byte totals equal the final container size.

## Implemented behavior

- Bounded inventories of 2–4096 MP4 or FLV files. Bilibili `durl` parsing verifies consecutive source order, distinct resource URLs, positive durations, a consistent container and a duration sum matching the complete playback response. Single-file responses retain their existing progressive path.
- Bounded concurrent transfers through the shared HTTP session, byte limiter and ordered worker scheduler. Each file uses its declared URL without changing access parameters, HTTP resume/no-continue policy and independently configured fragment retries. Completion of a later transfer never changes concatenation order. Failures cancel and drain active workers.
- Declared source sizes are checked when available. Every file must pass full decoding, required-media-role checks and declared-duration verification. ffprobe then checks that all files have compatible codec, dimensions, audio layout, time base and decoder configuration. Incompatible files fail explicitly rather than dropping tracks or silently converting them.
- ffmpeg concatenates only fixed local staging names using a generated safe file inventory and restricted file/pipe protocols. The joined media must preserve its stream identities and pass full decoding and duration verification before publication.
- Existing native metadata, related-file output, container/audio/video processing, archive, no-overwrite and atomic publication apply to the complete joined file. Keep-original retains each exact source chunk with numbered `part` suffixes; additional processed originals follow the existing rules. A failed join leaves previous published files and archive entries unchanged.

Both ffmpeg and its ffprobe companion are required before media transfer. Inspection, simulation and ordinary already-saved/archive skips do not require those tools. The implementation follows ffmpeg's documented [concat demuxer](https://ffmpeg.org/ffmpeg-formats.html#concat-1); no Python or yt-dlp code is used.

## Verification

Five deterministic provider checks cover complete sequence identity, invalid positions/durations/duplicates/container changes, bounded inventories, tool preflight, existing files, simulation and protocol selection.

Eleven generated-media checks cover MP4/FLV joins, complete audio-only/video-only output, exact retained source bytes, independent retries, declared-size mismatch, missing/corrupt files, incompatible decoder configuration, cancellation and parallel transfers completing out of order. Successful joins retain source video/audio frame counts; decoded video frame hashes also match the original order. Failed jobs preserve published data and do not write archive entries or completion callbacks.

A longer replay splits the previously downloaded Bilibili public-film part into four local MP4 files without re-encoding, then passes their ordered descriptors and bytes through the native Bilibili provider and download pipeline. Six requests and one completion callback produced a 63,940,577-byte H.264 852x480/AAC file lasting 596.624 seconds. It fully decodes with all 14,317 video frames and 27,966 audio frames. Every encoded packet payload matches the original film in order; track start offsets and durations also match. It is a local replay, not live evidence that Bilibili currently serves that film as multiple `durl` files. Current live progressive responses for the sample each supplied one file.

## Remaining work

Live multi-file provider verification, additional containers, mixed or changing decoder layouts, gapless handling of other independent encodings, authenticated resources, supplied backup selection, URL refresh, broader format facts and complete upstream option parity remain pending. This path does not complete the overall yt-dlp replacement.
