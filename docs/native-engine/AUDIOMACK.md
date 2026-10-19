# Native Audiomack public tracks and albums

Verified October 10, 2026, with native engine **0.105.0**. Captured keys `audiomack` and `audiomack:album` have a verified public subset. Full site/option parity and production cutover remain unfinished.

## Implemented behavior

Native C# discovery supports public artist/song and artist/album URLs, ordinary host/tracking aliases and embedded song/album routes. Private keys, user OAuth values, unsupported routes, fragments, credentials and ports fail. Audiomack domains are reserved from generic discovery.

The provider reads bounded literal public client configuration from website-advertised JavaScript resources. It independently implements OAuth 1.0 HMAC-SHA1 for public metadata and current GET playback endpoints; no publisher script is executed. Signing is limited to public song/album metadata and numeric playback identities. The published anonymous web-client configuration is not user authentication.

Requested owner, recording type, slug, numeric identity, canonical URL, album ownership and supplied media path must agree. Private, premium-only, regional, preview, unpublished and incomplete descriptors fail explicitly. Current `live` metadata denotes publication state; it is not treated as evidence of a live recording. Supplied signed resource parameters remain literal and must be complete and unexpired. MP3, M4A and AAC formats are reported only when offered; identical playback descriptors are deduplicated.

Album inspection preserves complete publisher order and unavailable positions. Bounds, duplicate identities, missing metadata, owner conflicts and changing final membership fail. Inspected selections refresh only selected stable track IDs and retain original album positions. Numeric `audiomack` archive identities skip playback and completion effects for recorded tracks.

Website and API discovery use separate anonymous sessions. Live diagnosis reproduced HTTP 406 after TLS session resumption despite a valid signed request; four identical-request combinations succeeded with resumption disabled. The native compatibility setting applies only to Audiomack's API session. Certificate verification and configured proxy, source address, IP policy, timeouts and user agent remain in the existing native HTTP path. Other providers retain default TLS behavior.

Fixed local ffmpeg demuxers completely decode offered audio and check declared duration before transactional publication. Metadata processing, original retention and conversion continue to use ffmpeg.

## Verification

Fourteen core checks cover independent encoded HMAC vectors, public literal configuration, metadata/formats, URL boundaries, owner/source conflicts, access restrictions, expired/ambiguous signatures, complete albums, unavailable positions, changing inventories, inspected selections, archives, credential isolation, denial and cancellation. Eight generated-media checks cover M4A/MP3, embeds, album/snapshot selection, conversion, exact originals and samples, incomplete sources and cancellation preserving prior publication.

The Release solution build passed with zero warnings/errors. All **1,568 native checks** passed: 1,172 core and 396 optional media checks. All **58 app checks** passed, including four optional browser-tool checks.

Live testing used public [Ab Haqeeqat Hai Tu - Instrumental](https://audiomack.com/mazhabeats/song/haqeeqat-final-session-with-final-vocals-2-mastered-1-music) and [In Lafzon Mein - Instrumental Album](https://audiomack.com/mazhabeats/album/in-lafzon-mein-instrumental-album). Native discovery returned track `77634097`, album `77634096` and five ordered entries. Selection retained position 1. HQ and standard requests returned the same M4A descriptor, so live coverage records one offered format.

| Live workflow | Processed output | Retained original | Decoded audio frames |
| --- | ---: | ---: | ---: |
| Direct song | 3,295,263-byte M4A | 3,295,192-byte M4A | 5,034 each |
| Embedded song | 3,295,263-byte M4A | 3,295,192-byte M4A | 5,034 each |
| Album selection, position 1 | 3,295,263-byte M4A | 3,295,192-byte M4A | 5,034 each |
| Inspected album selection, position 1 | 3,295,263-byte M4A | 3,295,192-byte M4A | 5,034 each |
| MP3 conversion | 1,871,413-byte MP3 | 3,295,192-byte M4A | 4,475 MP3 / 5,034 original |

All ten files independently decoded completely. Source duration was 116.872902 seconds against the publisher's rounded 117 seconds. Every unconverted processed/original pair had identical decoded PCM SHA256. Each job completed two files; archive repeats emitted no files or completion callbacks. Workflows used the native WPF adapter with isolated saved settings and archives, without Python or yt-dlp.

Captures and diagnostic evidence are under `work/audiomack-probe` and `work/audiomack-live-probe` in the October 10 Codex workspace. They are not runtime dependencies. Existing settings, history, media and uncommitted work were preserved; no Git commit or push was performed.

## Remaining work

Separate live standard MP3/AAC variants, album embeds, more ownership/legacy/resource layouts, protected/account/premium/regional workflows, complete collection/access semantics, generic embedding and full option/site parity remain pending. AMP/account availability and other playback restrictions need additional verification; the ungated public sample does not establish those workflows.

Production WPF still uses yt-dlp. Installer/runtime/fallback removal and packaged application verification without Python or yt-dlp remain unfinished. Native adapter probes are not packaged WPF verification.
