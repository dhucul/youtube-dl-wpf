# HDR preservation, metadata and wide-gamut/RGB — 0.176.0

This batch extends the opt-in native live DASH conversion policy. It does not complete the native replacement or change the default application backend. All previous uncommitted work is retained.

| Color policy | Input subset | Output |
| --- | --- | --- |
| `hdr-preserve` | Consistent PQ or HLG, BT.2020 nonconstant-luminance YUV, known range, 10/12-bit | 10-bit BT.2020 HEVC with the source transfer and unchanged supported static HDR metadata |
| `wide-to-bt709` | Tagged SDR BT.2020, Display-P3, BT.709/601; supported full-range packed/planar RGB or YUV | Limited-range 8-bit BT.709, with obsolete HDR metadata removed |
| `bt2020` | The same supported tagged SDR inputs | Limited-range 10-bit BT.2020 HEVC with BT.2020 SDR transfer |

Ten-bit output requires the explicit `hevc` video target. The preservation policy does not tone map. Twelve-bit input is explicitly dithered to the selected ten-bit output. Existing null/SDR/tone-mapping policies and serialized defaults retain their previous meanings. WPF exposes three additional color choices through the existing setting; there are still sixteen native-only extensions outside the frozen classic-option catalog. Saved settings, drafts, queued snapshots and opt-in backend routing retain their existing isolation.

## Metadata handling

The converter inspects every decoded source frame, including later GOPs, through streaming ffprobe XML. It validates bounded, exactly representable mastering chromaticities/luminance and 16-bit content-light values, passes their canonical values to HEVC encoding, and checks every decoded output frame against the declared profile. The source policy keeps a single unchanged static metadata profile across periods. Unknown/malformed metadata, changing static metadata within/across periods, mixed PQ/HLG, unsupported alpha/float RGB and unknown color facts refuse conversion before publication.

Reported HDR10+, Dolby Vision, HDR Vivid and unknown HDR side data refuse transition re-encoding. A generated source introduces valid HDR10+ only at its second IDR; both HDR-preserving conversion and HDR-to-SDR conversion refuse it while retaining previous media, captions and archive state. Native HLS stream copy is separately verified to preserve that source's exact coded packets and complete frame-indexed HDR10+ payloads through metadata processing, original retention and publication. Metadata-aware dynamic HDR re-encoding remains unfinished.

Inspection uses a local MOV demuxer with data references disabled, bounded allocation/document/frame limits, cancellation and the Windows owned-helper lifetime. Streaming parsing does not accumulate the recording's frame inventory in memory. Intermediates retain ten-bit samples and color tags; output verification covers pixel format, transfer, primaries, matrix, range, square pixels and cleared rotation. Original coded periods remain separately retained.

The encoder metadata mapping follows [FFmpeg's HEVC wrapper](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/libx265.c) and [x265's mastering/content-light options](https://x265.readthedocs.io/en/master/cli.html). Color conversion uses the explicitly tagged transformations supported by [FFmpeg zscale](https://ffmpeg.org/ffmpeg-filters.html#zscale). These references describe capabilities; the local media checks establish this batch's supported subset.

## Verification

A complete 3,270-case native run passed. Final targeted runs on the final binary cover 211 distinct cases, including every converted-period media case and four added cases, bringing distinct native coverage to **3,274 checks** (2,266 core + 1,008 media). **105 app checks** and **78 HDR/gamut focused checks** pass. This is combined coverage; it is not a single 3,274-case invocation. See `native-coverage.json` and `native-case-provenance.json` for hashes and per-case run provenance. Release has zero warnings/errors. Five independent generated native workflows and exact 0.174.0 default policy/recovery compatibility pass.

New coverage comprises 44 core, 27 optional media and nine WPF checks. Generated live workflows cover PQ/HLG, full-range and 12-bit input, source metadata absent/present, Display-P3/BT.2020 YUV and RGB, explicit output depth, exact reference frame sequences, audio, period-boundary clipping, mixed saved/embedded captions, fresh-session recovery, one forcibly stopped worker, corrupt prefixes, cancellation and publication guards. Later-GOP static changes and dynamic metadata are independently established in decoded fixture frames before testing refusal.

A separate retained-evidence probe runs five native workflows: PQ, HLG, Display-P3 RGB to BT.709, Display-P3 to ten-bit BT.2020 and HDR10+ stream copy. An independent verifier fully decodes 34 files; validates 12 output profiles covering 2,880 converted frames and every static metadata field; matches all 16 retained source-period packet inventories and nine main/original stream inventories; and compares both dynamic outputs' coded packets plus all 60 frame-indexed HDR10+ payloads. Archive repeats make zero HTTP requests or new completion callbacks. All HDR/gamut evidence is generated tagged media. Real-account verification remains user-deferred.

Workspace evidence: `work/hdr-gamut-batch`, `work/hdr-gamut-probe/bin/Release/net10.0/verified-0.176.0`, `work/verify-hdr-gamut.ps1`, and `work/hdr-compatibility`. The saved 0.174.0 engine is checked against its previous verified hash before comparing default policy JSON and video/audio recovery identities. Initial fixture/parser/probe diagnostics are retained separately.

## Next unfinished work

Prioritize metadata-aware HDR10+/Dolby Vision transformations, changing static HDR profiles, explicit mixed SDR/PQ/HLG targets, and additional HDR output codecs. RGB archival output, alpha/linear RGB, complex display matrices, wider audio layouts and bitrate controls, more containers and variable output profiles remain open. Continue caption/selection/recovery/provider/session work and the full frozen catalog/options acceptance backlog. The default backend and full cutover gate stay unchanged.
