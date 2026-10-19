# Native retained originals and multi-file output

`NativeDownloadRequest.KeepOriginal` now retains the complete media used before post-processing. The experimental WPF adapter maps the existing `Formats.KeepVideo` setting to it. An unchanged single-file download is not duplicated.

## Retained files

- Audio extraction and video-only processing retain the unmodified downloaded source alongside the processed result. Container changes and metadata/caption/artwork processing can also retain that source.
- Original and processed names are rendered using their respective extensions, including templates that place them in different folders. If both names resolve to the same path, the original receives an `.original` suffix.
- Explicit adaptive pairs and separate HLS, DASH and SABR tracks retain their complete video/audio components, with `.video` and `.audio` suffixes. These are reassembled media files, not individual fragments, encryption keys or session data.
- A merge followed by further processing can produce the processed result, merged original and both components. Sidecars belong to the processed result. Retained source bytes are not modified by later encoding or metadata work.

## Saving and reporting

All required media groups and sidecars are prepared before saving. Destination folders are locked in a consistent order. Complete sibling staging files are created next to each target, allowing each final rename and backup restoration to remain on its own volume. A flushed recovery manifest records target, staging and backup paths before existing files move.

Observed save failures restore previous outputs across the target directories. A failed restoration keeps its recovery information and backups for manual recovery. Power-loss consistency and automatic crash recovery remain unfinished; this is not a claim of filesystem-wide atomic visibility.

No-overwrite is applied per media group. An existing converted file can remain while a missing original is saved, and vice versa. Existing groups' sidecars are preserved. The result distinguishes all requested media paths from newly published paths; callbacks only report new files and run after the output set is complete and locks are released. An archive-write failure still reports already published files, while failed publication never records a success.

## Verification on October 8, 2026

Generated media checks cover exact retained bytes, different template folders, same-extension suffixes, video-only output, unchanged downloads, explicit adaptive pairs, conversion after merging, HLS/DASH/SABR components, partial no-overwrite, locked-original rollback, concurrent conversions and archive-write failure. Retained components and processed results are decoded where applicable. Existing publication, cancellation, archive and queue regressions remain part of the full suite.

A live Twitch audio extraction with keep-original enabled saved:

- `mp4/native-twitch-kept.mp4`: the 1,398,689-byte source, with 568 H.264 frames and 816 AAC frames; duration 18.954490 seconds.
- `m4a/native-twitch-kept.m4a`: the 454,772-byte processed AAC file, with artwork and 816 audio frames; duration 18.940476 seconds.

The files occupied separate template folders, both were returned as newly saved paths, and their decoded audio hashes matched. No sibling staging/backup files remained after success.

## Remaining work

Exact legacy intermediate filename/overwrite interactions, fragment retention as a separate option, publication of intermediate files after failed processing, automatic recovery after process/power loss, physical cross-volume failure testing, large-file performance and full production integration remain pending. Complete raw sources can remain in the private retry workspace after failed processing. The overall replacement is still incomplete.
