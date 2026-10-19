# SoundCloud inventories and playback renewal

Version 0.192.0, October 16, 2026. Full replacement remains open.

## Implemented batch

Public artist roots and `/tracks` aliases now return complete upload inventories; numeric `/users/{id}` resources bind the returned profile permalink and ID. Track `/recommended` URLs return related tracks. `/search` and `/search/sounds` support one bounded `q` query without other filters. Each inventory reads track metadata without requesting transcoding endpoints or media. Duplicate track IDs retain first-seen order; unavailable tracks retain positions. Search identifiers derive from the decoded query. Profiles bind upload owners, while related inventories bind their seed track.

Collection pages follow issuer-provided `next_href` cursors, with the original endpoint, public client and search terms pinned. Foreign hosts/paths, duplicate or unexpected query parameters, changed query identities, repeated/no-progress continuations, unsupported types and configured-limit overflow fail explicitly. Terminal pages may omit `next_href` or supply null. The implementation does not infer completeness from short page length: an observed public profile returned a short first page and a valid continuation.

Inspected selections revalidate complete membership before opening a selected track. Reordering preserves the inspected output position. Removal, changed identities, unavailable access, failed renewal and cancellation protect existing output/archive files and callbacks. Existing sets gain the same membership and selected archive bindings. Normal controlled track sessions use the shared clear progressive/HLS renewal contract, with SoundCloud CDN redirect scope, fresh page/client/track authorization and stable selected formats/manifest resources. One denied generation renews once; changed identities/format facts or HLS shape stop publication. Short-link extraction remains on its existing path and has no new renewal claim.

## Verification

All **3,837 native checks** (2,669 core + 1,168 optional media), **143 app checks** and **65 focused checks** pass on the final candidate. Release has zero warnings/errors. Default policy/video/audio recovery identities match the verified 0.190.0 binary. The earlier broad run was stopped before rebuilding for the observed terminal-page correction; its partial log is retained and is not counted as a complete run.

The batch adds 39 core contracts and six generated-media cases. Four retained positive workflows cover profile, related, search and HLS mid-transfer renewal. Every published and retained original is independently decoded with FFmpeg and compared with ordered ffprobe coded-audio hashes. Eight files retain all 1,136 coded audio packets. Archive repeats request no selected playback, media or new callbacks. Generated negative HLS cases protect prior output/archive after a denial followed by changed identity or graph shape. No real account was used.

Public profile, sounds-search and related inventories independently report 52, one and 39 metadata members. Profile position41 and search position1 each select the known Creative Commons track303195707. Each published file is3,689,151 bytes and its retained original3,692,920 bytes; all four fully decode182.815692 seconds and7,874 AAC frames per file. Both main/original ordered packet sequences match exactly. Archive repeats revalidate inventory with three/two metadata requests respectively, but issue zero selected playback/media requests and zero new callbacks. Related playback is generated-media verified; the public related inventory did not download unrelated tracks. First attempts exposed omitted terminal cursors; those refused runs and exact first-party responses remain retained alongside the corrected verification.

## Scope and remaining work

Four exact frozen rows gain implemented-subset evidence: `soundcloud:user`, the catalog's zero-width-space `soundcloud:​user:permalink`, `soundcloud:related` and `soundcloud:search`. Search evidence covers the supported Web URLs, without pseudo-search syntax or general filter parity. The 1,731-key catalog, 126 classic app settings, 276 captured long names and 18 separate native WPF extensions remain intact. There are 118 partial key subsets, 1,478 keys without key-specific evidence, 135 broken decisions pending and zero full-parity claims. The production startup and immutable native opt-in routing remain unchanged.

Other profile feeds (likes, reposts, sets/albums and popular), track stations, private-link shapes, official OAuth/URN API support and broader search grammar remain pending. Existing guest website API behavior is a separate protocol from the official registered-application OAuth API; no supplied-account or official OAuth support is inferred from public guest requests. Real-account verification remains user-deferred. Missing provider families, core options/defaults, long sessions and final packaged production cutover remain on the full backlog.

## References and evidence

- First-party website response captures: `work/audio-inventories-batch/reference/`, including profile hydration, upload/related/search pages and absent-cursor terminal responses.
- [SoundCloud API guide](https://developers.soundcloud.com/docs/): official OAuth/API contract and linked pagination semantics. This does not define the guest site's numeric/client-id API.
- Final logs and exits: `work/audio-inventories-batch/`.
- Retained generated probe: `work/audio-inventories-probe/bin/Release/net10.0/test-data/verified-0.192.0-final/verification.json`.
- Independent verifier: `work/verify-audio-inventories.py`, `independent-{summary,decodes,inventories}.json`.
- Public native probe: `work/audio-inventories-public/bin/Release/net10.0/public/verified-0.192.0-final/verification.json`.
- Default policy/recovery compatibility: `compatibility-0.190.json`. Preservation: `preservation.json`.
