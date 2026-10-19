# Native title filters and download limits

Engine 0.58.0 adds title inclusion/rejection, a per-job completed-item limit, and native mapping of the app's abort-on-error setting. These are tested subsets of the selection options; file-size/date/view/age filters and the general match-filter language remain separate pending work.

## Title selection

`MatchTitle` and `RejectTitle` are bounded, case-insensitive, culture-invariant .NET regular expressions. Empty patterns disable the corresponding predicate. Patterns are validated before network access, compiled once per download job, limited to 4,096 characters, and given a 250 ms evaluation timeout. Invalid patterns fail explicitly. A timeout stops the job even when ordinary per-video errors are configured to be ignored.

Predicates run against the resolved video's title before filename sanitization, transfer, standalone sidecar writing, archive locking/recording, or completion callbacks. A matching reject pattern excludes an otherwise matching title. Playlist titles and potentially stale listing titles do not substitute for resolved video titles. Snapshot entries use their bound media metadata; unresolved entries obtain fresh metadata before title filtering.

Filtered individual results report success with `Skipped` and `Filtered`. They do not add archive records, including with force-write-archive enabled. Known archived entries can still skip before metadata resolution because no new transfer or output is needed.

## Download limits

`MaxDownloads` limits completed media items within one top-level `DownloadAsync` invocation. Zero disables the limit, matching the app setting; negative values fail validation. Each invocation receives its own counter, even when an engine/request object is reused.

An item consumes one position when it successfully publishes new media or new standalone sidecars. Accepted simulated items also consume one position so simulations can honor a limit. Multiple files from one item, including retained originals, consume only one position. Filtered items, archive skips, no-overwrite skips and failures before publication do not consume the limit. A successfully published item still consumes its position if the subsequent archive write fails; the archive failure remains reported and completed output remains available.

Once exhausted, the playlist stops before the next entry's discovery. `DownloadLimitReached` reports this deliberate early stop; retained outputs and earlier failures remain in the result. A limit is a normal job boundary, not an invented media failure. It does not share a budget across separate queued jobs. Existing item-selection ordering, original indexes and selected-order autonumbers remain unchanged; title filtering does not renumber them.

The native adapter maps saved MatchTitle, RejectTitle and MaxDownloads. Saved AbortOnError overrides saved IgnoreErrors. Global and per-job arguments are then applied in order, including `--match-title`, `--reject-title`, `--max-downloads`, `--ignore-errors`/`-i`, and `--abort-on-error`/`--no-ignore-errors`. Later job arguments can override saved preferences without modifying those preferences.

## Verification

Eleven deterministic checks cover resolved versus listing titles, case/culture behavior, rejected side effects, archive and failure handling, existing output preservation, simulations, standalone metadata, snapshots/indexes, independent job counters, invalid input, regex timeouts and argument precedence. A generated-media check downloads two selected Imgur fixture videos with retained originals: four completed files still count as two items, with no third-video transfer. Every audio/video frame, exact original bytes and archive identities are verified. One WPF check covers saved preferences and job overrides.

On October 9, 2026, a native request for NFB's 26-episode *The Great List of Everything* series used `^the camera$` and a limit of one. It resolved positions 1–5, downloaded only position 5, and stopped before position 6. It saved:

- A 5,908,658-byte MP4 and its 6,442,572-byte original TS.
- H.264 480 × 270 and AAC, with 3,082 video and 3,013 audio frames in both files; both fully decoded.
- Durations of 128.633211 seconds (MP4) and 128.633201 seconds (TS).
- Two completion callbacks, `DownloadLimitReached=true`, source index 5, selected-order autonumber 5, `n_entries=26`, and archive record `nfb:registry 1020020`.

This validates title-based selection and stopping on a live series. It does not establish remaining selection-option parity or additional site coverage.

## Remaining parity

The patterns currently use .NET regex syntax and semantics; complete equivalence with all historical Python regex constructs is unproven. Full legacy counter/exit-code interactions, explicit CLI zero-limit behavior, more concurrent/interrupted publication cases, the general match-filter language, metadata-based file-size/date/view/age rules and the production WPF cutover remain pending. The explicit CLI zero value currently shares the native app's disabled-limit meaning; callers needing a limit should use a positive value.
