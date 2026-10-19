# Native Libsyn verification

Native engine 0.90 independently implements current public Libsyn episode players, hosted episode pages and complete player destinations in C#. No Python, yt-dlp runtime, extraction-code import or fallback is used. The frozen `Libsyn` key remains partially covered; production WPF cutover and full catalog/option parity remain unfinished.

## Public protocol and scope

The provider supports current `play.libsyn.com` and older `html5-player.libsyn.com` episode/destination embed identities, supplied display paths, destination tracking IDs and playback direction. Hosted `sites.libsyn.com/SHOW/EPISODE`, optional `/website/` aliases and older named subdomain episode pages discover one public episode player. Script/template/comment content cannot supply that player. Ambiguous players, private/filtered/cache-changing requests and unsupported routes fail explicitly.

The current publisher script's public API calls and response models were inspected without execution. Metadata comes from the fixed anonymous player API. Requested root/inner item IDs, content IDs and show ownership must agree. Complete destination records bind their show feed; older reduced episode-level show records retain their explicit owner IDs without requiring a missing optional feed field. Missing source media returns public metadata and an availability warning, allowing explicitly requested no-formats metadata workflows without recording media success.

The supplied playback URL retains its origin, path and literal query. Recognized Libsyn traffic/media origins and nested pscrb.fm/Podtrac prefixes are validated without stripping the tracking links. Supplied destination IDs append only the player's documented tracking parameter. Metadata and playback isolate caller credentials. Source MP3/M4A audio and MP4/M4V/MOV video use fixed local demuxers and full decoding before publication. Audio requires at least its listed program duration; video retains exact-duration checking. Originals, metadata/artwork, conversion, video-only processing and stable item archives use the shared engine pipeline.

Destination inventories use requested pages of 100 entries, retaining original source positions and unavailable entries. Destination/show ownership, repeated IDs, malformed pages and configured limits are checked. A short or empty page ends the requested public inventory; the first-page identities are fetched again and must remain unchanged. Inspected selections retain child IDs/positions and refresh only chosen playback. Publisher changes outside that initial anchor remain a coverage limitation.

The [current Libsyn player guide](https://five.libsynsupport.com/hc/en-us/articles/4415860385179-Customize-and-Share-the-Libsyn-Media-Player-for-Your-Podcast) describes episode/show players, audio/video, categories and playback direction. The [older player guide](https://help.libsynsupport.com/hc/en-us/articles/360040795712-Show-Media-Player) documents the earlier skins and playlist options. Public HTML, scripts, API records and RSS were inspected on October 9, 2026.

## Verification

Fourteen core checks cover episode/show/content binding, aliases, hosted-page discovery, private and unsupported routes, premium/no-media states, malformed and duplicate data, literal/nested tracking URLs, complete pagination, changed initial anchors, source positions, snapshots, archives, metadata-only missing-media handling, anonymous metadata, cancellation, denial and redirected identities. Eight generated-media workflows fully decode audio source/selected/snapshot/converted files and video/selected-track output, preserve exact originals, verify archive retries and reject incomplete audio without changing prior output or recording success.

Live downloads used [Trailer: Introducing The Entrepreneurial Therapist](https://sites.libsyn.com/397451/trailer-introducing-the-entrepreneurial-therapist), item `22065683`. Hosted page, older embedded player and audio conversion published fully decoded media/original pairs. MP3 source and output contained 4,026 decoded frames over 105.124286 seconds, compared with the publisher's rounded 106-second program duration. The exact original was 1,473,230 bytes and processed MP3 1,472,974 bytes. All 4,026 encoded packet payloads/order and complete decoded PCM matched after metadata processing. Converted AAC M4A was 1,884,664 bytes with 4,528 decoded frames over the same duration. Each successful initial job reported two completions; archive repeats reported zero.

Destination `3270797` exposed pages of 100, 100 and 49 entries. The owned Libsyn RSS independently contained 249 items. A request beyond that final short page returned HTTP 500; the provider does not request past the observed terminal page. Older entries on the last page omit optional show-feed details, which the reader now supports while retaining explicit show-ID checks.

The native provider completed all 249 entries, rechecked its initial page, and downloaded the inspected trailer at original position 249. That selected job published the same complete MP3/original pair, with two initial completions and zero repeat completions. Eight live files across the page, embed, inspected-selection and conversion workflows fully decoded.

Scratch evidence is under `work/libsyn-probe` in the Codex workspace, including public responses, RSS, live results, packet/PCM comparisons and regression logs. Downloaded publisher scripts and the probe harness are not runtime dependencies.

## Remaining work

Premium/account access, category filters, directory/custom-domain/other hosted routes, additional tracking prefixes, transcripts/chapters, other media containers and full site parity remain pending. Libsyn video workflows are generated-media verified; successful live Libsyn video playback remains unestablished. Production cutover, old dependency removal and packaged verification remain pending.
