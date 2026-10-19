# Native Dailymotion provider

The C# provider recognizes video URLs, short links, legacy embeds/slugs, configured player links, and playlist context. It parses player metadata, exposed progressive/HLS/DASH URLs, codec/frame-rate descriptors, thumbnails, author, duration, live status, and supported caption descriptors. Identity/resource limits and reported playback errors fail explicitly.

Public playlist enumeration currently uses the accessible legacy API with bounded page sizes and configured entry limits. It detects repeated/wrong pages, empty unfinished responses, and silent truncation. Profile/search workflows, current API-v2 migration, account/password/geographic workflows, live recording, chapters, remaining caption shapes, and complete site parity remain pending.

On October 7, 2026, the provider inspected Dailymotion's published `x84sh87` demo (25 seconds) and found one exposed HLS manifest. Fresh native and independent HTTP requests to that stream returned an empty HTTP 403 response with a Cloudflare server header. Enabling normal session cookies did not establish playback. No specific cause or successful live Dailymotion media transfer has been established.

The public example playlist `x5zhzj` returned its name and a first 100-item page with further pages available. Complete native enumeration has not yet been verified live. Video/playlist parsing, canonical requests, bounded pagination, and exposed progressive transfer have deterministic checks.

Primary references: [published player examples](https://developers.dailymotion.com/docs/player-embed-script-web), [API calls](https://developers.dailymotion.com/reference/perform-an-api-call), and [API-v2 migration](https://developers.dailymotion.com/docs/migrating-from-legacy-api-to-api-v2). No Dailymotion client library, Python, or yt-dlp runtime is used.
