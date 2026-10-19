# Stable inspected playlist selections

Engine 0.74.0 adds native in-memory playlist selection snapshots and connects them to the WPF native adapter and queue. An inspected source position now retains its original media identity when a site reorders its listing. Playback discovery is refreshed rather than reusing inspected download addresses. The production app still uses its original backend pending full replacement coverage and packaging.

## Native engine contract

`InspectAsync` can return `NativeMediaInfo.PlaylistSnapshot`. Callers pass that object as `NativeDownloadRequest.PlaylistSnapshot` when downloading the inspected playlist. The constructor is internal, and the selection records are privately copied. Mutating the public inspection entries or returning another preview does not change that snapshot.

The snapshot is bound to its requested URL, provider and playlist mode. Its count must satisfy the current request's inventory limit. Invalid bindings fail before HTTP access, including before an archive pre-skip. Source positions, unavailable slots, inspected count, ranges, reverse/random order and selected-job numbers continue through the existing playlist pipeline.

For dedicated child-page links, the engine retains stable identities/links and fetches only selected children freshly. Archive records can skip them before playback lookup. Changed child IDs/archive identities fail before media transfer. Actual inspected embed referrers are preserved, while explicit current-job referrers take precedence. Session authentication still comes from the current request.

For direct resources or parent-resolved entries, the snapshot retains member identities and refreshes the parent before joining those identities to current playback records. It checks the parent identity, maps matching members back to their inspected positions and rejects ambiguous refreshes. Missing selected members remain unavailable; a new item occupying the old position is never substituted. The parent recheck can still occur for an archive retry. An empty selection does not refresh unrelated parent data.

Captured media configurations, playback cookies/proofs and authorization headers are not retained. Stable discovery references and needed referrers are retained privately. Snapshot properties are excluded from ordinary request/media-info/preview/options JSON; directly serializing the snapshot exposes only its count. It is an in-memory selection object, not a persisted queue or account-session format.

Playlists whose available members lack sufficient stable identities cannot receive a usable snapshot. Their provider identity coverage still needs improvement. Snapshots retain the availability shown at inspection; restored unavailable slots need a new inspection. Added playlist members are not included retroactively.

## Queue integration

The native adapter attaches the inspected snapshot to its preview. Each queued item's copied options retain that specific preview's snapshot, so shared option objects, later inspections and later edits cannot replace an already queued selection. A playlist-mode download passes the snapshot after applying current job overrides. A single-video/no-playlist override uses normal single-video discovery.

No user-facing option, dependency, settings migration or fallback process was added. The queue remains in memory as before. Persisting selections across application restarts and broader provider/authentication/alias compatibility still need work before full replacement cutover.

## Live verification on October 9, 2026

The normal `NativeEngine` inspected all 21 declared pins in the public [videos board](https://www.pinterest.com/gs2134189/videos/). Its returned snapshot selected known pin `626281891952504847` at inspected position 20. Each download used that native snapshot and fetched fresh individual pin metadata. No special provider wrapper or manually supplied capture was used.

| Offered path | Published files | Strict full decoding | Selection and repeat |
| --- | --- | --- | --- |
| `v720P`, no height ceiling | 1,503,547-byte processed MP4; 1,503,402-byte original MP4 | H.264 504×760, 180 video frames; AAC 127 audio frames; 6.000000 s | Correct pin ID, original position 20/count 21; two callbacks; zero archive-repeat callbacks |
| `vHLSV4`, maximum height 360 | 283,325-byte processed MP4; 283,254-byte original MP4 | H.264 234×354, 180 video frames; silent; 6.000000 s | Same inspected identity/position/count; two callbacks; zero archive-repeat callbacks |

All four files fully decoded. The offered legacy HLS master is video-only and is not claimed audio-equivalent to MP4; its audio compatibility gap remains. Thumbnail/info JSON and archive behavior passed, without Python, yt-dlp or personal cookies.

Fourteen deterministic checks cover reordered child and parent/direct discovery, expired-resource avoidance, missing/replaced/changed/ambiguous identities, URL/mode/limit binding, referrers, original source metadata, unavailable entries, cancellation, archive skips, empty selections, mutation isolation and JSON exclusions. Two generated-media checks retain full MP4/HLS frames/audio, captions, originals and inspected source positions. Two app checks exercise actual native queue handoff and independent job/preview/options ownership.

Remaining all-site/options coverage, unknown identity layouts, authentication, restart persistence, old-runtime removal, packaged verification and WPF cutover are still required.
