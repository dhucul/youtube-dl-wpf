# Native Vidyard public players

The C# provider reads the current public player response used by Vidyard's own player. It validates the player UUID and interprets each player chapter as a separate video, preserving the underlying video UUID for metadata and archive records. It does not execute the player scripts or use a third-party extraction library.

## Implemented scope

- Current player, `.html`, player-JSON, legacy share, sharing-page, hub-watch and background-player URL shapes with 22-character UUIDs. Generic HTML supports iframes and responsive `vidyard-player-embed` image tags.
- Ready public upload sources, offered progressive MP4 and HLS, title/description, millisecond duration, artwork, full WebVTT captions and literal signed URLs. Explicit dimensions/frame rates are retained when offered; a profile label such as `1080p` does not become an invented measured height.
- The supplied automatic HLS master is preferred for normal quality choices, allowing its manifest to enforce actual size/codec/frame-rate constraints. Offered progressive and rendition-specific sources remain selectable by ID. Unknown progressive measurements cannot satisfy a requested constraint silently.
- Multi-video players form bounded playlists with original positions, unavailable items, per-video media snapshots, distinct chapter identities and selected-only media transfer. Discovery fetches a fresh player snapshot for each operation.
- Archive records use `vidyard <videoUuid>` after player discovery. A different player containing the same video can reuse that archive identity. Early skipping before metadata is not possible from a player UUID alone.
- Declared access codes, domain restrictions, token-dependent videos, unsupported player types, time-offset/selected-video links and unknown source shapes fail or remain unavailable explicitly. The actual caller/embedding referrer is forwarded where supplied; no allowed domain is fabricated. Responsive `data-second` values survive discovery so unsupported offsets cannot disappear silently.

## Verification on October 8, 2026

The player from [Vidyard's own API example](https://knowledge.vidyard.com/hc/en-us/articles/360009878294-Can-I-see-some-Player-API-examples), `MT1jv9tg2Cq1qWepjXv71v`, exposed three separate videos. Native downloads selected only entries 1 and 3 in separate operations:

| Selected entry | Media path | Output bytes | Video | Audio | Duration |
| --- | --- | ---: | --- | --- | ---: |
| 1: `4er9G5PCXV7T0TXACMBJRA` | Offered progressive MP4 | 3,915,594 | H.264, 640x360, 24000/1001 fps, 983 frames | AAC stereo 48 kHz, 1,922 frames | 41.003000 s |
| 3: `S19QCRDUPSbX9hVLGKKxyA` | Automatic HLS, maximum 360p | 1,491,505 | H.264, 640x360, 25 fps, 240 frames | AAC stereo 48 kHz, 452 frames | 9.680000 s |

Both outputs fully decoded and retained 1280x720 JPEG artwork. Entry 1 saved and embedded all 16 supplied English caption cues with matching extracted text and timing. Provider durations of 41.025 and 9.643 seconds were preserved in metadata without clipping media to rounded or differing values.

Nine deterministic checks cover player/video identities, signed resources, actual format facts, primary/explicit selection, captions, multi-video positions and limits, aliases, responsive/iframe embeds, access failures, unsupported offsets, malformed metadata, archives and cancellation.

## Remaining work

Legacy JSON/player layouts and UUID forms, player-link video/time selection, legitimate access-code/JWT/password/domain verification, external/AI/live video types, alternate audio tracks, richer captions/chapters, unknown source facts, other CDN/layout cases, URL refresh, full archive equivalence and production integration remain pending. The captured `Vidyard` key has partial verified coverage.

Primary references: [Vidyard Player API](https://knowledge.vidyard.com/hc/en-us/articles/360019034753-Using-the-Vidyard-Player-API), [embed construction](https://knowledge.vidyard.com/hc/en-us/articles/360010001713-Embed-your-video-on-a-webpage), and [time-offset links](https://knowledge.vidyard.com/hc/en-us/articles/360009870894-Make-your-video-play-at-a-specific-time). Current response fields and endpoint behavior were observed directly from the public player; signed addresses and session fields are excluded from fixtures.
