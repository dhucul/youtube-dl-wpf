# Native FEL, affine geometry and measured HDR10+ — 0.184.0

The new opt-in `hdr-reconstruct` live DASH policy reconstructs supported profile-7 FEL pictures, renders source affine transforms and explicit crop margins, and regenerates full-frame HDR10+ statistics from final decoded output pixels. Native C# performs prediction, residual composition, affine sampling, histogram accumulation and metadata writing. The existing FFmpeg installation supplies codec decoding/encoding. No Dolby utility or Python runtime is added to the application.

The older `hdr-transform`, `hdr-spatial` and metadata policies retain their acceptance boundaries. Default startup remains YtDlpEngine, with immutable native opt-in routing. The existing color setting gains one choice; one separate native crop setting raises the native extension count to seventeen. The frozen 126 classic settings and 276 captured long-option names remain unchanged. Full replacement is still open.

## Implemented behavior

| Input or operation | Behavior |
| --- | --- |
| Profile-7 full enhancement layer | Parse independent fixed-point RPU prediction/NLQ, decode both layers, apply polynomial or MMR prediction and signed linear-deadzone residuals, and reconstruct the twelve-bit signal |
| Full- or half-resolution enhancement video | Associate enhancement pictures with their original coded sample and require the same presentation order as the decoded base pictures; half-resolution samples use explicit center-aligned bilinear interpolation |
| Baked Dolby output | Convert the reconstructed signal to the explicit ten-bit HEVC target, replace already-applied reshaping with identity metadata, disable residual processing and preserve other display metadata with compatible profile-8.1 signaling |
| Crop margins | Apply validated `left,top,right,bottom` source-pixel margins before fitting the output canvas; persist through WPF settings and immutable queued requests |
| Source affine display matrices | Render reflection, shear, rotation, anisotropic scale and combinations through a native inverse map with bilinear sampling and black outside the cropped source |
| Dolby active areas | Reflect, crop and map rectangular areas; refuse nonrectangular inset areas that cannot be expressed by level-5 metadata |
| Full-frame HDR10+ | Measure every final decoded ten-bit output picture in linear BT.2020, regenerate maxima, average maxRGB, nearest-rank quantiles and quantized peak-bin occupancy, including actual black padding/interpolation |
| Transient receipt replacement denial | Retry owned same-directory atomic timeline replacement for bounded Windows sharing/access denials; preserve cancellation and permanent-failure behavior |

Output remains a fixed 16:9, square-pixel, ten-bit HEVC canvas at 30 fps. Joins retain the existing whole-number output-frame and source-frame-aligned clipping restrictions. FEL is composed at twelve-bit precision before conversion to that target. `Keep original` retains complete original coded periods, including enhancement data and RPUs, rather than replacing originals with reconstructed pictures.

## Reconstruction limits

FEL support requires a clear, progressive, HDR10-compatible HEVC profile-7 input within 3840×2160, ten-bit base/enhancement samples, twelve-bit VDR, complete independent uncompressed RPUs, unpartitioned linear-deadzone NLQ and a stable declared rational source cadence. Polynomial orders one/two and MMR orders one through three are implemented. Source standard PQ/HDR10 signal matrices are checked before claiming compatible baked output; custom signal matrices need further color reconstruction. Missing/duplicate enhancement or RPU data, wrong layer headers, unmatched picture order, unsupported precision/mapping and missing mastering metadata fail before publication.

This is full residual composition for the supported subset, not a base-only decode or an FEL-to-MEL relabeling. Half-resolution interpolation is the explicit software sampling model above; generated reference tests are not hardware certification. Compressed/dependent metadata, other profiles, partitioning, custom signal domains, additional resampling layouts and variable source cadence remain open.

Affine input supports bounded nonsingular two-dimensional matrices and normalizes their translated bounding box onto the output canvas. Perspective matrices remain refused. Multiple HDR10+ processing windows remain available in older policies; exact regeneration currently requires one full-frame window. Oblique/sheared inset Dolby active areas remain refused because rectangular level-5 offsets cannot represent the resulting region exactly. Input crop syntax is validated before processing, and crop bounds are validated against each period's actual dimensions.

## Exact-statistics contract

