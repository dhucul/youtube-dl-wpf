# Native TikTok public videos

The C# provider reads the current `__UNIVERSAL_DATA_FOR_REHYDRATION__` video-detail JSON, with a bounded reader for legacy `SIGI_STATE` data. It selects only the requested numeric video ID; related items and background music are not substitute downloads. No website scripts, TikTok SDK, browser profile or third-party extractor is executed.

## Implemented scope

- Public watch URLs, numeric player/embed/legacy video aliases and HTTP redirects from standard short-share links. Profile, photo, live and collection URL shapes remain unsupported.
- Video ID, caption/title, creator, duration, artwork and exposed MP4 variants. Actual width/height, positive frame rate, bitrate, declared size and recognized H.264/H.265/AV1 labels are retained. Portrait dimensions are not replaced by a marketing resolution label.
- Signed media URLs keep their literal query values. The first supplied delivery address for each variant is used; duplicate playback addresses are collapsed. Protected or unsupported variants cannot reappear through the generic playback address.
- Status errors, declared private/restricted/protected items, missing data, mismatched IDs and verification responses fail or remain unavailable explicitly. Photo posts are not treated as their music track.
- Standard TikTok blockquotes and iframe players can be delegated from generic HTML. Conflicting embed IDs are rejected.
- Numeric `tiktok <id>` archive identities skip known aliases before HTTP. Short links require resolution before the ID is known.

## HTTP behavior

The provider identifies the engine as `YoutubeDlNative/<version>` from its first page request unless the user supplies an agent string. Its playback session uses the same agent and a TikTok referrer. The default browser-style header produced a WAF verification response in research, while the native application identity received the public page. There is no client rotation or repeated extraction after a verification failure.

Playback-header scopes share the existing cookie handler, proxy and timeouts, but keep their default headers separate. Disposing a scope does not dispose the parent handler. Explicit caller user-agent/referrer settings take precedence. This shared mechanism is tested through multiple embedded snapshots and a local HTTP exchange that changes cookies in both directions without leaking scoped headers into the next item.

## Live verification on October 8, 2026

The [video used in TikTok's developer embed example](https://www.tiktok.com/@scout2015/video/6718335390845095173), ID `6718335390845095173`, exposed five H.264/H.265 variants. Native downloads with codec and 30-fps constraints completed with metadata and artwork:

| Requested codec | Output bytes | Video | Audio | Duration |
| --- | ---: | --- | --- | ---: |
| H.264 | 3,006,827 | 576×1024, 30 fps, 314 frames | AAC, stereo 44.1 kHz, 452 frames | 10.495011 s |
| H.265 | 2,058,463 | 720×1280, 30 fps, 314 frames | AAC, stereo 44.1 kHz, 227 frames | 10.495011 s |

Both outputs fully decoded and contained a 540×960 attached JPEG. The publisher's integer duration was 10 seconds; the served media was not truncated to that value.

Deterministic checks cover format facts, literal URLs, codec/dimension constraints, scoped headers and ownership, explicit settings, short/player aliases, single/multiple embeds, access/verification/protection handling, photo refusal, legacy exact-ID extraction, malformed data/origins, archive skipping and cancellation.

## Remaining work

Profiles/collections/tags/sounds/effects, live streams, photo posts, captions, additional formats/codecs/layouts, alternate-CDN failover, expiring URL refresh, private/account/regional workflows, legitimate verification completion when requested by the service, richer metadata and production integration remain pending. The captured `TikTok` and `vm.tiktok` keys have partial verified coverage. This is not full TikTok or overall replacement parity.

Primary references: [TikTok video embeds](https://developers.tiktok.com/docs/en/embed-videos) and [TikTok embed player](https://developers.tiktok.com/docs/en/embed-player). Current page fields and media responses were observed directly from the public website. Signed addresses and session data remain outside repository fixtures.
