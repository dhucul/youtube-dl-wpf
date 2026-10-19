# Native ReverbNation public songs and artist collections

Verified October 10, 2026, with native engine **0.112.0**. Captured key `ReverbNation` has a verified public subset. Full site/option parity and production cutover remain unfinished.

## Implemented behavior

Independent C# discovery supports current and legacy public song addresses, numeric song IDs with title slugs, artist homepages and selected-song embedded players. Public page bootstraps are read as bounded literal metadata without executing publisher JavaScript. Player discovery binds the selected numeric song and artist IDs to the declared public artist homepage, then refreshes the song page. Unknown selectors, private/account routes, ambiguous or executable bootstraps, credentials, fragments, nondefault ports and unsupported widgets fail explicitly. ReverbNation domains are reserved from generic discovery.

Artist discovery verifies declared result counts, page counts and sizes, numeric owners, canonical song addresses, duplicate identities, source order and unavailable positions. It rechecks the artist and first-page identities before accepting the complete inventory. Missing, truncated, repeated, changed or oversized inventories fail rather than silently truncate.

Public artist inventories intentionally omit playback URLs. Inventory validation therefore accepts complete identity, access and duration metadata without requiring a delivery URL. Selected playback always refreshes the individual song page and requires its currently supplied signed MP3 URL, matching the numeric song ID and published original resource path. Inspected selections refresh only chosen recordings and preserve their original collection positions.

Explicit public and streaming access flags are required. Metadata protection flags and exclusive download permission do not themselves prohibit an explicitly offered public stream; nonpublic, streaming-restricted and download-only recordings fail. Supplied signatures remain literal. Media and artwork resource paths are checked against the published recording/photo IDs. Anonymous discovery isolates caller authentication. Archives use `reverbnation` plus numeric song ID.

The native transfer path retains ffmpeg for complete local MP3 decoding, declared-duration verification, metadata processing and conversion before transactional publication. Exact originals, protected existing output, archive skips and completion callbacks use the shared native engine. No Python, yt-dlp or publisher-script execution is used by these workflows.

## Verification

Fourteen core checks cover source facts, current/legacy aliases, selected widget bindings, URL boundaries, conflicting song/artist/canonical/media identities, access restrictions, metadata protection, literal-only parsing, counted artist inventories without playback URLs, pagination failures, unavailable positions, inspected selections, numeric archives, anonymous isolation, cancellation and HTTP denial. Six generated-media checks cover complete MP3/player/artist/snapshot/conversion workflows, original retention and refusal of an incomplete source before publication.

The Release solution build passed with zero warnings/errors. All **1,685 native checks** passed: 1,255 core and 430 optional media checks. All **61 app checks** passed, including four optional browser-tool checks. The ReverbNation-filtered run passed 27 checks, including the runner's seven common preliminary checks.

Live testing used public [So Close - (instrumental clip) by Maggie Young](https://www.reverbnation.com/maggieyoung/song/25312777) and the [Maggie Young artist page](https://www.reverbnation.com/maggieyoung). The artist inventory declared and returned 28 unique songs in source order; the selected recording retained position 1. The recording is publicly posted as an instrumental clip. Verification establishes that complete supplied recording, not a longer release.

| Live workflow | Processed output | Retained original | Decoded audio frames |
| --- | ---: | ---: | ---: |
| Current song address | 1,110,732-byte MP3 | 1,114,798-byte MP3 | 2,655 each |
| Legacy song address with title slug | 1,110,732-byte MP3 | 1,114,798-byte MP3 | 2,655 each |
| Selected-song embedded player | 1,110,732-byte MP3 | 1,114,798-byte MP3 | 2,655 each |
| Artist selection, position 1 | 1,110,732-byte MP3 | 1,114,798-byte MP3 | 2,655 each |
| Inspected artist selection, position 1 | 1,110,732-byte MP3 | 1,114,798-byte MP3 | 2,655 each |
| M4A conversion | 1,130,347-byte AAC M4A | 1,114,798-byte MP3 | 2,986 AAC / 2,655 MP3 |

All twelve files independently decoded completely with no decoder diagnostics. MP3 files reported 69.319501 seconds; the converted M4A reported 69.32 seconds. Every unconverted processed/original PCM SHA-256 pair matched exactly. Each initial job emitted two completions; archive repeats emitted no outputs/callbacks. The workflows used the native WPF adapter with isolated saved settings and real .NET HTTP delivery.

Original live, inventory, build and regression evidence remains under `work/reverbnation-probe`, `work/reverbnation-stage` and `work/reverbnation-live-probe` in the October 10 `new-chat` workspace. Independent full-decoding results, per-file hashes and recovery verification are under `work/reverbnation-recovery` in the `we-ran-out-of-context-window` workspace. Recovery verified the original source/staging contents and documentation hashes before recording this checkpoint. Scratch evidence is not a runtime dependency. Existing user settings/history/media and uncommitted work were preserved; no Git commit or push was performed.

## Remaining work

Multi-song/full-catalog widgets, additional collection/layout/player routes, video/transcript/lyrics and other upstream features, private/account/authentication/access workflows, alternate media/storage formats and full site/app-option parity remain pending. The 28-song public artist inventory and one MP3 recording do not establish those workflows.

Production WPF still uses yt-dlp. Installer/runtime/fallback removal and packaged verification without Python or yt-dlp remain unfinished. Native adapter verification is not packaged WPF verification.
