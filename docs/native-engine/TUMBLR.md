# Native Tumblr public post media

Engine 0.77.0 adds independently implemented C# public themed-post extraction, native media and supplied player delegation. Legacy MP4 and modern resized/canonical MOV resources are verified live. Full Tumblr coverage and production WPF cutover remain unfinished.

## Post scope and native resources

HTTPS blog post, frontend, blog-view, post-embed and native player aliases resolve to a canonical blog/post identity. Profiles/feeds, custom domains, unsupported short/UUID layouts and account routes remain explicit boundaries. The returned page must confirm the same blog and post.

Public themes include suggested posts alongside the requested post. The reader isolates the exact identified article, balances nested article boundaries and reads only its literal HTML-decoded `data-npf` JSON. Suggested articles, comments, templates and script bodies do not supply media. Missing/conflicting/incomplete article scopes fail. Repeated cosmetic theme attributes are tolerated; conflicting post/resource attributes fail.

Native video/audio blocks retain supplied media MIME types, addresses, dimensions, posters and archive identities. HTTPS Tumblr media origins and offered resource queries are validated. Modern blocks can supply different canonical and resized resources; both must match their declared media-key directory. A canonical address is not assumed to be a full-resolution original. Its dimensions remain unknown, while an observed `s720` rendition exposes a 720-height quality fact without copying the original recording width.

The themed HTML annotation's rounded `duration` values are seconds in the verified samples. Tumblr's API NPF specification instead defines milliseconds. This reader does not apply its themed-HTML interpretation to API responses. Local full decoding and declared-duration checks precede publication; incomplete files do not replace prior output or update archives.

A single native asset keeps `tumblr <post-ID>`. Multi-media posts retain ordered block positions and use distinct `tumblr:media` identities, preserving non-media/unavailable slots. Only selected media are transferred. Native audio retains its audio-only role. Available source files and processed outputs use the shared transactional publication/original-retention/archive pipeline.

Supplied third-party player blocks and legacy iframe declarations delegate to the original native provider. Tumblr's safe inline wrapper is decoded to its actual player URL without executing scripts. Wrapper posts do not invent native media resources or take the underlying provider's archive identity. Paywall/account/unsupported-layout boundaries remain explicit.

Primary references are Tumblr's [NPF specification](https://github.com/tumblr/docs/blob/master/npf-spec.md), its [official API documentation](https://github.com/tumblr/docs/blob/master/api.md), and observed public themed post annotations/media. No API application key is embedded, and no Python, yt-dlp code or JavaScript runtime is used by this provider.

## Live verification on October 9, 2026

| Public post / supplied resource | Published files | Strict full decoding | Repeat |
| --- | --- | --- | --- |
| [NASA 616113690880720896](https://nasa.tumblr.com/post/616113690880720896), legacy MP4 | 28,261,393-byte processed MP4; 28,261,244-byte original | H.264 640×428, 2,163 video frames; AAC 3,112 audio frames; 72.271043 s | Two callbacks; zero archive-repeat callbacks |
| [NASA 826039965713612800](https://nasa.tumblr.com/post/826039965713612800), supplied 720-height MOV | 6,376,322-byte processed MOV; 6,376,239-byte original | H.264 1280×720, 943 video frames; AAC 1,478 audio frames; 31.530000 s | Two callbacks; zero archive-repeat callbacks |
| Same modern post, canonical preview MOV | 3,627,376-byte processed MOV; 3,627,225-byte original | H.264 852×480, same 943/1,478 video/audio frames; 31.530000 s | Two callbacks; zero archive-repeat callbacks |

All six files fully decoded. The canonical preview was discovered to be smaller than the supplied resized resource; its format label/dimension policy was corrected accordingly. Info JSON and thumbnails were published, with no personal cookies or manually supplied capture. Suggested videos elsewhere in themed pages were excluded.

Ten deterministic checks cover scope, aliases, changed identities, untrusted markup/JSON, origins/media keys, actual-versus-default dimensions, audio roles, multi-block positions, external delegation and archive pre-skips. Five generated-media checks verify complete MP4/MOV/audio playback, originals, selected-only multi-media transfer, archive repeats and incomplete-file protection. HLS parsing shares the existing transport but has not been live-verified for Tumblr in this stage.

Remaining work includes broader themes/dashboard/API/key handling, custom domains/short URLs, blog/tag pagination, reblog trails and attribution, native legacy player layouts without NPF, full image/photo workflows, accounts/subscriptions/age/access cases, captions/chapters/richer metadata, more source families and complete compatibility. All-site/options coverage, old-runtime removal, packaging without yt-dlp/Python and the WPF switch remain required.
