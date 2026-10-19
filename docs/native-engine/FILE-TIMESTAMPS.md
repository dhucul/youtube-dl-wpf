# Native file timestamps

Verified October 10, 2026, with native engine **0.104.0**. This is a partial option-parity checkpoint; the full replacement remains unfinished.

## Implemented behavior

Saved `General.NoMtime` now maps to the native engine. Native `--mtime` / `--no-mtime` arguments override job behavior without changing saved settings. By default, complete progressive HTTP downloads use a valid `Last-Modified` response header. Resumed/restarted downloads use the validated successful response's remote entity date. Missing, malformed or unsupported dates leave a current file time and do not prevent otherwise complete media from being saved.

Timestamp changes happen after complete transfer and before final publication. Processing/conversion preserves the original media's time when enabled. Retained progressive video/audio components preserve their distinct entity dates. When disabled, newly published media/original files retain current timestamps. Existing no-overwrite outputs and archive repeats are not retimed.

Transactional publication sets media timestamps on complete temporary sibling files before installation. Related sidecars retain their own current publication times. Failed transfer, processing or publication protects prior complete media, archive data, file dates and callbacks. Date updates follow the frozen upstream downloader's best-effort behavior; filesystem timestamp limitations do not cause a successful download to retry or fail.

Adaptive or joined output has the assembled file's local time rather than an invented server time. Complete upstream behavior for manifests, segments, multi-source merged filetime metadata, related resources and all transport interactions remains pending.

## Verification

Eight new core checks cover valid/disabled/missing/malformed dates, validated resume totals, failed-transfer preservation, transactional media-versus-sidecar timing, protected existing outputs and explicit argument precedence. Four generated-media checks fully decode conversions/originals, disabled output, distinct retained progressive component times and failed-publication preservation. One app check covers saved policy, job overrides and unchanged settings.

The Release solution build passed with zero warnings/errors. All **1,546 native checks** passed: 1,158 core and 388 optional media checks. All **58 app checks** passed, including four browser-tool checks. Native saved-option preflight now explicitly refuses 23 remaining configured options.

Live verification used the publisher-supplied public MP3 from [Short](https://www.newgrounds.com/audio/listen/1136677), through the native WPF adapter with isolated saved settings. The real media response reported `Last-Modified: 2022-06-07T21:06:36Z`.

| Saved policy/workflow | Processed output | Retained original | Published time |
| --- | ---: | ---: | --- |
| Preserve time, metadata remux | 4,863,023-byte MP3 | 4,862,954-byte MP3 | Exact server date on both |
| Disable time preservation | 4,863,023-byte MP3 | 4,862,954-byte MP3 | Current dates on both |
| Preserve time, M4A conversion | 2,129,918-byte AAC | 4,862,954-byte MP3 | Exact server date on both |

All six files independently decoded completely over 121.508980 seconds. Each MP3 decoded 4,653 frames; converted AAC decoded 5,233. Within each unconverted workflow, processed/original PCM SHA-256 hashes matched exactly. Every initial job emitted two completions; archive repeats fetched no media, emitted no outputs/completions and preserved exact existing file dates.

Scratch evidence is under `work/mtime-probe` in the October 10 Codex workspace, outside runtime dependencies. User settings, history, media and existing uncommitted work were preserved. No Git commit or push was performed.

## Remaining work

Complete upstream filetime metadata and template/sidecar interactions, live multi-source/adaptive timestamp workflows, all related-resource/transport semantics and production integration remain pending.

Production WPF still uses yt-dlp. Installer/runtime/fallback removal and final packaged application verification without Python or yt-dlp remain unfinished. These isolated native adapter checks are not a packaged application test.
