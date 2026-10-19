# Output inventory publication recovery and legacy migration

Version 0.160.0 closes the gap between committing live media and saving its completed-output inventory. The full replacement and production cutover remain open.

## Recovery behavior

The publication receipt now includes the optional inventory plan before output installation. It binds the reserved inventory destination, workspace scope, normalized job/format digests and original/post-processing policy. The actual published output list supplies paths, including retained source and clipping parts. Playback links and credentials are excluded.

After commit, the normal publication path saves a version-two inventory. Recovery can rebuild it after interruption during commit, inventory writing or replacement, archive completion, notifications or cleanup. The receipt is validated before replay. Matching completed records avoid needless rewrites; partial inventory temporary files are cleaned with bounded ordinary-file checks.

If inventory persistence is temporarily blocked, committed media stays successful and its completion receipt remains available for retry. A changed job cannot discard that evidence before the prior inventory recovers. Linked destinations, output/archive collisions, changed media, missing outputs and invalid intent remain errors. The existing callback contract remains: recovery can deliver the original committed job's pending callbacks; subsequent complete-set no-overwrite repeats create no callbacks.

## Legacy migration

Valid version-one inventories keep their original field order and digest compatibility. Matching scope, normalized recipe, format facts, policy and a complete ordinary-file output set allow an atomic upgrade to version two. A main-only request can reuse a previously retained set, but an incomplete full set is not upgraded merely because its main file exists.

Matching legacy committed receipts with known completion context can record a reconstructed inventory intent for bounded/live-from-start jobs. Already archived receipts may also migrate. An unknown, unarchived original completion context is not invented. Files without a valid inventory or trusted committed receipt continue through normal checks; presence alone does not prove variable live period/source closure.

## Validation

All **2,763 native checks** (1,936 core + 827 media), **76 app checks** and **46 focused checks** passed. Release build has zero warnings/errors. The batch adds 21 core and 18 generated-media checks. Two force-stopped fresh public native HLS/DASH workers and one relocated version-one public-source fixture recover/upgrade with **zero HTTP requests**. The stopped jobs deliver four pending callbacks each; later repeats and the legacy upgrade produce none. All 12 media/source files remain byte-identical, independently fully decode, and six exact ordered main/original video/audio packet inventories match. All recovered/migrated records use schema two; publication receipts and partial inventory writes are cleaned after successful persistence. Repeats use a nonexistent helper and a 24-hour delay to prove both are bypassed. The public engine SHA matches the final repository binary. No saved account was accessed.

The batch adds 21 core and 18 generated-media checks. Workers are forcibly stopped at preparation, installation, commit, inventory half-write/flush/replacement, archive, notification and cleanup boundaries. Tests verify committed output preservation, uncommitted rollback, edited/missing-file refusal, invalid intent rejection, blocked metadata retry, unknown-context handling, version-one upgrade, nested/absolute paths, changed jobs and archive-fast completion. Real media outputs fully decode and remain byte-identical through recovery and migration.

Public evidence uses two fresh native HLS/DASH captures stopped at commit and inventory writing, plus a relocated version-one fixture made from previously verified public HLS files. Recovery and later repeats request zero HTTP. The two stopped jobs complete their pending four-file callbacks; later repeats and the legacy migration produce none. All files independently fully decode and ordered main/original packet inventories are compared. The legacy fixture establishes migration behavior, not a new issuer/account verification.

Evidence lives in `work/inventory-recovery-batch` and `work/inventory-recovery-probe/bin/Release/net10.0/verified-0.160.0`. The public probe binary must match the final repository engine. Failed fixture artifacts are preserved in diagnostics. No saved account is accessed; real-account verification remains user-deferred. No hostile-local-user, hardware-power-loss or exactly-once external-callback guarantee is made.

## Remaining work

Broader inventory retention and legacy location/migration, storage/concurrency failure interleavings and remote/local failure contracts remain open. Live overlap/period and provider/session/player/proof/signing/default/locale coverage follow, along with full catalog/options/packaged-production acceptance. Frozen scope remains 1,731 extractor keys, 126 app options and 276 long-option names, with no full-parity claim added.
