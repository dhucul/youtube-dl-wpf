# Native Rumble creator listings

Rumble channel and user listing URLs now use the native C# collection reader. Supported paths include the root listing and `videos`, `shorts` and `livestreams` tabs. Playback of live/restricted entries remains subject to the separate Rumble video provider's capabilities.

## Discovery and selection

The reader uses the JSON payload inside the page's `rum-videos-grid`, ignoring sidebar streams, unrelated JSON and analytics. It verifies the canonical URL, collection type, slug and numeric header identity on every page. Numeric `c-<id>` aliases must match the supplied channel ID.

Only the server's `rel=next` links advance pagination. Page numbers must advance consecutively and retain the selected creator, tab and query filters. Repeated video identities, missing/ambiguous continuations, changed headers, stalled pages, exceeded limits and access failures abort discovery instead of returning a truncated playlist. Forward navigation links without a corresponding continuation also prevent a false completion result.

The original order and unavailable positions are retained. Restricted, processing, age-restricted and known live states remain unavailable; unsupported content types retain a placeholder. Ordinary public videos are not hidden because a Premium-access or download-button field is false. Each usable watch link is checked against its permalink identity.

An explicit starting `page` is honored, with a warning that earlier pages are outside that requested listing. Positions are relative to the returned listing. Tabs and non-tracking query filters remain bound throughout pagination. Volatile server listings are not an atomic historical snapshot.

Video playback is looked up freshly only for selected entries. The channel grid's media previews are not downloaded. Existing selection, reverse/random ordering, file templates, per-video archives, related files and atomic publication apply. The grid's watch-link ID is distinct from the resolved embed/archive ID. A collection itself is never marked downloaded in the video archive.

## Verification

Ten deterministic checks cover scoped grid extraction, pagination, user/tab/filter binding, explicit starting pages, unavailable positions, identity mismatches, duplicate/stalled/missing continuations, limits, denial, empty grids, selected-only simulation and cancellation. Two generated-media checks verify complete selected playback, original source positions and selected-order numbering, resolved video metadata, archive skips, and retention of successful output when another selected entry is unavailable.

The live [Rumble Help Videos listing](https://rumble.com/c/RumbleHelpVideos) exposed nine entries. Native inspection matched the captured grid's IDs and ordering exactly, with all nine available. A subsequent selected-video run encountered an HTTP failure while reading the collection and published no media. A larger public channel exposed 25 entries on each of its first two independently captured pages and advertised a third page; an independent page-three request encountered an access challenge. The native larger-listing attempt also failed with an HTTP error and returned no partial inventory. These partial captures do not establish complete live pagination or selected-playback success.

Public user-listing requests tested during this pass were denied, so user routes remain fixture-verified rather than live-verified. Playlist-of-playlists pages, search, additional layouts, stable access to challenged listings, richer unavailable metadata, broader account workflows and full upstream option parity remain pending.

With version 0.51.0, a single native channel job successfully selected entry 4 and published the complete video and original. The output retained 2,508 video and 3,917 audio frames and fully decoded; source index 4, selected-order number 1 and count 9 were retained. See `PLAYLIST-SESSIONS.md` for the session change and detailed evidence. Larger/user live pagination remains unverified.
