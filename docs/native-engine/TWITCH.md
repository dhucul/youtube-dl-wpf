# Native Twitch clips

The independent C# provider implements public Twitch clip metadata/playback and the channel clip-listing protocol. It uses bounded JSON requests to Twitch's current public GraphQL endpoint. The public WWW client identifier was observed in [Twitch's own shipped auth settings](https://assets.twitch.tv/assets/21956-bfb849d1a8b3b8e8f494.js) on October 8, 2026. It is pinned configuration that may require maintenance, not an account secret. No website scripts, Twitch SDK, browser profile or third-party extractor runtime is used.

## Implemented behavior

- Canonical `clips.twitch.tv` links, current channel `/clip/<slug>` links, legacy channel/slug links and the standard clip embed query. Generic HTML delegates recognized clip embeds to this provider. Live-stream, VOD and other collection links are not advertised as supported.
- Exact clip slug and returned numeric-identity validation, title, creator/channel display, duration, thumbnail and offered qualities. Positive frame rates and numeric heights are retained. A zero frame rate is reported as unknown; it does not satisfy a strict FPS filter. The current `/avc/` and `/hevc/` asset routes provide explicit codec labels; older routes may omit codec facts.
- Player-issued playback tokens retain their original JSON bytes when URL-encoded. Clip binding, explicit playback permission, expiry and media origins are checked. Existing unrelated query values are preserved. Denied, expired or absent authorization does not expose downloadable formats.
- Metadata queries disable automatic redirects while sharing the original HTTP cookies, headers, proxy and timeouts. Media transfer retains the normal native download behavior, including selected quality, staging, processing and cancellation.
- Channel clip listings support the all-time, 24-hour, 7-day and 30-day ranges with opaque cursor pagination. Channel identities, repeated clips/cursors, missing page state and configured limits are checked. Playback is resolved only for selected entries after complete enumeration.
- An API integrity challenge is terminal for the operation. A partial prefix is not returned as a complete listing, and no client rotation or repeated session creation is used to accumulate results past a denial.
- Archives use `native:twitch:clips <slug>` and can skip clip URL aliases before HTTP. The namespace deliberately avoids claiming unverified equivalence with older yt-dlp Twitch archive keys.

## Verification on October 8, 2026

Public clip [`CloudySarcasticSashimiTwitchRPG`](https://clips.twitch.tv/CloudySarcasticSashimiTwitchRPG) from the Twitch channel completed through `NativeEngine.DownloadAsync` with a 360p/H.264 constraint, metadata, artwork and info JSON. The final MP4 was 1,546,172 bytes and fully decoded: H.264 at 640×360, 30000/1001 fps, 569 video frames; stereo AAC at 44.1 kHz, 816 audio frames; and a 1920×1080 attached JPEG. The served container duration was 18.986003 seconds while the API declared 18 seconds; the file was not shortened to the rounded metadata value.

The public TwitchDev clip listing returned 46 entries and a continuation cursor on its first page. The second page returned `IntegrityCheckFailed`. The native provider reproduced the explicit challenge failure and did not claim complete enumeration. **Complete live channel-list downloading remains unverified.**

Deterministic checks cover quality/FPS/codec facts, literal token/query handling, clip identity, aliases and embeds, authorization denial/expiry, invalid origins, scoped archive skipping, pagination, selected-only playback, unsupported ranges, changed owners, missing state, loops, item limits, challenge refusal and cancellation.

## Remaining work

Legitimate integrity-challenge completion and full live listing verification; live streams, VODs, collections, other video listings; authentication/subscriber/regional workflows; token refresh, richer metadata/captions/chapters; probing unknown format facts; further codec/asset shapes; legacy archive mapping; public-client maintenance; and production integration remain pending. Captured keys `twitch:clips` and `twitch:videos:clips` have partial coverage. The other Twitch catalog keys are unfinished.

Primary platform references: [Twitch clip/video embedding](https://dev.twitch.tv/docs/embed/video-and-clips/) and [Twitch API reference](https://dev.twitch.tv/docs/api/reference/). Current playback fields and access behavior were observed directly from Twitch's public client and service responses; this implementation does not substitute the authenticated Helix API contract for the website's guest protocol.
