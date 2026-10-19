# Native output handling

Media and requested sidecars are now prepared in a private workspace before they replace published files. Raw partial files retain their validated resume receipts. Conversion, caption, and thumbnail failures leave the previous output set unchanged. Publication preflights file/folder conflicts, backs up existing files, and rolls back a failed commit. Tests cover locked media, failed/canceled captions, conversion errors, resumed partials, and callbacks that observe complete outputs.

Workspaces reject filesystem links and check ownership before cleanup. Failed recovery preserves its folder and blocks another attempt from deleting those backups. Crash/power-loss recovery, automatic recovery of retained backups, and reuse of complete downloaded source files still need work; the current rollback guarantee covers errors observed during the running operation.

Native filename templates support common metadata fields, playlist context, numeric widths/precision, Unicode character and UTF-8 byte precision, alternatives/default values, simple conditional replacements, and literal percent signs. Relative subfolders and explicit absolute templates are supported. Metadata values cannot inject directory separators. Reserved Windows names and restricted ASCII filenames are handled. Final output names and sidecar names use the actual post-processing extension.

Supported fields include `title`, `id`, `ext`, `uploader`, `creator`, `channel`, `extractor`, `webpage_url`, `format_id`, dimensions, fps, file size, duration, resolution, and playlist title/id/index/count. Conversions currently include `s`, `S`, `B`, `d`, `i`, and `f`. Missing simple metadata fields use `NA` or the supplied default. Complex traversal/slices, arithmetic/date operators, other conversions, typed sidecar templates, stdout, and remaining template semantics are pending and must not count as complete compatibility.

The no-overwrite option skips an existing predictable output before transfer/processing, and checks again during final publication. It preserves the existing media and sidecar set and does not emit a new-file callback for skipped output. Adaptive outputs whose final container is initially unknown may require transfer before the final skip check. Remaining upstream overwrite/post-processing interactions need verification.

Retained originals and adaptive components now share the publication transaction, including different output folders and partial no-overwrite. Complete sibling files and a recovery manifest precede final renames; callbacks distinguish new files from existing outputs. See `KEEP-ORIGINAL.md` for verified behavior and remaining recovery/compatibility work.

Explicit restart now ignores saved HTTP resume offsets, including fragment retries, while preserving previous outputs on denial or cancellation. Reverse and random playlist order retain original source positions and archive identities. See `RESTART-AND-ORDER.md` for verified request, adapter and extra-argument behavior.

The experimental WPF adapter now passes its output template, restricted-filename setting, no-overwrite setting and keep-video setting to the core. The production app still uses its original backend pending full replacement verification.

Behavioral reference: [upstream output-template documentation](https://github.com/yt-dlp/yt-dlp#output-template). The parser and publisher are independent C# implementations.

Supported MP4 timecode tracks now survive metadata processing and MP4-to-MOV remuxing. The engine recreates only a verified single timecode track, checks its output configuration/value and preserves retained originals. See `ODYSEE.md` for the live trigger, exact verification and remaining timecode layouts.

Playlist output now distinguishes the original `playlist_index` from the sequential `playlist_autonumber`. Both are saved in info JSON along with playlist identity and count. Ordered/negative/stepped selections preserve these meanings through archive skips and failures; see `PLAYLIST-SELECTION.md`.
