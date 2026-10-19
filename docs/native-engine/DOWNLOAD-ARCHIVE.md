# Native download archive

`NativeDownloadRequest.DownloadArchiveFile` enables a UTF-8 history file. The WPF native adapter maps the existing `General.DownloadArchive` setting into this request. Analysis/inspection does not read or create an archive.

Records use `lowercase-extractor media-id` lines with exact, case-sensitive ID matching. Existing UTF-8 BOMs, comments, unknown rows, line endings and unterminated final lines are preserved. New entries use LF terminators. Invalid UTF-8 and line-breaking IDs fail without rewriting the file.

## Download behavior

Known individual YouTube, Vimeo, Dailymotion, Wistia, JW, Brightcove numeric-video and Kaltura addresses can be checked before metadata requests. Collection URLs and reference aliases still need discovery. Every resolved item is checked again before selecting formats or transferring media. Archived items succeed with `Skipped=true`, produce no new output paths and do not invoke completion callbacks.

A record is written only after media and requested sidecars have been successfully published. Failed transfers, canceled work and failed post-processing remain unrecorded. Existing output accepted by `NoOverwrites` can seed an archive, matching the native engine's existing successful-skip behavior. Playlist items are recorded individually; a failed item or the parent playlist is never recorded as a successful video.

Completed media remains visible in `OutputPaths` and completion callbacks if the later archive update fails. The result reports that failure, so the caller can show both the retained file and the history problem. Completion callbacks run after archive and output locks are released.

## Storage and coordination

An in-process gate and a persistent zero-byte `<archive>.native.lock` file serialize native jobs using the same archive, including cooperating processes. Waiting honors cancellation, and process exit releases the operating-system lock. Downloads using different archives can run independently. Already-recorded read-only archives can be checked without creating the lock file.

Archive commits reread the current file, preserve external entries added during the transfer, write and flush a sibling temporary file, then replace the archive atomically. Ordinary external appenders are excluded during that short commit. Observed replacement edits trigger a retry instead of publishing a stale snapshot. This is not a coordination contract for unrelated programs that repeatedly replace the pathname without using the native lock.

Media, sidecar and workspace paths cannot overwrite or clean up the configured archive or its lock file. Windows device paths and alternate data streams are refused. Relative archive paths resolve against the process working directory. The current file-size limit is 64 MiB and updates copy the whole archive; larger files and higher-throughput coordination remain work for the full replacement.

## Identities and migration limits

Site-specific media IDs are used when available. The native Brightcove and Internet Archive provider labels map to `brightcovenew` and `archiveorg` record prefixes. Complete legacy identity equivalence for every provider remains unverified, especially Internet Archive file groups and historical aliases.

Generic/direct pages without a site ID use a `native:<provider>` prefix and a SHA-256 digest of the full source URL without its fragment. This avoids false skips caused by shared hosts or titles. It deliberately does not guess compatibility with historical generic IDs; signed-query changes may create a distinct identity. Legacy generic archive migration, force-write/simulation modes, break-on-existing behavior and remaining upstream archive interactions are pending.

## Verification

Eighteen deterministic native checks cover preservation, matching, failures, playlists, simultaneous jobs, a separate process, cancellation, callback reentry, read-only files, external additions, encoding errors and output/workspace collisions. A WPF regression check verifies the existing setting reaches the native engine without making a website request for an archived video.

On October 8, 2026, the native Kaltura path downloaded the complete 6,979,543-byte public sample `1_rcit0qgs` and wrote exactly `kaltura 1_rcit0qgs`. A repeat request succeeded as skipped with an HTTP factory that throws if contacted, confirming that no metadata or media request was attempted.

Behavioral references: [yt-dlp archive FAQ](https://github.com/yt-dlp/yt-dlp/wiki/FAQ#how-do-i-download-only-new-videos-from-a-playlist) and [documented options](https://github.com/yt-dlp/yt-dlp#video-selection). The implementation is original C# filesystem and engine code.
