# Native video recoding and processed-output protection

Engine 0.62.0 implements the six recode formats exposed by the app and adds per-output post-processing overwrite protection. The C# engine orchestrates ffmpeg and validates the converted media before the existing atomic publication step.

## Conversion profiles

| Target | Video | Audio | Defaults |
| --- | --- | --- | --- |
| MP4 | H.264 / libx264 | AAC | CRF 18, medium preset, 192 kb/s audio |
| Matroska | H.264 / libx264 | AAC | Same quality defaults |
| WebM | VP9 / libvpx-vp9 | Opus | CRF 30, unconstrained video bitrate, good deadline, CPU-used 2, 192 kb/s audio |
| AVI | MPEG-4 Part 2 | PCM 16-bit | Video quality 2 |
| FLV | H.264 / libx264 | AAC | CRF 18, medium preset, 192 kb/s audio |
| Ogg | Theora / libtheora | Vorbis | Video quality 7, audio quality 5 |

These are lossy video conversions. If the input already has the requested target extension, recoding is skipped; other requested processing can still run. Remuxing remains a separate operation that copies encoded streams. Saved recoding is suppressed for explicit container choices and audio jobs, matching the existing app argument precedence. The native adapter now also honors the audio quality preset as audio extraction.

All ordinary source video/audio streams are mapped. Compatible text captions and Matroska attachments can be retained; unsupported stream/container combinations fail instead of silently omitting tracks. Additional data/timecode streams and cross-container embedded-cover conversion are currently explicit unsupported cases. AVI/FLV/Ogg combinations that cannot retain requested embedded captions/chapters are not claimed as supported.

The converter probes complete source and output frame counts using fixed local demuxers. It checks stream roles/counts, every video frame, displayed geometry, output codecs, audio channel counts, nonempty audio, and available audio/video duration metadata. Orthogonal display rotation is baked into output geometry once; non-orthogonal transforms are rejected. Odd dimensions use a compatible H.264/VP9 pixel format without scaling. Input probing/processing restricts protocols and disables MP4 external data references. Full-frame inspection remains cancellable and is not subject to the short metadata-only probe timeout.

The six profiles do not establish all HDR, pixel-format, styling, variable-frame-rate or container combinations. Audio encoding can change sample rate, codec frame size and encoder delay; identical audio frame counts are not expected when codecs change.

## Overwrite and original-file behavior

`NoPostOverwrites` protects an existing processed media output and its grouped sidecars. It does not become a blanket ban on replacing an ordinary unprocessed download. With `KeepOriginal`, fresh source files can still be saved while the existing processed output is retained. Per-group publication protection remains active at the atomic commit boundary, including the check for files appearing during preparation. General `NoOverwrites` still protects all media groups.

Converted filenames/templates use the resulting extension. Requested originals remain byte-identical and are published with the existing collision-safe naming rules. Invalid/corrupt conversions and canceled encoders cannot replace previous outputs, fire new-file callbacks or add archive records.

Supported extra arguments include `--recode-video`, `--remux-video`, `--no-post-overwrites` and `--post-overwrites`. Later recode/remux choices replace the earlier choice. Contradictory typed audio/recode/remux requests fail before discovery. Compound extension-mapping grammar is not yet implemented.

## Verification

Six deterministic checks cover validation before discovery, existing processed-file skips without tools, fresh original retention, ordinary-download overwrites, per-group sidecars and argument precedence. Fourteen media checks cover all six targets, embedded caption text/timing, odd dimensions, corrupt input, multiple audio tracks/languages, silent video, display rotation, already-target packet preservation and cancellation during an active encoder. One app check covers saved options, explicit-container/audio precedence and job overrides.

A live public Imgur video converted from H.264/AAC MP4 to VP9/Opus WebM:

- The exact original was retained: 6,359,160 bytes, 854 × 480, 1,320 video frames, stereo 44.1 kHz AAC, 52.895011 seconds.
- Converted output: 4,612,613 bytes, 854 × 480, 1,320 video frames, stereo 48 kHz Opus, 52.903 seconds.
- Both files fully decoded. Two completion callbacks fired and the media archive was recorded.
- A repeat request with post-overwrite protection, no archive shortcut and no ffmpeg path skipped conversion and emitted zero new-file callbacks.

Remaining work includes complete historical recode/remux mapping grammar, more auxiliary-stream and display-transform cases, HDR/high-bit-depth/variable-frame-rate verification, exact legacy sidecar/overwrite interactions, broader conversion quality/progress controls and production cutover. This milestone does not complete the all-site/all-option replacement.
