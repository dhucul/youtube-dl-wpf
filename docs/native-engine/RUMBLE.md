# Native Rumble public playback

The C# provider reads public Rumble player declarations and the current `embedJS/u4` video response. It does not execute site JavaScript, Python, yt-dlp or another extraction library.

## Implemented scope

- Public watch and sharing links, ordinary player embeds and publisher-prefixed embed aliases. Watch IDs and playback IDs are different; the provider reads the declared player ID and checks the response's numeric identity instead of deriving one from the other. Redirected and canonical watch identities are checked.
- The public page is visited before playback metadata using the same HTTP session. Literal player identity and access options are read without executing callbacks. String/comment examples are ignored, duplicate identity/access properties fail, and computed identities remain unsupported. Both quoted and unquoted HTML metadata attributes are accepted.
- Generic iframe and literal SDK declarations delegate to the native Rumble provider. Explicit region-blocked declarations are honored in watch pages and SDK discovery. Rumble's generic Premium dialog template is not treated as evidence that a video is restricted.
- Ready on-demand responses preserve title, description, uploader, cover, duration and actual offered rendition facts. MP4, WebM and HLS sources are supported. Global player defaults and quality labels do not replace missing rendition dimensions/frame rates. Supplied URL query parameters are retained literally.
- Preview `timeline` assets and raw `tar` archives are excluded from downloadable formats. Current archive-backed HLS works through the playlist's supplied CDN extraction URLs; its `r_file`, `r_type` and `r_range` query parameters remain server instructions, not locally invented HTTP byte ranges or a request to save the whole archive.
- MP4/WebM sources and selected HLS output are decoded before publication. Local MOV/Matroska/MPEG-TS demuxers verify required media roles and the complete declared duration with the existing tolerance. HLS validation follows the selected audio/video role and actual downloaded TS/MP4 container. Incomplete playback cannot write an archive record or replace an earlier published file.
- Explicit user agent/referrer preferences and session cookies flow through discovery and media requests. Native defaults identify the C# engine. Native archives use `rumble <embed-id>` consistently across aliases; known archived embeds skip discovery, while watch links resolve their player identity before skipping media. This does not establish every upstream archive alias.

Unavailable/loading responses, nonzero live states, previews, nonzero source-trim offsets, protected/premium flags and restricted embedding modes fail explicitly. The current public player allows restriction modes at or below `-3`; other domain/account workflows remain pending. No alternate access route is attempted after HTTP denial.

## Verification on October 8, 2026

Twelve deterministic checks cover metadata, available-format filtering, aliases, URL boundaries, static declaration parsing, callbacks/comments, HTML embedding, access states, identity mismatches, protected variants, archives, headers, HTTP errors and cancellation. Six generated-media checks verify complete MP4/WebM/HLS playback, literal archive-extraction queries, preserved originals and metadata, all selected audio/video frames, and rejection of a shortened HLS presentation.

The public [Big Buck Bunny sample](https://rumble.com/v71gwwi-big-buck-bunny-60fps-4k-official-blender-foundation-short-film.html) resolves to player `v6za8wa`. Its lowest offered HLS variant completed through the default native engine with two concurrent fragment transfers. The saved MP4 is 16,320,527 bytes, H.264 640x360 at 12 fps with AAC 44.1 kHz audio, lasting 634.713111 seconds. It retains all 7,616 video frames and 27,331 audio frames from the 17,603,756-byte original TS. Both files fully decoded. The relative audio/video start offset, title/creator metadata, two completion callbacks and native video archive identity were verified. The video's title is not used as a claim about its offered resolution or frame rate.

An earlier attempt returned HTTP 403; later independent native runs completed the public HLS workflow. The public [Rumble embed tutorial](https://rumble.com/embed/v17j3tt/) exposed six MP4 renditions and one WebM rendition during inspection, but attempted native downloads received HTTP 403 during discovery. Those specific direct-file downloads remain unverified. The cause of those intermittent discovery denials remains unresolved.

A subsequent version 0.51.0 channel job completed direct MP4 playback for `va5o2f` / `v7jhvr`, retaining all 2,508 video and 3,917 audio frames and the complete original. MP4 therefore also has a successful live sample; the specific tutorial above and live WebM playback remain unverified. See `PLAYLIST-SESSIONS.md`.

Rumble's own [embedding help](https://rumble.support/en/help/rumble-basics-how-to-embed-your-video) provides the public embed context used for this integration. Current page/player responses, rather than another downloader's implementation, supplied the protocol evidence.

## Remaining work

Channel/user listing discovery and selected-video integration are now implemented with bounded, identity-checked pagination; see `RUMBLE-COLLECTIONS.md` for scope and live verification limits. Search, playlists, live and recorded-live state handling, captions, chapters, trim offsets, domain/account/premium workflows, other CDN/format layouts, backup selection and refresh, complete options/archive parity, stable direct-file live verification and production application cutover remain incomplete.
