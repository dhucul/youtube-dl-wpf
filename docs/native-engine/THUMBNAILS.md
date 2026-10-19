# Native cover images

The C# download pipeline retrieves thumbnail inventories through the existing media HTTP session. Saving one, saving all and embedding are independent options: embedding alone does not publish an extra image file. The WPF migration adapter maps the all-thumbnail setting and preview inventory; the production application still uses its original backend.

## Saving all supplied variants

`WriteAllThumbnails` and `--write-all-thumbnails` save every distinct supplied image address. Inventories are available from YouTube, Vimeo, PeerTube, Wistia, TikTok, TED, Vidyard, Brightcove, JW and Dailymotion response fields. Other providers retain their existing preferred-image fallback until broader inventories are implemented.

Duplicate URLs are fetched once. Stable thumbnail IDs are sanitized and disambiguated for filenames such as `title.small.png` and `title.normal.jpg`; image bytes determine the extension. The single-thumbnail option keeps its existing `title.jpg` style. Info JSON and native preview metadata include the normalized inventory and offered dimensions. The existing preferred image stays preferred for embedding; when only an inventory exists, available dimension facts determine the default.

Saving supports JPEG, PNG, WebP, GIF, BMP and AVIF signatures, including AVIF compatible brands. Original variants are retained without conversion. Limits are 256 declared images, 8 MiB per image and 64 MiB for the set. Failed or canceled sets do not publish partially written images. Standalone jobs support per-file no-overwrite and preserve prior files. The same feature works with video downloads and cover embedding.

## Implemented behavior

- MP3, MP4/M4A and FLAC use attached pictures. The cover's video-stream index is calculated from the actual media tracks, including audio-only files and videos with several video streams. M4B/M4V use the same MP4 muxer path but lack separate media checks.
- Matroska video/audio files use named `cover.jpg` or `cover.png` attachments with the matching MIME type. Existing cover streams are replaced; unrelated attachments remain mapped.
- Ogg Vorbis and Opus use a C#-encoded Xiph picture block in `METADATA_BLOCK_PICTURE`. The metadata argument is supplied through a temporary file, so large artwork does not exceed Windows command-line limits. Existing media tags are retained and obsolete cover tags are cleared.
- JPEG and PNG pixels are preserved. WebP, GIF, BMP and AVIF are decoded to temporary PNG for compatible embedding; animated inputs contribute their first frame. The separately saved originals remain unchanged. Embedded images are bounded to 8 MiB and 16 megapixels; conversion output is also bounded. Inspection and image decoding have time limits and child processes are drained on cancellation.
- When the container is automatic, WebM video becomes Matroska and transport-stream output becomes MP4 to carry artwork. Explicit unsupported containers, including MOV, WebM, WAV, AVI and FLV, fail before media transfer when the final extension is known. They are not silently changed.
- Cover preparation, caption/chapter embedding, metadata, conversion and publication share the existing staged-output transaction. Failed or canceled artwork processing leaves prior media, saved images and archive entries unchanged.
- ffprobe inspects the completed output before publication. A missing/wrong cover or lost audio/video track count makes the operation fail even if ffmpeg reported success. Normal remuxing now maps all source streams instead of automatically selecting only one track of each type.

## Verification on October 8, 2026

Generated MP4, Matroska, MP3, M4A, FLAC, Ogg and Opus outputs were checked for media codecs, complete frame counts, embedded artwork dimensions and decoded pixel hashes. Tests cover two-video-track files, audio-only files, WebP conversion, large artwork, replacement of previous covers, unrelated Matroska attachments, metadata, captions, chapters, automatic WebM remuxing, no-overwrite prediction, malformed images and cancellation.

Native YouTube downloading of `jNQXAC9IVRw` with artwork, metadata and chapters produced a 766,208-byte MP4. Its H.264/AAC media retained all 284 video frames and 821 audio frames, 19.063583 seconds, and three chapter markers. The embedded 480×360 JPEG matched the saved 20,969-byte source image's decoded RGB hash. Full media decoding passed. This confirms one live cover workflow, not full YouTube or site parity.

Ten inventory/saving checks cover preferred selection, deduplication, safe IDs, actual image formats, standalone saving, per-variant no-overwrite, failed/canceled sets, bounds, provider inventories and native inspection. Three generated-media checks preserve original GIF/BMP/AVIF files while embedding matching first-frame pixels and retaining the complete video.

Live TED `814` saved both supplied variants without media: a 1,860,540-byte 2400x1800 JPEG and a 1,868,828-byte 2880x1620 JPEG. Both fully decoded. Live Vidyard player `MT1jv9tg2Cq1qWepjXv71v`, selected entry 3, saved all four supplied JPEG variants: normal 64,210 bytes, play-button 63,530 bytes, small play-button 20,016 bytes, and small 19,533 bytes. Large images were 1280x720; small images were 480x270. All decoded. Its 1,491,505-byte MP4 retained 240 video frames and 452 AAC frames over 9.680000 seconds, fully decoded, and the embedded cover's RGB hash matched the saved preferred normal image.

## Remaining work

Additional provider inventories, further image formats and multi-image layouts, animated-cover playback, richer picture roles, exact legacy naming/option interactions, older ffmpeg compatibility, broader live/container/player verification, and production integration remain pending. Explicit MOV artwork is unsupported because the tested ffmpeg MOV muxer discarded the image; output verification prevents reporting that result as successful. No change here completes the overall replacement.

References: [ffmpeg stream mapping and attachments](https://ffmpeg.org/ffmpeg.html), [ffmpeg file-backed option arguments](https://ffmpeg.org/ffmpeg-all.html), [Matroska cover attachments](https://www.matroska.org/technical/attachments.html), and [Xiph picture comments](https://wiki.xiph.org/VorbisComment#METADATA_BLOCK_PICTURE). The picture-block writer is independent C# code; ffmpeg and its bundled ffprobe handle media inspection and processing.
