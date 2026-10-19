# Native download delays

Verified October 10, 2026, with native engine **0.101.0**. This is a partial option-parity checkpoint; the full replacement remains unfinished.

## Implemented behavior

Saved `Network.SleepInterval` and `Network.MaxSleepInterval` now reach the native adapter. Native extra arguments support `--sleep-interval`, its `--min-sleep-interval` alias, and `--max-sleep-interval`, including fractional seconds and job overrides without changing saved settings. Default zero app values disable the setting. Negative/nonfinite values, maximum-only ranges and inverted ranges fail before discovery or output changes.

A minimum with no maximum gives a fixed delay. A supplied maximum selects a uniform random interval between the inclusive bounds. The engine logs the chosen delay and awaits it with cancellation before opening the selected item's download workspace or requesting its media. Long configured delays use bounded timers to avoid timer overflow.

Selected playlist items wait independently in their original selected order. Separate progressive video/audio inputs, separate HLS audio renditions and separate DASH representations each wait before their component transfer. A coordinated SABR transfer waits once; HTTP sequences and fragments use their transport's one media-transfer delay rather than a delay per fragment. Internal retries do not add a second delay.

Archive skips, title/metadata filters, simulation, skip-media jobs and existing no-overwrite jobs requiring no further media skip their media delay. Cancelling while waiting does not fetch the pending media or publish completion/history/archive changes. Earlier completed playlist entries remain saved and archived. Cancelling before a later component protects prior published media and archive state; already downloaded component data remains in the native workspace under the existing resume policy.

The frozen [upstream downloader behavior](https://github.com/yt-dlp/yt-dlp/blob/51bab8a0116f4d8004c315706d809782607d5847/yt_dlp/downloader/common.py) was consulted as a timing reference. No upstream runtime or extraction code was imported.

## Verification

Twelve new core checks cover fixed/random bounds, fractional arguments and precedence, invalid preflight, actual delay before media bytes, cancellation, archive/filter/metadata-only/existing-output skips, selected playlist ordering and preservation of earlier completions. One app check verifies saved ranges, job overrides, unchanged saved settings and cancellation without published media/history callbacks.

Four generated-media checks verify independent progressive/HLS/DASH component waits and cancellation before the second component. Completed media and retained originals fully decode; all twelve video frames and both exact source byte sets are preserved. Archive repeats skip without additional waiting or completions.

The Release solution build passed with zero warnings/errors. All **1,489 native checks** passed: 1,122 core and 367 optional media checks. All **53 app checks** passed, including four browser checks.

Live testing used the public [iHeart episode 315219505](https://www.iheart.com/podcast/112696666/episode/315219505/) through the native WPF adapter with isolated saved settings. An HTTP observer measured the first real media request after the wait log:

| Saved range | Logged chosen delay | Observed time before media request | Result |
| --- | ---: | ---: | --- |
| Fixed 2 seconds | 2.00 seconds | 2.0091543 seconds | Complete media/original and archive skip |
| Random 1–2 seconds | 1.32 seconds | 1.3290773 seconds | Complete media/original and archive skip |

Both processed MP3 files were 1,063,078 bytes; both retained originals were 1,062,972 bytes. All four files independently decoded 2,542 audio frames over 66.376553 seconds. All decoded PCM SHA-256 hashes matched: `9584fb829ba65cf86c32e4344c81f546ea92c71a6ec0ec5c3d55e3d3a011a37b`.

A separate live cancellation during a saved two-second delay made no media request or completion, and preserved pre-existing media and archive contents. The first scratch probe stopped on an ordinary redirect because its observation wrapper disabled redirect following; correcting the wrapper restored the same redirect behavior used by the default native session. The production native implementation did not need a redirect change.

Scratch evidence is under `work/sleep-probe` in the October 10 Codex workspace, outside runtime dependencies. User settings, history, media and existing uncommitted work were preserved. No Git commit or push was performed.

## Remaining work

Provider-required availability waits, distinct subtitle/request sleep options, live delay verification across adaptive and account workflows, and complete site/option interactions remain pending. Native saved-option preflight now explicitly refuses 24 remaining configured options.

Production WPF still uses yt-dlp. Installer/runtime/fallback removal and final packaged application verification without Python or yt-dlp remain unfinished. These isolated adapter checks are not a packaged application test.
