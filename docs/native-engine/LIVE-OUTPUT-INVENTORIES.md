# Live and HTTP-sequence output inventories

Version 0.162.0 serializes inventory writes across processes, retains previous valid records before replacement and bounds opened-file reads and temporary cleanup. Matching legacy inventories in configured/literal roots and bounded intermediate folders migrate without deleting their source; present current records are authoritative and conflicting legacy evidence remains conservative. All 2,816 native checks (1,977 core + 839 media), 76 app and 60 focused checks passed; Release build has zero warnings/errors. Fresh public HLS/DASH storage-outage recovery and two relocated schema-one fixtures each pass six concurrent requests with zero HTTP or new callbacks; 16 files fully decode unchanged and eight ordered main/original packet inventories match. Full replacement remains open. See [inventory retention and storage](INVENTORY-RETENTION-STORAGE.md).

Version 0.158.0 extends complete-output no-overwrite checks to live recordings and declared HTTP sequences, and reuses freshly parsed initial manifests when an incomplete output set needs transfer. Full replacement and production cutover remain open.

## Behavior

A successfully published live HLS/DASH recording saves a small optional inventory beside its output anchor. It contains a schema, normalized job/format digests and actual published paths, including retained source components and any clipping-period parts. It contains no playback links or credentials. Media callbacks and visible output lists exclude this internal metadata.

For a matching job, `NoOverwrites` checks the complete recorded output set before reading a manifest, invoking a media helper or applying the download delay. A main-only request can project a previously retained set down to its main output. Requests upgrading to retained originals need an inventory that actually includes them. `NoPostOverwrites` with retained originals continues to obtain fresh raw originals/components; main-only post protection applies only to a recorded processed output.

Missing requested files, invalid/corrupt/unreadable records and changed recipes or format facts use the normal transfer path. Records are atomically replaced after media publication; an interrupted or failed optional inventory write does not undo committed media. Legacy live files without a usable inventory conservatively use normal checks. Inventory metadata does not replace publication recovery or establish hostile-local-user or hardware-power-loss integrity.

HTTP-sequence inventories use the same part validation as transfer: bounded count, supported MP4/FLV container, HTTP(S) addresses, valid durations and optional sizes. Complete sets include the joined main, an original when processing modifies it, numbered `.part`/`.part-2` sources, and selected video/audio components for supported pairs. Missing parts still download and publish only the missing outputs under no-overwrite policy.

HLS master, selected video/audio and DASH manifests read during preflight pass into the actual download within the same job. Each prepared snapshot is consumed once. Static snapshots have a five-second maximum age; live snapshots use a shorter target/update-period bound. Near-expired resource hints force a new read. Subsequent live polling and playback-authorization renewal keep their existing fresh readers. No metadata is shared across jobs.

## Validation

All **2,724 native checks** (1,915 core + 809 media), **76 app checks** and **62 focused checks** passed. Release build has zero warnings/errors. The batch adds 23 core and 32 generated-media checks. Two fresh public native live HLS/DASH workflows and one controlled HTTP-sequence using actual public Wistia downloads each repeat with **zero HTTP requests, zero media bytes and zero new-file callbacks**. All 12 media/source files remain byte-identical, independently fully decode, and six exact ordered main/original video/audio packet inventories match. Both live recordings save one internal inventory; HTTP sequences use their declared part inventory. Repeats use a nonexistent helper and a 24-hour delay to prove both are bypassed. The public engine SHA matches the final repository binary. No saved account was accessed.

The batch adds 23 core and 32 generated-media checks. They cover MP4/FLV sequences, automatic/explicit audio and paired tracks, original/numbered part paths, nested templates, archive and post-only policy, invalid/incomplete inventories, initial manifest handoff, and aged live snapshot refresh. Real generated recordings exercise transport-stream and fragmented HLS, paired live HLS, dynamic DASH audio/video/pairs, changed jobs, corrupt records, missing-file repair, retained-output upgrades and concurrent repeats. Protected files are byte-compared and independently fully decoded.

Public evidence comprises two fresh native live captures from Unified Streaming and one controlled two-member HTTP-sequence inventory using real public Wistia media. The sequence fixture verifies the transport and retained-file inventory; it does not establish a real issuer playlist or new Bilibili site coverage. All three repeat with zero HTTP, media bytes and new-file callbacks, using a missing helper path and a configured 24-hour delay to prove both are bypassed. Independent evidence records full decoding and ordered main/original packet inventories.

Evidence is in `work/live-inventory-batch` and `work/live-inventory-probe/bin/Release/net10.0/verified-0.158.0`. The public probe engine hash must match the final repository binary. No saved account is accessed; real-account verification remains user-deferred.

## Remaining work

Older live outputs and jobs interrupted before inventory persistence still need normal checks. Broader inventory migration/retention, storage/concurrency/recovery interleavings, overlapping live windows and period contracts remain open, followed by provider/session/player/proof/signing/default/locale coverage and complete catalog/options/packaged-production acceptance. The original scope remains 1,731 extractor keys, 126 app options and 276 long-option names. This batch adds no full-parity claim.