Statistics are measured after final ten-bit 4:2:0 encoding, using the decoded linear BT.2020 float samples normalized to 10,000 nits. The native accumulator clips finite RGB values to the representable 0–1 range, measures component maxima and the mean of per-pixel maxRGB, and builds a complete 100,001-bin maxRGB histogram at HDR10+'s 0.00001 precision. Percentiles use nearest-rank selection; `fraction_bright_pixels` records occupancy of the peak representable bin at its declared 0.001 precision. All-black pictures therefore report zero luminance and full peak-bin occupancy.

Existing percentile positions are retained; a bounded default distribution is generated when absent. The version-1 nine-entry reserved 5%/10% sentinel fields remain 0/255, and undeclared extended semantics are refused. Target/mastering display peak grids and tone/saturation controls remain intact. This regenerates representable statistics from actual decoded pictures; it no longer estimates padding statistics by scaling sparse source knots. Color conversion and metadata rounding retain their declared finite precision.

Raw frames stream between owned codec processes with bounded native buffers. Histogram verification retains a presence marker instead of accumulating every verbose HDR10+ XML descriptor; complete coded payloads are still compared per picture. Cancellation and parent-process lifetime handling apply to the raw processors.

## Verification

All **3,603 native checks** (2,482 core + 1,121 optional media), **135 app checks** and **92 focused checks** pass on the final candidate. Release has zero warnings/errors. The broad core suite and final full suite run serially in isolated owned temporary registries. Independent six-workflow picture/metadata checks and verified 0.182.0 default policy/recovery compatibility pass.

New coverage comprises 61 core, 24 optional-media and nine app checks. It includes exact black/white anchors and quantiles, crop/reflection/shear geometry, signed residuals and deadzone boundaries, polynomial/MMR prediction including higher orders, twelve-bit composition, half-resolution enhancement, applied-prediction metadata reset, persistent/released/cancelled receipt locks and durable reopen, WPF persistence/snapshot isolation, captions, a stopped worker, new-session recovery, corrupt prefixes and unchanged prior outputs/archive on refusal.

Six retained generated native workflows independently compare 4,320 final picture hashes with a separate NumPy renderer. That oracle independently applies affine transforms, source RPU coefficients and NLQ residuals, codec precision conversion and output selection. It checks 2,160 HDR10+ frame histograms against final pixels and 2,160 Dolby metadata frames through the separate SHA-256-verified `dovi_tool 2.3.4` validator. All 2,160 FEL output references differ from the prediction-only reference, proving visible residual contribution. The enhancement decoder independently processes 300 source pictures. A separate generated standalone FEL bridge check retains all forty picture timestamps at 24000/1001 fps and fully decodes its output; it does not broaden the fixed-30-fps join restrictions. Forty-eight published files fully decode, twenty-four retained source inventories and twelve main/original stream inventories match, and archive repeats issue zero HTTP requests/callbacks.

Broad crash-recovery suites use the global temporary source/scope recovery registry. An overlapping full/core diagnostic run demonstrated fixture-identity interference; final broad runs are serialized and use dedicated owned temporary registries. Initial missing raw color tags and the stopped-worker receipt diagnostic remain retained. The final candidate is verified against preserved 0.182.0 policy JSON and video/audio recovery identities. User accounts were not accessed; real-account verification remains deferred.

Evidence: `work/hdr-reconstruct-batch`, `work/hdr-reconstruct-probe`, `work/hdr-reconstruct-verify` and `work/hdr-reconstruct-compatibility`. Reference fixtures derive from the existing public MIT-licensed tiny RPU data; the existing Dolby metadata notice applies. NumPy and the external Dolby validator are test-only.

## Sources and next work

Primary references include [Dolby prediction and residual composition](https://github.com/haasn/libplacebo/blob/master/src/shaders/colorspace.c), [coefficient normalization](https://github.com/haasn/libplacebo/blob/master/src/include/libplacebo/utils/libav_internal.h), [HDR10+ metadata fields](https://github.com/FFmpeg/FFmpeg/blob/master/libavutil/hdr_dynamic_metadata.h), [HDR10+ field semantics](https://github.com/haasn/libplacebo/blob/master/src/include/libplacebo/utils/libav_internal.h), [Dolby bitstream separation](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/bsf/dovi_split.c) and the [independent validator release](https://github.com/quietvoid/dovi_tool/releases/tag/2.3.4).

Continue with compressed/dependent Dolby metadata, broader signal matrices/EL sampling and cadence, richer multiwindow exact statistics and perspective/nonrectangular metadata policies. Full provider/session, codec/container, selector, storage/concurrency and frozen catalog/options/packaged acceptance remain open. Production cutover stays blocked by the original replacement scope.
