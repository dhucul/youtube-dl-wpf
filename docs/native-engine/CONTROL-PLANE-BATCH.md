# Shared native argument and audio batch

Version 0.116.0, verified October 11, 2026. This checkpoint implements shared behavior across existing providers and establishes the [complete coverage backlog](COVERAGE-BACKLOG.md). Full replacement remains unfinished; production WPF still starts the original engine.

## What changed

- Ordered native arguments now include audio extraction, audio format/quality and ffmpeg directory/file selection. Extraction clears conflicting video policies; later arguments retain their normal override order.
- Added metadata/chapter aliases and inverse retention, filename, archive and cookie controls. Cookie file and browser resets are independent. Forced overwrites also disable continuation and post-processing overwrite protection. Disabling thumbnails clears both preferred and all-thumbnail saving.
- Inline buffer/chunk values now follow the same value handling as separated values.
- Native analysis scans argument arity before choosing discovery/connection controls. IP-family, certificate and cookie resets are preserved. Download-only values are consumed as opaque data, so a value resembling a connection flag cannot become a new instruction. Unsupported authentication/discovery controls reach explicit preflight rejection instead of being silently discarded. Saved settings remain unchanged.
- Vorbis re-encoding uses decoded sample positions for its new clock. A live Clyp conversion exposed non-monotonic timestamps in the prior behavior. Every extracted audio output now receives full local decoding before it can be published.

The native argument switch contains 105 of the 276 captured long option names, up from 91. Handler presence is not complete option parity. The 18 explicitly refused app settings remain open.

## Verification

The Release solution build passed with zero warnings and errors. All 1,766 native checks passed: 1,308 core and 458 optional media checks. All 69 app checks passed: 65 regular and four optional browser checks. This batch adds 12 core checks, six generated-media checks and three app checks. Generated tests cover ordered overrides, preflight, opaque argument values, retention, archive repeats, actual output formats and variable-block Vorbis timing.

Six live native adapter workflows exercised saved arguments followed by job overrides, forced IPv4 during analysis and downloading, explicit ffmpeg location, complete conversion, completion callbacks, original retention choices and archive repeats. All ten resulting files passed independent full stream decoding with no diagnostics.

| Provider / workflow | Requested output | Keep original | Converted bytes | Duration |
| --- | --- | --- | ---: | ---: |
| Vocaroo imPgJC013AW | FLAC | yes | 540,650 | 26.09 s |
| Clyp aw2jlf0j | WAV | yes | 45,113,460 | 127.87 s |
| PeerTube ff2EVqgHrQX5WJyJJc7Uax | Opus | yes | 149,664 | 9.24 s |
| Vocaroo short alias | AAC/M4A | no | 117,368 | 26.09 s |
| Clyp supplied Ogg | Vorbis/Ogg | no | 1,203,264 | 127.87 s |
| PeerTube same video | AAC/M4A | yes | 5,248 | 9.24 s |

The Clyp WAV retained exactly equal decoded floating-point samples. The Vocaroo FLAC retained the full sample count, with maximum absolute sample difference 1.1896463547600433e-7, within one 24-bit output quantization step; exact floating-point equality is not claimed. The generated 16-bit PCM-to-FLAC case preserved exact samples. Retained source files were fully decoded. Every archive repeat produced no new output or callback; Vocaroo/Clyp repeats opened no HTTP session, while PeerTube repeats still performed discovery in one session.

## Remaining batch and release gates

P0-01 remains open for complete argument/default/alias semantics, quality 10, multi-format rules, separate media/sidecar overwrite defaults, app-managed settings and broader provider/format interactions. The scanner deliberately rejects unrecognized arguments; it does not implement all captured CLI grammar.

The next priorities are authentication/session lifecycle (including eight refused authentication settings), live/adaptive recording, and YouTube/Vimeo access/playback coverage. Shared hosting and consumer delegation, collections/post-processing, remaining provider families, all 136 known-broken key decisions and the final packaged-app cutover also remain open. Every frozen key and option has an explicit row and acceptance requirements in [coverage-backlog.json](coverage-backlog.json). None is marked complete solely from provider count, source references or passing tests.

All prior working changes remain uncommitted. No commit, push, data reset, production backend switch or dependency removal was performed at this checkpoint.
