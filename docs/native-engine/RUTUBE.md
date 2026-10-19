# Native Rutube provider

Version 0.194.0 provides bounded public VOD and collection subsets without third-party extraction code or JavaScript execution. Full provider parity remains open.

| Exact frozen key | Implemented subset |
| --- | --- |
| rutube | Hash video URLs; canonical metadata and publisher-granted clear playback |
| rutube:embed | Hash `/play/embed/` and `/embed/` aliases with the same video archive identity |
| rutube:person | Numeric `/video/person/` complete public video inventories |
| rutube:channel | Numeric channel aliases bound to the same public person endpoint |
| rutube:movie | Numeric `/metainfo/tv/` metadata and its complete publisher video collection |
| rutube:tags | Numeric `/tags/video/` paged video inventories |
| rutube:playlist | Numeric `/plst/` custom playlists, with exact declared counts and source order |

Metadata and playback options must match the selected video hash, effective video, permalink and numeric track identity. ACL denial, paid/club/hidden/deleted/locked resources and declared protection never become clear full media. Publisher URLs stay literal. Known HTTPS Rutube/rtbcdn/rutubelist hosts bound all media and redirects. Public HLS masters expose two distinct CDN mirrors for each quality: equal-fact variants qualify as mirrors only when their full paths and queries match. The provider retains the first publisher-listed mirror for each quality and preserves its height, rate, codec and parent expiry. Different equal-fact paths remain ambiguous and are refused. This avoids weakening the shared adaptive graph guards. Standard offered progressive and DASH resources use the existing native transfer and renewal pipeline.

Person/channel upload owners and every collection member hash/permalink are checked. Inventories request metadata only; selected members obtain fresh normal playback. Paging requires actual `has_next`/`next` progress, endpoint/client/page binding and bounded counts. Public `num_pages` is a growing estimate, not a completeness declaration. Exact playlist counts remain mandatory. Non-playlist duplicate IDs retain first-seen order; custom playlist occurrences retain source positions. Inspected membership is revalidated; reordering retains inspected output indexes and removal or changed identity protects prior output/archive/callback state. Direct video and embed aliases skip before HTTP when archived.

The public Creative Commons test video `5be358abcc24c90010a954dbba2e898a` completed through ordinary video and embed URLs. Each main/source pair fully decodes 5,247 H.264 pictures and 7,543 AAC frames; ordered raw packet hashes and independently normalized coded picture/AAC payloads match. Published TS is 10,107,632 bytes/175.124889 seconds; retained original is 9,512,800 bytes/175.108267 seconds. Each archive repeat makes zero HTTP requests and emits zero new callbacks. Public person and channel inventories each contain 549 entries over 28 metadata requests; a public custom playlist contains four entries with two metadata requests. Show/tag first-party response shapes were observed directly and their complete selected workflows are generated-media verified.

Live/account/private/paid playback, numeric embed IDs, track-linked playlist context, additional URL/feed/filter shapes, captions, richer audio/subtitle rendition groups, protected streams and longer renewal evidence remain pending. A registry entry and generated subsets are not full site parity. Real-account verification remains user-deferred.

References: [Rutube's embed/API guide](https://rutube.ru/info/embed/), [public CC video](https://rutube.ru/video/5be358abcc24c90010a954dbba2e898a/). First-party API/client captures and refused initial attempts are retained in `work/provider-cache-batch/reference/` and public observation logs. No upstream extractor implementation was used. See [batch evidence](PROVIDER-CACHE.md).
