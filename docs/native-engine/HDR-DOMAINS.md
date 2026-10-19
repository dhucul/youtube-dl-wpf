# Native Dolby sequences, PQ domains, perspective and HDR10+ windows — 0.186.0

The existing opt-in `hdr-reconstruct` policy now resolves supported compressed/dependent Dolby metadata, reconstructs custom PQ signal matrices, renders bounded perspective transforms and regenerates statistics for one to three HDR10+ processing windows. Native C# owns sequence state, signal math, raster sampling and metadata writing. FFmpeg supplies codec decoding/encoding; Python and the independent Dolby validator remain test-only.

## Implemented contracts

| Area | Behavior |
| --- | --- |
| Dolby profile 8.1 sequences | Resolve explicit mapping references by ID and DM compression method 1 in coded sample order, then write complete independent uncompressed RPUs before presentation reordering, clipping or geometry |
| Static/dynamic inheritance | Preserve static signal fields and static extension levels 6/10/32/254/255 during compressed frames; replace dynamic extensions each frame; reset static state on an independent full DM update |
| Sequence failure | Check CRC, mapping IDs/signatures, container compression signaling, missing/incompatible references, extension bounds and complete samples; failed frames do not publish partial parser state |
| Custom PQ signal matrices | After polynomial/MMR prediction and optional FEL residual composition, apply the declared nonlinear matrix/offset, PQ EOTF, source linear matrix, canonical linear inverse, PQ inverse EOTF and canonical nonlinear inverse/offset |
| Baked metadata | Replace applied reshaping with identity, disable residual dependencies, canonicalize converted signal matrices, preserve display trims and update compatible profile 8.1 signaling |
| Perspective | Interpret all nine display-matrix terms in their signed fixed-point domains, fit the projected cropped canvas, inverse-map output pixel centers and sample bilinearly with black outside the source |
| HDR10+ statistics | Parse one to three processing windows and measure each inclusive rectangular region from its own final decoded pixels; regenerate maxima, average maxRGB, nearest-rank percentiles and peak-bin occupancy |
| HDR10+ selectors | Remap representable axis-aligned rectangles and ellipses through scale, reflection, quarter-turn and anisotropic transforms; retain overlap options, display grids, tone controls and saturation metadata |

Sequence state belongs to one coded source stream. It is never carried from one independent period into another. Expanded metadata uses bounded inventories and is attached to the verified coded picture before any output selection. Original source periods, including compressed RPUs and enhancement packets, remain exact `Keep original` artifacts.

Custom-domain conversion is performed in double precision after prediction/residual composition and quantized to twelve bits before the existing ten-bit HEVC target. Canonical matrices retain the prior path. Finite normalized values are clipped to the representable signal range; singular or poorly conditioned matrices are refused. Output remains a fixed 16:9 square-pixel HEVC/PQ canvas at 30 fps, with the existing source-frame-aligned and whole-output-frame clipping rules.

Perspective uses homogeneous division in both directions. Corner denominators must remain positive and bounded across the cropped source; horizon crossings, singular matrices, extreme conditioning and oversized canvases fail before publication. Full-frame dynamic metadata can follow this transform. An inset rectangular Dolby area or HDR10+ processing window is refused when the transformed region cannot be represented by the metadata syntax. Cropping an inset HDR10+ selector also remains refused.

## Exact-statistics meaning

Statistics are calculated after final ten-bit 4:2:0 encoding from decoded linear BT.2020 float samples normalized to 10,000 nits. Each window includes both endpoint pixels of its processing rectangle. Its ellipse controls tone-mapping selection, rather than weighting the scene distribution. Overlapping rectangles are measured independently; their populations are not subtracted or reassigned by layering order.

The accumulator uses all selected pixels, including actual interpolated samples and black padding. It builds 100,001 maxRGB bins at the coded 0.00001 precision. Quantiles use nearest-rank selection. Bright-pixel fraction is the occupancy of the brightest representable bin, rounded to 0.001; black pictures have zero luminance and full peak-bin occupancy. Existing percentile positions and version-1 reserved 5%/10% sentinel values are preserved. Absent distributions receive the existing bounded default positions. These are exact measurements of the declared decoded samples at metadata precision, with the documented codec, color and rounding model.

