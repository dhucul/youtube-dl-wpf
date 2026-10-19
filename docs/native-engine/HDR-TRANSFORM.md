# HDR10+ padding, display transforms and Dolby MEL — 0.182.0

The opt-in `hdr-transform` live DASH color policy extends the existing HEVC geometry path. It uses the existing WPF color setting, requires explicit HEVC transition conversion, and retains immutable queued settings, the default YtDlpEngine startup and all sixteen native extension settings. The `hdr-spatial` and older policies retain their prior acceptance boundaries. Full replacement remains open.

## Implemented subset

| Input | New behavior |
| --- | --- |
| HDR10+ letterbox/pillarbox padding | Reweight global average maxRGB, bright-pixel fraction and supplied CDF knots for the added black area; preserve per-window statistics, tone controls and display calibration grids |
| Non-square pixels and fractional fits | Normalize pixel aspect and fit the fixed 16:9 canvas, mapping inclusive HDR10+ pixel coordinates and Dolby active-area edges separately |
| Quarter turns | Transform rectangular windows, centers, ellipse axes/orientation and Dolby active areas |
| Arbitrary-angle pure rotations | Rotate single-window HDR10+ and full-active-area Dolby pictures; use the precise validated display matrix rather than ffprobe's truncated integer angle |
| Anisotropic geometry | Map external ellipses using the transformed quadratic form and its principal axes, retaining the internal/external major-axis ratio |
| Profile-7 MEL | Verify complete independent uncompressed RPUs and neutral NLQ coefficients, remove neutral enhancement payloads, preserve mapping/display metadata and sample timing, and produce compatible profile-8.1 output |

The transform pipeline uses explicit orientation, bounded raster dimensions, ten-bit HEVC, square output pixels and 30 fps. Frame selection, source-aligned clipping and metadata families retain the existing limits. Original coded source periods, including enhancement data, remain exact when originals are requested.

## Statistics and representation limits

HDR10+ padding uses the geometric fraction of content in the output canvas. Averages and bright fractions are rounded to their declared precision. Supplied distribution knots keep their luminance values, with percentile positions reweighted for black padding; a black knot is added when representable. Whole-percent fields quantize this CDF model, and colliding percentile positions merge. The engine does not infer an exact dense histogram from sparse percentile metadata or remeasure every transformed pixel. More than fifteen resulting knots is refused. Arbitrary-angle raster interpolation can also change measured statistics slightly; the retained metadata is this explicit geometric model.

Target/mastering display peak-luminance grids describe displays, so picture transforms retain them unchanged. Multiple-window oblique rotations are refused because ST 2094-40 cannot describe the resulting nonrectangular processing windows. Inset Dolby active areas are refused for oblique rotations; a full-frame Dolby active area uses the transformed bounding rectangle. Reflections, shears, translations, perspective matrices and arbitrary user crop/warp controls remain open. Geometry is rounded to the metadata's integer pixel/degree precision.

MEL conversion accepts the neutral linear-deadzone NLQ configuration only. Native C# checks RPU CRC, bit depth, mapping, independent metadata, partitions, extension bounds and enhancement presence before conversion. A local packet-copy MP4 bridge rewrites sample lengths/chunk offsets and the Dolby configuration while retaining time/sample tables. No Dolby utility is a runtime dependency. FEL residual-bearing inputs are explicitly refused because the bundled HEVC decoder skips enhancement NALs; their base pictures cannot prove full residual reconstruction. Compressed/dependent RPUs, other enhancement layouts and full FEL reconstruction remain open.

## Verification

All **3,518 native checks** (2,421 core + 1,097 optional media), **126 app checks** and **95 focused checks** pass on the final implementation. Release has zero warnings/errors. Five independent workflows verify forty full decodes, 3,600 exact reference picture/geometry frames, twenty retained source inventories, ten main/original inventories, 2,160 HDR10+ geometry/statistics frames and 1,440 independently parsed Dolby frames. Default policy/recovery compatibility with verified 0.180.0 passes.

The full suite, focused checks, app checks and independent workflows use the final implementation, including refusal of ellipses that round below positive integer metadata precision. Earlier candidate, reference-orientation and validator-mode diagnostics remain preserved.

The batch adds 63 core, 25 optional-media and seven app checks. Coverage includes one/two/three-window padding, supplied CDFs, display grids, fractional/non-square pixels, quarter and oblique rotations, native MEL conversion, independently identified FEL refusal, mixed captions, a force-stopped worker, fresh-session recovery, corrupt prefixes, cancellation and unchanged prior output/archive on refusal. The shared readiness test reader now permits Windows atomic checkpoint replacement while polling; earlier diagnostic logs are retained.

Five retained generated clear-media workflows independently check complete decodes, reference picture hashes, source packet inventories, published main/original inventories, HDR10+ geometry/statistics and Dolby metadata through a separate SHA-256-verified `dovi_tool 2.3.4`. The validator is test-only. A preserved verified 0.180.0 binary checks serialized defaults and video/audio recovery identities. No saved user accounts were inspected; real-account verification remains user-deferred. Generated fixtures do not constitute hardware or commercial-content certification.

Evidence lives in `work/hdr-transform-batch`, `work/hdr-transform-probe`, `work/hdr-transform-verify` and `work/hdr-transform-compatibility`. Fixture provenance is recorded in the existing MIT notice and `tests/YoutubeDl.Engine.Tests/HDR-TRANSFORM-FIXTURES.txt`.

## Sources and remaining work

Implementation was checked against primary [HDR10+ metadata field definitions](https://github.com/FFmpeg/FFmpeg/blob/master/libavutil/hdr_dynamic_metadata.h), [HDR10+ bitstream parsing](https://github.com/FFmpeg/FFmpeg/blob/master/libavutil/hdr_dynamic_metadata.c), [HEVC enhancement handling](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/hevc/hevcdec.c), [Dolby NLQ interpretation](https://github.com/quietvoid/dovi_tool/blob/main/dolby_vision/src/rpu/rpu_data_nlq.rs) and the installed tools. Independent validation uses the [official dovi_tool 2.3.4 release](https://github.com/quietvoid/dovi_tool/releases/tag/2.3.4), with the previously verified archive digest retained.

Continue with full FEL residual reconstruction, exact histogram regeneration, representable crop/shear/reflection/oblique multiwindow mappings, compressed/dependent Dolby metadata, family resets and mixed transfer policies. Broader provider/session, codec/container, selector, storage/recovery and frozen catalog/options acceptance remain in the full backlog; production cutover stays blocked by that scope.
