# Native Jamendo tracks and albums

Verified October 10, 2026, with native engine **0.102.0**. This covers a verified public subset of captured keys `Jamendo` and `JamendoAlbum`; full site and option parity remain unfinished.

## Implemented behavior

Native C# discovery handles public track/album numeric IDs with optional slugs, language prefixes and ordinary language/tracking parameters. Unsupported access parameters, fragments, credentials, ports and routes fail explicitly. Jamendo domains are reserved from generic extraction so unsupported private/player routes cannot silently fall back to Open Graph audio.

The current site's public metadata endpoints return exact track, album and artist records. Its public client advertises an `X-Jam-Call` checksum: SHA-1 of the endpoint path plus a fresh nonce, framed with `$`, `*` and `~`. The endpoint is signed before query data is appended. C# computes this directly; no website JavaScript is executed or included in the runtime. Metadata calls are bounded to the public Jamendo origin and known endpoint paths. Anonymous discovery/playback removes common authentication headers and user cookies; the request checksum is confined to metadata calls and is not sent to storage.

Track, artist, album membership and supplied resource identities must agree. Current public availability, artist visibility, album publication and the artist's `isDownloadable` choice are respected. Denied, unavailable, preview, upcoming and explicitly restricted/live/protected states fail. Singles can omit an album. Public download permissions are also described in the official [track API documentation](https://developer.jamendo.com/v3.0/tracks); the provider uses the current web metadata endpoints rather than inventing developer credentials.

Only supplied audio URLs are used. MP3/Ogg download and stream alternatives preserve literal source queries, formats and identities. A provider source preference favors the publisher's MP3 download by default; it does not invent bitrates for undocumented variants. Explicit format selection and the shared free-format preference remain available. Fixed local demuxers fully decode the selected resource and check its declared program duration before processing/publication. Jamendo adds a 0.1-second minimum-duration tolerance to the shared verifier, so a shorter radio variant cannot pass the general rounding allowance as a complete track. Other providers retain their existing tolerance. Exact original bytes are retained; conversion uses the shared native audio pipeline and ffmpeg.

Metadata preserves titles, artist, album/track facts, release time, public listen/like/download counts, license reference and deduplicated cover variants. Thumbnail dimensions come from the actual supplied resource query rather than possibly misleading descriptor names.

Albums use the complete declared membership array, sorted by publisher position. Missing/duplicate IDs, gaps/duplicate positions, conflicting ownership, omitted metadata, configured limits and changes during inspection fail without truncation. Metadata batches are limited to 100 IDs and combined metadata to 32 MiB. Explicitly unavailable or download-denied tracks retain their original unavailable positions. Final owner/membership/revision checks detect an album changing during inspection.

Inspected selections retain immutable track IDs and original positions, refreshing only selected playback descriptors. Direct and selected tracks use stable `jamendo` numeric archive identities; recorded repeats do not download again or emit new file completions.

## Verification

Fourteen new core checks cover request vectors and scope, identities/facts, aliases/boundaries, ownership conflicts, access/publication/download states, singles, literal audio/cover resources, complete/changing/oversized albums, unavailable positions, snapshots, archives, credentials, cancellation and denial.

Nine generated-media checks fully decode MP3/Ogg and supplied streams, selected/inspected album downloads, conversion and retained originals. They verify exact source bytes and unchanged unconverted decoded samples, archives/source positions, short-source refusal before publication and preservation of earlier completed tracks during cancellation. A near-duration short source now fails even when it would fit the shared general rounding tolerance.

The Release solution build passed with zero warnings/errors. All **1,512 native checks** passed: 1,136 core and 376 optional media checks. All **53 app checks** passed, including four browser checks.

Live verification used [Episode 4 Pt. 3](https://www.jamendo.com/track/887205/episode-4-pt-3) by WE ARE FM and [Season One](https://www.jamendo.com/album/104336/season-one). The complete album contained ten tracks; the selected track retained source position 6. Five native adapter workflows completed ten files:

| Workflow | Processed output | Retained original | Decoded frames |
| --- | ---: | ---: | ---: |
| MP3 download | 3,011,200-byte MP3 | 3,012,404-byte MP3 | 3,819 each |
| Ogg download | 1,411,276-byte Vorbis | 1,417,553-byte Vorbis | 7,036 each |
| Supplied low-bitrate stream | 1,216,900-byte MP3 | 1,217,536-byte MP3 | 3,819 each |
| Inspected album position 6 | 3,011,200-byte MP3 | 3,012,404-byte MP3 | 3,819 each |
| M4A conversion | 1,624,310-byte AAC | 3,012,404-byte MP3 | 4,295 AAC / 3,819 MP3 |

Three additional complete live workflows verified default format selection, the supplied `stream-mp32` URL and the supplied Ogg stream. Default and `mp32` output/original sizes and frames matched the MP3-download row; the Ogg-stream output/original matched the Ogg-download row. This establishes eight complete workflows and sixteen files.

All sixteen files independently decoded completely. Durations were 99.719977 seconds for MP3/AAC and 99.720000 for Vorbis; the publisher declares rounded duration 99 seconds. Within every unconverted workflow, processed/original PCM SHA-256 hashes matched exactly. Each initial job emitted two media/original completions; archive repeats emitted none. Inspected selection used the app's existing test access to its internal playlist snapshot, exercising the same adapter selection path used by WPF. All eight original sources were revalidated with the final tighter duration floor without redundant downloads.

The advertised `stream-mp33` resource exposed a material difference: it decoded only 3,759 MP3 frames over 98.169229 seconds, about 1.55 seconds shorter than the full sources. Its diagnostic output/original files (2,485,230/2,485,022 bytes) are excluded from complete-track coverage. The final native guard refuses a fresh download before publication, emits no completion, and preserves prior media/archive contents. Complete radio-variant semantics remain unresolved.

Scratch public page/API captures, client-source inspection and verification evidence are under `work/jamendo-probe`, `work/jamendo-variants-probe` and `work/jamendo-guard-probe` in the October 10 Codex workspace, outside runtime dependencies. User settings, history, media and existing uncommitted work were preserved. No Git commit or push was performed.

## Remaining work

Complete shorter-radio-variant behavior, any future FLAC sources, broader artist/account/licensing/radio/widget/legacy layouts, larger/mutable inventories and complete site/option parity remain pending. The current web request/metadata layout is explicit and bounded; a changed layout requires additional implementation rather than a hidden fallback. Album ZIP downloads are not implemented; individual artist-authorized tracks use their own supplied resources.

Production WPF still uses yt-dlp. Installer/runtime/fallback removal and final packaged application verification without Python or yt-dlp remain unfinished. These isolated native adapter checks are not a packaged application test.
