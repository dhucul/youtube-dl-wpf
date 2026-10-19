# Native Dolby compression, non-PQ sources and HDR10+ ellipse authoring — 0.188.0

The existing `hdr-reconstruct` policy now handles documented limited/extended Dolby mapping references in HEVC profile 8 and AV1 profile 10, reconstructs supported HLG Dolby bases into PQ, and converts tagged relative-light source domains using their declared mastering peak. A separate explicit selector-authoring choice supports nonrectangular HDR10+ ellipses while measuring valid processing envelopes.

Native C# owns reference state, EMDF extraction, signal conversion, conic transforms and metadata writing. FFmpeg supplies the existing codec/probe processes. Python/NumPy and the independent Dolby validator remain test-only.

## Implemented contracts

| Area | Behavior |
| --- | --- |
| Limited Dolby compression | Require mapping ID zero and resolve declared mapping/DM method-1 dependencies before clipping or presentation reordering |
| Extended Dolby compression | Resolve up to sixteen bounded mapping IDs, retain their individual state and canonicalize output mapping/DM IDs for complete independent uncompressed output |
| AV1 profile 10 | Parse sized OBUs, T.35/EMDF payloads and the protection footer; expand RPUs natively and bind one complete RPU to each verified coded/decoded picture |
| HLG-compatible Dolby | Bake the RPU's prediction and supported display matrices, then signal the resulting PQ HEVC output as profile 8.1; retain original HLG/AV1 source packets separately |
| Tagged non-PQ sources | Convert HLG, linear, BT.709 inverse-OETF and gamma-2.2/2.8 (`bt470m`/`bt470bg`) sources through native double-precision transfer math and BT.2020 limited-range YCbCr |
| Source reference luminance | Require a declared mastering maximum of 100–10,000 nits; use it as the explicit reconstruction calibration rather than guessing a relative signal's peak |
| HLG display model | Apply inverse OETF and luminance-dependent OOTF with ideal zero black; use the BT.2100 nominal gamma formula at 400–2,000 nits and its extended-range formula outside that interval |
| Affine selectors | Under explicit `ellipse-envelope` authoring, map supported unclipped soft/hard elliptical selectors through rotation, shear, reflection, anisotropic scale and crop |
| Projective selectors | Transform a hard-edge ellipse as a homogeneous conic, recover its new centre/axes/angle, and write a representable hard-edge selector |
| Statistics | Measure the newly authored inclusive processing rectangles from every final decoded output picture; retain tone/saturation controls and overlap options |

Output remains square-pixel ten-bit HEVC/BT.2020/PQ at the configured 16:9 height and 30 fps. Existing whole-output-frame and source-frame-aligned clipping restrictions remain. Non-PQ raster conversion quantizes into the ten-bit reconstruction raster before geometry; supported Dolby/FEL baking retains the existing twelve-bit intermediate before the ten-bit target. The complete coded source periods remain exact `Keep original` artifacts.

## Explicit selector authoring

`Formats.LiveHdrSelectorPolicy` is a new native WPF setting, separate from the frozen classic CLI settings. Its default is `strict`. The optional native request field `TransitionSelectorPolicy` is omitted from default JSON and recovery identities. Choosing **Preserve ellipses; measure rectangle envelopes** while HDR reconstruction is active sets `ellipse-envelope`; queued requests retain an immutable snapshot. Dormant choices are preserved in settings and excluded from inactive native policies.

This authoring choice preserves supported elliptical selection geometry at coded coordinate precision while changing the processing rectangle to a bounded envelope and regenerating its scene statistics. Those statistics describe the authored rectangle, including its padding/interpolation; they are not ellipse-weighted distributions. Overlapping windows remain independently measured. Tone/saturation parameters and selector overlap mode remain intact.

The external ellipse must lie entirely within the source processing rectangle and cropped source. Cropping part of the old rectangle is accepted when the actual ellipse remains whole. Affine mapping preserves the shared centre and proportional inner/outer ellipses. A projective hard edge has coincident inner and outer boundaries, so its conic is representable as one ellipse. Projective soft edges generally acquire different centres and cannot be written faithfully into HDR10+'s concentric selector syntax; they remain refused. Actually clipped ellipses require pixel-mask/tone baking and remain refused. Dolby level-5 inset-area restrictions remain unchanged.

This is an authoring contract for representable metadata, not a hardware certification or a promise of identical display tone mapping across players.

## Compression and signal boundaries

Both container compression names and RPU compression syntax are validated. Container values 0/1/2/3 mean none/limited/reserved/extended; RPU DM method 1 performs static-field inheritance. **RPU DM methods above 1 remain unsupported.** The inspected FFmpeg decoder explicitly leaves them unimplemented, and the public Dolby parser does not define their interpretation. The engine does not guess a byte layout or silently discard their metadata.

