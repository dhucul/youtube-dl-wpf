# Native Vocaroo public recordings

Verified October 11, 2026, with native engine **0.114.0**. Captured key `Vocaroo` has a verified public subset. Full site/option parity and production cutover remain unfinished.

## Implemented behavior

Independent C# discovery supports current and legacy recording gateways, public recording pages, `voca.ro` short links, historical `/i/` page aliases, selected embedded players and direct MP3 resource aliases. Recording IDs remain case-sensitive. Supported published ID layouts are ten or eleven alphanumeric characters and twelve-character current IDs beginning with `1`. The public client declares separate current and legacy MP3 gateways; native discovery binds those declarations to the requested ID without executing publisher JavaScript or downloading client code at runtime.

A bounded page request must retain the requested recording identity and expose one literal configuration with the known HTTPS gateways. Audio discovery then uses a read-only HEAD request, requiring a complete MP3 media type, exact positive byte count and a filename/storage descriptor bound to that recording ID. Conflicting identities, gateway changes, ambiguous headers, wrong page/resource redirects, partial responses, missing resource descriptors, invalid digests, unavailable objects and oversized/executable/duplicate configuration fail explicitly. Unsupported routes, owner/access selectors, fragments, credentials, nondefault ports and unknown gateways cannot fall through to direct or generic extraction.

When the publisher supplies its storage SHA-1 digest, the complete downloaded original is checked against that digest and declared byte count before processing. SHA-1 is used as the publisher's supplied object-integrity descriptor; it is not a new authentication scheme. The native fixed local MP3 demuxer then fully decodes a nonempty audio timeline before transactional publication. No duration, upload date, author or longer program is invented when the public descriptor does not supply that fact. This verifies the complete posted MP3 object, not an unavailable original upload in another format.

Anonymous discovery and playback isolate caller authentication. The provider uses public page/HEAD/media requests; upload, deletion and ownership APIs are not called. A referrer accompanies descriptor/playback access. Native metadata processing, audio conversion, exact original retention and existing-output/archive/callback protection retain ffmpeg. Archives use `vocaroo` plus the case-sensitive recording ID, and known archived aliases skip before the HTTP session. No Python, yt-dlp, publisher-script execution or hidden fallback is used by these workflows.

## Verification

Fourteen core checks cover descriptors/facts, current/legacy/short/embed/direct aliases, ten-character routing, URL boundaries and direct/generic reservation, gateway declarations, literal-only configuration, filename/storage/digest conflicts, missing sizes/types/identity descriptors, changed redirects, denial/partial responses, metadata-only simulation, stable archives, anonymous isolation, cancellation and document limits.

Eight generated-media checks verify current/legacy/embed/direct MP3 and AAC conversion, complete decoding, exact originals/unconverted PCM and archives, refusal of a different valid same-size recording by digest, incomplete-object protection and cancellation. The same-size replacement is independently decoded as valid before testing refusal, so digest verification is exercised separately from decoder rejection. One new app check verifies analysis and format listing retain native recording/format facts without fetching audio.

The Release solution build passed with zero warnings/errors. All **1,726 native checks** passed: 1,282 core and 444 optional media checks. All **65 app checks** passed, including four optional browser-tool checks. The Vocaroo-focused run passed 29 checks, including the runner's seven common checks.

Live testing used two publicly accessible recordings, [current `1lZ1ZZto8FgC`](https://vocaroo.com/1lZ1ZZto8FgC) and [legacy `imPgJC013AW`](https://vocaroo.com/imPgJC013AW).

| Live workflow | Processed output | Retained original | Decoded audio frames |
| --- | ---: | ---: | ---: |
| Current recording gateway | 4,392,646-byte MP3 | 4,406,269-byte MP3 | 7,870 each |
| Legacy recording gateway | 77,043-byte MP3 | 77,013-byte MP3 | 364 each |
| Historical page alias | 77,043-byte MP3 | 77,013-byte MP3 | 364 each |
| Short sharing link | 77,043-byte MP3 | 77,013-byte MP3 | 364 each |
| Selected embedded player | 77,043-byte MP3 | 77,013-byte MP3 | 364 each |
| Direct MP3 alias | 77,043-byte MP3 | 77,013-byte MP3 | 364 each |
| M4A conversion | 117,368-byte AAC M4A | 77,013-byte MP3 | 204 AAC / 364 MP3 |

All fourteen files independently decoded completely with no decoder diagnostics. Current audio reported approximately 188.84 seconds at 48 kHz stereo; legacy audio and converted M4A reported 26.09 seconds at 8 kHz mono. Every unconverted processed/original PCM SHA-256 pair matched exactly. Retained originals matched observed storage lengths and SHA-1 digests; SHA-256 file hashes are also retained in the independent evidence. Each initial job emitted two completions, and every archive repeat made no discovery or media requests and emitted no files/callbacks.

The seven workflows used the native WPF adapter with isolated saved settings. A read-only observer recorded 21 actual page requests, 21 HEAD requests and seven actual media requests without supplying fixtures at runtime. Native analysis and format listing resolved the same recording and one complete MP3 format in each workflow.

Protocol references are the publisher's current public pages, embedded-player markup, declared gateway configuration and [published client](https://cdn.vocaroo.com/bundle.f8f0b68782166d564c33.js), inspected October 11. Implementation is independently written C#. Evidence, source hashes and build/regression/decode logs are under `work/vocaroo-probe`, `work/vocaroo-stage` and `work/vocaroo-live` in the `we-ran-out-of-context-window` workspace. Scratch evidence is not a runtime dependency. User settings/history/media and existing uncommitted work were preserved. No Git commit or push was performed.

## Remaining work

Other historical/future ID layouts, additional player selectors/locales, further storage generations and digest formats, publisher frame-count metadata, availability/expiry/cache variations and complete upstream site/app-option interactions remain pending. Ten-character routing is fixture-verified against the published client; the live samples verify current twelve-character and legacy eleven-character objects. The byte/digest and full-decoding evidence does not establish unsupported variants.

Eighteen configured settings remain explicitly refused by the native adapter. Full catalog/default/partial-option parity, native WPF startup/cutover, yt-dlp installer/runtime/fallback removal and packaged verification without Python or yt-dlp remain unfinished. Isolated native adapter verification is not packaged WPF verification.
