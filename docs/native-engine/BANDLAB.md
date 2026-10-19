# Native BandLab public recordings and playlists

Verified October 10, 2026, with native engine **0.110.0**. Captured keys `Bandlab` and `BandlabPlaylist` have a verified public subset. Full site/option parity and production cutover remain unfinished.

## Implemented behavior

Independent native C# discovery supports canonical user/song slugs, public post/track GUIDs, revision GUIDs, revision embeds and public playlist GUID/owner URLs. A canonical song can select a specific public revision. Private shared keys, credentials, ports, fragments, unknown selectors and unsupported routes fail; BandLab domains are reserved from generic discovery.

The website's public player advertises read-only song/post/revision/collection APIs. Public revision requests explicitly use `edit=false`; the editing form returns HTTP 401 without authentication. Native public playback uses no user token or client secret, does not execute publisher JavaScript and does not contact account/editor or auxiliary third-party integrations. Anonymous sessions isolate caller authentication.

Song, creator, revision, public post and mixdown IDs must agree. Public publication/revision states, ready complete audio and supplied static media paths are checked; unsupported private/exclusive/preview/MIDI/unready/non-revision descriptors fail. Only provided M4A/MP3/WAV resources are reported. Fixed local demuxers completely decode selected audio and verify declared duration before publication. Metadata dates/counters, source artwork, conversion and exact originals use the existing C#/ffmpeg pipeline.

Playlist inspection requires complete declared `counters.items` membership, preserving original order and unavailable positions. Missing/filtered counts, duplicate posts, owner conflicts, limits and changed final membership/revision/mixdown descriptors fail. Inspected selections refresh selected public post metadata and bind expected revision archive IDs. Numeric source positions remain stable. Archives use `bandlab` plus revision GUID to deduplicate canonical/post/embed aliases; complete compatibility/migration for other legacy archive conventions remains pending.

## Verification

Thirteen core checks cover public facts, aliases/canonical/selected revision binding, unsupported URL boundaries, publication/owner/source conflicts, access/preview states, exact media paths, complete/changing/missing/overlimit playlists, unavailable positions, snapshots, known archives, credential isolation, denial and cancellation. Seven generated-media checks fully decode public audio/embeds/selection/snapshots/conversion, preserve exact originals/unconverted samples, reject short advertised sources and retain earlier completed selections during later cancellation.

The Release solution build passed with zero warnings/errors. All **1,646 native checks** passed: 1,228 core and 418 optional media checks. All **61 app checks** passed, including four optional browser-tool checks.

Live testing used [Mary Mary Mucho](https://www.bandlab.com/half_pint_/mary-mary-mucho-a5798), public post `fdbef3c7-bb82-e811-80c2-00155d60cb8b`, revision `fcbef3c7-bb82-e811-80c2-00155d60cb8b`, and [Half Pint's public playlist](https://www.bandlab.com/half_pint_/collections/b995e6f8-1381-e811-80c2-00155d60cb8b). Complete declared membership was five entries; selected playback retained source position 4. Workflows used the native WPF adapter with isolated saved settings.

| Live workflow | Processed output | Retained original | Decoded audio frames |
| --- | ---: | ---: | ---: |
| Canonical song | 1,650,147-byte AAC M4A | 1,651,300-byte AAC M4A | 5,831 each |
| Public post alias | 1,650,147-byte AAC M4A | 1,651,300-byte AAC M4A | 5,831 each |
| Revision embed | 1,650,147-byte AAC M4A | 1,651,300-byte AAC M4A | 5,831 each |
| Playlist position 4 | 1,650,147-byte AAC M4A | 1,651,300-byte AAC M4A | 5,831 each |
| Inspected selection, position 4 | 1,650,147-byte AAC M4A | 1,651,300-byte AAC M4A | 5,831 each |
| MP3 conversion | 2,167,443-byte MP3 | 1,651,300-byte AAC M4A | 5,184 MP3 / 5,831 original |

All twelve files independently decoded completely. The source reports 135.44 seconds; processed files report approximately 135.37 seconds after container timing normalization. Every unconverted processed/original PCM SHA256 matched exactly. Each completed job emitted two completions; archive repeats emitted no outputs/callbacks.

Protocol discovery also read the official site's [public track embeds](https://blog.bandlab.com/top-jams-5-tracks-to-sing-rap-or-solo-over/) and current advertised client resources. Captures/evidence are under `work/bandlab-probe`, `work/bandlab-stage` and `work/bandlab-live-probe` in the October 10 Codex workspace. They are not runtime dependencies. Python and yt-dlp were not used in native verification. User settings/history/media and existing uncommitted work were preserved; no Git commit or push was performed.

## Remaining work

Band/distributed-track/video layouts, all alternate public media/storage/legacy/share routes, additional collection layouts and very large/filtered inventories, account/private/exclusive/authentication semantics, complete legacy archive migration, generic embeds and all remaining site/app-option parity remain pending. The verified five-entry public playlist does not establish every collection or account workflow.

Production WPF still uses yt-dlp. Installer/runtime/fallback removal and packaged application verification without Python or yt-dlp remain unfinished; native adapter verification is not packaged WPF verification.
