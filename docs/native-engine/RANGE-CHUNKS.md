# Native protocol range subdivision

Verified October 10, 2026, with native engine **0.109.0**. Configured HTTP chunk sizes now subdivide protocol-required HLS/indexed-DASH/caption byte ranges, extending [native HTTP chunks](HTTP-CHUNKS.md). Full site/option parity and production cutover remain unfinished.

## Implemented behavior

The shared native range reader divides a declared parent range into bounded contiguous requests while preserving its absolute resource offsets and exact output bytes. Indexed DASH index, initialization, gap and media reads receive the setting; HLS initialization/media ranges and bounded HLS/DASH captions use the same reader. Each subrange retains configured read buffers, rate limiting, retries and cancellation.

Strong resource tags or adequately old modification/date descriptors bind successive pieces. Expected tags/dates must remain present and equal, and declared resource totals cannot change. Missing/conflicting validators fail instead of allowing unrelated pieces to mix. If a source cannot identify successive pieces, the reader rewinds its output and acquires the whole protocol-required parent range as one matching partial response. An HTTP 200 whole resource cannot substitute for a required slice.

Failed/cancelled subdivided ranges rewind incomplete parent output while preserving previously completed output prefixes. Per-piece retry truncates only the failed piece before restarting it. Bounds, body lengths, source identities and complete parent assembly are verified before existing media publication. HLS byte ranges now share the stronger indexed-DASH source guards.

## Verification

Eight core checks cover exact nonzero parent offsets, literal queries, preserved output prefixes, interrupted-piece retries, changed/missing validators/totals, unidentifiable-source parent retrieval, cancellation rollback, preflight bounds, HLS assembly and range-ignoring failures. Three generated-media checks fully decode sequential/parallel indexed DASH and verify ranged HLS caption text, timing and embedding under small chunk limits.

The Release solution build passed with zero warnings/errors. All **1,626 native checks** passed: 1,215 core and 411 optional media checks. All **61 app checks** passed, including four optional browser-tool checks.

Live verification used the native PeerTube provider and WPF adapter with the actual public [PeerTube V6: storyboard](https://framatube.org/w/ff2EVqgHrQX5WJyJJc7Uax) HLS master (`hls-master-6`). No synthetic manifest or captured response supplied live media. Saved 128 KiB chunks, audio extraction, no-part delivery and cancelled-then-retried HLS each completed two files. Every completed job made nine matching HTTP 206 range requests, from byte 0 through 819,933, with an 819,934-byte resource total and maximum request length 131,072 bytes.

| Live workflow | Processed output | Retained original | Decoded frames |
| --- | ---: | ---: | --- |
| Ranged HLS | 820,991-byte MP4 | 819,934-byte MP4 | 221 video / 433 audio each |
| Audio extraction | 5,248-byte AAC M4A | 819,934-byte MP4 | 433 audio / original 221 video |
| No-part ranged HLS | 820,991-byte MP4 | 819,934-byte MP4 | 221 video / 433 audio each |
| Cancelled HLS, then retry | 820,991-byte MP4 | 819,934-byte MP4 | 221 video / 433 audio each |

All eight files independently decoded completely. Video resolution was 1280x888; duration was approximately 9.24 seconds. Unconverted processed/original audio hashes and video-frame counts matched exactly. Cancelled HLS preserved prior published media and archive text; retries completed the source rather than claiming persistent HLS fragment resume. Each successful job emitted two completions; archive repeats made no media requests or callbacks.

Evidence is under `work/range-chunk-stage` and `work/range-chunk-live-probe` in the October 10 Codex workspace. It is not a runtime dependency. Python and yt-dlp were not used in native verification. Existing uncommitted work and user settings/history/media were preserved; no Git commit or push was performed.

## Remaining work

Broader live indexed-DASH and caption-range provider verification, remaining related-resource and option semantics, all remaining catalog/site/app-option parity, production cutover and packaged verification without Python or yt-dlp remain pending. One real PeerTube clip does not establish every instance/site workflow. Production WPF still uses yt-dlp.
