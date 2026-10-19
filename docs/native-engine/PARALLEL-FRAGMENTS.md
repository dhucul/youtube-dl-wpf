# Native parallel fragment transfers

The C# engine now honors the app's 1-32 concurrent-fragment setting for static HLS and segmented/indexed DASH. A shared ordered scheduler fetches a bounded window of resources concurrently, then assembles them in manifest order. It bounds completed lookahead as well as active work, so a slow first fragment cannot cause the entire presentation to accumulate in temporary storage.

## Implemented behavior

- HLS media, initialization and byte-range resources use distinct staged files. AES-128 decryption and key changes are applied in order, preserving key-rotation identity and implicit sequence IVs. Timeline periods remain separate until their existing validation/join step.
- DASH segment templates/lists use the same ordered transfer path. Indexed MP4 ranges retain the source byte positions and the index's entity conditions. Initialization prefixes remain validated before media transfer. Indexed DASH caption resources use the same concurrency setting within their existing size limit.
- A failed worker cancels its peers, and the scheduler awaits every started worker before cleanup. Consumer/output/progress failures also drain pending work. The original failure survives cancellation of sibling requests. Existing published media and archive records remain intact.
- `FragmentRetries` is separate from ordinary file retries. A missing native override inherits `Retries`; the adapter maps the app's fragment-retry setting. Media/initialization ranges and ranged/indexed captions use the effective fragment retry count. Zero disables those retries.
- Progress reports ordered assembled bytes. The shared rate limiter reserves one aggregate timeline across parallel workers, with a one-second initial allowance; wall-clock window changes cannot erase outstanding reservations. Invalid rates fail before HTTP instead of becoming unlimited downloads. K/M/G suffixes use powers of 1024.

Each period/representation has at most the requested number of active or buffered fragment resources. Selected tracks are still processed in sequence. Read buffers permit bounded read-ahead before rate throttling, and temporary storage is bounded by fragment count rather than a fixed total byte quota. Progressive files and stateful SABR sessions retain their existing single-transfer behavior.

## Verification on October 8, 2026

Thirteen deterministic checks force requests to overlap and finish in reverse order. They verify initialization, exact byte-range output, AES key rotation and IVs, indexed offsets/entity guards, bounded lookahead, producer/consumer failure, cancellation, output/archive protection, independent retry settings, aggregate rate reservations and invalid-option preflight. Four generated-media cases verify encrypted and fragmented HLS plus segmented/indexed DASH, with complete decoded video-frame counts, audio presence and retained original components.

Both public Angel One samples downloaded through the native engine with four concurrent transfers, English/French captions and metadata:

| Transport | Output bytes | Video | Audio frames | Duration |
| --- | ---: | --- | ---: | ---: |
| [HLS](https://storage.googleapis.com/shaka-demo-assets/angel-one-hls/hls.m3u8) | 3,993,832 | H.264, 320x240, 1,500 frames | AAC, 2,813 | 60.000000 s |
| [Indexed DASH](https://storage.googleapis.com/shaka-demo-assets/angel-one/dash.mpd) | 2,774,914 | H.264, 300x240, 1,500 frames | AAC, 2,814 | 60.021333 s |

An instrumented HTTP handler counted responses until their bodies were disposed. Each run reached exactly four concurrent media-resource responses and returned with zero active responses. Both complete outputs fully decoded. Each language retained all 16 caption cues with matching saved/embedded text and timing, matched by language tags because the manifests order their languages differently.

## Remaining work

Live-fragment scheduling, durable fragment resume, adaptive concurrency, fragment-level speed/ETA reporting, additional caption/key retry paths, remaining caption concurrency, exact legacy option interactions and production integration remain pending. Full transport/site coverage and the WPF backend cutover are still incomplete.

Separate segmented DASH caption resources now use the bounded ordered scheduler and a shared byte budget checked during response reads. Ranged sequences retain sequential validator state. The one-hour public STPP caption-only run completed 1,800 caption fragments with concurrency four; see `MP4-TTML.md`.
