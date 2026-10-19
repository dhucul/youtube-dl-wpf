# Native Newgrounds public media and collections

Verified October 10, 2026, with native engine **0.103.0**. Captured keys `Newgrounds`, `Newgrounds:playlist` and `Newgrounds:user` have a verified public subset; full site/option parity and production cutover remain unfinished.

## Implemented behavior

Native C# discovery supports public numeric audio-listen and portal-view URLs, declared playlists and creator audio/movie listings. Unsupported access/filter/manual-page URLs, credentials, fragments, ports and private routes fail. Newgrounds domains are reserved from generic extraction.

Current audio pages expose a literal `NgAudioPlayer.fromListenPage` object. A bounded independent literal reader accepts quoted strings, numbers, booleans, null, arrays and objects; computed values, calls, identifiers and duplicate fields fail. It never executes publisher JavaScript. Submission/type identity and supplied MP3/artwork bindings must agree. Movie barriers and the advertised public video endpoint bind portal IDs to their internal project IDs and offered MP4 qualities. Literal media queries are retained.

Fixed local demuxers fully decode downloaded audio/movie resources before publication. Offered audio duration is verified; the current movie endpoint does not declare duration, so its tested complete-file transfer, picture/audio decoding and original preservation are recorded separately. Audio-only conversion, source originals and the shared metadata pipeline retain ffmpeg.

Creator collections follow the exact published next-page URL and terminal JSON envelope, preserving server order across year groups. Requested creator/type/page scope, declared count, limits, duplicates and final first-page/count stability are checked; missing/filtered inventories fail rather than truncate. Inspected selections refresh only chosen stable submission IDs and retain original positions.

Playlists read the original declared membership before browser presentation removes filtered items. Bounded read-only metadata batches retain the site's ordinary suitability rules. Missing availability, conflicting item URLs/IDs and changed final membership fail. Unsupported, unpublished, missing and account-filtered items keep their original unavailable positions; they are not treated as downloadable or as an empty complete playlist.

Native archives use distinct `newgrounds:audio` and `newgrounds:video` namespaces to avoid collisions between the site's overlapping numeric ID spaces. Compatibility/migration for ambiguous old `newgrounds <number>` archive lines remains pending. Existing archive data is preserved.

## Native Windows browser discovery

The site initially returned `NG Guard` to ordinary native HTTP requests. A normal browser opened the public pages after its automatic check. The native WPF adapter now supplies an optional C# WebView2 discovery source: one owned temporary browser profile per extraction, bounded deadlines and raw response capture before browser scripts remove unsuitable collection positions. Downloads remain in the C# core.

The browser bridge confines navigation and read-only metadata requests to the requested site/identity and published continuation scope. It blocks browser media delivery, downloads, popups, permissions, password saving and basic-auth prompts. Configured supported proxy/rate/cookie preferences use the existing native Windows network/browser paths; unsupported connection/header configurations fail before acquisition. Valid collection/generic referrers are retained. Interactive account, age-verification and remaining authentication workflows require further verification.

Raw HTTP may become available after ordinary browser access; both paths are explicit. The live download probe deliberately denied metadata in its test wrapper so every required discovery used the actual owned native browser path. It did not supply captured pages as a runtime replacement or use Python/yt-dlp.

## Verification

Fourteen new core checks cover literal parsing, source facts, namespaces/aliases/boundaries, audio/movie identity conflicts, creator pagination/count/change failures, playlist order/availability/conflicts, snapshots, archives and bounded browser dispatch. Eight generated-media checks fully decode audio/video, conversions and originals, verify exact source bytes/unconverted samples and all picture frames, and protect prior publication during incomplete sources or later cancellation.

Four app checks cover browser preflight, referrers, caller cancellation, distinct deadlines, gate serialization and recovery after queued cancellation. The Release solution build passed with zero warnings/errors. All **1,534 native checks** passed: 1,150 core and 384 optional media checks. All **57 app checks** passed, including four browser-tool checks.

Live testing used public [Short](https://www.newgrounds.com/audio/listen/1136677), [AmeliaUwU's audio](https://ameliauwu.newgrounds.com/audio) and [Slap The Deck](https://www.newgrounds.com/portal/view/1053822). The creator's declared 52-track inventory completed; Short retained position 7. The inspected playlist had ten declared account-filtered entries, preserved as unavailable positions; it was not used as evidence of working account downloads.

| Live workflow | Processed output | Retained original | Decoded frames |
| --- | ---: | ---: | --- |
| Direct audio / inspected creator position 7 | 4,863,036-byte MP3 each | 4,862,954-byte MP3 each | 4,653 audio each |
| Audio conversion | 2,129,943-byte AAC M4A | 4,862,954-byte MP3 | 5,233 AAC / 4,653 MP3 |
| 360p movie | 30,284,502-byte MP4 | 30,284,434-byte MP4 | 7,800 video / 15,235 audio each |
| 720p movie | 72,647,339-byte MP4 | 72,647,271-byte MP4 | 7,800 video / 15,235 audio each |
| Movie audio extraction | 5,320,509-byte AAC M4A | 30,284,434-byte MP4 | 15,235 audio / original 7,800 video |

All twelve files independently decoded completely. Audio-source duration was 121.508980 seconds against the publisher's rounded 122 seconds; movie duration was 325.013000 seconds. All unconverted processed/original decoded audio hashes matched. Each job completed two files; archive repeats emitted no outputs/completions. All owned browser session profiles were removed. The first completed audio workflow was reused and revalidated after the live creator-selection referrer fix.

Public metadata captures and isolated verification evidence are under `work/newgrounds-probe` and `work/newgrounds-live-probe` in the October 10 Codex workspace. They are not runtime dependencies. User settings, history, media and existing uncommitted work were preserved. No Git commit or push was performed.

## Remaining work

Live 1080p/default-best movie verification, additional movie codecs/legacy Flash and game layouts, all playlist pagination/layouts, complete account/age/authentication and cookie semantics, ambiguous legacy archive migration, broader generic embeds/custom network options and full site/option parity remain pending. Public creator/movie and unavailable-playlist protocol coverage is not complete upstream parity.

Production WPF still uses yt-dlp. Installer/runtime/fallback removal and final packaged application verification without Python or yt-dlp remain unfinished. The native adapter/browser probes are not packaged WPF verification.
