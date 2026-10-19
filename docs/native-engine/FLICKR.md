# Native Flickr public video posts

Engine 0.71.0 adds independently implemented C# public-video extraction. Two public MP4 samples are verified live. The production app still uses its original backend pending complete replacement coverage and packaging.

## Metadata and offered media

The provider reads the public page's bounded `modelExport` JSON without executing scripts. The requested photo/video ID, public privacy model and photo model must agree. Still images and private/account-sharing layouts fail explicitly. A fresh player key and resource identity come from that page; neither is hard-coded into the application.

Read-only `flickr.photos.getInfo` confirms the same video ID/resource identity, public visibility, owner and ready/not-pending/not-failed state. The player's `flickr.video.getStreamInfo` supplies the actual media addresses. Lookups are bounded, use the offered identity, reject redirected responses and expose only an error code on API refusal. Discovery/playback use anonymous owned HTTP sessions. No Python or yt-dlp extraction code is used.

Primary references are Flickr's [API overview](https://www.flickr.com/services/api/misc.overview.html), [getInfo documentation](https://www.flickr.com/services/api/flickr.photos.getInfo.html), [URL and short-link documentation](https://www.flickr.com/services/api/misc.urls.html), and the observed public page/player/API responses. The player stream method is verified through current public responses; it is not presented as a separately documented public API contract.

HTTPS public photo-page, mobile, `photo.gne`, embedded-player and base-58 short aliases retain the same identity. Profiles/albums/galleries remain explicit unsupported layouts. Optional album-context suffixes preserve the requested video. The current implementation bounds numeric IDs to unsigned 64-bit values; broader opaque-ID formats remain pending.

Offered media must belong to the declared video ID/resource identity on the verified CDN, with the exact MP4 stream name or HLS master path. Encoded query values are preserved, including supplied child-playlist and fragment queries. Repeated or excessive stream declarations fail. Modern `360p`/`720p`/`1080p` labels expose height; legacy labels such as `700`, `appletv` and `iphone_wifi` do not invent dimensions. HLS quality uses manifest facts. Metadata poster dimensions never override each stream's dimensions.

Title, description, uploader, full duration and supplied poster variants are mapped. Poster resources are bound to the verified post ID and origin. Videos use `flickr <video-ID>` archive keys and can skip before any page request. The engine decodes staged media and checks its complete duration before publishing, and retains byte-identical progressive originals when requested.

## Live verification on October 9, 2026

| Public video | Saved media | Strict full decoding | Publication and repeat |
| --- | --- | --- | --- |
| [NASA filament, 7753690742](https://www.flickr.com/photos/gsfc/7753690742/), legacy `appletv` | 5,399,283-byte processed MP4; 5,399,257-byte original MP4 | H.264 958×720, 450 video frames, silent; 18.768750 s | Thumbnail/info JSON, two callbacks; zero archive-repeat callbacks |
| [NASA Orion, 52520519307](https://www.flickr.com/photos/nasa2explore/52520519307/), `360p` | 7,120,565-byte processed MP4; 7,135,173-byte original MP4 | H.264 640×360, 5,631 video frames; AAC 8,807 audio frames; 187.887700 s | Thumbnail/info JSON, two callbacks; zero archive-repeat callbacks |

All four published media files fully decoded without diagnostics. The older silent video did not invent audio. Fresh metadata and access values were obtained autonomously, with no personal cookies or manually supplied playback capture.

Live inspection of the modern video's [short alias](https://flic.kr/p/2o24oAX) returned the same video ID, title, 188-second declared duration and four offered formats.

Both offered live HLS sources remain unverified. The older master's endpoint returned HTTP 500. The modern maximum-360 job assembled an 8,259,780-byte TS with 5,631 decoded frames, but its final frame moved from 187.854333 s backwards to 187.853967 s. Strict media verification refused this clock. Neither failed HLS job published media or created an archive record. The error was not suppressed and the source timestamps were not silently rewritten.

A follow-up checked the decoded frame's actual PTS as well as its best-effort clock. Both show the same final regression. FFmpeg's documented `igndts` input option did not resolve it and still emitted diagnostics; it has not been applied to native downloads or used to bypass verification.

Eleven deterministic checks cover metadata, offered queries, aliases/base-58, access/ready states, ownership/resource binding, legacy labels, archives, native embed delegation, JSON-only parsing, bounded API diagnostics and inventory limits. Three generated-media checks verify complete MP4/HLS frames and audio, byte-identical MP4 originals, supplied child queries, archive retries and incomplete-source refusal while preserving previous output.

Remaining Flickr work includes the observed HLS clock/server issues, account/private/share access, collections, additional resource/origin/ID layouts, more legacy quality facts, captions/richer metadata and complete upstream parity. Remaining sites and app options, removal of the old runtime, packaged verification and WPF cutover are still required.
