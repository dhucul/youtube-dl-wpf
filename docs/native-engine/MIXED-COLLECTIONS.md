# Mixed-host collection playback — 0.138.0

Version 0.140.0 adds durable completed-fragment recovery for static HLS and segmented/JSON/indexed DASH, fresh provider/track/membership checks, strong server-validator rechecks, local SHA verification and safe publication after retry. All 2,274 native and 74 app checks passed, including 45 new checks. Three public interrupted/recovered adapter workflows produced ten fully decoded files and ten exact ordered packet inventories. Brightcove/JW each reuse three validated network fragments; Vimeo lacks media validators and safely retrieves all 94 media fragments again, reusing only fresh matching initialization. Full coverage, live/SABR and wider recovery, production cutover and user-deferred private evidence remain open. See [adaptive recovery verification](ADAPTIVE-RECOVERY.md).

The native engine now records the tested public Nowness Vimeo member through the issuer's literal player configuration and static JSON segment inventory. Inspected Nowness, Wistia, JW and Brightcove collections revalidate selected membership and fresh playback before transfer. Repeated source positions retain occurrence identity; unavailable or changed selected members cannot publish stale media.

## Implemented scope

- Vimeo configuration requests carry the actual publisher/caller referrer. An exact configuration endpoint returning 403/404 may be followed by the exact issuer player page. One bounded literal window.playerConfig object must retain the video ID; computed state, duplicate fields/configuration objects, changed resources, unsupported views and password gates fail. API bearer requests stay on the API route and never use this path after denial.
- The issuer's static playlist.json inventory supports explicit clear MP4 video/audio tracks, relative clip/track addressing, complete contiguous segment timing, bounded initialization and track/segment counts. Inline initialization is written inside ordered transfer staging, without a synthetic network request. MP4 initialization with protection markers is refused; protection descriptors and foreign/escaped addresses fail. Other JSON, live JSON, containers and advanced layouts remain unsupported.
- Renewal retains stable track/segment facts and the initialization digest. A rejected generation refreshes issuer configuration and the observed resource graph once; changed initialization or revoked resources fail before publication. Referrers remain consistent through configuration, playback and renewed manifests.
- Complete collection snapshots carry member IDs distinct from hosted media IDs. The chosen source occurrence is rechecked against a fresh complete inventory even when its child URI is stable. Original selected numbering survives reordering. Removed/unavailable members, switched hosted identity or changed duplicate occurrence counts fail without loading a stale child.
- Nowness accepts repeated consistent post references and distinct posts sharing a hosted video. Unsupported explicitly named hosting retains an unavailable collection position; missing/malformed hosting is refused. Public single-story unsupported hosting fails explicitly.
- Wistia channel episodes bind stable episode identity separately from media hash, allowing the same media in different episodes. Legacy members retain numeric membership identity and unavailable positions. JW/Brightcove inspected selections use fresh materialized occurrence leases, preserving playlist credential scope and per-video archives.

## Verification

All **2,229 native checks** (1,626 core and 603 media), **74 app checks** and **63 focused checks** passed. The batch adds 34 core and 22 generated-media checks. Release build: zero warnings and errors. All ten public source/output files fully decode; all ten nonempty ordered main/original/component coded-packet inventories match.

| Public inspected selection | Main bytes | Original bytes | Decoded seconds | Video frames | Audio frames |
| --- | ---: | ---: | ---: | ---: | ---: |
| nowness-vimeo | 19301281 | 19301195 | 278.400 | 6674 | 13050 |
| nowness-brightcove | 9732945 | 9732907 | 133.793 | 3343 | 5762 |
| wistia-channel | 8478372 | 8478513 | 241.418 | 5788 | 10397 |

The full Nowness inventory contains 21 positions across three pages. Selected positions 1 (Vimeo 148162161) and 3 (Brightcove 2516061569001) completed. Wistia selected position 33 of 205 (nwkeyx5swc) completed. Vimeo retains 10,312,376-byte video and 8,969,282-byte audio components; Brightcove retains 7,566,470-byte video and 2,171,294-byte audio components. Archive repeats make only 4/4/1 metadata requests, with zero media or caption requests and no callbacks. Vimeo retains all 47 segments per selected track, including inline initialization in the original files.

Generated source checks retain all 36 video frames and every ordered audio/video packet. Cases cover literal player retrieval, wrong/computed/repeated player state, access failure, JSON clip/track/timing/protection limits, repeated/unavailable memberships, inspection changes, reordered selections, fresh materialized leases, forced signed-resource renewal, changed initialization, original retention, archive/callback protection and cancellation. Scoped Brightcove playlist renewal and repeated JW occurrence renewal are tested after inspection.

## Remaining work and observed failures

The Nowness YouTube member at position 15 (_jW-7ikH-cU) resolved metadata but its native playback session encountered a server authorization challenge. It did not publish a completed recording. The failed attempt is retained; this is not verified YouTube collection playback. The separate Vimeo member 853640441 continues to deny its actual player/configuration resources. This batch does not grant access to that member.

Public Vimeo JSON playback and controlled forced renewal are verified separately; sustained remote expiry/renewal, real private accounts, remote grants and signing remain unverified. Real-account verification remains deferred by the user's instruction. Wider Vimeo JSON codecs/containers/remote-init/protection/layouts, language/alternate-audio selection, additional Nowness locales/private/preview routes, Wistia registration/collection grants, captions/live variants and durable restart recovery remain in the backlog.

The full frozen scope remains 1,731 extractor keys, 126 app options and 276 captured long options. There are 107 partial keys, 1,489 keys without key-specific evidence, 135 broken-key decisions pending, 62 registered providers, 13 globally unmapped settings, five provider-scoped authentication settings, 113 captured typed handlers and zero complete parity claims. Production still starts YtDlpEngine; native cutover and standalone packaging await the original acceptance gates. All prior uncommitted work is preserved.

## Evidence and primary references

Public verification: work/mixed-collections-probe/bin/Release/net10.0-windows/verified-0.138.0-final/verification.json. Independent evidence: work/mixed-collections-batch/public-decoding.json and public-packets.json. Failed public attempts, focused/full/app logs and SHA preservation audit remain in that batch directory.

The protocol implementation comes from observed issuer-owned player/segment documents. First-party references: [Vimeo video file links](https://developer.vimeo.com/api/files/video-links), [Vimeo oEmbed](https://developer.vimeo.com/api/oembed/videos), and [Vimeo referrer-policy troubleshooting](https://help.vimeo.com/hc/en-us/articles/35817429341457-Troubleshooting-video-playback-errors-due-to-referrer-policy-conflicts). No yt-dlp extraction source is used.

