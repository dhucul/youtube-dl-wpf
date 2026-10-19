# Live/SABR recovery and YouTube playback authorization — 0.142.0

The native C# downloader completed the previously failing public Nowness YouTube selection using a playback proof issued during an isolated browser session. Live HLS/DASH and SABR now retain bounded, source-bound recording journals across interrupted jobs. Existing uncommitted work and production startup remain preserved.

## Implemented scope

- Browser capture accepts a validated publisher referrer on the exact selected watch navigation. Existing account/network/header restrictions remain enforced. A real capture now waits for a matching browser playback response before returning a proof-bound session. It never clicks consent/sign-in pages or loads a pre-existing profile unless the caller explicitly selected cookies.
- C# SABR playback uses the observed configuration, client information, proof and browser user agent. Explicit server protection status 3 remains terminal. This batch does not discard a denial or relabel startup status as full authorization.
- Live journals bind source/track/window controls, segment identities, timing, ranges, initialization, key identity and payload hashes. A retry reloads discovery and requires the saved beginning to remain in the advertised window. Fresh server validators can reuse network fragments; otherwise the bytes are fetched again and checked against the prior payload digest.
- Separate live tracks retain their original shared start/end and stable staging names. Clear DASH projection carries stable period/representation/presentation identities. Fresh AES keys and requested caption resources are loaded by the current job.
- SABR journals retain only complete validated segments. A new job obtains a fresh playback session and replays the saved prefix from the server; identities, timing and byte hashes must match before continuation. Session cookies, endpoint URLs, client proofs and pending partial UMP state are not persisted or trusted across jobs. No network-prefix-skipping claim is made.
- Changed payload/timing, unavailable saved starts, malformed receipts and denied playback stop without publishing media or updating archives. Restart mode downloads fresh data. Successful publication removes the recovery workspace.

## Verification

All **2,296 native checks** (1,660 core and 636 media), **76 app checks** and **29 focused checks** passed. This batch adds eight core, fourteen media and two app checks. Release build: zero warnings/errors. All four public media/source files fully decode; three ordered packet inventories are byte-exact and the fourth retains every AV1 coded unit with the documented temporal-delimiter difference.

Generated-media tests exercise transport interruption, cancellation, live-window loss, changed live/SABR payloads, changed SABR timing, explicit denial, restart controls, synchronized live HLS with audio offset/static closure, and dynamic DASH recovery. Positive cases fully decode and preserve complete ordered source audio/video packets. Existing static recovery and process-termination evidence remains in ADAPTIVE-RECOVERY.md.

## Real public YouTube recording

The complete 21-position Nowness playlist was reloaded across three pages. Selected position 15, YouTube _jW-7ikH-cU, acquired an 87-byte Web-client playback proof in a new anonymous browser profile. C# SABR downloaded the complete recording; the browser did not perform the media download.

Four retained files independently fully decode:

| Output | Bytes | Decoded seconds |
| --- | ---: | ---: |
| Main MP4 | 4,163,806 | 200.226 |
| Original MP4 | 4,163,695 | 200.226 |
| Original video | 885,826 | 200.160 |
| Original audio | 3,241,202 | 200.226 |

All 5,004 video packets and their presentation times are retained. Main/original video and both main/source audio inventories are byte-exact. AV1 MP4 remuxing removed only the source packets' leading two-byte temporal-delimiter OBU, 1200; every remaining coded OBU byte matches in order. No coded frame loss is inferred from packed-packet hash differences. The source component retains the original encapsulation. Archive repeat makes four publisher metadata requests, with no media requests or callbacks.

The publisher inventory rounds this item to 204 seconds; the actual YouTube player reports 200 seconds. Verification uses complete recorded track output rather than that rounded publisher duration.

## Remaining work

Recovery after a rolling window has dropped the original start, renamed/removed DASH periods, changing end-boundary clips, expired unavailable resources, persistent caption state and abrupt live/SABR process termination need further evidence/contracts. SABR prefix replay currently retransfers the prefix; optional server-authorized skipping is not implemented. Broader proof/player layouts, live YouTube, real private accounts, long remote expiry and provider/account/region/default/catalog parity remain open. Private-account verification is still deferred by the user's instruction.

The frozen 1,731-key/126-app-option/276-long-option scope remains intact: 107 partial keys, 1,489 without key-specific evidence, 135 pending broken-key decisions, 62 providers, 13 globally unmapped settings, five scoped authentication settings, 113 typed handlers and zero complete parity claims. Production still starts YtDlpEngine; native cutover/packaging await the original acceptance gates.

## Evidence and primary reference

Public record: work/live-sabr-recovery-probe/bin/Release/net10.0-windows/verified-0.142.0-final/verification.json. Decoder output, AV1 coded-OBU/presentation-time and packet inventories, browser preflight, source hashes and regression/preservation logs remain in work/live-sabr-recovery-batch.

The AV1 delimiter behavior is described in [AV1 ISO Media File Format Binding, sample format](https://aomediacodec.github.io/av1-isobmff/#av1-sample-format). Playback metadata/proof came from the actual issuer-owned browser page and responses; no upstream extractor code or Python fallback is used in the native runtime.
