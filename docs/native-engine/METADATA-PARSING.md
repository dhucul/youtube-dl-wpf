# Native text metadata parsing

Verified October 10, 2026, with native engine **0.113.0**. Saved `PostProcess.ParseMetadata` now reaches the native engine. Full metadata grammar, app-option parity, site coverage and production cutover remain unfinished.

## Implemented behavior

The saved rule runs first, followed by saved general extra arguments and per-job extra arguments in their original order. Repeated `--parse-metadata` values append ordered rules; `--metadata-from-title` supplies the title-input alias. Job rules do not modify persisted settings. Rules support the default `pre_process` stage and its explicit prefix.

Inputs can be scalar fields or the native engine's supported text output-template expressions, including alternatives, fallbacks, numeric formatting and literal percent signs. Metadata input expands as text without filename sanitization. Outputs can copy one complete field, split text with `%(field)s` capture templates, or use common regex syntax with Python-style named groups and named references. Inline multiline/dot-all flags work. Missing scalar inputs and unmatched patterns leave existing values intact and emit a generic warning; unmatched optional groups retain explicit null values. Rule errors do not report configured values.

Selected leaf metadata is copied before transformations. Later rules can read earlier results. Parsed title/uploader/description and other descriptive fields reach common native filters, output templates and info JSON. Actual filenames still sanitize metadata path separators and unsafe components; source text remains intact in metadata. Native analysis and inspected snapshots retain original provider metadata, and each selected download refreshes and transforms its own leaf. YouTube jobs with parse rules defer their early original-title rejection so the common filter can evaluate transformed metadata.

When metadata embedding is enabled, parsed artist/track/album and other supported descriptive tags reach ffmpeg. `meta_` fields override corresponding global embedded tags independently of display fields; explicit empty overrides remove those tags. Disabling embedding still changes requested filenames and saved descriptive JSON while preserving supplied media bytes and existing embedded tags. Source identities, archive records, media addresses, declared duration/access facts and other structural/selection fields cannot be reassigned by this supported subset. Stream-specific tags and additional-URL downloads are explicitly refused.

Rules are validated before the HTTP session, archive skip or output creation. Limits are 32 rules, 8 KiB per rule, 64 distinct captures per pattern, 1 MiB input and 4 MiB combined parsed text. Regex matches have a 100 ms execution limit. Cancellation is checked between rules and after matching. Actual compiled capture names must match declared fields; unsupported comment/capture syntax fails before discovery rather than clearing fields that were never captured. Timeout, structured-input, size and runtime formatting failures stop an ignored-error playlist rather than repeatedly retrying the same invalid rule.

The engine remains independent C#, retaining ffmpeg for verification and processing. No Python, yt-dlp, publisher script execution or shell evaluation is used for metadata rules.

## Verification

Thirteen new core checks cover capture templates, named regex groups/references, whole-field copies, ordering and source immutability, missing/unmatched/optional captures, multiline and empty overrides, input formatting/fallbacks, argument aliases, reserved fields/stages, invalid syntax and ignored-comment captures, limits, sanitized publication and filters, original snapshot positions, fatal regex timeouts, cancellation/structured values and YouTube transformed-title filtering.

Six generated-media checks completely decode processed/original MP3 and converted AAC, verify exact unconverted samples and original bytes, inspect embedded tags and disabled embedding, preserve selected source positions, skip archive repeats and protect prior media/archive/callbacks after incomplete-source refusal. Three new app checks verify saved/general/job ordering, invalid-rule preflight/privacy and actual saved-option filenames/info JSON.

The final Release solution build passed with zero warnings/errors. All **1,704 native checks** passed: 1,268 core and 436 optional media checks. All **64 app checks** passed, including four optional browser-tool checks. The focused metadata-parsing run passed 27 checks, including the runner's seven common checks and an existing Loom metadata parser check.

Live verification used the complete publicly posted [So Close instrumental clip by Maggie Young](https://www.reverbnation.com/maggieyoung/song/25312777). A saved named-group rule extracted `So Close` and `Maggie Young` from the published title. The counted 28-song [artist inventory](https://www.reverbnation.com/maggieyoung) and inspected selection retained position 1. This verifies the posted clip, not a longer release.

| Live saved-option workflow | Processed output | Retained original | Decoded frames |
| --- | ---: | ---: | ---: |
| Saved parsing and embedded tags | 1,110,836-byte MP3 | 1,114,798-byte MP3 | 2,655 each |
| Ordered album/comment and embedded-title override | 1,110,805-byte MP3 | 1,114,798-byte MP3 | 2,655 each |
| Embedding disabled | 1,114,798-byte original MP3 | No separate modified copy | 2,655 |
| M4A conversion with parsed tags | 1,130,381-byte AAC M4A | 1,114,798-byte MP3 | 2,986 AAC / 2,655 MP3 |
| Inspected artist selection, position 1 | 1,110,836-byte MP3 | 1,114,798-byte MP3 | 2,655 each |

All nine files independently decoded completely with no decoder diagnostics. MP3 files reported 69.319501 seconds; M4A reported 69.32 seconds. Every unconverted processed/original PCM hash matched exactly. Retained originals and the disabled-embedding output had identical file SHA-256 hashes. Parsed info JSON retained numeric recording ID `25312777`, names and source position; explicit embedded-title changes remained separate from the displayed title. Each initial job emitted one or two completions as appropriate; archive repeats emitted no files/callbacks and retained `reverbnation 25312777`. Saved settings remained unchanged.

The behavior reference is the frozen upstream [metadata option documentation](https://github.com/yt-dlp/yt-dlp/blob/51bab8a0116f4d8004c315706d809782607d5847/README.md#modifying-metadata). Implementation is independently written C#. Evidence, source hashes, build/regression logs and independent decode hashes are under `work/metadata-parsing-stage`; isolated live settings/output are under `work/metadata-parsing-live` in the `we-ran-out-of-context-window` workspace. Scratch evidence is not a runtime dependency. Existing user settings/history/media and uncommitted work were preserved. No Git commit or push was performed.

## Remaining work

Additional processing stages, replacement rules, complete Python-regex and output-template grammar, structured/list metadata, source/structural/typed selection-field transformations, stream-specific tag overrides, additional URL downloads, complete upstream tag precedence and all provider/format interactions remain pending. Partial native support is not a claim of complete upstream metadata compatibility.

Eighteen configured settings remain explicitly refused by the native adapter; default/partial behaviors also still require parity verification. Production WPF still uses yt-dlp. Native startup/cutover, installer/runtime/fallback removal and packaged application verification without Python or yt-dlp remain unfinished. These isolated native adapter tests do not establish packaged WPF verification.
