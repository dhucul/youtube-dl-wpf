# Native Podomatic verification

Native engine 0.92 independently implements current public Podomatic episodes, embedded players and complete public podcasts in C#. No Python, yt-dlp runtime, imported extractor or fallback is used. The frozen `podomatic` key is marked upstream-known-broken in the captured catalog; that historical flag remains unchanged. The native implementation has separately verified working public workflows. Full site/option parity and production WPF cutover remain unfinished.

## Public protocol and scope

Supported inputs include current publisher/timestamp episode pages, older named-subdomain `/entry/` aliases, numeric single-episode embeds, named podcast roots and numeric podcast embeds. Publication timestamps are parsed as valid offset dates; known numeric episode IDs share stable archives across aliases. Private tokens, manual pages, malformed dates and unsupported routes fail explicitly. Dedicated handling prevents generic HTML from bypassing those boundaries, while named Podomatic RSS feeds retain generic support.

The current publisher frontend script was inspected without execution. Its public API uses `/v2` and `~NAME` lookups, rather than appending `.json` to ordinary page URLs. Anonymous read-only responses must report success, contain bounded unique JSON fields, and bind requested episode IDs, collection IDs, publisher names, canonical permalinks, podcast pages and owned RSS links.

The supplied `streams.podomatic.com` media URL retains its literal query and must contain the matching publisher's published-media path. MP3/M4A audio and MP4/M4V/MOV video use fixed local demuxers, full decoding and complete-transfer checks before publication. The API's explicit dynamic-ad flag chooses exact program duration or a program-duration lower bound. Caller credentials are isolated from metadata and playback. Missing public media retains metadata without inventing a format.

Podcast inventories retain source order and unavailable positions. The public listing's declared item/page counts, ownership, duplicate IDs, early termination and configured limits are checked. General metadata posting counts are not substituted for public listing counts. Inspected selections retain original child IDs and positions, refreshing only selected episode/owner metadata. Original retention, metadata/artwork, conversion, video/audio-only processing and archives use the common native pipeline.

The [official sharing guide](https://www.podomatic.com/help-center/articles/podomatic-share-features) describes episode, podcast and embedded players. The [official RSS guide](https://www.podomatic.com/help-center/articles/rss-feed-and-podcast-url) documents publisher feeds and podcast page URLs. Current public HTML, scripts and API responses were inspected on October 9, 2026.

## Verification

Eleven core checks cover public media facts, current/older/embed aliases, URL boundaries, conflicting identities and publisher paths, malformed/duplicate JSON, delivery states, complete/changing inventories, unavailable positions, snapshots, archives, anonymous metadata, cancellation, denial and redirected identity. Eight generated-media checks fully decode source/selected/inspected/converted audio plus full video and selected video/audio tracks, retain exact originals, check archive retries and refuse incomplete source publication without changing prior files or recording success. Libsyn/Megaphone and other existing provider checks remain part of the full regression suite.

Live downloads used [Trailer, Seeing the full picture](https://www.podomatic.com/podcasts/alliancehpsr/episodes/2021-07-27T02_54_01-07_00), stable episode ID `10085572`. Current episode page, single-episode embed, inspected podcast selection and audio conversion fully decoded eight processed/original files. MP3 output was 6,458,281 bytes; its exact original was 6,459,455 bytes. Both contained 9,851 decoded audio frames over 257.306122 seconds and retained the attached JPEG artwork. All 9,851 encoded audio payloads/order and complete decoded PCM matched after metadata processing.

AAC M4A output was 4,545,178 bytes, with 11,082 decoded audio frames over the same duration, retaining the source MP3. Initial jobs reported two completions each; archive repeats reported zero. The native reader completed the six-entry podcast and selected its trailer at original position five. A second public podcast with one entry was also inspected successfully.

The native provider completed all 538 entries declared by [Best SEO Podcast's public listing](https://www.podomatic.com/podcasts/bestseopodcast) across six pages. General podcast metadata reported 544 postings, while its RSS document contained 694 items. Investigation in 0.93 established that the RSS address redirects to Buzzsprout. The native feed route now enumerates all 694 entries and has downloaded the oldest 2009 episode with its legacy GUID intact. See [feed migration verification](PODOMATIC-FEEDS.md). These remain separate publisher views. The test CLI's 45-second inspection attempt canceled; a longer bounded native harness completed the public inventory. Empty public podcasts also report zero pages and are supported without a false incomplete-inventory error.

Scratch evidence is under `work/podomatic-probe` in the Codex workspace, including public responses, live results, packet/PCM comparisons and regression logs. The probe harness and downloaded publisher scripts are not runtime dependencies.

## Remaining work

Custom playlists, account/private workflows, further transcript/chapter formats, other containers and media origins, remaining migration destinations, larger changing-catalog consistency and full site parity remain pending. Audio/video handling is generated-media verified; successful live Podomatic video playback remains unestablished. The captured upstream broken flag does not mean all native Podomatic workflows are complete. Production cutover, old dependency removal and packaged verification remain pending.
