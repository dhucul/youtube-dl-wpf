# Representation transitions and provider contracts — 0.166.0

This batch extends the independent C# engine. Full replacement remains open and production startup continues to use YtDlpEngine.

## Representation selection

Live DASH can keep the selected track across a new period whose representation ID changes. An exact ID takes precedence. Otherwise, the successor must be unique under the same explicit adaptation ID and preserve MIME type, codec, dimensions, frame rate, bandwidth, language, roles, channel configuration and sampling rate. Missing/ambiguous profiles, implicit groups and changed quality facts remain refusals. Once accepted, that period's representation ID is pinned; a later refresh cannot replace it with a similar ID. Old periods may leave the window without losing the accepted successor.

The parser and canonical reader retain width, explicit adaptation identity, language, role and audio-channel descriptors. Width, language and default-audio facts reach candidate selection. Renewal also guards these DASH facts. Actual decoder verification and clear period clipping remain in place; incompatible source layouts cannot publish success.

This is ordinary cross-period track selection, consistent with [dash.js period handling](https://dashif.org/dash.js/pages/usage/multiperiod.html). It does not interpret period-connectivity signaling as permission to rename representations; the [DASH-IF timing model](https://dashif.org/Guidelines-TimingModel/) requires identical representation sets for connected adaptations.

A literal ID/selector choosing an outer DASH manifest is now consumed before selecting its inner tracks. It no longer incorrectly asks the inner manifest for the outer source's ID. More complex selector grammar and precedence remain in the broader option audit.

## Provider/session behavior

Authorization or observation marks a resource root as used for this job. Used media/caption identities remain required during renewal. Missing, expired, ambiguous or changed used resources reject the entire generation and preserve prior mappings. An unused original format/caption may disappear, change or expire without interrupting the used recording. Its old aliases stay inside authorization handling and explicitly refuse reads; they cannot silently fall through to an unregistered request. A later valid generation can restore an unused resource only with its original identity and format facts.

The Wistia/JW/Brightcove/Kaltura shared hosting helper matches used resources strictly while allowing unused inventory churn. Cached hosting metadata reports the accepted live state. The shared lease also guards audio language/default role. This tracks resources actually used; proactive pinning of future selections before their first use is still a separate contract.

A live-to-ended metadata update requires a fresh, validated adaptive graph for every used media root. HLS must expose completed leaf playlists, not just a master. DASH must expose segments through its declared final period end. Open, unobserved, progressive-only and incomplete endings fail without changing the accepted live state. An ended session cannot revive. Root/graph updates and the live-state change retain the prior atomic commit. The clear adaptive YouTube helper permits this proof-based path, while SABR renewal keeps its existing strict live-state boundary.

No new provider login protocol or real-account evidence is claimed. Existing issuer identity, access, redirect, credential-scope, cancellation and disposal protections remain active.

## Verification

All **2,936 native checks** (2,053 core + 883 media), **76 app checks** and **73 focused checks** passed. Release build has zero warnings/errors. The batch adds44 core and22 media checks. Two fresh public native workflows use controlled per-period representation renaming/unused-format churn and complete-snapshot ending metadata. Both perform24 fixture renewals, recover one injected media refusal, and repeat archives with zero HTTP/callbacks. Ten public/source files independently fully decode and four exact ordered main/original packet inventories match. Public/final engine SHA values match.

The batch adds 44 core and 22 generated-media checks. They exercise renamed, dropped and pinned periods; ambiguous profiles; width/height/codec/bandwidth/language/role/channel/sample-rate mutation; exact-ID precedence; unused retirement/restoration; used-resource refusal; ending proof, declared-end coverage, revival and concurrency; four hosting consumers; and the YouTube clear adaptive helper.

Native paired/audio-only/video-only recordings and a clipped boundary preserve source roles, complete encoded source periods and all expected decoded video frames (80 frames, or 70 for the clipped layout). Root ID/selector fixtures verify the corrected selection boundary. Failure cases preserve prior media, archives and zero completion callbacks, including a late closure that would remove already captured complete segments. Successful archive repeats issue no HTTP or new callbacks.

Public proof uses fresh native recordings backed by actual public assets. The DASH manifest fixture renames representation IDs by period while preserving original resource addresses and declared profiles. The HLS issuer fixture removes an unused format and later serves the last observed complete media snapshots with ENDLIST before returning ended metadata. Both workflows perform 24 preliminary fixture renewals and one injected media rejection/renewal, retire the unused format and repeat archives without HTTP/callbacks. The transformations, issuer and refusal are controlled fixtures, not real provider/account verification. No saved account is accessed.

The final probe binary must match the repository engine. Independent decoding and ordered main/original packet comparisons verify the published media. Evidence lives in `work/representation-contract-batch` and `work/representation-contract-probe/bin/Release/net10.0/verified-0.166.0-final`. Interim logs and the earlier public capture are preserved.

## Remaining work

Changed adaptation identities, adaptive quality policy, caption identity transitions, advanced decoder/color/codec changes, cross-session renewed/shared-graph recovery, proactive selection pinning and additional real provider/player/proof/signing/login contracts remain open. VOD/SABR handoff, late closures removing captured complete segments, indexed/non-MP4/low-latency modes, defaults/locale/full selector grammar and full catalog/options/packaged-production acceptance follow. Inventory lineage/limits, legacy changed anchors and cross-source/storage interleavings stay on the backlog. Real-account verification remains user-deferred. Frozen scope stays 1,731 extractor keys, 126 app options and 276 long-option names, with no new full-parity claim.
