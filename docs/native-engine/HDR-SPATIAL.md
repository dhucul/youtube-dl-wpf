# Dynamic HDR spatial mapping and broader Dolby profiles — 0.180.0

The new opt-in live DASH color policy, `hdr-spatial`, extends metadata-aware HEVC output without changing the verified `hdr-metadata`/`hdr-preserve` behavior, serialized defaults, queued snapshot isolation or default YtDlpEngine startup. It uses the existing color setting; the native extension count stays sixteen. Full replacement remains open.

## Implemented subset

| Input | Added behavior |
| --- | --- |
| Reordered HEVC HDR10+ | Bind each decoded picture to its coded MP4 sample position, retaining the effective payload in decoding order before presentation/30-fps selection |
| One, two or three HDR10+ windows | Uniformly scale window corners, centers and ellipse axes while retaining angles, overlap, luminance/distribution/tone metadata and unrelated payload bits |
| Dolby Vision 8.1/8.4 | Uniform scaling and centered padding, with updated or newly inserted level-5 active-area offsets |
| Dolby Vision profile 5 | Preserve the full-range IPT signal domain and profile-5 signaling through supported spatial processing |
| Compatible AV1 Dolby Vision profile 10 | Native T.35/EMDF extraction and a local HEVC bridge preserve independent metadata, then produce compatible HEVC profile 8.1/8.4 output |

Output remains a fixed 16:9, ten-bit HEVC, 30-fps canvas. Spatial transforms require an exact uniform scale, square source pixels, no rotation, and chroma-aligned centered padding. HDR10+ padding remains refused because full-frame statistics require additional treatment. Dolby padding uses explicit active-area records. Geometry fields are rounded to their integer precision and checked against source/target bounds.

Dolby processing requires complete independent uncompressed single-layer metadata with a compatible base signal and no residual/enhancement processing. Profile 5 keeps its full-range IPT interpretation; it is not relabeled as an HDR10-compatible signal. Compatible profile-10 AV1 metadata is handled natively because the bundled AV1 software decoder omits its Dolby frame side data. Original AV1 coded periods are retained. PQ/HDR10-compatible Dolby re-encoding requires source mastering-display metadata; absent metadata is not invented.

Changing families/configurations, disappearing active metadata families, compressed/unsupported profiles, profile-7/enhancement reconstruction, arbitrary rotations/anisotropic transforms and mixed transfer targets remain refused. Existing frame/count/allocation/metadata bounds and source-aligned clipping rules remain in force.

## Metadata processing

Packet inspection reads bounded local MP4 samples using their declared positions and sizes. It matches decoded picture positions rather than assuming decoding and presentation order coincide. HDR10+ payload state is preserved before window fields are mapped. Ambiguous/missing picture bindings and unsupported sample framing fail before publication.

Dolby metadata is canonicalized through the bundled encoder, then edited in native C#. The bounded parser checks sequence/mapping data, independent metadata, extension lengths and CRC before updating or adding level-5 records. Other extension blocks and non-geometric metadata remain intact. CRC and escaping are rebuilt; the encoder's MP4 Dolby configuration is restored with an output level appropriate to the target geometry. Every coded RPU/window payload and every decoded output frame/timeline is verified before joining/publication.

No Dolby utility is an application dependency. The separate `dovi_tool` executable described below is an independent verification tool retained only in the workspace.

## Verification

All **3,430 native checks** (2,358 core + 1,072 optional media), **119 app checks** and **87 focused checks** pass on the final implementation. Release has zero warnings/errors. Five independent generated native workflows and exact 0.178.0 default policy/recovery compatibility pass.

New coverage comprises 48 core, 32 optional-media and seven app checks. Native workflows cover reordered/windowed HDR10+, up/down scaling, metadata onset, clips, Dolby active areas and padding, profile 5, compatible AV1/profile 10 including HLG, CM v4 metadata, mixed captions, stopped-worker/new-session recovery, corrupt prefixes/cancellation and negative geometry/profile/metadata cases. Reordered fixtures preserve original MP4 sample timestamps; an initial raw-remux fixture defect and one transient checkpoint-file diagnostic are retained separately.

Five retained generated clear-media workflows independently verify forty full decodes, fifteen picture/geometry profiles over 3,600 frames, twenty exact source-period packet inventories and ten main/original stream inventories. Two HDR10+ workflows verify all 1,440 frame-associated window payloads. Three Dolby workflows verify all 2,160 metadata frames with the separate SHA-256-verified `dovi_tool 2.3.4` release, comparing active areas and all non-geometric parsed metadata. Reference picture selection uses a separate marked reference stream to disambiguate identical pictures, while actual output picture hashes are compared against unmodified references. Archive repeats produce zero HTTP requests or new completion callbacks.

Default policy JSON and video/audio recovery identities match the preserved, verified 0.178.0 binary. Evidence lives in `work/hdr-spatial-batch`, `work/hdr-spatial-probe`, `work/hdr-spatial-verify` and `work/hdr-spatial-compatibility`. The validator archive digest is `0d9733d311dfe49f9dca0c350f7e6d978182673d333c6f07f7365daf9e6c81eb`; its release metadata and exports are retained. These are generated decoded-media checks, not real-account or hardware-certification evidence.

## Sources and next work

Implementation was checked against primary [HDR10+ metadata parsing](https://github.com/FFmpeg/FFmpeg/blob/master/libavutil/hdr_dynamic_metadata.c), [Dolby RPU coding](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/dovi_rpuenc.c), [AV1 metadata syntax](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/cbs_av1_syntax_template.c) and installed tool behavior. Public profile-5 reference payload provenance follows [quietvoid/dovi_tool commit 614c816b](https://github.com/quietvoid/dovi_tool/tree/614c816b6446dcd1dbaf433403d499a6026fbb5a/assets/tests), with the existing MIT notice retained. Independent verification uses the [official 2.3.4 release](https://github.com/quietvoid/dovi_tool/releases/tag/2.3.4).

Continue with HDR10+ padding statistics, arbitrary/rotated/anisotropic geometry, Dolby profile-7/enhancement reconstruction, more compressed/other profiles and metadata reset/mixed-transfer policies. Codec/container/RGB/caption/audio/selection coverage, historical recovery/crash/write cases, longer provider sessions, player/proof/signing/login contracts and the full frozen catalog/options/packaged acceptance backlog remain open. Real-account verification stays user-deferred.
