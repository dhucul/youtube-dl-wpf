# Native direct transfer filenames

Verified October 10, 2026, with native engine **0.106.0**. Saved `General.NoPart` and explicit `--no-part` / `--part` job overrides now reach the native engine. Complete site/option parity and production cutover remain unfinished.

## Implemented behavior

No-part mode writes the named transfer file directly, without appending `.part`. Validated resource/strong-ETag or Last-Modified receipts in `.native-resume.json` bind recoverable direct bytes to their source. Retries resume only validated matching entities; explicit no-continue starts fresh. Range rejection, changed entities, truncated responses, size bounds, cancellation and full-byte completion retain the existing HTTP guards. Receipts are removed after successful completion.

Pre-existing direct destinations without an owned valid resume receipt are backed up before writing and restored, with their prior receipt and timestamp, when replacement fails or is cancelled. Pre-cancelled requests do not create output directories or send HTTP. Switching to direct mode preserves separately recoverable legacy `.part` files and receipts.

The policy propagates through progressive tracks, paired audio/video, whole-resource and segmented DASH, HLS fragments/assembly, indexed DASH assembly and ordered HTTP media sequences. Adaptive joins still require their owned staging and complete assembly; the no-part flag does not eliminate protocol or processing workspaces. Downloads, verification and processing finish in the native owned workspace before transactional publication. Unfinished direct bytes are not published or recorded as archive success, and existing published user media remains protected on failure.

Saved settings are copied into requests, while explicit job arguments can override them without changing saved data. The adapter now explicitly refuses 22 remaining configured settings instead of 23. No user settings schema or history migration was required.

## Verification

Ten core checks cover direct naming during writes, complete bytes/date preservation, validated resume, restart/retry interactions, failed replacement rollback including prior receipts/timestamps, cancellation/pre-cancellation, declared and streaming size failures, preservation of another mode's partial data, native publication/archive protection, HLS/segmented-DASH retry/assembly and argument precedence.

Four generated-media checks fully decode separate progressive/HLS/DASH audio/video and converted audio. They preserve every picture frame, exact original component bytes, timestamps and archive skips. One app check verifies saved settings and job override precedence; the remaining configured-setting rejection checks still pass.

The Release solution build passed with zero warnings/errors. All **1,582 native checks** passed: 1,182 core and 400 optional media checks. All **59 app checks** passed, including four optional browser-tool checks.

Live testing used the publisher's original public MP3 for [Short](https://www.newgrounds.com/audio/listen/1136677), through the native direct-media provider and WPF adapter with isolated saved settings. Transfer files were observed while data was being written: saved no-part and conversion jobs used direct MP3 names without `.part`; explicit `--part` used `.part`. A cancelled no-part transfer preserved prior published media and archive text, then completed on retry. Each completed job produced two files; archive repeats emitted no files/completions and did not touch raw transfer files.

| Live workflow | Processed output | Retained original | Decoded audio frames |
| --- | ---: | ---: | ---: |
| Saved no-part | 4,863,023-byte MP3 | 4,862,954-byte MP3 | 4,653 each |
| Explicit part override | 4,863,023-byte MP3 | 4,862,954-byte MP3 | 4,653 each |
| No-part AAC conversion | 2,144,814-byte raw AAC | 4,862,954-byte MP3 | 5,234 AAC / 4,653 MP3 |
| Cancelled no-part, then retry | 4,863,023-byte MP3 | 4,862,954-byte MP3 | 4,653 each |

All eight files independently decoded completely. The MP3 source and unchanged outputs had 5,358,546 decoded samples / 121.508980 seconds; raw AAC had 5,359,616 samples / 121.533243 seconds including encoder padding. The raw AAC demuxer's estimated file duration was 127.719460 seconds; complete decoded sample counts, rather than that estimate, established playback length. Unconverted processed/original PCM hashes matched exactly. Retained source MP3 bytes also matched across workflows.

Evidence is under `work/no-part-probe`, `work/no-part-stage` and `work/no-part-live-probe` in the October 10 Codex workspace. These files are not runtime dependencies. Python and yt-dlp were not used in native verification. Existing uncommitted work and user settings/history/media were preserved; no Git commit or push was performed.

## Remaining work

Broader live indexed-DASH/ordered-sequence and additional-provider option interactions, all remaining catalog/site/app-option parity, production cutover and packaged verification without Python or yt-dlp remain pending. The native adapter remains a migration path; production WPF still uses yt-dlp.
