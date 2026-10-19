# HTTP sessions within playlist jobs

One native playlist download now owns one HTTP session for listing discovery, selected-entry discovery, media and related files. Previously, an unresolved selected entry recursively started a new top-level download, creating a fresh cookie jar. A website cookie set while loading the listing could therefore disappear before the video was requested.

The new internal entry path retains the parent session while still validating each selected request, checking known archive identities before discovery, resolving fresh video metadata and applying the existing output pipeline. Resolved snapshots and newly discovered entries can be mixed in the same job. Completion callbacks remain owned by the playlist coordinator, avoiding duplicate notifications.

Provider playback headers are still temporary views of the session. An entry-specific media header does not become a default header on the next video's metadata request. Website-set cookies and subsequent updates do carry forward according to the HTTP cookie rules. Provider-specific anonymous playback isolation remains in place.

Separate top-level downloads and inspections continue to create independent sessions. This change does not cache credentials across jobs, solve access challenges, or establish a cause for the previously observed Rumble HTTP denials.

## Verification

Six real loopback-HTTP checks exercise `Set-Cookie` and actual native requests rather than a simulated cookie dictionary:

- Listing cookies reach selected metadata and media requests; media responses can update cookies used by the following entry.
- Resolved snapshots and unresolved entries share the same job session.
- Two jobs on the same engine start with independent cookie jars.
- Archived entries skip their own requests without resetting the session for remaining entries.
- A failed entry can update the job's cookies, and later selected entries retain valid successful output.
- Cancellation preserves completed media and archive records; a later job starts with a clean session and finishes the remaining entry.

The cookie-continuity checks failed against the previous implementation and passed after the session-sharing change. The full native/media and application regression results are recorded in `implementation-status.json`.

A live native job selected item 4 from the nine-entry Rumble Help Videos channel. It resolved watch ID `va5o2f` to playback ID `v7jhvr`, saved a 2,557,605-byte processed MP4 and a 2,557,507-byte original, and reported two completed files. Both fully decoded with 2,508 H.264 560x360 video frames and 3,917 AAC audio frames, lasting 83.683600 seconds. The info JSON retained source index 4, selected-order number 1 and playlist count 9; the archive recorded the resolved video ID. This successful run does not establish that every earlier site denial had the same cause as the reproduced cookie-loss bug.
