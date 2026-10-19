# Native SoundCloud provider

Version 0.192.0 adds complete public SoundCloud upload/profile, related-track and sounds-search URL inventories, selected membership/identity revalidation and clear progressive/HLS playback renewal. All 3,837 native checks (2,669 core + 1,168 media), 143 app and 65 focused checks pass; Release has zero warnings/errors. Four retained generated workflows independently decode eight files with 1,136 exact coded audio packets. Public inventories contain 52 uploads, one search result and 39 related tracks; two selected Creative Commons downloads produce four fully decoded 182.815692-second AAC files with exact main/original packets and zero playback/media/callback archive repeats. Four newly evidenced subsets bring partial coverage to 118; 1,478 keys still lack key-specific evidence. Full replacement remains open, default startup is unchanged and real-account checks remain user-deferred. See [SoundCloud inventories and renewal](AUDIO-INVENTORIES.md).

The independent C# provider reads the website's `__sc_hydration` JSON and its supplied public `apiClient` identifier. It does not execute website JavaScript or use third-party extraction code. Playback requests use the same native HTTP session and the track authorization returned in that page. No client secret, browser profile or account creation is involved in the verified public workflow.

## Implemented scope

- Public track and set URLs, mobile/www aliases, numeric public API track/playlist URLs, standard `w.soundcloud.com/player` embeds and redirects from `on.soundcloud.com`.
- Track identity, canonical permalink, title, description, creator, artwork, duration, offered transcoding names, known codec/bitrate facts and explicit format availability.
- Bounded playback lookup for exposed progressive/HLS transcodings. A denied format stays unavailable while accessible alternatives remain selectable. Signed CDN addresses and supplied query values retain their literal values.
- Restricted tracks and previews are not published as complete downloads. Unknown streaming protocols/types remain explicitly unavailable. Page, API, permalink and media-host boundaries are checked.
- Public playlists use the page's complete ordered ID list. Missing track details are fetched in batches of 50; returned IDs are checked. Duplicate positions remain in order and unavailable items keep their positions. Limits, missing IDs and changed counts do not silently truncate a collection. Streams are resolved from fresh track pages only for selected items when downloading.
- Native archives record `soundcloud <track-id>`. Numeric track/API embed identities can skip before HTTP; ordinary permalink identities are known after page extraction.
- Audio-only HLS formats now ignore video resolution/codec/FPS constraints. Fragmented AAC is saved as M4A and exposed packed MP3 streams as MP3. Audio master playlists retain those rules.

## Live verification on October 8, 2026

The [public Unwritten Stories track](https://soundcloud.com/unwritten-stories/like-this-creative-commons-free-chillout-background-music), ID `303195707`, exposed progressive MP3, MP3 HLS and AAC HLS. One offered adaptive variant denied lookup and remained unavailable; it did not remove the accessible alternatives.

| Native output | Bytes | Duration | Audio frames | Validation |
| --- | ---: | ---: | ---: | --- |
| Progressive MP3 with metadata and cover | 2,928,576 | 182.769252 s | 6,998 | MP3, stereo 44.1 kHz, attached JPEG; full decoding passed |
| AAC HLS, default quality | 3,692,920 | 182.815692 s | 7,874 | AAC, stereo 44.1 kHz; full decoding passed |

The [Steven O'Brien public set](https://soundcloud.com/stevenobrien/sets/creative-commons-favorites), ID `495978294`, was enumerated through the native provider: all 153 ordered entries had available metadata. A native download selecting only entry 1 completed as a 2,546,887-byte M4A: stereo AAC at 44.1 kHz, 126.022200 seconds, 5,428 audio frames, with full decoding passed. Playlist enumeration does not verify playback of every entry.

Deterministic checks cover literal page JSON, metadata, format/client authorization parameters, direct transfer, archive skipping, batched playlist ordering and missing tracks, selected-item transfer, API/widget/share aliases, HTML embed delegation, previews/restrictions, partial format failures, invalid identities/addresses, limits, cancellation and packed MP3 HLS. Generated AAC media verifies both media/master playlists, unchanged native file bytes, actual AAC frames and full decoding without video constraints.

## Remaining work

Other profile feeds (likes/reposts/sets/albums/popular), track stations, alternate collections, pseudo-search syntax and filters, playlist context on track links, true original-file retrieval, private-link shapes, authenticated/subscription workflows, broader renewal/region contracts, older clients, official OAuth/URN API addresses, additional codecs and richer metadata remain pending. The original four SoundCloud rows and the four new profile/permalink/related/search rows have implemented-subset evidence; trackstation remains without key-specific evidence. Registry presence and passing subset tests do not establish full site parity. See AUDIO-INVENTORIES.md for the supported URL shapes and current verification.

The official [SoundCloud developer guide](https://developers.soundcloud.com/docs) and [API reference](https://developers.soundcloud.com/docs/api/explorer/) describe the public API and its authentication model. The website's hydration and guest playback shapes above were observed directly from current public responses, independently of that OAuth API and any third-party extractor implementation. Expiring playback URLs and guest-session data stay outside repository fixtures.
