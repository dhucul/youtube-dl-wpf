# Native Megaphone verification

Native engine 0.91 independently implements public Megaphone episode, podcast and selected-channel players in C#. It uses no Python, yt-dlp executable, imported extractor or fallback. The frozen `megaphone.fm` key remains partially covered. Production WPF cutover and full site/option parity remain unfinished.

## Public protocol and scope

Supported inputs include classic player/origin episode UIDs, modern `playlist.megaphone.fm` episode and podcast links, public player metadata routes, `megaphone.link` episode links and named CMS channels. A named channel's `selected` UID must occur in its complete public inventory. Combined/ambiguous episode-podcast identities, private tokens, manual pages and clip requests fail explicitly. Known player surfaces require dedicated extraction, preventing generic HTML from turning an unsupported clip request into a whole episode. Generic public RSS support remains available.

The current publisher player scripts and anonymous JSON responses were inspected as protocol evidence without executing them. Public episode lookup returns exactly one matching UID; podcast lookup supplies explicit total count, page size and page number. Named podcast aliases bind their canonical feed; opaque podcast UIDs retain the initial canonical feed as their ownership anchor. Pagination must preserve that anchor, counts and limits. Wrong filenames/share links, repeated UIDs, changed pages, early termination and configured limits fail before publication.

The `episodes` display option was checked live: it limits the latest-episode view and its declared count, rather than changing page size. The native reader preserves that requested view. Unavailable entries retain source positions. Inspected selections preserve original child identities and refresh chosen playback; archived UIDs skip playback discovery.

The exact supplied traffic URL and nested Chartable/Podtrac/pscrb tracking prefixes are retained. The underlying `traffic.megaphone.fm/UID.mp3` filename must match the requested episode. Caller credentials are isolated from metadata and playback. Full local MP3 decoding, complete transfer and program-duration lower bounds precede publication. Originals, metadata, artwork, conversion and archives use the shared engine pipeline. An attached album image remains artwork, not a video format.

The [official single-episode player guide](https://support.megaphone.fm/en/articles/71376-embeddable-player-single-episode) describes public embedding. Publisher HTML/scripts and current public metadata endpoints were inspected on October 9, 2026. These observations do not establish private or clip API coverage.

## Verification

Twelve core checks cover public facts, aliases, unsupported routes, identity/resource binding, malformed and duplicate JSON, complete pagination, changing inventories, latest scopes, selected channels, source positions, snapshots, archives, anonymous metadata, cancellation, denial and redirected identities. Five generated MP3 workflows verify normal/selected/inspected/converted media, exact original bytes, encoded packet order, anonymous playback and incomplete-source refusal without changing prior files or recording success.

Live workflows used [Introducing StartUp](https://player.megaphone.fm/GLT8924910686), through its classic player, modern episode link, inspected podcast selection and audio conversion. Eight processed/original files fully decoded. MP3 output was 1,569,203 bytes; its exact original was 1,569,309 bytes. Both contained 2,454 audio frames over 64.104458 seconds and retained the existing attached JPEG artwork. All 2,454 encoded audio payloads/order and the complete decoded PCM hash matched after metadata processing.

Converted AAC M4A was 1,136,922 bytes, with 2,761 decoded audio frames over 64.104490 seconds, retaining the original MP3. Initial jobs reported two file completions each; archive repeats reported zero. The inspected [StartUp podcast](https://playlist.megaphone.fm/?p=startup) contained 29 entries, with the trailer at original position one. Native pagination also completed all 214 entries of [Reply All](https://playlist.megaphone.fm/?p=replyall). Native inspection of a latest-three view returned exactly three entries; inspection of the selected CMS channel link returned the requested single trailer with a ready format.

Scratch evidence is under `work/megaphone-probe` in the Codex workspace, including public responses, live results, packet/PCM verification and regression logs. Publisher scripts and the probe harness are not runtime dependencies.

## Remaining work

Clip tools, private/account workflows, broader player layouts, transcript/chapter metadata, video workflows, other tracking prefixes and full site/option parity remain pending. Count/owner checks do not prove immutable order while publishers change their catalogs. Production cutover, old dependency removal and packaged verification remain pending.
