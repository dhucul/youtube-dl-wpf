# Native indexed DASH

Static MP4 `SegmentBase` representations now use an original C# ISO-BMFF segment-index reader and bounded HTTP byte-range transfers. No parser or downloader from yt-dlp is used.

## Implemented behavior

- Inherited `SegmentBase` index/initialization ranges and explicit timescales. A representation's addressing mode overrides a different inherited mode; conflicting modes at one level are rejected.
- Version 0/1 `sidx` boxes, ordinary/extended box sizes, 32/64-bit times and offsets, and harmless surrounding `free`, `skip` and `styp` boxes.
- Exact reference sizes, consecutive byte positions and duration fields, with bounded counts, overflow/truncation checks and explicit refusal of nested or ambiguous indexes.
- HTTP 206 and exact `Content-Range` validation. Strong ETags use `If-Match`; otherwise available modification dates use `If-Unmodified-Since`. Changed resource lengths/validators and incomplete or excessive responses fail before publication.
- Per-range retries reset the partial attempt. Cancellation and transfer errors retain the previous destination. Completed indexed tracks use the existing native selection and ffmpeg merge pipeline.
- Initialization/index/prefix bytes retain their original positions before media ranges are appended. Keeping that layout avoids invalidating offsets in the MP4 structure.
- HLS and DASH manifest redirects now resolve relative resources against the final response URL.

The supported layout is one unshifted static period, MP4 media, an explicit same-resource initialization prefix beginning at byte zero, and a bounded flat segment index. Index reads are capped at 1 MiB, the prefix at 16 MiB and each media range at 512 MiB. Presentation checks permit small codec padding (100 ms or one declared video frame) while refusing materially clipped/inconsistent index windows.

Unsupported WebM index alternatives retain an unavailable reason and do not hide usable MP4 alternatives. Required audio/video cannot silently disappear when only an unsupported addressing mode supplies that track. Embedded text representations do not block ordinary media downloads. Selected MP4 WebVTT captions now use the native converter and common subtitle pipeline; unsupported selected codecs still fail explicitly. See `DASH-CAPTIONS.md`.

## Verification

Fourteen deterministic checks cover independent wire expectations, 64-bit fields, inheritance, malformed indexes, unavailable alternatives, captions, redirects, changed resources, retries, cancellation and timing boundaries. A generated H.264/AAC test downloads indexed tracks, merges them, checks all six video frames and fully decodes the result.

On October 8, 2026, the native engine downloaded [Shaka's public Angel One manifest](https://storage.googleapis.com/shaka-demo-assets/angel-one/dash.mpd), selecting H.264 at a maximum height of 240. The complete merged output was 2,770,891 bytes, 300×240 at 25 fps, with H.264/AAC and a 60.021333-second container duration. ffprobe counted all 1,500 video frames and 2,814 audio frames, and full ffmpeg decoding passed. Both selected tracks were retrieved from their MP4 indexes; the manifest also contained unselected WebM and embedded caption alternatives.

## Remaining work

WebM cues, nested/multiple indexes, separate index resources, shifted/clipped media periods, multi-period presentations, dynamic/live updates, remaining subtitle codecs and other addressing forms remain pending. Precise media editing windows and more varied stream timelines require additional integration and live verification. Index support does not establish full site coverage or complete YouTube playback.

Protocol references: [DASH-IF indexed addressing and index structure](https://dashif.org/Guidelines-TimingModel/#indexed-addressing) and [W3C ISO-BMFF byte streams](https://www.w3.org/TR/mse-byte-stream-format-isobmff/).
