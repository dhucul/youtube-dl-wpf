# Native restart and playlist ordering

The independent C# engine now honors the app's restart, reverse-playlist and random-playlist settings. These settings also have typed request fields and supported extra-argument overrides. Production WPF cutover remains pending.

## Restart behavior

`NoContinue=true` starts progressive HTTP transfers from byte zero, including retries and complete-resource DASH representations. Requests omit stale Range and If-Range headers; a successful response replaces the staged partial file and creates a new validator receipt. Switching resume back on can resume that new partial entity normally.

The policy also reaches HLS and segmented DASH fragment retries. Explicit manifest byte ranges still identify required media resources and are retained. Indexed DASH already restarts its assembly and requests its declared ranges; SABR starts each new job with empty track sinks and discards incomplete response buffers on a retry. Durable cross-job fragment/SABR resume remains pending.

Restart does not delete an existing partial file before the server accepts the request. An access failure or pre-canceled job preserves both the previous partial/receipt and the published file. A response that starts writing replaces the partial data; subsequent cancellation retains the new partial data. Media publication still requires a complete transfer. No-overwrite, simulation and metadata-only behavior take precedence over media transfer, and do not erase an existing media workspace.

The adapter maps `General.NoContinue`. Supported extra arguments are `--no-continue`, `--continue` and `-c`; later arguments override earlier settings. This does not implement the separate `--no-part` direct-output behavior.

## Playlist ordering

Selection first applies the requested source positions/ranges and start/end bounds. `PlaylistReverse` reverses that work list. `PlaylistRandom` then shuffles the selected entries; when both are enabled, random order is the final order. Provider metadata and preview inventory remain unchanged.

Original playlist positions remain in filenames, templates, failure reports and per-item metadata. Archive identities follow the actual video, regardless of work order. Unavailable entries retain their positions, stop-on-error stops at the first attempted failing item, and cancellation leaves already completed files and callbacks intact. Only selected entries are downloaded.

The adapter maps `Network.PlaylistReverse` and `Network.PlaylistRandom`. Supported overrides are `--playlist-reverse`, `--no-playlist-reverse`, `--playlist-random` and the native `--no-playlist-random` inverse. Ordered signed indexes, stepped/open ranges and explicit list-order semantics are now implemented; see `PLAYLIST-SELECTION.md`. Lazy enumeration, remaining option interactions and full parity remain pending.

## Verification on October 8, 2026

Thirteen deterministic checks cover saved-partial restart, re-enabled resume, same-job retries, denial/cancellation, invalid partial responses, progressive and DASH integration, HLS/DASH fragment retries, no-overwrite/simulation, argument precedence, reverse selection/failures, seeded random selection without inventory mutation, random cancellation and archive preservation. App regression checks verify saved-setting mapping and per-job overrides.

A live native download of Streamable's public `hn8hq` example was canceled after 65,536 bytes. The existing published file and partial data survived; the canceled item was not archived. Restart then made one media request with no Range or If-Range header and saved a complete 4,746,054-byte H.264/AAC MP4. Full decoding passed: 640x360, 720 video frames, 1,033 audio frames, 24.000000 seconds. This check used the source file without artwork or metadata processing, so its byte count differs from the processed sample in `STREAMABLE.md`.

The public Vidyard example `MT1jv9tg2Cq1qWepjXv71v` selected entries 1 and 3 with reverse ordering. Completion callbacks and files arrived in order 3, then 1; filenames retained the original `003` and `001` indexes. Both native HLS outputs were remuxed to MP4 and fully decoded:

| Source position | Bytes | Video | Audio frames | Duration |
| --- | ---: | --- | ---: | ---: |
| 3 | 1,427,232 | H.264, 640x360, 240 frames | 452 AAC | 9.680000 s |
| 1 | 3,869,460 | H.264, 640x360, 983 frames | 1,923 AAC | 41.082711 s |

The item 1 HLS duration/audio-frame count differs from the earlier progressive sample; no clipping or synthetic frames were introduced to force them to match. These are live checks of restart and ordering, not evidence of complete provider parity.

Behavioral reference: [upstream option documentation](https://github.com/yt-dlp/yt-dlp#usage-and-options). The transfer and ordering implementation is original C#; no upstream extraction/runtime code was added.
