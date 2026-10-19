# Native iHeartRadio verification

Native engine 0.97.0 independently implements public iHeartRadio podcast audio, episode and widget URL aliases, complete public episode inventories and inspected selections in C#. It uses no Python, yt-dlp executable/extraction code or composite fallback. The frozen `iheartradio` and `iheartradio:podcast` keys remain partially covered. Production native WPF startup and full catalog/option parity remain unfinished.

## Protocol and scope

Public numeric and SEO podcast/episode paths resolve the same stable IDs. The `embed` and light/dark `theme` parameters are UI aliases, while account, manual-pagination, fragment and unsupported station paths fail explicitly. Known unsupported iHeart website paths cannot fall through to generic discovery. Generic iframe dispatch is fixture-verified; the embed URL itself is live-verified.

The normal public website advertises its regional API in literal indexed router data. A bounded C# reader resolves only the needed loader/config fields and validates the API origin. It never evaluates publisher JavaScript or executes/fetches client bundles at runtime. Website and API redirects must retain the requested podcast/episode identity; API responses must bind exact numeric episode and owner IDs. The current publisher client was inspected as protocol evidence during development, rather than imported into the engine.

The live Canadian website advertised `ca.api.iheart.com`; native discovery followed that origin without substituting a different region. Public requests use an empty cookie jar and omit common authorization/cookie headers, while caller network settings and ordinary headers remain available. Account and service-specific authentication behavior is not established by these public checks.

Episode detail supplies an offered media URL, MIME type, publication bounds and program duration. Complete clear MP3, M4A and ADTS audio use fixed local decoding with audio-role, nonempty-completion and minimum program-duration checks before publication. Dynamic delivery may be longer than the listed program; a short source is refused. Literal tracking/source URLs and queries are retained. Upcoming/expired, flagged private/premium/preview/live/protected, mixed/video descriptors and unsupported requested transcripts fail explicitly.

Collections follow the public client's descending-date cursor contract until the server returns its terminal links state. Tokens are opaque, bounded and encoded as values on the same parent API path. Duplicate identities/cursors, wrong owners, backwards page ordering, empty continuing pages, excessive inventories/data and changed first-page/podcast identities fail explicitly. Unavailable entries retain original positions. There is no invented total count: this API signals completion through pagination, and broader mutable/backdated inventory behavior remains pending.

Inspected selections retain original episode identity/position and freshly resolve only selected playback. Stable `iheartradio` episode-ID archives skip already completed direct and selected jobs. Originals, metadata, conversion and completion callbacks use the shared publication pipeline.

The [official widget guide](https://help.iheart.com/hc/en-us/articles/115000879272-How-do-I-add-iHeartRadio-to-my-website) documents podcast and episode embeds. API/layout details above come from current publisher website/client/API observations on October 10, 2026. [FFmpeg's format documentation](https://ffmpeg.org/ffmpeg-formats.html#aac) identifies the AAC demuxer; the shared verifier now allows that fixed local demuxer with its existing file/pipe protocol restriction.

## Verification

Fourteen new core checks cover facts, URL aliases and boundaries, literal hydration without execution, conflicting IDs/origins, access/publication states, source/type/transcript refusal, terminal/opaque pagination, duplicates/changes/limits/order, unavailable positions, inspected selections, archives, cancellation, denial and common credential isolation.

Eight generated-media checks cover MP3, selected/inspected audio, conversion, delivery longer than program duration, M4A, ADTS best-audio copying and short-source refusal. They fully decode outputs, compare original bytes and unconverted decoded samples, check episode/position JSON and archives, and preserve prior output on failure. ADTS verification exposed a missing fixed-demuxer allowance in the shared engine; adding it permits complete decoding, with sample-preserving conversion verified.

The Release solution build passed with zero warnings/errors. All **1,420 native checks** passed: 1,070 core and 350 optional media checks. All **49 app checks** passed: 45 core and four browser checks.

Live media was the public [Trailer Park teaser](https://www.iheart.com/podcast/112696666/episode/315219505/) and its [podcast inventory](https://www.iheart.com/podcast/112696666/). The native inventory completed 57 records across three API pages; the selected teaser retained original position 11 and its declared 66-second program.

| Workflow | Processed file | Original | Complete decoding |
| --- | ---: | ---: | --- |
| Episode | 1,063,078-byte MP3 | 1,062,972-byte MP3 | Both 2,542 MP3 frames, 66.376553 s |
| Embed alias | 1,063,078-byte MP3 | 1,062,972-byte MP3 | Both 2,542 MP3 frames, 66.376553 s |
| Inspected position 11 | 1,063,078-byte MP3 | 1,062,972-byte MP3 | Both 2,542 MP3 frames, 66.376553 s |
| AAC conversion | 1,583,455-byte M4A | 1,062,972-byte MP3 | 2,859 AAC frames and 2,542 MP3 frames, 66.376553 s |

All eight files independently decoded completely. All 2,542 encoded MP3 payloads and their order were retained after metadata processing, and unconverted processed/original decoded PCM hashes matched exactly. The single/embed/inspected files were byte-identical across those workflows. Conversion retained the exact original source. Each initial job reported two completions; each archive repeat succeeded without files or callbacks.

Scratch captures, publisher bundles, probe code and evidence are under `work/iheart-probe` in the October 10 Codex workspace. They are not runtime dependencies. Existing user settings, history, media and managed engines were preserved. No Git commit or push was performed.

## Remaining work

Video/multiple descriptors, transcripts and chapters, accounts/subscriptions, stations/live/music paths, older website/widget layouts, other regions, large changing/backdated inventories and complete site/option parity remain unfinished. Requested unsupported workflows stay explicit. M4A and ADTS are generated-media verified; the eight live files establish the public MP3/conversion subset described here.

Production WPF still uses yt-dlp. Removal of its installer/runtime/fallback paths and final packaged application verification without Python or yt-dlp remain pending. The native probe is not a packaged WPF application test.
