# Native playlist item selection

The C# selector now supports ordered item expressions, negative indexes, inclusive ranges, signed steps and open bounds. It evaluates a complete bounded inventory and retains the original source positions for identity, filenames and error reporting.

## Implemented behavior

- Individual signed indexes and comma-separated clauses, including explicit order such as `4,2`.
- Inclusive `START:STOP[:STEP]` ranges with omitted start/stop, negative indexes counted from the positional extent, and negative steps for reverse traversal.
- Legacy hyphen ranges, including stepped forms such as `1-:2` and `0--2:2`. `inf` and `infinite` are supported as open positive endpoints.
- Repeated selected positions are included once, at their first occurrence. Reverse/shuffle settings apply to the resulting selected work list.
- Explicit item expressions override legacy start/end settings. A log message reports this precedence during downloading. Without an item expression, start/end settings define the range; the legacy end value `-1` means the whole list. An explicit extra argument `--playlist-end 0` is distinct from the app's unset zero default.
- `-I`, attached `-IEXPRESSION`, equals forms, and `--playlist-items` all map through the native argument layer. Global settings/arguments remain overridable by job arguments.
- Zero, out-of-range indexes and ranges with no matching entries produce a successful skipped job. They create no media output, new output directory, archive record or media-completion callback.

For a 15-item inventory, `1:3,7,-5::2` selects 1, 2, 3, 7, 11, 13 and 15. On a 10-item inventory, `-15::2` selects 2, 4, 6, 8 and 10: clipping does not change the arithmetic phase of the range. Sparse source positions remain unrenumbered; their highest declared position defines the end-relative extent.

Signed integer arithmetic uses the .NET `BigInteger` type. Large endpoints/steps never allocate or iterate a corresponding numerical range; only existing inventory entries are considered. Expressions are bounded to 4,096 characters, inventories to the configured maximum of at most 10,000 entries, and cancellation is checked during selection. Empty/malformed clauses, zero steps, unsupported tokens and duplicate/invalid inventory positions fail explicitly. Syntax is validated before an HTTP session or browser-cookie import begins.

## Numbering and output

`playlist_index` remains the original source position. `playlist_autonumber` is now the one-based position in selected download order, rather than an alias for the source index. Failed, unavailable or archived selections still occupy their selected-order position. Filename templates and info JSON retain both values, along with playlist ID/title and inventory count.

Existing partial-result, stop-on-error, archive, no-overwrite and cancellation behavior continues to apply. Completed files survive a later failure or cancellation. An empty or fully skipped successful playlist reports `Skipped=true`.

## Verification on October 8, 2026

Eleven deterministic checks cover the documented and captured-reference selection cases, legacy/open/negative syntax, ordering/deduplication, arbitrary-size numbers, sparse/invalid inventories, preflight/cancellation, argument precedence, filename/JSON numbering, empty-job side effects, unavailable-item behavior, archives and cancellation. A WPF regression verifies typed/global/job option precedence through real native output and no-file job results.

A live Vidyard example with three entries used `-1:1:-2` while legacy start/end were both set to 2. The explicit expression correctly selected source entries 3 and 1, with callbacks and files in that order. Filenames used `01-003-...` and `02-001-...`; their info JSON recorded autonumbers 1/2, source indexes 3/1 and total inventory count 3.

Both selected HLS downloads fully decoded as H.264/AAC at 640x360. Entry 3 produced 1,427,232 bytes, 240 video frames, 452 audio frames and 9.680000 seconds. Entry 1 produced 3,869,460 bytes, 983 video frames, 1,923 audio frames and 41.082711 seconds. The unselected entry did not become an output.

## Remaining work

Lazy/incomplete live inventory evaluation, collections beyond configured limits, remaining extraction/selection interactions and complete production settings integration are still pending. This does not establish full upstream option or site parity.

Behavioral references: [documented video-selection options](https://github.com/yt-dlp/yt-dlp#video-selection) and the captured revision's public selection tests. These were used as compatibility evidence only. Parsing, arithmetic and execution are independently implemented in C#; the replacement does not load or execute upstream code.
