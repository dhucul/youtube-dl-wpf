# Native Cloudflare Stream public players

The C# provider resolves Cloudflare Stream player declarations and server-reported video state, then uses the existing native HLS or DASH downloader. It does not execute the player SDK or use yt-dlp, Python or another extraction library.

## Implemented scope

- Current customer-domain player/watch links, legacy iframe/watch/video-delivery aliases, video-specific SDK URLs, and explicitly supplied HLS/DASH manifests. Generic HTML discovers iframe, `stream` and video-specific SDK script declarations without executing JavaScript.
- Plain 32-character video identifiers and caller-supplied signed token keys. Discovery retains literal access parameters. Token payloads provide only an expected identity; the server still authenticates the token. SDK video arguments cannot introduce paths, query parameters or conflicting identities.
- Bounded player, lifecycle and optional enhancement responses; exact video/token and customer-domain checks; server-confirmed archive identities. Optional enhancement responses with HTTP 204 do not hide playable media.
- Offered public title/description, default thumbnail, and HLS/DASH candidates. Resolution, codec and audio facts come from the actual manifests; player dimensions are not assigned to every stream. A missing public title falls back to the video ID. Duration remains unknown until media discovery provides it.
- Actual embedding referrer and parent origin, shared HTTP settings/cookies, native quality selection, parallel fragment transfers, metadata, cover embedding and related files. No alternate origin is tried after an access denial.
- Live-input routing, active live recording, previews, mismatched identities and unsupported playback states fail or remain unavailable explicitly. Direct manifests preserve the supplied URL exactly and rely on transport-level manifest validation. Signed direct manifests use a token digest for archive identity rather than trusting an unverified token claim.

## Verification on October 8, 2026

Two public examples from Cloudflare's documentation downloaded through `NativeEngine.DownloadAsync` with four concurrent fragments:

| Sample | Transfer | Output bytes | Decoded video | Decoded audio | Duration |
| --- | --- | ---: | --- | --- | ---: |
| `6b9e68b07dfee8cc2d116e4c51d6a957` | HLS, maximum 360p | 2,780,827 | H.264, 640x360, 720 frames | None offered | 30.030000 s |
| Same sample | DASH, maximum 360p | 2,780,827 | H.264, 640x360, 720 frames | None offered | 30.030000 s |
| `5d5bc37ffcf54c9b82e996823bffbb81` | Legacy iframe to HLS, maximum 240p | 48,362,732 | H.264, 426x240, 21,608 frames | AAC, 22,507 frames | 720.266667 s |

All outputs fully decoded. The modern HLS/DASH files had identical SHA-256 hashes. Each retained a 640x360 JPEG cover; the longer sample's saved and embedded artwork had identical decoded RGB pixels. The short sample's lack of audio was confirmed by its published manifests. The longer sample's manifest advertised 720.2 seconds; the complete downloaded media was retained without clipping to that rounded duration.

Nine deterministic checks cover identity and format facts, caller-supplied token/query preservation, exact direct manifests, modern/legacy/SDK embeds, optional metadata, invalid/access/live/preview boundaries, archives and cancellation. They include conflicting SDK IDs and encoded path/query injection attempts that must fail before HTTP access. The general native transport suite supplies generated-media validation; these public downloads provide separate live-site evidence.

## Remaining work

Live inputs and recordings, preview-to-full workflows, account-side token generation, legitimate account/domain access workflows, expiring URL refresh, richer public metadata, caption/alternate-audio selection specific to this service, additional legacy layouts and production integration remain pending. Manifest-exposed captions use the shared transport pipeline but have not been live-verified on a Cloudflare sample. The captured `CloudflareStream` extractor key therefore has partial verified coverage.

Primary references: [Stream player](https://developers.cloudflare.com/stream/viewing-videos/using-the-stream-player/), [HLS and DASH playback](https://developers.cloudflare.com/stream/viewing-videos/using-own-player/), [signed URLs and allowed origins](https://developers.cloudflare.com/stream/viewing-videos/securing-your-stream/), [thumbnails](https://developers.cloudflare.com/stream/viewing-videos/displaying-thumbnails/), and [captions](https://developers.cloudflare.com/stream/edit-videos/adding-captions/). Lifecycle and enhancement response fields were observed from the public player. Fixtures contain no real credentials or signed segment addresses.
