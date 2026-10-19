# Native Spreaker verification

The independent C# provider in native engine 0.87 supports public recorded Spreaker episodes and complete public show inventories. It has no Python, yt-dlp runtime, extractor import, or fallback. This is partial coverage of the frozen `Spreaker` and `SpreakerShow` keys; the production WPF app still uses its original backend while full replacement remains unfinished.

## Public protocol and scope

Public numeric episode/show routes, current `slug--ID` episode and podcast routes, older show aliases, direct numeric API routes, and episode/show embedded-player links dispatch to the provider. Metadata uses anonymous GET requests to the fixed public API. Episode, show and author identities must agree; returned public links and supplied download paths retain the requested identity. Current public responses omit some older documented access fields. Explicit contradictory or malformed access states are refused; absent older fields are accepted only in the anonymously returned public record.

Only recorded episodes with an explicitly enabled public download are transferred; a supplied encoding state must be READY. The exact supplied `api.spreaker.com/v2/episodes/ID/download.mp3` address and its literal query are preserved. Local MP3 decoding, media roles and declared duration are checked before publication. Originals, metadata, information JSON, supplied artwork, audio conversion and stable episode archive identities use the common engine pipeline.

Shows use the public listenable listing, explicit newest sorting and pages of at most 100 episodes. Each continuation must retain the exact show, filter, sorting, limit and last supplied episode ID. A terminal null continuation is required; repeated identities/pages, wrong ownership, incomplete pages and configured limits fail explicitly. Unavailable entries retain source positions. Inspected selections keep stable child identities and refresh only chosen episodes; known episode archives avoid metadata and playback requests.

The [official episode API](https://developers.spreaker.com/api/episodes/) documents recorded/live/external/draft types, millisecond duration, playback/download endpoints, visibility and show listings. The [show API](https://developers.spreaker.com/api/shows/) and [embedded-player guide](https://developers.spreaker.com/guides/embedding-the-player/) define show metadata and episode/show player identities. Actual current public responses were checked on October 9, 2026.

## Verification

Eleven core checks exercise identity/alias binding, current and older field layouts, access restrictions, literal resource links, complete pagination, unavailable positions, snapshots, archives, anonymous metadata, cancellation, denial and redirects. Five generated-media checks fully decode normal, selected, inspected and converted audio, retain exact original bytes and reject incomplete enclosures without changing prior output or recording success.

Live native-engine downloads used [Podcast Trailer, episode 60424856](https://www.spreaker.com/episode/podcast-trailer--60424856). Its anonymously supplied metadata declared 58.54 seconds. Direct episode, embedded player and inspected show selection each published a 937,741-byte processed MP3 and the exact 937,715-byte source. All contained 2,242 decoded audio frames over 58.538685 seconds. Independent packet hashes confirmed all 2,242 encoded source packets retained identical payload and order after metadata processing.

Audio conversion produced a 710,530-byte AAC M4A, 2,522 decoded audio frames, and the retained MP3 source. All eight published files fully decoded without errors. Each initial job reported two file completions; archive repeats reported zero.

The inspected [Deep Dive show](https://www.spreaker.com/podcast/the-deep-dive-with-sydney-nadiancio--6208766) contained eight episodes and selected the trailer at original position eight. The native provider also completed all 198 entries in the paginated [Spreaker Live Show](https://www.spreaker.com/show/1433865).

Scratch evidence is under `work/spreaker-probe` in the Codex workspace, including public metadata, live results, packet verification and regression logs. The scratch harness is not a production dependency.

## Remaining work

Live broadcasts, external hosting, private/limited/account workflows, user collections, broader legacy routes, player-specific latest-only behavior, chapters, transcripts, alternate media layouts and full site/option parity remain pending. A completed public inventory does not establish private inventory completeness or stability across a publisher changing its list. Production cutover, removal of the old app dependency and packaged verification remain pending.
