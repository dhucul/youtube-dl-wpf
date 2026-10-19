# Native TTML and DFXP captions

The C# engine now interprets supported TTML/DFXP documents directly and renders SRT or WebVTT without ffmpeg. ASS and LRC conversion use the resulting timed text with the retained ffmpeg tools. Saving an original TTML/DFXP document preserves its bytes; embedding uses a temporary WebVTT representation because ffmpeg does not directly read these XML documents in the configured build.

## Implemented scope

- Current TTML and legacy 2006 DFXP namespaces, plus simple unqualified documents. XML parsing prohibits DTDs and external resolution, validates structure and checks cancellation.
- Media clock/offset times in hours, minutes, seconds, milliseconds, frames and ticks. Explicit frame-rate multipliers, subframes and tick rates are respected. Decimal calculations precede rounding to the output's millisecond precision.
- Nested paragraph/container bounds, end/duration precedence, parallel and sequential children, and finite implicit container ends. Timed spans split a paragraph when visible text changes; adjacent identical states merge. Simultaneous paragraphs retain document order.
- Unicode, XML entities, line breaks, default/preserved whitespace, and inherited bold/italic/underline emphasis. Static style references, basic region styles, visibility, display suppression and zero-alpha/opacity text are handled. Intentional blank payload lines use a nonbreaking-space marker so SRT/VTT parsers do not terminate the cue early.
- Existing language/format selection, standalone saving, transactional publication, no-overwrite and embedding. TTML-to-SRT/VTT needs no media tool; ASS/LRC and media embedding retain the existing ffmpeg requirement. LRC sidecars still use full timed text for embedding, preserving explicit cue ends.

The reader bounds input/output to 2 MiB, XML depth to 64, XML reader nodes to 50,000, styles to 512, regions to 256, cues to 10,000, and conversion work to 8 million visits/characters. Unbounded text, unsupported time bases, sub-millisecond cues that cannot survive output rounding, unknown timing/animation/image content, and invalid references fail before publication.

This is a text converter, not a complete TTML presentation renderer. Font family/size, colors, positioning, layout and some decoration do not map completely to these output formats. Timed regions, nested/animated style definitions, image-based TTML, conditionals, repeats/frozen presentation, clock/SMPTE/drop-frame modes and remaining segmented/live/multi-period timing remain pending. Clear fragmented MP4 `stpp` text now uses this reader with per-sample clipping; see `MP4-TTML.md`. Original XML may still be saved without converting unsupported presentation features.

## Verification on October 8, 2026

Thirteen deterministic checks cover text/timing, nested clipping, sequential offsets, timed-span visibility, style/region inheritance, whitespace, frame/tick calculations, legacy namespaces, malformed XML/references, limits/cancellation, native standalone conversion, original-byte preservation and failed-publication/archive protection.

Four generated-media checks save original TTML or converted SRT/ASS/LRC while embedding captions into MP4. They retain both cue start/end pairs, Unicode and multiline text, all 30 generated video frames, English language metadata and temporary-file cleanup. Full media decoding passed.

The public [Shaka Tears of Steel TTML sample](https://storage.googleapis.com/shaka-demo-assets/tos-ttml/dash.mpd), listed in [Shaka's sample assets](https://github.com/shaka-project/shaka-player-embedded/blob/main/sample_xcode_project/sample_xcode_project/assets.plist), exercised the actual native DASH caption path:

| Language | Original TTML | Native SRT | Cues |
| --- | ---: | ---: | ---: |
| English | 6,266 bytes | 4,350 bytes | 71 |
| German | 7,079 bytes | 5,058 bytes | 76 |

The original-saving and SRT-conversion jobs made six total requests for the manifest and text documents, with a guard rejecting any audio/video request. An independent comparison retained every cue start/end and visible text. The source documents end paragraphs with line breaks; comparison treats a standalone nonbreaking-space line in SRT as the equivalent blank line.

A subsequent complete native DASH download with both converted languages embedded produced a 22,023,244-byte MP4: H.264 256x106, 17,620 video frames, AAC with 31,617 audio frames, duration 734.166667 seconds. Both subtitle streams used `mov_text` with English/German language tags. Full decoding passed, and extraction of the embedded captions matched all 71/76 saved cues in text and timing. The operation made 375 HTTP requests, using bounded parallel fragment transfer.

Primary timing references: [TTML1](https://www.w3.org/TR/ttml1/) and its referenced [SMIL timing model](https://www.w3.org/TR/SMIL2/smil-timing.html). Parsing, timing resolution and rendering are independent C# implementations; no other player/parser runtime was added.