## Verification

All **3,650 native checks** (2,518 core + 1,132 optional media) and **135 app checks** pass on the frozen final candidate. The 36 new domain contracts also pass in a 43-check focused invocation. Release has zero warnings/errors. Eight independent workflows verify 64 complete decodes, 5,760 exact picture/metadata frames, 2,880 HDR10+ pictures with 5,760 exact processing-window measurements, 2,880 Dolby metadata frames, 100 enhancement pictures, 32 retained source inventories and 16 main/original inventories. Default policy and recovery compatibility with verified 0.184.0 passes.

The batch adds 36 core contracts and 11 media workflows. New cases cover independent/dependent/compressed chains, inherited and refreshed static state, invalid CRC and missing references, container signaling, unsupported EOTF, custom nonlinear/linear matrices, custom FEL reconstruction, perspective raster/cancellation/horizon guards, independent two/three-window populations and selector transforms. Existing caption, clipping, stopped-process, recovery, refusal/publication and source-retention checks remain enabled.

Eight retained generated native workflows exercise perspective, two/three windows, reflected selectors, compressed Dolby, custom nonlinear and linear domains, and custom FEL. A separate NumPy renderer reconstructs source pixels and compares complete final picture hashes. It independently reads HDR10+ payloads, derives transformed rectangle/ellipse geometry and measures every final window. The SHA-verified test-only `dovi_tool 2.3.4` parses source and output Dolby metadata; a separate oracle resolves its exported references/static inheritance before comparison. Full published files decode, original coded packets match, and archive repeats issue zero HTTP requests/callbacks.

The oracle retains its initial diagnostic runs: generic matrix inversion changed floating-point half-code tie evaluation, and the external Dolby export exposes transmitted references/extensions rather than a fully expanded frame. The completed oracle explicitly evaluates the declared double-precision sampling equations and independently resolves stream state. Production code was unchanged during this oracle correction.

Default policy JSON and video/audio recovery keys are compared with the preserved, previously verified 0.184.0 binary. The WPF/app regression suite retains all 135 checks and seventeen native extension settings. No UI setting, default backend or automatic fallback is added. Broad native suites use their own temporary recovery registry and run serially with other broad suites. Real-account verification remains user-deferred; no saved accounts are accessed.

## Remaining coverage

Supported compressed playback is clear HEVC profile 8.1 with explicit sequence metadata, bounded mapping references and DM method 1. Other compression methods, implicit/default sequence state, mismatched affected/current DM IDs, other profiles, partitioned prediction and unsupported coefficient/color layouts remain refused. Custom-domain reconstruction requires twelve-bit PQ YCbCr with the declared range/chroma semantics; arbitrary transfer functions, RGB/ICtCp signal declarations and unsupported enhancement resampling/cadence remain open.

Projective/oblique inset selectors and clipped selectors need an explicit authoring policy. Their quadrilaterals, clipped conics or nonrectangular active areas cannot be represented exactly by the current output metadata. No enclosing selector is silently substituted.

Full replacement also still requires the frozen catalog/options, broader provider sessions and login/player/proof/signing contracts, codec/container/caption/audio/selection controls, historical recovery/storage/concurrency evidence and packaged acceptance. This batch makes no full-parity or production-cutover claim.

Evidence: `work/hdr-domains-batch`, `work/hdr-domains-probe`, `work/hdr-domains-verify` and `work/hdr-domains-compatibility`. Tiny generated RPU variants use the existing public MIT fixture attribution in `DOLBY-METADATA-NOTICE.txt`.

Primary references: [Dolby dependency and DM parsing](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/dovi_rpudec.c), [static extension classification](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/dovi_rpu.h), [configuration compression signaling](https://github.com/FFmpeg/FFmpeg/blob/master/libavformat/dovi_isom.c), [Dolby signal conversion](https://github.com/haasn/libplacebo/blob/master/src/shaders/colorspace.c), and [HDR10+ field/processing-window semantics](https://github.com/FFmpeg/FFmpeg/blob/master/libavutil/hdr_dynamic_metadata.h).
