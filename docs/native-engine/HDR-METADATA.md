# HDR10+, Dolby Vision and changing static profiles — 0.178.0

The opt-in native live DASH policy now offers `TransitionColorPolicy = "hdr-metadata"` with the explicit `hevc` output codec. It re-encodes supported HDR pictures while keeping their frame-associated display metadata. Existing `hdr-preserve` behavior, serialized defaults, queued snapshot isolation and the default YtDlpEngine backend remain unchanged. Full replacement remains open.

## Implemented subset

| Input metadata | Behavior |
| --- | --- |
| Mastering-display/content-light metadata | Preserve changed values within and across periods, including mastering primaries/luminance and MaxCLL/MaxFALL |
| HDR10+ | Preserve exact registered SEI payload bytes and associate them with the selected pictures after 30-fps retiming and clipping |
| Dolby Vision 8.1 / 8.4 | Regenerate parsed RPU metadata during HEVC encoding; retain the complete decoded metadata and compatible MP4 configuration |

Output remains ten-bit HEVC, limited-range BT.2020, with a consistent PQ or HLG transfer, a fixed 16:9 canvas and 30-fps timeline. Supported static-only sources can use the existing aspect/size conversion. Dynamic HDR sources require ten-bit 4:2:0 HEVC with unchanged dimensions, square pixels, zero rotation and limited range. Spatial metadata remapping is unfinished.

HDR10+ currently requires application version 1, one full-frame window and source pictures without reordering. Dolby Vision requires uncompressed single-layer profile 8.1 or 8.4 with a compatible base layer, a complete RPU for each decoded frame, no enhancement layer, and no residual/spatial processing. Reordered Dolby Vision pictures are covered. Switching Dolby configuration or combining Dolby Vision and HDR10+ in one workflow remains refused. An active static/HDR10+ metadata family cannot disappear without a supported reset sequence.

Clips must start at a source frame boundary and span a whole number of output frames with complete source coverage. The metadata path bounds source and output inventories to 120,000 frames, 4,096 unique metadata descriptors, 64 MiB of descriptor text, bounded SEI/access-unit sizes and local movie indexes. Unsupported/malformed profiles, unknown HDR families and incomplete timelines stop before publication. The original coded source periods remain separately retained.

## Processing and publication

The native scanner reads every decoded frame and its clock, validates color/sample facts, and associates the selected pictures with their metadata. The bundled encoder performs HEVC/RPU coding. Native C# code edits bounded local HEVC access units to replace static metadata and insert the corresponding HDR10+ payloads. It restores the encoder's exact Dolby configuration in the owned MP4's final movie index; media chunk offsets remain unchanged. Every resulting frame, timeline and configuration is verified before joining periods and publication.

Dolby Vision MP4 signaling also survives separate-track merging, video extraction, remuxing, original retention and metadata/caption embedding. The muxer requires an explicit compatibility flag to write its Dolby configuration; the processing paths now supply it and verify the result. Dolby `dvh1`/`dvhe` aliases participate in explicit HEVC selection and merge-container prediction.

One compiled WPF choice exposes the policy through the existing color setting. The native extension count remains sixteen, outside the frozen 126 classic app options. No external Dolby utility, new production package, account access, fallback backend or full cutover was added.

## Verification

All **3,350 native checks** (2,310 core + 1,040 optional media), **112 app checks** and **83 focused checks** pass on the final implementation. Release has zero warnings/errors. Five independent generated native workflows and exact 0.176.0 default policy/recovery compatibility pass.

New coverage comprises 44 core, 32 optional media and seven app checks. Media cases cover changing light/mastering profiles, absent static metadata, HDR10+ onset and changes, retiming, clips, Dolby 8.1/8.4 and CM v4 metadata, changing RPU metadata, reordered Dolby pictures, actual Dolby sample-entry aliases, ALAC/FLAC merging, mixed captions, fresh-session recovery, one forcibly stopped worker, corruption/cancellation and negative profile/layout/publication cases. A separate copy test verifies unchanged coded video and Dolby configuration through extraction, remuxing, embedding and merging.

Five retained native workflows use generated tagged media: changing light levels, changing mastering profiles, changing HDR10+, changing Dolby 8.1/CM v4 metadata and Dolby 8.4 HLG. An independent verifier fully decodes 40 files and checks 15 picture/metadata profiles over 3,600 frames. It identifies metadata by independently transformed reference pictures rather than reusing the native timestamp mapping. All twenty retained source-period packet inventories and ten main/original stream inventories match. All 720 HDR10+ frame payloads retain their exact encoded SEI bytes; all 1,440 decoded Dolby metadata frames retain their full parsed metadata and required track configuration. Archive repeats produce zero requests or new callbacks. This is generated clear-media evidence, not real-account or hardware-certification evidence.

The prior verified 0.176.0 engine is hash-checked before comparing default policy JSON and video/audio recovery identities. Workspace evidence lives in `work/hdr-metadata-batch`, `work/hdr-metadata-probe`, `work/hdr-metadata-verify` and `work/hdr-metadata-compatibility`. Initial capability and fixture diagnostics are retained.

## Sources and next work

Encoder/mux behavior was checked against the installed tools and primary [FFmpeg HEVC encoder](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/libx265.c), [Dolby metadata encoder](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/dovi_rpuenc.c), [MP4 muxer](https://github.com/FFmpeg/FFmpeg/blob/master/libavformat/movenc.c) and [x265 metadata documentation](https://x265.readthedocs.io/en/master/cli.html). Small public RPU reference fixtures come from [quietvoid/dovi_tool commit 614c816b](https://github.com/quietvoid/dovi_tool/tree/614c816b6446dcd1dbaf433403d499a6026fbb5a/assets/tests), with its MIT notice retained in `tests/YoutubeDl.Engine.Tests/DOLBY-METADATA-NOTICE.txt`. No dovi_tool runtime is used.

Continue with dynamic HDR spatial remapping, reordered/multiple-window HDR10+, more Dolby profiles/layers and metadata reset sequences. Mixed SDR/PQ/HLG targets, other codecs/containers, RGB/alpha/linear profiles and complex geometry remain open. Continue the caption, selection, audio, recovery, provider/session and full frozen catalog/options acceptance backlog. Real-account verification remains user-deferred.
