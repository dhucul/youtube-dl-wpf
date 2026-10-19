# Automatic audio and adaptive no-overwrite inventories

Version 0.156.0 extends the native engine's complete-output check to automatic audio, unresolved merge-container preferences, static DASH and declared SABR playback. The full yt-dlp replacement remains unfinished, and production startup still uses its existing backend.

## Resulting behavior

- Automatic `best` and `copy` audio can resolve their final extension from a retained local source. Declared codec and standard-container facts can also establish the extension without transferring media. Shared audio planning retains Matroska/WebM delay and discard-padding behavior.
- When codec metadata cannot decide a merge preference such as `webm/mkv`, retained video and audio components are probed locally to choose the same container as a fresh merge. Unrelated files with another candidate extension are preserved.
- Static DASH parses the manifest and uses the same representation-selection helper as the downloader. It checks the main file and requested originals/components without requesting initialization, index ranges or segments. Component output templates use the selected representation identities.
- SABR uses its selected format descriptors to check the complete output inventory before sending media POSTs. A selected pair must share its playback context. Unknown codec choices can use retained local components.
- Missing retained files follow the normal download path and publish only the missing outputs under `NoOverwrites`. Invalid local probe evidence cannot establish an unknown choice. `NoPostOverwrites` with retained originals continues to transfer fresh originals/components.
- Existing partial transfer data, publication locks and archive collision checks retain their prior behavior. Unresolved DASH caption selections, including unrelated page languages and sidecar captions, defer the inventory check so a required original is not omitted.
- Expired Kaltura playback links defer preflight to the existing downloader path, preserving its publisher-offered alternate-profile recovery.
- Concurrent completion discovery tolerates optional recovery records and ancestor folders removed by a finished job. Changed, malformed and linked records still fail validation. Stress checks exercise three readers while 150 workspaces or ancestor folders disappear.

## Validation

All **2,669 native checks** (1,892 core + 777 media), **76 app checks** and **80 focused checks** passed. This batch adds 41 core and 32 generated-media checks. Release build has zero warnings/errors. Five fresh public native Wistia/DASH workflows and two controlled retained-public-source SABR fixtures each produce zero repeat media requests, zero media bytes and zero new-file callbacks. All 13 unique files remain byte-identical across repeats and independently fully decode. Six exact ordered packet inventories and two exact AV1 coded-OBU inventories after temporal-delimiter removal preserve complete ordered source data. Metadata or the selected DASH manifest may still be read. The probe binary matches the final engine SHA; configured 24-hour download delays are bypassed on these skips. Twelve bursts of six concurrent native jobs and ten repeated disappearing-record/ancestor stress runs also pass (1,500 workspace removals and 1,500 ancestor changes). No saved account is accessed. The SABR retained-source fixtures do not verify fresh YouTube issuer authorization.

The 41 new core checks cover declared automatic audio formats, template paths, complete and incomplete inventories, SABR context/selection constraints, static versus dynamic DASH, unsupported/DRM manifests, caption uncertainty and expired-profile fallback. The 32 new media checks cover actual local audio inference, unknown merge preferences, invalid retained files, missing-file repair, post-overwrite policy, nested representation templates, segmented DASH and SABR. They verify zero media transfers for complete sets, byte preservation, full decoding and ordered encoded-packet preservation.

Five fresh public native workflows verify Wistia automatic best/copy audio and DASH-IF static paired, main-only and automatic-audio jobs. Two additional controlled metadata fixtures reuse previously captured public YouTube SABR source files to exercise declared and unknown-codec inventory inference. These two fixtures do not establish fresh YouTube issuer authorization. No saved account is accessed.

Evidence lives in `work/adaptive-no-overwrite-batch` and `work/adaptive-no-overwrite-probe/bin/Release/net10.0/verified-0.156.0`. The public probe binary must match the final repository engine binary. Independent verification records distinguish six exact packet inventories from two exact AV1 coded-OBU inventories after removal of the temporal delimiter (`1200`). AV1 remuxing does not support a blanket packet-byte-identity claim.

## Remaining work

Unknown output choices without retained source files or sufficient metadata still need transfer-time resolution. Live and HTTP-sequence output inventories, manifest reuse after an incomplete check, broader storage/concurrency/recovery and provider-session contracts remain open. The original scope remains 1,731 extractor keys, 126 app options and 276 long-option names; no provider gains a full-parity claim from this batch. Real-account verification remains deferred by the user. Catalog coverage, native production cutover and packaged acceptance are still required.
