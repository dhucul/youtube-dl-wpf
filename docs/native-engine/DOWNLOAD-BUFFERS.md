# Native download response buffering

Verified October 10, 2026, with native engine **0.107.0**. Saved `Network.BufferSize`, `Network.NoResizeBuffer` and explicit `--buffer-size`, `--resize-buffer`, `--no-resize-buffer` overrides now reach native media transfers. Full site/option parity and production cutover remain unfinished.

## Implemented behavior

The independent C# reader starts at the configured positive byte size, or 1,024 bytes when unspecified. Binary-unit sizes use the existing native byte-size grammar. Zero, malformed and values beyond the runtime's array capacity fail before native discovery/output changes; supplied sizes are not silently clamped. These defaults follow the [documented upstream options](https://github.com/yt-dlp/yt-dlp/blob/master/README.md#download-options), without importing upstream downloader/extractor code.

Fixed mode keeps every underlying response read at the requested buffer size. Automatic mode independently adapts each response: full fast reads grow the next buffer, slow reads reduce it. Automatic growth is bounded at 4 MiB or the explicitly larger initial size. Native file-output buffering remains separate from the network read option. Already returned memory is not replaced before its consumer finishes; concurrent resources never share mutable read buffers.

Progressive files, paired tracks, ordinary/ranged HLS, whole-resource/segmented/indexed DASH, ordered HTTP sequences and the native SABR response reader receive the policy. Bounded HLS/DASH caption byte ranges also receive it. SABR's small protocol-field reads consume the response buffer while retaining HTTP read timeouts, cancellation, framing and caller-owned stream lifetime.

Existing source-size, byte-range, validator/resume, retries, rate limits, original retention, processing and transactional publication guards remain active. Saved settings are copied into requests; explicit job arguments override them without changing saved settings. The adapter now explicitly refuses 20 remaining configured settings instead of 22.

## Verification

Eleven core checks cover exact sizes/defaults/runtime overflow, actual fixed/progressive reads including sizes above the previous 64 KiB buffer, automatic growth, slow-read reduction and unconsumed memory, one-byte protocol fields, concurrent response isolation, indexed DASH/HLS byte ranges, native propagation/archive skips, invalid-size preflight and argument precedence. Four generated-media checks fully decode progressive/HLS/DASH pairs and protect prior media/archive data during cancelled paired transfers. One app check verifies saved settings, overrides and invalid-size preflight.

The Release solution build passed with zero warnings/errors. All **1,597 native checks** passed: 1,193 core and 404 optional media checks, including the existing SABR suite. All **60 app checks** passed, including four optional browser-tool checks.

Live testing used the public original MP3 for [Short](https://www.newgrounds.com/audio/listen/1136677), through native C# HTTP/direct-media and the WPF adapter with isolated saved settings. Fixed, automatic, explicit override, M4A conversion and cancelled-then-resumed workflows each completed two files. Per-workflow assertions verified actual initial/fixed/growing read sizes. Cancellation preserved prior published media and archive text; its retry requested a validated positive offset and completed. Archive repeats made no media requests or completion callbacks.

| Live workflow | Processed output | Retained original | Decoded audio frames |
| --- | ---: | ---: | ---: |
| Fixed 4 KiB | 4,863,023-byte MP3 | 4,862,954-byte MP3 | 4,653 each |
| Automatic, initially 4 KiB | 4,863,023-byte MP3 | 4,862,954-byte MP3 | 4,653 each |
| Job override, initially 8 KiB | 4,863,023-byte MP3 | 4,862,954-byte MP3 | 4,653 each |
| Fixed-buffer M4A conversion | 2,129,918-byte AAC M4A | 4,862,954-byte MP3 | 5,233 AAC / 4,653 MP3 |
| Cancelled fixed buffer, then resume | 4,863,023-byte MP3 | 4,862,954-byte MP3 | 4,653 each |

All ten files independently decoded completely: 5,358,546 samples / 121.508980 seconds each. Unconverted processed/original PCM hashes matched exactly.

The initial probe deferred its response-list serialization and incorrectly retained the last mode's list for earlier modes. Download assertions and files remained valid. Incorrect earlier lists were removed, and the probe now snapshots lists eagerly. Separate native transfers remeasured fixed 4 KiB reads, automatic 4/8/16/32 KiB reads and overridden 8/16/32 KiB reads; each downloaded exactly 4,862,954 bytes identical to the independently verified original. These supplemental raw transfers are diagnostic evidence, not three additional adapter workflows or additional independent media-decoding claims.

Evidence is under `work/buffer-stage` and `work/buffer-live-probe` in the October 10 Codex workspace. It is not a runtime dependency. Python and yt-dlp were not used in native verification. Existing uncommitted work and user settings/history/media were preserved; no Git commit or push was performed.

## Remaining work

Complete related-resource buffering semantics outside the implemented caption ranges, broader live indexed/ordered/SABR/provider interactions, exact remaining size/option grammar, all remaining catalog/site/app-option parity, production cutover and packaged verification without Python or yt-dlp remain pending. Production WPF still uses yt-dlp.
