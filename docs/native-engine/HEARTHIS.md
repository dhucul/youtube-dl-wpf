# Native hearthis.at public audio and artist collections

Verified October 10, 2026, with native engine **0.111.0**. Captured key `HearThisAt` has a verified public subset. Full site/option parity and production cutover remain unfinished.

## Implemented behavior

Independent C# discovery supports public artist/track pages, artist profiles, API-host aliases and numeric embedded players. Player discovery reads one bounded declared tile and canonical recording link without executing JavaScript, then binds fresh API metadata to the requested numeric ID. Private keys, unknown selectors, manual page URLs, credentials, fragments, ports and unsupported routes fail. Site/gateway domains are reserved from generic discovery.

The current public API exposes numeric recording/owner IDs, canonical paths, declared duration, source facts/artwork, current playback gateways and permitted download descriptors. Owner/recording/API URL bindings, explicit private/fan-gated/live/video flags, duration and supplied gateway paths are checked. Published stream queries remain literal. Separate `preview_url` resources are never substituted for complete playback. Permitted MP3/M4A/WAV/Ogg/FLAC original descriptors are reported only when explicitly offered; the live sample offered one MP3 stream.

Fixed local ffmpeg demuxers fully decode selected audio and check declared duration before transactional publication. Metadata processing, exact originals and conversion retain ffmpeg. Anonymous discovery isolates caller authentication; no Python, yt-dlp, account login or publisher-script execution is used.

Artist discovery uses counted bounded public pages, exact owner/entry identity and duplicate checks, original source order and unavailable positions. Missing/filtered totals, oversized inventories, repeated/conflicting entries and changed final profile/first-page descriptors fail rather than truncate. Inspected selections refresh only chosen recording metadata and retain source positions. Archives use `hearthisat` plus numeric track ID.

## Verification

Thirteen core checks cover facts/source identities, player binding, URL boundaries, conflicting owner/recording/player/media paths, private/fan/live/video states, preview exclusion, offered original formats, complete/change/missing/duplicate/overlimit artist pages, unavailable positions, snapshots, archives, credentials, denial and cancellation. Six generated-media checks fully decode MP3/player/selection/snapshot/conversion workflows, preserve source bytes/unconverted samples and protect prior media/archive data when an advertised longer source is incomplete.

The Release solution build passed with zero warnings/errors. All **1,665 native checks** passed: 1,241 core and 424 optional media checks. All **61 app checks** passed, including four optional browser-tool checks.

Live testing used public [Akuratyde - Time Left Behind](https://hearthis.at/blumarten/akuratyde-time-left-behind-feat-eusebeia-out-now-on-bmtm/) (`937984`) and [Blu Mar Ten](https://hearthis.at/blumarten/) (`4050`). The artist's declared 384 unique public entries completed across seven 50-entry pages and one 34-entry page; the selected recording retained position 6. The recording is publicly posted as a 105-second excerpt, and the verified source is that complete posted recording, not a claim about a longer release.

One repeat artist inspection returned conflicting membership and the native guards rejected it before any media publication. Earlier raw page data also conflicted with the declared count. Fresh bounded acquisition with public page-ID capture completed the correct inventory; no guard or count check was disabled. Transient server pagination inconsistencies remain an observed availability limitation, not evidence of a complete inventory when verification fails.

| Live workflow | Processed output | Retained original | Decoded audio frames |
| --- | ---: | ---: | ---: |
| Public track | 1,690,368-byte MP3 | 1,690,226-byte MP3 | 4,043 each |
| Embedded player | 1,690,368-byte MP3 | 1,690,226-byte MP3 | 4,043 each |
| Artist selection, position 6 | 1,690,368-byte MP3 | 1,690,226-byte MP3 | 4,043 each |
| Inspected artist selection, position 6 | 1,690,368-byte MP3 | 1,690,226-byte MP3 | 4,043 each |
| M4A conversion | 1,732,812-byte AAC M4A | 1,690,226-byte MP3 | 4,548 AAC / 4,043 MP3 |

All ten files independently decoded completely at approximately 105.59 seconds against the publisher's rounded 105 seconds. All unconverted processed/original PCM hashes matched exactly. Each job emitted two completions; archive repeats emitted no outputs/callbacks. Workflows used the native WPF adapter with isolated saved settings and real .NET HTTP delivery; the page-ID observer captured actual API responses rather than supplying fixtures at runtime.

Protocol references include the site's [official API documentation entry](https://hearthis.at/api-v2), current public website/player markup and supplied API metadata. Evidence is under `work/hearthis-probe`, `work/hearthis-stage` and `work/hearthis-live-probe` in the October 10 Codex workspace. It is not a runtime dependency. Existing user settings/history/media and uncommitted work were preserved; no Git commit or push was performed.

## Remaining work

Explicit sets/playlists, transcripts/features, fan/private/account/authentication/live/video workflows, live permitted-original codecs, additional player/storage/layouts and full site/app-option parity remain pending. The public artist inventory and MP3 sample do not establish those workflows.

Production WPF still uses yt-dlp. Installer/runtime/fallback removal and packaged verification without Python or yt-dlp remain unfinished. Native adapter verification is not packaged WPF verification.
