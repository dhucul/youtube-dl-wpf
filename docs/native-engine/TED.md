# Native TED public media

The independent C# provider reads the public site's bounded Next.js page data. It uses offered media and caption addresses without running site scripts or a third-party extractor. Talk identity is kept separate from player identity: these are different IDs in current live responses.

## Implemented scope

- Public talk slugs/numeric IDs, embedded talk pages, transcript aliases and language-prefixed talk URLs. Canonical talk identity and any player slug/canonical reference are checked.
- Offered HLS plus progressive H.264 fallback streams, metadata, artwork, speaker, duration, and full WebVTT documents. Caption parameters are preserved. Presentation duration includes the site's introductory material when its metadata provides domain durations.
- Normal best/worst choices prefer the main HLS presentation; explicit `mp4-1` selection can request its offered progressive fallback. The engine does not invent resolution-specific URLs.
- Complete playlist snapshots and series season snapshots, flattened in page order. Unavailable entries retain their positions, per-season identities are checked, and the overall item limit is enforced. Only selected talks receive fresh media discovery.
- Numeric `ted <id>` archive identity and scoped playback referrer. External YouTube delegation is limited to an offered full-video reference without a start offset; clip offsets remain explicit unsupported cases.

Collection completeness uses each provided `totalCount` and its exact node count. A mismatched count fails instead of returning a partial collection. Current series responses report `hasNextPage=true` even when their node count equals the complete declared total; the provider therefore uses the explicit count rather than that flag. Additional pagination is not implemented. Duplicate talk IDs within a season are refused, while the same talk can retain separate positions in different seasons.

## Verification on October 8, 2026

[Derek Sivers' public talk](https://www.ted.com/talks/derek_sivers_how_to_start_a_movement), talk ID `814` and player ID `35572`, exposed 68 full caption languages. Native streaming and explicit fallback downloads both completed:

| Media path | Output bytes | Video | Audio frames | Duration |
| --- | ---: | --- | ---: | ---: |
| HLS with separate audio and introductory timeline | 6,787,694 | H.264, 320x180, 5,156 frames | AAC, 7,443 | 172.800703 s |
| Progressive fallback | 24,253,480 | H.264, 854x480, 5,156 frames | AAC, 7,442 | 172.752033 s |

Both fully decoded with variable-frame-rate timing preserved. The stream's introduction uses approximately 23.976 fps while its main video uses approximately 29.97 fps. A forced constant-rate validation output produces misleading timestamp warnings; passthrough decoding with the demuxed time base passes without warnings. The two service encodings have different audio priming/sample boundaries and are not claimed to be byte- or sample-identical.

Both outputs saved and embedded English (65 cues) and French (68 cues). All extracted caption text and cue times matched the saved full documents, including the first cue at 4.308 seconds. Raw merged and separate HLS components were also retained by the keep-original workflow.

[The public playlist](https://www.ted.com/playlists/171/the_most_popular_ted_talks_of_all_time) enumerated 25 entries. [The Way We Work series](https://www.ted.com/series/the_way_we_work), ID `8`, enumerated 48 entries across six seasons. Selected entry 40, talk `58586`, downloaded through native HLS to a 6,698,840-byte H.264/AAC MP4 at 320x180, 173.244082 seconds, 4,153 video frames and 7,461 audio frames; full decoding passed.

Ten deterministic provider checks cover metadata, fallback preference, aliases, identities, resource boundaries, full captions, playlist/series selection and limits, unavailable positions, inconsistent snapshots, external offsets and cancellation. Shared HLS timeline tests are described in `HLS-DISCONTINUITIES.md`.

## Remaining work

The captured `TedTalk`, `TedEmbed`, `TedPlaylist` and `TedSeries` keys have partial verified coverage. Older page/player layouts, paginated collections, dubbed/audio-language choice, clip offsets, richer metadata, account/restricted media, expiring URLs, additional embedded hosts, broader media layouts and full parity remain unfinished. Production WPF cutover is still pending.
