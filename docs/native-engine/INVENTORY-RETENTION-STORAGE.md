# Inventory retention, legacy locations and storage concurrency

Version 0.162.0 broadens completed-output inventory storage and legacy lookup. The full native replacement remains open.

## Retention and storage

Writers acquire a cancellable process-shared file lock keyed by the canonical inventory path before reading, retaining, replacing or cleaning metadata. This prevents one writer's cleanup from removing another active writer's temporary file. Workspace/destination locks retain their existing order; inventory storage never takes them in reverse.

A differing valid current record is saved as a digest-named `.retained.json` snapshot before atomic replacement. The snapshot preserves its schema, checksum, recipe, format facts, policy and complete path list. Identical writes do not grow retention. Schema-one upgrades also retain the original record. Retention has a 64-record bound per inventory; reaching it preserves existing records and returns a storage failure. There is no automatic deletion of snapshots or media. Historical snapshots are evidence, not an alternate output set: they cannot justify skipping a new capture, combine old originals with a new main file, or override a present current record.

Reads enforce the eight-MiB limit on the opened file, including replacement between checking a path and opening it, and reject growth beyond that bounded snapshot. Pending-write cleanup takes at most 65 names, validates the entire set before deletion and only removes strict GUID-named ordinary files for that inventory. Unknown names, changed scopes and invalid retained evidence remain errors. Cancellation, unavailable storage, locked files and retention-limit failures leave the previous current record in place. A failure after atomic replacement leaves the complete new record available for replay.

Committed publication keeps its receipt until inventory persistence succeeds. Injected storage outages during retention and current-record writes do not undo successful media. Recovery validates the committed media, retries metadata and finishes cleanup. Already claimed completion callbacks remain claimed; this does not add an exactly-once external-callback guarantee.

## Legacy locations

Lookup checks the current anchor first. A present current record is authoritative, including when corrupt or incomplete. If it is absent, lookup checks the configured output root, the literal template root and at most 16 intermediate ancestors within those roots. This covers nested, absolute and relative-parent templates without scanning arbitrary volumes or accepting a different output identity.

Legacy records must have a valid checksum and the same scope, normalized recipe and selected-format facts. Duplicate records must agree; conflicting or unreadable legacy evidence uses normal checks. Complete requested outputs allow a no-overwrite skip. A complete full set also permits migration to the current anchor and schema two, preserving the source record. A main-only projection with a missing original can skip its existing main but does not migrate the incomplete full set. Archive collisions, links, unavailable records and changed requests remain conservative.

## Verification

All **2,816 native checks** (1,977 core + 839 media), **76 app checks** and **60 focused checks** passed. Release build has zero warnings/errors. The batch adds 41 core and 12 media checks. Fresh public HLS/DASH captures survive injected local inventory storage outages; those captures and two relocated schema-one fixtures each pass six simultaneous recovery/repeat requests with zero HTTP and no new callbacks. Sixteen byte-preserved files independently fully decode, and eight exact ordered main/original coded-packet inventories match. The public probe engine SHA matches the final binary.

The batch adds 41 core checks and 12 generated-media checks. They cover preserved weaker/changed records, schema-one retention, size/retention/pending limits, blocked or locked metadata, canceled writers, injected half-write/flush/replacement failures, process interruption, process-shared serialization, conflicting legacy locations, nested/absolute/relative-parent templates, unavailable legacy records, missing originals and six simultaneous native requests. Media checks fully decode and preserve existing file hashes during recovery/migration.

Public proof uses fresh native HLS and DASH captures with an injected local inventory retention-write outage, plus two controlled relocated schema-one fixtures copied from those fresh captures. Six simultaneous recovery/repeat requests per workflow use a nonexistent helper and a 24-hour delay, make zero HTTP requests and deliver no new callbacks. Sixteen files remain byte-identical through recovery/migration, independently fully decode, and eight exact ordered main/original video/audio packet inventories match. The probe binary matches the final repository engine. No saved account is accessed.

Evidence lives in `work/inventory-retention-batch` and `work/inventory-retention-probe/bin/Release/net10.0/verified-0.162.0`. Injected IOException/lock/cancellation failures establish local storage contracts; they do not establish actual exhausted-disk, SMB/NFS, changed mount, permission inheritance or hardware-power-loss behavior.

## Remaining work

Retention-limit management, media fingerprints/lineage for historical inventories, legacy changed anchors/jobs, cross-source/process destination publication and additional filesystem/network-share/permission/full-disk/remote-local interleavings remain open. Next are broader live overlap/period and provider/session/player/proof/signing/default/locale coverage, followed by full catalog/options and packaged-production acceptance. Real-account verification remains user-deferred. Production still uses YtDlpEngine. Frozen scope stays 1,731 extractor keys, 126 app options and 276 long-option names, with no new full-parity claim.


