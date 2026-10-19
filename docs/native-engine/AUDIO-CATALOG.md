# Native ARD Sounds audio and extraction pacing

Checkpoint 0.218.0, 2026-10-18. Full replacement remains incomplete. The frozen scope remains 1,731 extractor keys, 126 classic settings and 276 captured long option names. These are partial implementations of two exact keys, not complete ARD or catalog parity.

## Episodes and full audio

Current canonical ARD Sounds and legacy ardaudiothek.de episode URLs resolve through current first-party literal JSON page state. The selected asset URN, episode/extra type, canonical path, numeric ID, publication URN, programme and complete duration must agree. Publication, programme and duration remain pinned across bounded playback renewal. Published and already-started scheduled episodes are supported. Unpublished/future/expired/DRM/region/subscription conditions do not produce playable sources.

Only explicit complete downloadable renditions are selected. The declared URL and download URL must agree and allowDownload must be true. Observed MP3 and AAC-in-MP4 are supported. Streaming-only renditions, additional formats and unobserved delivery hosts are not silently accepted. Current observed exact hosts include SWR, MDR, BR and NDR delivery; actual full public playback evidence is SWR MP3 and MDR AAC. Signed query renewal remains scoped to the selected rendition identity; changed publication/show/duration or a missing used rendition refuses replacement. Public API/media requests clear source credentials and custom headers.

The canonical public episode and complete source bytes remain available independently of the old frontend host. No yt-dlp, Python extractor or evaluated website script runs in the application.

## Complete flat shows

The website-declared public GraphQL programSet API is paged in batches of 12. Each page binds the show URN, numeric identity, canonical path and stable total; row count and hasNextPage must agree with the declared inventory. Unique episode IDs, source positions, exact parent membership and advancing cursors are required. Oversized, partial, repeated or changed inventories fail explicitly. Unavailable positions are preserved. Inventories contain canonical episode URLs and archive/member identities, without retained playback grants. Selected playback revalidates inspected membership and obtains fresh episode metadata and links.

The currently published six-item Twilight forever show is verified in full, including its extra/trailer member. This is the current publisher inventory; future/unpublished episodes are not implied. Multiple pages, limits, unavailable entries and changed inspected selections are also verified using owned 13-item fixtures. Seasons, live, search, other collection types and old numeric routes remain pending.

Primary observed contracts:

- https://www.ardsounds.de/sendung/twilight-forever/urn:ard:show:365fdfb5a3299aab/
- https://api.ardaudiothek.de/graphql and the website-declared ProgramSetEpisodesQuery
- https://www.ardsounds.de/episode/urn:ard:episode:73e4dde3e5a870ad/
- https://www.ardsounds.de/episode/urn:ard:episode:a8333f279f4836db/
- The show's selected extra https://www.ardsounds.de/episode/urn:ard:extra:38e1302cbd05c263/

These support exact ARDAudiothek and ARDAudiothekPlaylist subset evidence. Other ARD keys are not counted.

## --sleep-requests

The captured option controls finite nonnegative seconds between HTTP requests during data extraction. Last argument wins; invalid values fail before native discovery. The first extraction request starts without a delay. Subsequent extraction requests share one cancellable job-owned pacing gate, including derived public sessions and concurrent extraction calls. Nested scopes restore their caller state; independent jobs do not inherit each other's request count. The delay is active only during provider extraction and shared provider-session metadata renewal. Media bodies, manifest fragments and caption polling outside those scopes do not receive extraction waits. Provider-specific account/renewal paths outside the shared session still need contract coverage.

WPF preserves extraction pacing during read-only analysis and respects saved/job argument precedence without mutating saved settings. Null defaults preserve request serialization, publication recipe and existing adaptive recovery/selection identities. Pacing is excluded from the publication recipe, so changing it does not invalidate durable media recovery. Very large finite waits are chunked and cancellable.

## Remaining workload

There is no defensible completion percentage or finish date from extractor-key counts alone. The common native transport, processing and recovery engine has extensive evidence, but most frozen provider keys still lack implementation evidence and every partial key retains workflows to close. Remaining option grammar/semantics, three AdobePass fields, deferred real-account evidence, the complete optional/release gate, WPF production cutover and packaging without yt-dlp/Python are still open. At the current small-family batch pace, full catalog closure is not a matter of a few remaining turns. Real-account verification remains explicitly user-deferred; no saved accounts were inspected.

## Final verification

3,472 complete core checks; nine new media cases; 97 additional distinct affected media cases; 3578 current native checks; 190 WPF checks. The focused total is 63 including foundation/core/media. All 175 affected renewal and 122 prior broadcast/caption-timing checks pass. Release builds without warnings or errors. Full optional tests were not rerun; 1,395 optional cases are available, and the previous complete 4,199-check run on 0.200.0 remains explicitly separate.

Seven retained generated workflows cover MP3, AAC, FLAC conversion, renewal, no-overwrite, interrupted processing recovery and configured pacing. Three complete public audio workflows and the entire current six-item inventory are independently verified from fresh publisher responses. All 21 source/output files decode strictly in full. Thirty comparisons verify exact full publisher bodies, 25,480 coded audio packets and 53,864,422 PCM24 samples, including exact decoded FLAC conversion samples. No preview or truncated source is counted. Archive repeats avoid duplicate completion callbacks.

Compared with the retained verified 0.216.0 binary, default request serialization, selection policy/scopes, publication recipes and static/live HLS recovery identities are unchanged. Configured extraction pacing also leaves the publication recipe unchanged. Engine SHA256: F75063B0659D2ECBDB8A835C9D5ADF4251FE96094D7C90D842553690FDB70894.
