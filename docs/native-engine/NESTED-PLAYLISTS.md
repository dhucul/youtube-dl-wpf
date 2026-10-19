# Native nested playlist downloads

Engine 0.76.0 adds in-process downloads of linked or resolved collections inside another selected playlist. Pinterest board entries can now expand exposed multi-video stories. Generic pages with several native embeds also retain collection children. Full all-site/options coverage and production cutover remain unfinished.

## Traversal and source context

The existing outer selector determines which outer entries run. A selected collection expands its own ordered inventory; outer item indexes are not accidentally applied again to its children. Reverse/random order, current authentication/network choices, failure policy and the shared download-limit state continue through the same owned job session.

Each saved leaf keeps its immediate playlist ID/title/index/selected-job number/count. Its info JSON also includes ordered `playlist_parents` records, preserving each ancestor's original ID/title/index/selected-job number/count. Default filenames retain nested numeric prefixes; custom templates continue to use the immediate playlist fields.

Container identities are verified before expansion. Pinterest multi-asset stories declare their post/container identity separately from each video asset's archive identity. Only completed media leaves are recorded; a collection wrapper is not recorded as if it were a media file.

Native traversal tracks ancestor identities, limits collection depth to five levels, validates per-level positions/counts and caps one job at 10,000 visited source positions. These are explicit failure boundaries rather than silently truncated results. A recursion/budget error stops traversal even when ordinary item failures may be ignored. Already published and notified files remain intact.

## Completion, failure and cancellation

Completion notifications are forwarded exactly once from each saved leaf. Parent aggregation retains all returned outputs/sidecars without notifying them a second time. Nested failures retain their immediate source position and ancestor path. Ignore-errors can continue through later inner/outer entries; abort-on-error stops both. The existing job-wide maximum-download counter does not reset for another nested collection.

Cancellation or a fatal traversal boundary can interrupt result aggregation; finalized files and their already delivered callbacks/archive records remain preserved. Archive retries can inspect collection membership but do not transfer or re-notify recorded leaves.

## Verification

Eleven deterministic checks cover linked/resolved collections, generic multi-embed collections, parent/child JSON metadata, unique callbacks, partial and aborting failures, cancellation, shared limits, ancestor cycles, depth/aggregate bounds, archives and changed-container refusal.

Two generated MP4/HLS checks exercise an inspected Pinterest board entry expanding into two story clips. Both paths produce four fully decoded media files (processed/original per clip), each with all 48 video frames and audio. They verify two parent-aware leaf sidecars, original board/story positions/counts, leaf-only archives, no unselected pin access and zero repeat media transfers/callbacks. This is deterministic protocol plus real generated-media verification, not a claim of complete live nested-site coverage.

Prior public Pinterest board and single-story playback protocols are live verified in their provider documents. A complete current public multi-clip story inside a board remains a live verification gap. Exploratory public examples did not establish that complete case in this stage. Complete page effects/music/image rendering and broader nested/authenticated formats also remain pending.

See [Pinterest stories](PINTEREST-STORIES.md), [Pinterest boards](PINTEREST-COLLECTIONS.md) and [inspected selections](PLAYLIST-SNAPSHOTS.md). Remaining sites/options, old-runtime removal, packaging without yt-dlp/Python and the WPF switch remain required.
