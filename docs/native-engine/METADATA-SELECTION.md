# Native metadata selection verification

Native engine 0.98.0 implements metadata selection in C# and maps seven previously refused saved app options: upload date, earliest/latest date, minimum/maximum views, generic match filter and age limit. The full 126-option contract, every provider's facts and production native WPF cutover remain incomplete.

## Behavior

An explicit `Date` selects one day and takes precedence over earliest/latest bounds. Otherwise ranges are inclusive. `YYYYMMDD` and documented lower-case `now`, `today`, `yesterday` minus days/weeks/calendar months/years are supported. Relative dates use UTC and calendar clamping at month/year boundaries. Invalid/overflowing dates and inverted ranges fail before HTTP discovery.

Legacy date, view and age settings filter reported facts, while missing facts remain permitted. Known limits include their boundary. Native typed/extra-argument maximum view and age values can explicitly be zero; the saved app's zero default still means unset. Invalid request bounds fail before discovery. No view counts or exact upload instants are invented for providers that do not supply them.

Generic filters support numeric and ordinal string comparisons, negation, containment/prefix/suffix, bounded regular expressions, field presence and Boolean tests. Numeric zero and empty strings are present values; Boolean false matches a negated Boolean test. Unknown values fail ordinary comparisons, pass a `?` comparison or can be tested with presence/absence. Numeric metadata retains its original precision; numeric comparands use the frozen reference's integer/byte-threshold rounding and decimal/binary unit distinctions. String IDs and dates retain their original string type.

Conditions use `&`; literal ampersands use `\&`. Quoted comparisons support escaped quote delimiters. Repeated `--match-filter`/`--match-filters` arguments provide alternatives; `--no-match-filters` clears saved and prior alternatives. Rules are parsed as data, never executed as expressions or commands. Length, condition-count and regex-time bounds are enforced. Invalid conditions and regex timeouts stop with an explicit selection error, including within ignore-errors playlists.

Filtering applies to resolved media items before transfer, sidecar staging, archive updates or completion/history callbacks. Filtered entries do not consume the completed-item limit. Collections retain original positions and selected-order numbering. During incomplete YouTube discovery, absent facts defer generic checks; reported watch facts can reject before browser/mobile playback. Refreshed playback retains available date, view and age facts for final evaluation.

The contract follows the captured revision's [selection options](https://github.com/yt-dlp/yt-dlp/blob/51bab8a0116f4d8004c315706d809782607d5847/README.md#video-selection) and [filter syntax](https://github.com/yt-dlp/yt-dlp/blob/51bab8a0116f4d8004c315706d809782607d5847/README.md#filtering-formats). Generic option behavior was checked for missing metadata and calendar/unit semantics; no upstream extraction implementation or Python runtime was imported.

## Provider facts and app mapping

Public YouTube player data supplies upload day, view count, channel ID and the family-safety minimum. iHeart supplies its publication timestamp/day. RSS/Atom supplies publication time/day. Typed Schema.org media supplies declared upload day and unambiguous WatchAction/LikeAction/CommentAction counters. Additional native public metadata fields can participate without CLR reflection or arbitrary evaluation.

Native inspection, the WPF adapter's preview, filename fields and info JSON preserve available facts. Saved filters and job overrides are independent of persisted settings. The adapter's remaining unsupported-setting preflight now checks 28 cases; removing seven refusal cases reflects working selection implementations rather than silently dropping settings.

Date/view/age filtering is not an account or availability mechanism: inaccessible/protected media still requires its own verified provider workflow. Further provider fact population is required before complete site/option parity can be claimed.

## Verification

Sixteen new native checks cover exact/inclusive/relative dates, leap/month clamping, missing legacy facts, zero/large count bounds, numeric units and rounding, original string types, presence/Boolean distinctions, escaped conjunctions/quotes, regex behavior, repeated alternatives/reset, rejection side effects, simulation limits, invalid preflight, regex timeout, inspection/template/JSON facts, early YouTube rejection and publisher feed/schema facts.

Two new app checks cover saved/job override mapping, persisted-setting isolation, preview facts and rejection without media/archive/history effects. The Release solution build passed with zero warnings/errors. All **1,436 native checks** passed: 1,086 core and 350 optional media checks. All **51 app checks** passed: 47 core and four browser checks.

Live verification used the existing public [iHeart teaser](https://www.iheart.com/podcast/112696666/episode/315219505/). Native preview exposed upload day `20251230` and unknown views. Saved date `20250101` rejected it without creating the output directory, archive or completion callbacks. The correct date, a 60–90-second duration condition and creator regex accepted it while minimum/maximum view settings permitted the unknown count.

The accepted workflow published a 1,063,078-byte MP3 and its exact 1,062,972-byte original. Both independently decoded all 2,542 MP3 frames over 66.376553 seconds, with identical decoded PCM. The upload date appeared in filenames and info JSON; unknown views remained JSON null. Two completion callbacks occurred initially; the archive repeat produced none.

Scratch references, probe code and evidence are under `work/selection-probe` in the October 10 Codex workspace. User settings, history, downloaded media and managed engines were preserved. No Git commit or push was performed.

## Remaining work

Every provider/workflow still needs complete fact population. Other output-template fields, all numeric-duration spellings, regex-dialect differences, interactive/break filters and broader option interactions remain pending. Missing legacy metadata being permitted is compatible behavior, not proof that a provider has complete metadata coverage.

Production WPF still starts yt-dlp. Full native coverage, installer/runtime/fallback removal and final application packaging verification without Python or yt-dlp remain unfinished.
