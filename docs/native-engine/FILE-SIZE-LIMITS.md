# Native file-size limits

Verified October 10, 2026, with native engine **0.100.0**. This is a partial option-parity checkpoint; the full replacement remains unfinished.

## Implemented behavior

Saved `Selection.MinFilesize` and `Selection.MaxFilesize`, job snapshots, and native `--min-filesize` / `--max-filesize` overrides now reach the native engine. Sizes use nonnegative decimal byte expressions with optional binary K/M/G/T/P/E/Z/Y suffixes, optional B/iB, and rounding to the nearest byte with ties to even. Empty values disable the bound. Invalid, overflowing or inverted bounds fail before discovery or output changes. Both boundaries are inclusive.

Limits apply to original media before conversion, metadata remux, related-file publication and archive recording. A conversion can therefore produce a smaller final file while satisfying the original-source minimum. Retained originals preserve the source bytes.

Progressive HTTP uses the full declared response size, including the total from a resumed Content-Range. Known out-of-range media fails before opening the body or changing a prior partial file. With unknown response length, crossing the maximum stops before writing the offending buffer; the minimum is checked at completed transfer. Size refusals are terminal and do not consume download retries. Existing published files, archive entries and completion callbacks remain protected.

All transport branches also check the completed original media before processing or publication. Adaptive and multi-source downloads currently enforce this check after assembly, rather than stopping fragment/component transfer at the maximum. Full upstream behavior for these paths remains pending.

## Verification

Ten new core checks cover size parsing, known and unknown lengths, inclusive boundaries, full resumed size, terminal refusal without retries, argument/preflight handling, accepted publication, archive repeats and preservation of prior outputs. One app check covers saved limits, job overrides, unchanged saved settings and refusal without published files or history callbacks.

Three generated-media checks verify a real WAV-to-MP3 conversion against the original-source minimum and HLS minimum/maximum refusals before publication. Converted and original audio fully decode; rejected HLS leaves prior output/archive state intact.

The Release solution build passed with zero warnings/errors. All **1,473 native checks** passed: 1,110 core and 363 optional media checks. All **52 app checks** passed, including four browser checks.

Live testing used the public [iHeart episode 315219505](https://www.iheart.com/podcast/112696666/episode/315219505/), through the native WPF adapter with isolated saved settings. A saved maximum of `500K` rejected the original source without published audio, archive writes or completion callbacks. Saved inclusive `1M`–`2M` limits accepted complete media and its original; the archive repeat skipped without outputs or callbacks.

| Live file | Bytes | Decoded frames | Duration |
| --- | ---: | ---: | ---: |
| Processed MP3 | 1,063,078 | 2,542 | 66.376553 seconds |
| Retained original MP3 | 1,062,972 | 2,542 | 66.376553 seconds |

Independent full decoding produced identical PCM SHA-256 hashes: `9584fb829ba65cf86c32e4344c81f546ea92c71a6ec0ec5c3d55e3d3a011a37b`. Scratch evidence is under `work/filesize-probe` in the October 10 Codex workspace. It is not a runtime dependency. User settings, history, media and existing uncommitted work were preserved. No Git commit or push was performed.

## Remaining work

Transfer-time enforcement for adaptive fragments and multiple components, complete upstream size-estimate/selection/error interactions, live coverage across all transports, and production settings integration remain pending. Native saved-option preflight now explicitly refuses 26 remaining configured options.

Production WPF still uses yt-dlp. Installer/runtime/fallback removal and final packaged application verification without Python or yt-dlp remain unfinished. These isolated adapter checks are not a packaged application test.
