# Rutube family and native public-script cache

Version 0.194.0, October 16, 2026. Full replacement remains open.

## Batch

Seven previously missing Rutube keys gain scoped public video/embed and collection support. Native inventories cover person/channel, show, numeric tag and custom playlist URLs. Selected members obtain fresh ordinary playback, bind archive identities and revalidate inspected membership. The shared native progressive/HLS/DASH pipelines handle transfer, renewal, original retention, cancellation and protected publication. Equal-quality CDN mirrors discovered in the public HLS master are resolved by exact path/query equivalence inside the Rutube provider; shared graph identity safeguards stay intact. See [Rutube contracts](RUTUBE.md).

The classic `General.CacheDir` setting and two captured long names (`--cache-dir`, `--no-cache-dir`) now work through saved settings, inspection and per-job precedence. An explicitly configured directory enables only public versioned YouTube player-source storage in the owned `native-public-player-v1` child directory. The default continues without disk caching. Every hit validates schema, engine version, exact source URL, UTF-8 size, SHA-256 content and a 30-day age bound. Each use creates a separate bounded runtime session. Writes use unique temporary files, flush and atomic replacement. Malformed/stale/unusable cache data fetches fresh source; storage failure does not prevent a fresh playable result. Reparse paths are refused for cache storage. Waits, HTTP, runtime creation and writes honor cancellation.

Watch/player responses, credentials, cookies, selected media, manifests and signed playback links are never serialized into this cache. Authenticated contexts, explicit cookie selections, custom headers and query-bearing script URLs bypass it. Opaque injected cookie jars that cannot be anonymized also bypass storage. Cache fields are operational and excluded from request/recovery serialization, preserving media recovery when cache preferences change. Existing default policy, request JSON, publication recipe and video/audio recovery identities match the verified 0.192.0 engine.

## Verification

All **3,925 native checks** (2,746 core + 1,179 optional media), **149 app checks** and **95 focused checks** pass on the frozen candidate. Release has zero warnings/errors. All old/new default request, publication recipe and video/audio recovery scope comparisons pass. The complete original 896-file working baseline is accounted for; 878 files outside the 18 staged existing paths remain byte-identical, 27 current source/doc hashes verify and 905 files remain uncommitted. HEAD and production startup are unchanged; no reset/stash/commit/push/cutover.

The batch adds 77 core contracts, 11 generated-media cases and six app cases. Nine retained positive workflows cover all five collection types, an embed, mid-transfer HLS/DASH renewal and a YouTube download whose fresh watch page uses cached public source. Independent FFmpeg/ffprobe verification fully decodes 20 generated outputs/originals, matches 38 ordered source stream inventories and 684 video pictures. Archive repeats fetch no selected playback/media and emit no new callbacks. Changed identities, access, pagination, mirror facts or manifest shape preserve prior publication. Concurrent cache callers share source fetches but receive independent interpreter sessions; corruption/hash/age/identity/runtime/IO/cancellation/private-context and saved/analysis/job controls are tested.

Public native video and embed workflows producedfour complete TS files, with exact ordered main/original video and audio packet inventories, 10,494 coded picture NALs and 15,086 AAC frames across both workflow comparisons. All four fully decode. Public person/channel inventories each have 549 entries and the custom playlist hasfour. First attempts refused estimated page growth and mirror ambiguity; those results and exact first-party responses are retained, then the actual contracts were implemented and reverified. The independent generated verifier's initial file-count assumption missed the additional retained DASH combined original; all actual 20 files / 38 streams passed comparison and the count was corrected without changing production code.

## Remaining scope

Seven new subsets bring the frozen catalog to 125 partial rows, 1,471 without key-specific evidence and 135 broken decisions pending, with zero complete-parity claims. There are 63 registered providers. All 1,731 keys, 126 classic settings, 276 captured names and 18 separate native WPF extensions remain intact. The explicitly refused classic settings fall from 13 to 12; typed long-name handlers rise from 113 to 115. Default startup remains YtDlpEngine, with immutable explicit native opt-in and no fallback on native refusal.

Prioritize the missing JioSaavn, QQ Music/NetEase and other multi-key families next, alongside argument/default/alias controls. Remaining classic gaps include geo controls, external downloader integration, AdobePass fields, fixup, postprocessor arguments, exec and SponsorBlock. Cache default-location/cleanup/eviction controls and other public provider consumers remain pending, as do broader Rutube live/account/caption/filter/rendition contracts. Continue core provider/session/player/proof/signing, long adaptive/recovery/storage/concurrency and remaining conversion work. Full frozen-scope and packaged-cutover acceptance remain blocked. Real-account verification stays user-deferred; no saved profiles or credentials were read.

## Evidence

- `work/provider-cache-batch/`: build, full native, WPF, 95 focused, compatibility and independent logs/exits, original/applied hashes and preservation audit.
- `work/provider-cache-probe/bin/Release/net10.0/test-data/verified-0.194.0/verification.json`: nine generated native workflows.
- `work/verify-provider-cache.py`; `independent-{summary,decodes,inventories}.json`.
- `work/provider-cache-public/bin/Release/net10.0/public/observed-0.194.0-mirrors/verification.json`: public playback and complete collections; exact URLs are public temporary evidence, not provider fixtures.
- `work/verify-provider-cache-public.py`; `public-independent-{summary,decodes,comparisons}.json`.
- `compatibility-0.192.json`: independently loaded old/current assemblies compare defaults and request/publication/recovery serialization.
