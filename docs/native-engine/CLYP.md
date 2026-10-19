# Native Clyp public audio

Verified October 11, 2026, with native engine **0.115.0**. Captured key `Clyp` has a verified public subset. Full site/option parity and production cutover remain unfinished.

## Implemented behavior

Independent C# discovery uses the current public recording API for eight-character canonical IDs. Public pages, API metadata addresses, selected widgets, supplied MP3/Ogg stream addresses and permitted download aliases retain one recording identity and stable archive record. Tracking aliases and supported autoplay choices do not alter the selected audio. Private token/access selectors, unsupported vanity/collection routes, credentials, fragments, nondefault ports and other formats cannot fall through to direct or generic discovery.

The API response must have one unambiguous metadata object, matching recording ID and public-page identity, supported public availability, a complete positive duration, a title and supplied secure MP3/Ogg URLs bound to that recording's published storage paths. Public `DownloadDisabled` records retain their explicit false download-permission flag while exposing the full streams offered by the site's player. Explicit website/API download-gateway aliases require `Public` download permission. Private, private-download-disabled, deleted, distribution-disabled and unknown states fail; no owner, upload, deletion or premium endpoint is called.

Selected widget discovery reads bounded literal `createPlayer` data without executing JavaScript. Widget IDs, duration, public access, page/widget links and supplied media URLs must agree with fresh API metadata. Duplicate/ambiguous/executable expressions and conflicting player facts fail. Each selected format's HEAD response must retain the supplied resource identity, complete positive byte count and supported audio type. Missing, changed, redirected, partial or unavailable descriptors fail explicitly.

Complete native transfers are checked against declared original byte counts and fully decoded with fixed local MP3/Ogg demuxers before transactional publication. The declared program duration supplies the normal full-duration guard plus a 0.15-second minimum-duration tolerance for codec boundaries; a source 0.4 seconds short is refused even when it would pass the shared broad tolerance. Metadata facts retain the published title, description, duration and UTC creation date. Exact originals, metadata processing, audio conversion, cancellation, protected prior output and callbacks use the shared native engine and ffmpeg.

Anonymous discovery/playback isolates caller authentication. Archives use `clyp` plus the canonical recording ID and skip known aliases before the HTTP session, including all discovery and media requests. No Python, yt-dlp, publisher-script execution or hidden fallback is used by these workflows.

## Verification

Fourteen core checks cover facts/dates/formats, canonical/API/widget/stream/download aliases, URL boundaries and generic/direct reservation, public playback versus download permission, private/unavailable states, conflicting IDs/canonical/storage URLs, duplicate/incomplete metadata, widget identity/access/duration/source bindings, literal-only player data, changed descriptors/redirects/media types/sizes, denial, missing formats, simulation, stable archives, anonymous isolation, cancellation and document limits.

Eight generated-media checks verify full MP3/Ogg/widget/direct/permitted-download/conversion workflows, exact originals and unconverted PCM, archive skips, refusal of subtly incomplete audio and cancellation with prior-file/archive/callback protection. One app check verifies native analysis/format listing retain source identity, date and public format facts without transferring audio.

The Release solution build passed with zero warnings/errors. All **1,748 native checks** passed: 1,296 core and 452 optional media checks. All **66 app checks** passed, including four optional browser-tool checks. The Clyp-focused run passed 29 checks, including the runner's seven common checks.

Live testing used the website's own “See Example” recording, [A place for us](https://clyp.it/aw2jlf0j). Its public API and widget both declared 127.922 seconds and matching complete MP3/Ogg resources. The source's `DownloadDisabled` state remained distinct from its public player streams; private and download-gateway boundaries were verified separately with fixtures.

| Live workflow | Processed output | Retained original | Decoded audio frames |
| --- | ---: | ---: | ---: |
| Public MP3 | 2,046,816-byte MP3 | 2,046,746-byte MP3 | 4,897 each |
| Selected Ogg | 1,327,427-byte Ogg | 1,332,436-byte Ogg | 8,800 each |
| Selected widget | 2,046,816-byte MP3 | 2,046,746-byte MP3 | 4,897 each |
| API metadata alias | 2,046,816-byte MP3 | 2,046,746-byte MP3 | 4,897 each |
| Direct Ogg stream alias | 1,327,427-byte Ogg | 1,332,436-byte Ogg | 8,800 each |
| HTTP/www page alias | 2,046,816-byte MP3 | 2,046,746-byte MP3 | 4,897 each |
| AAC M4A conversion | 2,068,141-byte M4A | 2,046,746-byte MP3 | 5,510 AAC / 4,897 MP3 |

All fourteen files independently decoded completely with no decoder diagnostics, at 44.1 kHz stereo. MP3 and M4A reported approximately 127.92 seconds; Ogg reported 127.87 seconds. Every unconverted processed/original PCM SHA-256 pair matched exactly. Retained originals matched independent source-file SHA-256 hashes and observed complete byte counts. Initial jobs emitted two completions each. Archive repeats made no HTTP requests and emitted no outputs/callbacks. Source recording ID and creation date remained in info JSON.

The workflows used the native WPF adapter with isolated saved settings and a read-only observer of actual .NET HTTP responses, without runtime fixtures. The observer captured 21 API metadata GETs, three widget GETs, 39 HEAD requests and seven media GETs. Native format analysis resolved the same recording and supplied formats throughout.

The publisher's [API documentation](https://clyp.it/api), current [public example selection](https://clyp.it/example-url), player markup and [published client](https://static.clyp.it/site/js/global-1.0.87.js) were inspected October 11. The documentation's old recent-list endpoint returned 404, so it was not used to establish a complete inventory. Implementation is independently written C#. Source hashes, build/regression logs, actual request observations and independent decode/hash evidence are under `work/clyp-probe`, `work/clyp-stage` and `work/clyp-live` in the `we-ran-out-of-context-window` workspace. Scratch evidence is not a runtime dependency. Existing user settings/history/media and uncommitted work were preserved; no Git commit or push was performed.

## Remaining work

Vanity IDs, playlists/collections/search, private/token/account authentication, premium/lossless/original-upload resources, additional player selectors/artwork/metadata, further storage/layout variants, live permitted-download states and complete site/app-option interactions remain pending. The single public example and fixture boundaries do not establish those workflows.

Eighteen configured settings remain explicitly refused by the native adapter. Full catalog/default/partial-option parity, native WPF startup/cutover, yt-dlp installer/runtime/fallback removal and packaged verification without Python or yt-dlp remain unfinished. Isolated native adapter verification is not packaged WPF verification.
