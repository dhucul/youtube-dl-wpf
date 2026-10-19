# Native X/Twitter public post videos

Engine 0.70.0 adds independently implemented C# public-post extraction. The app's production backend remains unchanged until complete coverage, packaging and cutover verification are ready.

## Public metadata and media

The provider reads the public embed's bounded syndication JSON. Requested and returned post IDs must agree; tombstones, protected posts and unsupported layouts fail explicitly. HTTPS X, Twitter, mobile, `i/status`, `i/web/status` and platform embed aliases use a canonical post identity. An explicit `/video/N` or `/photo/N` selects the original media position and refuses still images.

The public embed computes a numeric base-36 checksum. C# reproduces the browser's rounding behavior without a JavaScript runtime. Six numeric vectors were compared against the current primary embed script. The V8-derived numeric rounding behavior is attributed in the redistributed V8 notice; no V8 runtime is required. Protocol evidence comes from [X's public embedding guidance](https://help.x.com/en/using-x/how-to-embed-a-post) and its [current embed script](https://platform.twitter.com/embed/embed.Tweet.70198ef24afe05adbecf.js).

Only offered complete MP4/HLS variants on the declared video origin are accepted. Literal access queries are retained. Variants must share one declared media asset/family. Progressive dimensions come from each offered resource, while HLS dimensions are discovered through its manifest. The original poster dimensions do not determine selected video quality. Posters use the supplied post item's declared image origin; their separate image IDs need not equal video IDs.

Title, post text, author, duration, poster and per-post/per-media archive identities are mapped. Anonymous discovery/playback avoids sending unrelated account authorization. Animated GIF video resources retain their silent role. Multi-video posts preserve all original positions, including photos and unavailable/unsupported videos, and reject oversized inventories without truncation. Unsupported items remain unavailable; malformed identity bindings still fail. A quoted post with video delegates to the original post identity.

## Live verification on October 9, 2026

The public [NASA post](https://x.com/NASA/status/2039908597748940872) supplied both paths. These are separate encodes, so their audio frame counts differ.

| Path | Saved media | Verified playback | Archive repeat |
| --- | --- | --- | --- |
| MP4, maximum 720 | 4,242,639-byte processed MP4; 4,244,956-byte original | H.264 1280×720, 1,174 video frames; AAC 1,838 audio frames; 39.210667 s | Two publication callbacks, zero repeat callbacks |
| HLS, maximum 720 | 4,238,737-byte processed MP4; 4,238,402-byte merged original; 3,580,810-byte video component; 633,840-byte audio component | H.264 1280×720, 1,174 video frames; AAC 1,828 audio frames; 39.164840 s merged | Four publication callbacks, zero repeat callbacks |

All six media files passed strict full decoding. No Python, yt-dlp, personal cookies or manually supplied playback capture was used.

Eleven deterministic checks cover checksums, metadata, aliases, exact selection, access/identity boundaries, multi-media and unavailable positions, limits, silent GIFs, quote delegation, archives and embedded HTML. Four generated-media checks verify complete MP4/HLS/GIF frames, retained originals, selected-only transfers, publication and repeat behavior.

Full X parity remains partial: accounts/protected posts, retweets and additional quote/card layouts, broadcasts, spaces, Amplify pages, explicit shortener behavior, alternate media origins, captions and richer metadata still need implementation and verification. Remaining sites and app options, removal of the old runtime, packaged verification and WPF cutover remain required.