Explicit sequence information remains required. Unsupported implicit/default headers, mismatched affected/current DM IDs, missing references, incompatible reference signatures, invalid CRC/footer, partitioned prediction and unsupported profiles/layer layouts remain refused. Source mapping IDs are preserved in parser state and replaced only in complete output metadata.

Non-PQ input support requires a clear progressive ten-bit HEVC BT.2020 nonconstant-luminance limited-range source, or the supported AV1 Dolby route. HLG-compatible Dolby uses the RPU's actual reshaping/display domain, rather than applying a plain HLG curve to its base and retaining incompatible metadata. Arbitrary non-PQ inner-RPU EOTF parameters remain unresolved. RGB/ICtCp/range/headroom/other-primary combinations and absent mastering calibration remain outside this batch. HDR10+ input still requires a PQ base signal.

For relative-light sources, the chosen calibration is the mastering maximum: linear/gamma/inverse-OETF values are scaled to that peak before PQ encoding; HLG uses it as the nominal display peak. Finite out-of-range samples are clipped into this policy's representable signal interval. This explicit output model does not claim production/exchange headroom preservation or an inferred display environment.

## Verification

All **3,719 native checks** (2,567 core + 1,152 optional media) and **143 app checks** pass on the final candidate. Release has zero warnings/errors. The 49 new core contracts pass in a 56-check focused invocation; twenty new media workflows and eight app cases pass. Independent verification covers the 88 files and 7,920 pictures below. Default/recovery compatibility with verified 0.186.0 passes.

The batch adds 49 core, 20 optional-media and eight app checks. Cases cover nonzero/interleaved mapping references, replacement and missing state, limited/extended constraints, AV1 protection footer, source calibration and finite/cancelled rasters, fixed HLG gamma anchors at 100/400/2,000/10,000 nits, oblique/sheared/reflected/cropped selectors, projective hard/soft edges, strict defaults, WPF persistence, queued snapshots and compiled controls.

Eleven retained generated native workflows cover four selector transforms, three relative-light sources, HLG Dolby, extended mapping IDs and compressed PQ/HLG AV1. A separate NumPy implementation reconstructs the source pixels and conics, reads the output payloads independently and measures every final HDR10+ window. The SHA-verified test-only `dovi_tool 2.3.4` parses source/output Dolby data; the oracle independently resolves its exported references and static inheritance. The final retained set contains 88 fully decoded files, 7,920 picture/metadata comparisons, 2,160 non-PQ source-picture comparisons, 5,760 HDR10+ window measurements across 2,880 pictures, 2,880 Dolby metadata pictures, 44 exact source inventories and 22 exact main/original inventories. Archive repeats make zero HTTP requests/callbacks.

The source-tag/reserved-compression fixture diagnostics and analytical half-unit conic-rounding correction remain retained. The first full run was stopped before completion to add the BT.2100 extended HLG gamma formula and fixed boundary anchors. The final candidate uses a fresh isolated temporary recovery registry, without overlapping broad suites. Default JSON and video/audio recovery identities are compared with the preserved verified 0.186.0 binary. The compiled selector control is rendered and inspected. Real-account verification remains user-deferred; saved accounts are not accessed.

## Remaining work

Obtain defined syntax and independent samples for other Dolby DM methods and implicit/default sequence forms; extend only with verifiable contracts. Continue arbitrary inner-RPU transfer domains, additional primaries/RGB/ICtCp/range/headroom handling, enhancement resampling/cadence and genuinely clipped/projective-soft selector authoring. Broader provider/session/login/player/proof/signing, codec/container/caption/audio/selection, historical recovery/storage/concurrency and full frozen catalog/options/packaged acceptance remain open. Default startup remains YtDlpEngine with immutable native opt-in routing; full replacement is not complete.

Evidence: `work/hdr-selectors-batch`, `work/hdr-selectors-probe`, `work/hdr-selectors-verify`, `work/hdr-selectors-compatibility`. Generated tiny RPU derivatives retain the existing MIT fixture notice.

Primary references: [Dolby compression and reference rules](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/dovi_rpuenc.c), [decoder boundaries](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/dovi_rpudec.c), [compression enum and signal metadata](https://github.com/FFmpeg/FFmpeg/blob/master/libavutil/dovi_meta.h), [public Dolby metadata parser](https://github.com/quietvoid/dovi_tool/blob/main/dolby_vision/src/rpu/vdr_dm_data.rs), [ITU-R BT.2100-3 transfer functions](https://www.itu.int/dms_pubrec/itu-r/rec/bt/R-REC-BT.2100-3-202502-I!!PDF-E.pdf), and [HDR10+ selector fields](https://github.com/FFmpeg/FFmpeg/blob/master/libavutil/hdr_dynamic_metadata.h).
