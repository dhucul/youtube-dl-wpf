# Native Reddit video handling

The C# provider now parses Reddit post-video responses and routes offered media through the native HLS/DASH/file pipeline. Post discovery is fixture-verified; live post discovery remains blocked by Reddit's access controls in this environment. Explicit public media manifests have been downloaded and fully decoded separately.

## Implemented scope

- Post URLs on standard, old, new, mobile and embed Reddit hosts, post JSON aliases, and `redd.it` post shortlinks. Post identifiers and comment-versus-post path boundaries are checked. Unsupported feeds, share-token links, comment videos, fragments and media-affecting query options fail explicitly.
- One bounded JSON post listing from the canonical comments endpoint, using `raw_json=1` and no requested comment expansion. The native application user agent is used unless the caller explicitly supplies one. HTTP settings/cookies are shared; redirects, ambiguous listings and mismatched post IDs/fullnames/permalinks are rejected. Access denial does not trigger alternate origins, clients or unauthenticated retries.
- Public `secure_media.reddit_video` / `media.reddit_video` descriptors, title, author, self-text, duration, preview images and declared fallback measurements. Conflicting descriptors or media asset identities fail. Dimensions from the fallback are not assigned to every manifest rendition.
- Offered HLS/DASH URLs take priority over the fallback MP4. A normal Reddit fallback is represented as video-only; it cannot silently satisfy a complete video/audio request. An explicit video-only request can use it. When Reddit explicitly marks the resource as GIF-like, the complete silent MP4 may download by default and still remains unavailable to audio-only selection.
- Bounded single-parent crosspost chains with verified parent identities. The requested post's title and archive identity are retained for native Reddit media. Linked external videos delegate to an already implemented native provider. Galleries, mixed-media posts, processing/live sources and removed/private-source workflows remain unsupported.
- Explicitly supplied `v.redd.it` MP4/HLS/DASH resource URLs retain their literal query strings. These do not fetch post metadata or guess missing sibling resources. A bare `v.redd.it` redirect link still requires its post URL or a supplied resource URL.
- Native post archives use `reddit <postId>` after discovery. Direct resources use `reddit:video <assetId>` and can skip before HTTP. External delegated videos use the destination provider's identity. Full equivalence with historical upstream archive keys is not yet established.

## Video-constraint correction

Live testing exposed a shared selection defect: when a DASH height/codec constraint removed every video representation, the remaining audio representation could make the source appear to be an audio-only item. The engine now classifies the source before filtering and refuses an implicit audio downgrade. Explicit audio-only requests and genuine audio-only sources remain supported.

The provider also supplies an explicit complete-media fact for a known silent GIF-like resource. This permits complete silent videos without weakening the missing-audio checks for ordinary adaptive video tracks.

## Verification on October 8, 2026

Eleven deterministic checks cover metadata/source identities, manifest preference, literal parameters, silent versus video-only media, URL aliases, crossposts, external delegation, unavailable/invalid data, access denial, archives/cancellation and constrained-video rejection. Two generated-media checks fully decode normal HLS video/audio and byte-identical silent MP4, preserving all ten generated video frames and the intended audio presence.

The integrated native provider made one metadata request for [post `1uwzmbi`](https://www.reddit.com/r/aww/comments/1uwzmbi/this_is_an_amazing_dream/) and received HTTP 403. A separate normal-page diagnostic returned a verification challenge. Neither established post extraction; no challenge completion, client rotation or authenticated access was attempted.

The post's publicly linked media asset `u5pu7ad5rcdh1` was tested using explicit HLS and DASH manifest URLs. A maximum height of 360 correctly failed after one DASH manifest request with zero audio/video requests and no published output: the HLS inventory shows its smallest video rendition is 220x392.

With a maximum height of 480, each native manifest path completed in 13 HTTP requests and produced a 739,508-byte MP4: H.264 at 270x480, 381 video frames, AAC with 591 audio frames, duration 12.700000 seconds. Both files fully decoded. Their container hashes differ, while decoded RGB-video and PCM-audio hashes match. This verifies public direct-media playback, not access to Reddit post metadata.

## Remaining work

Legitimate API/account authorization and live post discovery, current HTML/player layouts, bare media/share redirects, comment videos, galleries/mixed media, feeds/collections, broader crossposts, captions/chapters, source refresh, more formats and full archive/settings parity remain pending. API availability and distribution requirements need to be resolved before release. The captured `Reddit` extractor key has partial coverage; production WPF cutover remains pending.

Primary references: [Reddit API documentation](https://www.reddit.com/dev/api/), [Reddit video descriptor](https://developers.reddit.com/docs/api/redditapi/models/type-aliases/RedditVideo), and [current API access documentation](https://support.reddithelp.com/hc/en-us/articles/16160319875092-Reddit-Data-API-Wiki). Fixtures use invented post/media identities and contain no real credentials.
