# Consumer delegation and Kaltura DASH

Version 0.132.0 implements scoped public article delegation for Business Insider and Al Jazeera, plus validation of signed DASH resource expiry and selection of fresh Kaltura profiles. This is partial evidence for two exact frozen catalog keys; full provider, option and replacement parity remains unfinished.

## Article identity and delegation

Business Insider reads one bounded literal `fenrirClientData` object from the publisher's `fenrir-client-data` script. The post ID and path must match the requested article. A visible primary hero video and an explicitly exempt public paywall state are required. Only the primary relationship's eight-character JW asset ID delegates to JWPlatform. Related videos, advertising configuration and narration audio do not become the article's video.

Al Jazeera reads one strict UTF-8/base64 Apollo state assignment without executing JavaScript. Exactly one publisher-origin post link must match the current article path. Its primary video reference must resolve to a matching Brightcove video/account/player record. Related post references are ignored. Live posts, external articles and incomplete identities fail explicitly.

Both publishers use exact host boundaries. Redirects cannot change publisher families. The child provider must return the expected hosted archive identity before media transfer. The article's referrer reaches the issuer; consumer provenance accompanies the hosted metadata. Hosted credentials use the existing exact provider/account/media scope and are supplied only to the matching issuer. Tokens stay out of article and media requests. A delegated archive repeat can read article/issuer metadata to establish identity, then skips media and callbacks.

Listings, paid account flows, dynamic or missing state, and live consumer workflows are not covered by these article contracts. Unsupported pages do not silently become generic unrelated-video downloads.

## Kaltura DASH profiles and signed links

The public partner `309`, entry `1_rcit0qgs` offers distinct DASH profiles. Earlier diagnostic responses from profile `4662` advertised Akamai `__hdnea__` resource links that expired before verification and returned fragment denials. Cache-control and cache-query diagnostics retained the expired tokens. Later unmodified issuer URLs returned fresh tokens and readable fragments from `kalhlspd-a.akamaihd.net`. The separately offered profile `12972` redirects to Kaltura's `cfvod.kaltura.com` and also supplies readable clear MP4 resources. Both profiles completed actual public playback; the expired diagnostic response remains recorded separately.

The engine reads numeric `expire`/`expires` hints and standard Akamai token `exp` fields without changing, stripping, verifying or creating the signature. Missing, duplicate, malformed or out-of-range expiry fields fail. Initialization, segment, progressive and caption/period resource graphs are checked before registration. An expired graph cannot contaminate another offered graph's subsequent renewal.

When automatic Kaltura DASH selection encounters an already expired resource graph, it can inspect at most eight other DASH profiles from the same issuer response before media transfer. Explicit format selectors retain their requested profile. HTTP denials, protected media and failures after transfer do not trigger profile switching. All-expired inventories fail without completed publication, archive advancement or callbacks. Selected fresh profiles retain the existing scoped issuer-owned renewal behavior.

## Verification

The batch adds 26 core and 12 generated-media checks. They cover primary/related article identity, exact origins, redirect boundaries, paid/live refusals, duplicate or computed state, delegated credential/referrer scope, token expiry, alternatives, renewal and protected failure behavior. Positive generated downloads retain all 36 video frames and every ordered coded video/audio source packet, along with complete originals, callbacks and hosted archive identity.

The complete Business Insider source exposed transport decode clocks later than reordered pictures' presentation clocks. Ordinary stream-copy metadata processing changed picture timing. TS/M2TS metadata and remux inputs now retain presentation clocks and let ffmpeg derive decode clocks. New tests cover both already valid and deliberately conflicting decode clocks, checking every relative packet presentation time and payload after metadata/remux processing. The public recording was repeated through the corrected adapter in a fresh folder; previous diagnostic outputs remain preserved.

All **2,064 native checks** (1,509 core plus 555 media), **74 app checks** and **45 focused checks** passed. The Release solution build reports zero warnings and errors. Four actual native WPF-adapter workflows produced fourteen independently fully decoded outputs/sources and fourteen exact ordered main/original/component packet inventories:

| Workflow | Main bytes | Original bytes | Source seconds | Video frames | Audio frames |
| --- | ---: | ---: | ---: | ---: | ---: |
| businessinsider | 40059040 | 38057592 | 899.245 | 21581 | 38725 |
| aljazeera | 2179950 | 2179853 | 30.101 | 751 | 1411 |
| kaltura-auto | 7297974 | 7297874 | 124.162 | 2975 | 5347 |
| kaltura-explicit | 7297974 | 7297874 | 124.162 | 2975 | 5347 |

Business Insider retained exact relative video packet presentation times; audio timing differs by at most two 90 kHz ticks after corrected metadata processing. the complete 899-second output now fully decodes with strict decoder checks and its ordinary presentation timeline. Al Jazeera retained 1,699,993-byte video and 484,249-byte audio components. Each Kaltura profile retained 6,203,585-byte video and 1,110,870-byte audio components. Consumer archive repeats made two/three identity metadata requests and no media/callbacks. Direct Kaltura repeats skipped before HTTP. Public default profile4662 was already fresh; automatic expired-alternative selection, explicit stale refusal and forced renewal are separately verified with controlled protocol/generated-media cases.

## Remaining work

- More exact consumer keys and independently specified account, regional, collection, live and embed variants.
- Additional hosting service profiles, advanced DASH addressing/containers, changed-track contracts and durable recovery.
- Real private accounts, publisher signing and sustained remote renewal remain explicitly user-deferred.
- Full option/default/catalog compatibility and native production packaging remain open. Production startup continues to use the existing yt-dlp engine until the original replacement acceptance gates are met.
