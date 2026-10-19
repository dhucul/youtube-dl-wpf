# Native merge containers and free format preference

Engine 0.66.0 maps the app's saved merge-output and free-format settings to the independent C# engine. The main WPF application still uses its original backend pending full site and option coverage.

## Merge containers

`NativeDownloadRequest.MergeOutputFormat` is separate from `Container` (final remuxing) and `RecodeVideo` (video conversion). It applies only when separate video and audio are joined. A single complete download, a single explicit format or an unpaired video-only request keeps its original format and does not require ffmpeg because of this setting alone.

Lowercase choices may be separated by `/`, for example `webm/mkv`. The engine selects the first container compatible with the selected video and audio codecs. Supported choices are `mp4`, `mov`, `mkv`, `webm`, `avi`, `flv` and the app's saved `ogg` choice. Container selection copies encoded tracks; it never implies recoding. Known incompatible choices fail before media transfer. If metadata omits codec facts, downloaded tracks are inspected before selecting the final container. Inspection permits local media formats only; playlist demuxers and remote protocols are excluded. Actual muxer errors remain fatal before publication.

The same setting reaches exposed track pairs, SABR, separate HLS renditions and DASH representations. HLS uses the downloaded tracks' codec facts. An uncertain container list does not cause the engine to skip a job merely because a file with the tentative first extension already exists. Internally merged HLS/DASH outputs now honor post-processing overwrite protection as merged files.

Explicit app container choices override the saved merge preference and also request final remuxing. Audio presets suppress the saved video merge setting. A saved recode target can coexist with a merge preference: the merged intermediate retains its chosen container before conversion. Later extra arguments override saved values without changing the settings object.

## Free formats

`PreferFreeFormats` prioritizes WebM video when resolution and known frame rate match. Higher resolution/frame rate and the existing fallback boundary take priority. Audio extension preference is Ogg, Opus, WebM, MP3, M4A, AAC, then other formats. Automatic pairing first keeps the provider's default-audio pool; this setting does not select a different language merely to obtain a free container. With the setting enabled, compatible-container preference no longer suppresses a preferred audio format; mixed formats can use Matroska.

The preference also reaches best/worst aliases, ranked selectors, filtered selectors, and formats inside DASH manifests. Literal IDs, explicit filters and codec/frame-rate constraints remain binding. Worst selectors reverse the format preference along with their ordinary quality order.

Extra arguments support `--merge-output-format`, `--prefer-free-formats` and `--no-prefer-free-formats`. The independent implementation uses the public [yt-dlp option descriptions](https://github.com/yt-dlp/yt-dlp/blob/master/README.md#video-format-options) as the behavior reference; it does not execute or import yt-dlp.

## Verification on October 9, 2026

Eight deterministic checks cover selection, single-file behavior, merge/remux/recode independence, resolution/frame-rate priority, default audio, explicit selectors, ordered overrides and validation before discovery. Eight generated-media checks cover exact encoded packet preservation for H.264/AAC in Matroska, VP9/Opus in WebM and Theora/Vorbis in Ogg; untouched single-file MP4; HLS/DASH fallback and overwrite protection; omitted codec facts and filename prediction; and failed merges without publication or archive updates. One app check covers saved settings, explicit containers, audio presets and job overrides.

Fresh public YouTube `jNQXAC9IVRw` downloads used automatic browser acquisition and native SABR transfer:

| Request | Output | Tracks | Retained original and components |
| --- | --- | --- | --- |
| Explicit `133+140`, `webm/mkv` | 742,430-byte MKV | H.264 320×240, 284 video frames; AAC 821 frames; 19.063 s | 742,393-byte merged original, 433,081-byte MP4 video, 309,288-byte M4A audio |
| Automatic, maximum height 240, free formats, `webm/mkv` | 548,664-byte WebM | VP9 320×240, 284 video frames; Opus 951 frames; 19.021 s | 548,628-byte merged original, 293,080-byte WebM video, 255,427-byte WebM audio |

All eight saved media files decoded fully. Each job emitted four publication callbacks, updated its archive and left zero owned browser profiles. The live probe used no Python, yt-dlp or personal browser cookies.

This does not establish complete upstream format-sort parity, provider-specific quality/HDR/channel/sample-rate ranking, multiple video/audio stream merging, every codec/container combination, live adaptive formats or every site. Remaining coverage and final app cutover are still outstanding.
