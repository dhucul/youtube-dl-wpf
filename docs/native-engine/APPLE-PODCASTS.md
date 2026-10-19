# Native Apple Podcasts provider

The C# provider recognizes public show and episode links on `podcasts.apple.com`, standard embedded-player links and legacy iTunes podcast aliases. Storefront codes are retained; regionless links use `us`. An explicit `i=<episode-id>` link remains a single episode even when playlist downloading is enabled.

## Implementation

Show links use the [Apple catalogue lookup](https://developer.apple.com/library/archive/documentation/AudioVideo/Conceptual/iTuneSearchAPI/LookupExamples.html) to obtain the publisher feed. The returned kind and numeric show IDs are validated. The complete exposed feed then uses the native RSS/Atom pipeline, including item limits, selection, metadata, snapshots, artwork, available transcript/chapter fields and shared HTTP cookies. The catalogue's limited episode listing is not used as the full show inventory.

Episode links read the current page's `serialized-server-data` JSON directly. The page intent, episode ID, parent show ID, available storefront identity, page context and header playback models must agree. Related shelves are not searched for substitute media. Conflicting models and incomplete pages fail explicitly. Only models declaring free playback and non-preview, unprotected enclosures become usable formats. No page scripts, MusicKit runtime, account creation or additional authentication mechanism is invoked.

Metadata includes the Apple episode ID, show context, title, description text, channel name when present, duration and a bounded artwork-template URL. Known media links use the native transport; opaque links can be resolved from response headers without consuming the discovery body. Common audio MIME aliases now map to the appropriate file extension. Unsupported or inaccessible response types stay unavailable rather than becoming guessed formats.

Explicit episode downloads record `applepodcasts <episode-id>` and can skip aliases before HTTP when that key exists. Show-feed entries retain their publisher GUID and scoped native feed archive identity. Full archive equivalence between those two routes and existing legacy records remains pending.

## Verification on October 8, 2026

The [NASA show](https://podcasts.apple.com/us/podcast/nasas-curious-universe/id1505624059), ID `1505624059`, resolved through the native catalogue/feed path to 109 publisher episodes. The [introductory episode](https://podcasts.apple.com/us/podcast/introducing-nasas-curious-universe/id1505624059?i=1000668639916), ID `1000668639916`, was downloaded both through its explicit episode link and as selected show-feed entry 109.

| Native route | Bytes | Artwork | Media verification |
| --- | ---: | --- | --- |
| Explicit episode | 2,912,373 | 1200×1200 JPEG | MP3, stereo 44.1 kHz, 4,313 audio frames |
| Selected show entry | 3,484,097 | 3000×3000 PNG | MP3, stereo 44.1 kHz, 4,313 audio frames |

Both outputs fully decoded and their decoded audio hashes matched. The served files were 112.666122 seconds; source metadata declared 100 seconds and was retained without truncating the files. The explicit episode info JSON retained its Apple numeric ID.

Deterministic checks verify exact episode selection, publisher-feed completeness/selection, storefront requests, embed/iTunes/regionless aliases, metadata/artwork, numeric archive skipping, unavailable entitlements, previews/protection, wrong/related episode isolation, changed redirects, conflicting models, malformed catalogue/page data, unsafe addresses, cancellation, item limits and header-only discovery for opaque media URLs.

## Remaining work

Channels/search, further URL and page layouts, subscription/authenticated playback, additional video/HLS/DASH/codec workflows, Apple-specific transcripts/chapters, richer metadata, signed URL refresh, preference/locale details, opaque endpoints without usable MIME information, archive equivalence between episode/feed routes, broader regional checks and production WPF integration remain pending. The captured `apple:podcasts` key has partial verified coverage; this does not complete the overall replacement.

Primary API background: [Apple Search API overview](https://developer.apple.com/library/archive/documentation/AudioVideo/Conceptual/iTuneSearchAPI/). The current episode JSON structure was observed from public Apple pages; fixture data and extraction logic were authored independently in C#.
