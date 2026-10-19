# Conversion targets, audio layouts and color/geometry — 0.174.0

This batch continues the native C# replacement. Full replacement remains open. The existing explicit WPF native live DASH path and immutable queued settings are preserved; default startup remains `YtDlpEngine`.

## Explicit conversion profiles

Live DASH conversion can now select H.264, HEVC, VP9 or AV1 video, and ALAC, AAC, FLAC or Opus audio. Video retains the explicit fixed 16:9 canvas and 30-fps cadence. The video encoders preserve the normalized reference pixels in the generated checks. AAC and Opus are intentionally lossy; ALAC/FLAC preserve the normalized audio reference. Ordinary later audio extraction, remuxing and metadata/subtitle processing retain their existing contracts.

The typed policy adds `TransitionVideoCodec`, `TransitionAudioCodec`, `TransitionAudioLayout`, `TransitionAudioSampleRate`, `NormalizeVideoGeometry`, `TransitionColorPolicy` and `HdrPeakNits`. Their unused defaults are omitted from JSON. A separate binary comparison loads the verified 0.172.0 engine and 0.174.0, confirms identical default normalized-policy JSON, and compares both default video and audio recovery keys exactly.

Seven new WPF settings expose these choices, bringing native-only extensions to sixteen. They use the existing validated draft/save path, queue snapshots and optional native routing. The frozen classic catalog remains 126 app options and 276 captured long names; the extensions are tracked separately and add no upstream parity claim.

## Audio layout and rate transitions

An explicit fixed target supports mono, stereo or 5.1 at a selected supported sample rate. Named input layouts up to eight channels can be remapped and resampled between periods. Supported source codecs include AAC, ALAC, FLAC, Opus, AC-3 and E-AC-3. Unknown layouts, inconsistent channel counts and unsupported rates remain refusals. Without an explicit target, the previous stable mono/stereo layout and rate requirements remain.

Remapping uses FFmpeg's named channel-layout conversion and a bounded rematrix gain. Resampling and trimming preserve the selected period duration. Opus requires an explicit 48-kHz target. AAC uses 256 kbps for mono/stereo and 512 kbps for surround; Opus uses 128/384 kbps respectively. These are bounded conversion profiles, not a general encoder-argument interface. The implementation uses the documented [FFmpeg resampler](https://ffmpeg.org/ffmpeg-resampler.html).

Recovery intent includes the fixed destination layout/rate rather than the first source layout. Accepted physical source profiles remain pinned within each period, and saved media still requires a fresh matching initialization/overlap plus verified prefix digests. Language, role, viewpoint and accessibility cannot silently change during audio remapping. Changing a conversion policy creates a different recovery intent.

## Color and HDR to SDR

An explicit BT.709 policy converts supported tagged SDR primaries, transfer, matrix and full/limited range into limited-range BT.709. Generated coverage includes BT.601/BT.709, full-range input and ten-bit SDR. Explicit color conversion requires known supported signal tags; missing or contradictory information is refused.

The HDR policy handles tagged BT.2020 PQ or HLG input by converting to linear floating-point light, applying the selected nominal peak with Mobius tone mapping, then encoding limited-range BT.709 SDR. The peak is explicit and bounded; it is not inferred from a private player or guessed from unknown content. Static mastering-display/content-light metadata is removed from the converted stream, and output color tags are verified. The conversion follows the [FFmpeg color and tone-map filters](https://ffmpeg.org/ffmpeg-filters.html#tonemap).

This is HDR-to-SDR conversion. HDR-preserving output, Dolby Vision/HDR10+ enhancement processing, broad dynamic-metadata discovery and arbitrary wide-gamut/RGB profiles remain open. Reported advanced HDR metadata and unsupported color layouts remain guarded. Retained source periods keep the original coded media and metadata for later processing.

## Pixel aspect and rotation

The optional geometry policy normalizes a known bounded sample-aspect ratio while preserving display aspect inside the output canvas. Supported display matrices are pure quarter-turn rotations. FFmpeg performs the source orientation before scaling; output rotation is cleared and square pixels are verified. Generated checks cover wide/narrow pixel aspect and 90/180/270-degree matrices. Arbitrary angles, mirrored/sheared matrices and unknown/extreme aspect ratios are refused.

These settings are explicit. The default conversion profile retains the previous conservative geometry/color handling and compatible recovery identities. The existing final-complete-segment duration behavior also remains unchanged.

## Verification

All **3,203 native checks** (2,222 core + 981 media), **96 app checks** and **87 focused checks** passed. Release build has zero warnings/errors. The lifecycle-focused check also passes after atomic marker publication. Both public profiles, the separate native mono reference and binary compatibility proof pass.

The batch adds 44 core checks, 36 generated-media checks and eight WPF integration checks. References verify 240-frame complete layouts and 210-frame clipping, codec targets, named remapping/resampling, normalized audio samples, SDR/PQ/HLG transforms and geometry. Lossy audio publication is compared against the encoded component's decoded samples. All four captured original period packet inventories match their source inputs. Mixed WebVTT/TTML captions, new-session recovery and one forcibly stopped combined HDR/layout worker are covered. Failure cases preserve prior media, captions, archive and zero callbacks.

The compiled WPF controls are changed, validated, saved, reloaded and rendered in isolated settings. The current preview is `work/color-audio-batch/color-audio-policy.png`. No saved user account or real private issuer is accessed.

A separate native audio-only job converts changing mono/stereo 44.1/48-kHz inputs to mono at 48 kHz and matches an independent decoded sample reference exactly. The full suite also exposed an existing readiness-file race in the processor-parent-death test; its PID markers now publish atomically before the parent reads them. The lifecycle behavior itself is unchanged, and its focused check passes.

Two public workflows use actual alternating 360p/720p AVC assets and mixed WebVTT/TTML captions, then publish HEVC/FLAC stereo and AV1/Opus 5.1 targets at 640×360/30fps and 48 kHz. After durable prefixes, an injected transfer failure is followed by fresh HTTP/authorization sessions and new resource URL generations with earlier periods removed. The manifests, beep-audio language label and interruptions are controlled fixtures. They verify public SDR conversion/recovery; PQ/HLG verification uses generated tagged sources.

Independent checks fully decode twenty files, verify both target codec/layout profiles and actual retained source dimensions, match four main/original ordered packet inventories, and find all forty saved cue payloads in extracted embedded captions. Each main output has 600 decoded video frames. Recovery issues zero retired-period requests; archive repeats issue zero HTTP requests or new callbacks. Final/probe engine hashes match.

Evidence is retained in `work/color-audio-batch`, `work/color-audio-probe/bin/Release/net10.0/verified-0.174.0-profiles`, `work/verify-color-audio.ps1`, and the 0.172.0 compatibility report. Earlier source-signal diagnostics are preserved.

## Remaining work

Expand HDR preservation/dynamic metadata, wide-gamut/RGB and complex display transforms, additional named audio layouts/bitrate controls, containers and variable output profiles. Richer caption styling/transports, in-recording language/role policies, throughput-driven adaptation and full selector/default/locale semantics remain open. Continue historical checkpoint migration, crash/write interleavings, long renewed/private sessions, provider/player/proof/signing/login contracts, VOD/SABR handoff, inventory/storage and full catalog/options/packaged acceptance. Real-account verification remains user-deferred, and no complete-parity claim is added.
