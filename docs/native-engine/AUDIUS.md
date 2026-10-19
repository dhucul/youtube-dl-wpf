# Native Audius verification

Native engine 0.94 independently implements Audius public tracks, artist uploads, playlists and albums in C#. It uses no Python, yt-dlp runtime, imported extractor, or fallback. The frozen `Audius`, `audius:track`, `audius:artist` and `audius:playlist` keys remain partially covered. Production WPF cutover and full site/option parity remain unfinished.

## Public protocol and scope

Supported inputs include canonical Audius track/artist/playlist/album URLs, direct public API resource IDs and `audius:TRACK_ID`. Scheme dispatch is isolated to the native provider and preserves existing LBRY routing. Private/API-key/preview/manual-page requests and unsupported website modes do not fall through to generic whole-track extraction.

The provider uses anonymous read-only API requests. Canonical URL resolution may redirect only to matching Audius ID or permalink endpoints; returned resource IDs, artist handles and permalinks must agree. Track/artist availability, deletion, scheduled-release, streamability, gates, access and API-key restrictions are checked explicitly. Gated or preview access cannot become a complete public track.

Fresh supplied content-node stream URLs retain their literal signatures. The content path must match the track CID; public descriptor data must bind that CID, the numeric track ID and anonymous user zero. The client does not generate or decrypt authorization signatures; the serving node validates them. When the response lacks a supplied stream URL, the documented public track stream endpoint retains the requested ID. MP3 decoding, complete transfer, audio roles and declared duration are verified before publication. Native original retention, metadata/artwork, audio conversion and track archives use the shared pipeline.

Public artist inventories bind their owner, offsets, declared track count and a final owner/count recheck. Complete playlist membership comes from explicit ordered track IDs, rather than guessing from only returned playable tracks. Missing/gated entries retain positions; repeated memberships remain distinct positions. Foreign/conflicting returned tracks and configured limits fail explicitly. Inspected selections refresh only chosen track metadata and signed playback; archived IDs avoid playback discovery.

The [official API](https://docs.audius.co/api/), [track documentation](https://docs.audius.co/sdk/tracks/), [resolver documentation](https://docs.audius.co/sdk/resolve/), [artist documentation](https://docs.audius.co/sdk/users/) and [playlist documentation](https://docs.audius.co/sdk/playlists/) describe these read/resolve/stream/membership operations. Current anonymous responses were inspected on October 10, 2026.

## Verification

Twelve core checks cover public facts, URI schemes/aliases, access and route boundaries, resource/artist/CID/descriptor identity, gates and unavailable states, ordered playlist memberships, missing/repeated members, complete artist counts, snapshots, archives, anonymous metadata, cancellation, denial, malformed JSON and scoped resolver redirects. Five generated-media checks fully decode normal/selected/inspected/converted audio, retain exact originals and encoded payloads, and refuse incomplete sources without changing prior output or recording success.

Live workflows used [NØCTRIS, techcorporal — Deeper](https://audius.co/noctrismusic/n%C3%B8ctris-techcorporal-deeper), ID `XB4rzbJ`. Its website URL, native ID, inspected artist selection and conversion were exercised. Source MP3 was 4,470,849 bytes; processed MP3 4,470,911 bytes. Both contained 4,656 decoded audio frames over 111.697854 seconds. All 4,656 encoded packet payloads/order and complete decoded PCM matched after metadata processing. AAC M4A was 2,237,227 bytes with 5,236 decoded frames over the same duration, retaining its original MP3.

The native reader completed all 15 uploads for [noctrismusic](https://audius.co/noctrismusic), with the selected track at source position one. It also completed the 15-entry [My Friday vibe playlist](https://audius.co/lottaboom/playlist/my-friday-vibe). A selected playlist track at original position two published fully decoded MP3/original files of 4,362,451 and 4,362,407 bytes, 4,543 audio frames and 108.987583 seconds. Initial successful jobs reported two file completions; archive repeats reported zero. Two native-ID attempts encountered I/O transport errors; a subsequent bounded retry with fresh metadata completed the same track and original with two initial completions and zero repeat completions. No specific transport cause was established. Ten live files across the five successful workflows fully decoded.

Scratch evidence is under `work/audius-probe` in the Codex workspace. The harness and public response captures are not production dependencies; public opaque stream signatures should not be copied into diagnostic output.

## Remaining work

Paid/gated/account/API-key workflows, originals/stems, additional storage/stream formats, unavailable artist/count variants, other artist feeds, large changing inventories and full site/option parity remain pending. Descriptor checks do not perform client-side cryptographic signature verification. Production cutover, old dependency removal and packaged verification remain pending.
