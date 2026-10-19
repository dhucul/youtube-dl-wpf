# Native Mixcloud verification

Native engine 0.95 independently implements public Mixcloud shows, creator uploads, explicit playlists and widgets in C#. It uses no Python, yt-dlp runtime, imported extractor or fallback. The captured `mixcloud`, `mixcloud:user` and `mixcloud:playlist` keys remain partially covered. Production WPF cutover and full site/option parity remain unfinished.

## Public protocol and scope

Supported inputs include canonical/API show URLs, creator roots/uploads and named playlists, plus public widget feed aliases. Requested REST keys, owner names and show slugs must agree with playback metadata. Anonymous read-only playback queries use the application's own identification. Public metadata and selected media isolate caller credentials. Private tokens, manual pages, unrelated feeds, live routes and unsupported track-only profile filters fail explicitly without generic whole-show fallback.

The current public widget script was inspected without execution. Its clear stream fields contain Base64/XOR-obfuscated URLs; the C# implementation performs that same data decoding and preserves resulting literal URLs and signatures. This is separate from DRM playback. Exclusive, copyright-disabled, exclusive-preview-only and DRM-only responses are refused. No DRM keys or license service are used. Clear progressive AAC/M4A, static audio HLS and segmented DASH use local fixed-demuxer decoding, complete transfer, media-role and declared-duration checks before publication.

Creator/playlist inventories use public REST connections with declared counts, bounded offsets and scoped supplied continuation URLs. Wrong owners, duplicate creator uploads, changed counts, malformed pages, premature termination and configured limits fail explicitly. Playlist repetitions retain source positions. A final collection identity/count recheck precedes completion. Inspected selections preserve original identities and refresh only chosen playback. Stable canonical show-key archives skip repeated playback discovery.

The [official REST documentation](https://www.mixcloud.com/developers/) describes anonymous reads, canonical keys and pagination, while [the widget guide](https://help.mixcloud.com/hc/en-us/articles/360004031440-Embedding-content-using-the-Mixcloud-widget) describes shows, tracks, creator uploads and playlists. Publisher widget code and current public responses were inspected on October 10, 2026. The documented REST API does not itself expose direct audio links; playback observations come from the public widget's read-only service.

## Verification

Twelve core checks cover public facts, widgets/API aliases, URL and access boundaries, REST/playback identity conflicts, previews/rights/DRM-only refusal, clear source decoding, adaptive descriptors, complete collections, scoped cursors, repeated playlist positions, snapshots, archives, anonymous identification, cancellation, denial and redirects. Seven generated-media checks fully decode progressive/selected/inspected/converted and clear HLS/DASH audio, retain originals, verify archives and reject incomplete source publication without changing prior files or recording success.

Live workflows used [HAILE JAH Ras Tafari and Empress Menen INI](https://www.mixcloud.com/kandakekweensize/haile-jah-ras-tafari-and-empress-menen-ini-performed-composed-written-produced-by-diana-music/). Direct and widget downloads and inspected creator selection published 491,993-byte processed M4A files with exact 491,988-byte originals. Each contained 2,581 decoded AAC frames over 59.925011 seconds. Independent packet checks confirmed all 2,583 encoded audio payloads/order and exact decoded PCM retained after metadata processing. Conversion produced a 567,903-byte AAC M4A with the original retained and full decoding passed.

Clear HLS published a 607,428-byte processed TS and 537,680-byte original, with 2,583 decoded AAC frames and the supplied timed-ID3 stream retained. Clear DASH published 491,756-byte processed M4A and 502,691-byte original, also 2,583 decoded AAC frames over about 59.9719 seconds. All twelve files across six workflows fully decoded. Each successful initial job reported two completions; archive repeats zero.

The native creator inventory completed all 14 uploads and selected the test track at original position eight. The inspected public playlist contained all nine entries. The verified clear sources coexist with offered DRM variants, which were excluded. A browser-style default identification was rejected for the playback request; the app's own identification was accepted. Earlier scratch runs also referenced an older copied engine assembly; later verification checked matching assembly hashes before playback.

Scratch evidence is under `work/mixcloud-probe` in the Codex workspace. Publisher scripts, response captures and the probe harness are not runtime dependencies; signed resource queries should not be copied into diagnostic output.

## Remaining work

Exclusive/account/licensing workflows, DRM-only playback, live recording, other profile feeds and media origins, larger changing inventories, extra tracklist/chapters and full site/option parity remain pending. Production cutover, old dependency removal and packaged verification remain pending.
