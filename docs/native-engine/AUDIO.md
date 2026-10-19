# Native audio extraction

The native pipeline now inspects the downloaded media with the ffprobe already included in the ffmpeg kit, chooses an actual audio stream and output container, extracts it in staging, then verifies the resulting codec and stream layout before publication. Source default disposition takes priority, followed by channel count and stream order. Declared website codec labels do not override the downloaded media's facts.

## Formats and preservation

- `best` and `copy` normally retain the selected audio packets. AAC/ALAC use M4A, MP3 uses MP3, Opus uses Opus, Vorbis uses Ogg, FLAC uses FLAC and supported PCM uses WAV. Other codecs use Matroska audio when ffmpeg can copy them there; unsupported muxing fails explicitly.
- AAC, Opus and Vorbis originating in Matroska/WebM retain Matroska audio (`.mka`). Direct remuxing to M4A/Ogg changed codec-delay/discard-padding behavior in generated samples despite preserving packet bytes. Retaining Matroska preserved both packets and decoded samples.
- Explicit MP3, M4A, AAC, Opus, Vorbis/Ogg, FLAC, WAV and ALAC requests encode the requested format. AAC produces raw ADTS `.aac`; M4A produces an AAC `.m4a` container. Requested metadata on ADTS uses its ID3-writing option.
- WAV conversion retains detected PCM precision, including the verified 24-bit path. Copying and lossless encoders do not receive lossy quality flags. Numeric/bitrate quality settings are validated, but they do not cause automatic/copy audio to be re-encoded.
- Final filename templates and sidecars use the resolved extension. Unknown automatic extensions are checked again for no-overwrite after inspection, before conversion or sidecar downloads. Skipped staging is cleaned. Archive-path collisions, missing audio and processing failures cannot overwrite prior files or record a new success.

## Quality settings

Native quality levels run from 0 (highest) to 9 (lowest). MP3 uses that qscale directly. Vorbis maps it from q=10 to q=0; native AAC maps it from q=2 to q=0.1. Opus has no ffmpeg qscale, so the native levels use VBR targets. Stereo targets for levels 0–9 are 256, 224, 192, 160, 144, 128, 112, 96, 80 and 64 kbit/s, scaled by channel count and bounded to the encoder range. Fractional levels interpolate those targets.

Bitrate values such as `128K` or `0.192M` are passed as bitrate targets. Opus uses fixed-rate mode for these explicit requests; other lossy encoders use their bitrate mode. No single qscale is passed indiscriminately to every encoder. Exact historical yt-dlp quality calibration is not established by these native profiles and remains a compatibility task.

## Verification on October 8, 2026

Generated AAC, MP3, Opus, Vorbis, FLAC and 24-bit PCM cases verified automatic containers, packet preservation where applicable and matching decoded sample hashes. Tests include both Matroska timing cases and ordinary MP4/Ogg sources, source-default track selection, all explicit formats above, quality direction, fixed-rate Opus packets, ADTS metadata, lossless 24-bit conversions, final templates, late no-overwrite, archive collision and missing-audio failure.

A live native audio-only download of Twitch clip `CloudySarcasticSashimiTwitchRPG`, requesting `best` with metadata/artwork, produced a 454,772-byte M4A: stereo AAC at 44.1 kHz, 816 audio frames, 18.940476 seconds and an attached JPEG. Full decoding passed. Its decoded audio hash matched the earlier complete clip's audio, confirming that the automatic extraction preserved the selected track.

## Remaining work

Exact upstream quality-profile equivalence, richer format mappings, less common codecs and PCM representations, audio stream/language preference parity, retention of pre-existing artwork, keep-original output handling, more caption/chapter/container combinations, larger-file/performance coverage and production integration remain pending. The new behavior completes verified paths through the app's audio choices, not all audio-option compatibility or the overall replacement.

Encoder behavior references: [ffmpeg audio codecs](https://ffmpeg.org/ffmpeg-codecs.html), [stream mapping and stream copy](https://ffmpeg.org/ffmpeg.html), and [container formats](https://ffmpeg.org/ffmpeg-formats.html). The planning, validation, quality profiles and transactional integration are original C# implementation.
