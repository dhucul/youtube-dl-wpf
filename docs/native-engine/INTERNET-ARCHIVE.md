# Native Internet Archive provider

The C# provider reads public item metadata through the [official item metadata API](https://archive.org/developers/md-read.html). Root item and direct-file URLs remain distinct. It validates item identity, escapes file paths, excludes private files, preserves metadata arrays and numeric strings, groups derivative files by original source, and exposes supported audio/video formats and sizes.

Multiple original media entries form a native playlist when expansion is requested. Resolved per-entry metadata retains its formats, author, identifier, and duration without re-fetching the item. Playlist quality/height selection therefore uses the offered variants, and unavailable/oversized/changed metadata fails explicitly. Derivative cycles and invalid file paths are rejected.

On October 7, 2026, native inspection/download of `BigBuckBunny_124` fetched `Content/big_buck_bunny_720p_surround.mp4`. The complete 61,878,609-byte result fully decoded as H.264/AAC, at 640×360 and 596.480000 seconds. The filename's resolution label differs from the actual video. This verifies one public item/file workflow, not complete site coverage.

Collection/search enumeration, remaining archive media types, richer per-file metadata, unknown-resolution probing, additional authentication/access workflows, and full upstream parity remain pending. No Python archive client or yt-dlp code is used.
