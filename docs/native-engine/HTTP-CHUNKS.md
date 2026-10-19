# Native bounded HTTP media chunks

Verified October 10, 2026, with native engine **0.108.0**. Saved `Network.HttpChunkSize` and explicit `--http-chunk-size` overrides now reach native file transfers. Empty/zero disables chunking. Full site/option parity and production cutover remain unfinished.

## Implemented behavior

Independent C# progressive delivery requests bounded contiguous HTTP byte ranges. Binary-unit sizes support long byte counts without allocating a chunk-sized array; network read buffers remain independent. Supplied resource URLs and queries remain literal. Native preflight rejects invalid/overflowing sizes before discovery/output effects.

Accepted partial responses must bind their start, end, total and body length to the request and source. Successive requests retain a strong entity tag or an adequately old Last-Modified/date validator; weak entity tags are not used. Matching totals/validators prevent mixed representations. These guards follow [HTTP range and validator semantics](https://www.rfc-editor.org/rfc/rfc9110.html#section-14), without importing upstream downloader/extractor code.

If a server returns a complete HTTP 200 response, it is transferred as a complete resource. Invalid ranges, changed source descriptors, unknown totals or insufficient validators trigger one complete-resource request, with an explicit native range-restart log. An unsolicited partial response to that complete request fails. This remains native HTTP delivery, with no external extractor/runtime fallback.

Validated receipts preserve actual bytes when a middle chunk fails. Retries continue at the verified offset, while explicit no-continue restarts each attempt. No-part mode, prior-file recovery, cancellation, server timestamps and transactional publication retain their existing behavior. Complete-resource size bounds are checked before chunk writes; stream bounds and final byte counts protect every response. Partial chunks never publish media or archive success. Progress reports aggregate media bytes and full resource size.

The policy propagates through progressive pairs, whole-resource DASH, unranged HLS resources and ordered HTTP media files. Protocol-imposed byte-range subdivision and remaining related-resource semantics are still pending. SABR POST streaming is a separate protocol rather than an HTTP GET range download. Saved settings are copied into requests; job arguments can change or disable them without modifying saved settings. The adapter now explicitly refuses 19 remaining configured settings instead of 20.

## Verification

Fourteen core checks cover binary/disabled/overflow sizes, bounded contiguous ranges, complete progress/bytes/timestamps, literal resource queries, strong tags/date rules, interrupted middle-chunk resume, restart/direct retries, source/total changes, ignored ranges, invalid-range access denial, whole-resource size preflight, cancellation/rollback, native publication/archive protection, HLS/whole-DASH propagation and argument precedence. Four generated-media checks fully decode progressive/HLS/DASH pairs with exact original bytes and protect prior media/archive state during cancelled paired transfers. One app check verifies saved settings, disabling and invalid-size preflight.

The Release solution build passed with zero warnings/errors. All **1,615 native checks** passed: 1,207 core and 408 optional media checks. All **61 app checks** passed, including four optional browser-tool checks.

Live testing used the public original MP3 for [Short](https://www.newgrounds.com/audio/listen/1136677), through native C# HTTP/direct media and the WPF adapter with isolated saved settings. Saved 256 KiB chunking, explicit disabling, M4A conversion, cancelled-then-resumed transfer and direct no-part transfer each produced two files. Requested and returned ranges were captured eagerly.

| Live workflow | HTTP requests | Processed output | Retained original |
| --- | ---: | ---: | ---: |
| Saved 256 KiB chunks | 19 matching partial ranges | 4,863,023-byte MP3 | 4,862,954-byte MP3 |
| Explicit zero/disabled | 1 complete response | 4,863,023-byte MP3 | 4,862,954-byte MP3 |
| Chunked M4A conversion | 19 matching partial ranges | 2,129,918-byte AAC M4A | 4,862,954-byte MP3 |
| Cancelled, then resume | 1 interrupted + 19 matching ranges | 4,863,023-byte MP3 | 4,862,954-byte MP3 |
| Chunked no-part | 19 matching partial ranges | 4,863,023-byte MP3 | 4,862,954-byte MP3 |

Ordinary ranges began at 0-262,143, continued at 262,144 and ended at 4,862,953 with no gaps and consistent 4,862,954-byte totals. Cancelled delivery preserved prior published media and archive text; its retry began at validated byte 13,476. Every accepted response respected the configured range bound. Each completed job emitted two completions; archive repeats made no media requests or callbacks.

All ten files independently decoded completely: 5,358,546 samples / 121.508980 seconds each. MP3 files had 4,653 decoded frames; the converted M4A had 5,233. Unconverted processed/original PCM hashes matched exactly.

Evidence is under `work/http-chunk-stage` and `work/http-chunk-live-probe` in the October 10 Codex workspace. It is not a runtime dependency. Python and yt-dlp were not used in native verification. Existing uncommitted work and user settings/history/media were preserved; no Git commit or push was performed.

## Remaining work

Subdivision of protocol-required HLS/indexed-DASH/related-resource byte ranges, broader live provider/ordered/segmented interactions, all remaining option/site/catalog parity, production cutover and packaged verification without Python or yt-dlp remain pending. Production WPF still uses yt-dlp.
