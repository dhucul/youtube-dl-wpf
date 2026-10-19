# Native Acast public podcasts

Native engine 0.86 adds independently implemented C# extraction for public Acast episodes, embeds and complete show inventories. It uses the publisher's public embed feeder API and supplied open audio enclosure. No Python, yt-dlp, JavaScript execution, private API keys, token generation/decryption, browser capture or login is used. ffmpeg remains for audio verification/processing.

## Implemented scope

- Current `shows.acast.com` show/episode URLs, older `play.acast.com/s` aliases and `embed.acast.com` show/episode forms, including aliases and Mongo-style IDs. HTTP normalizes to fixed HTTPS API requests. Private token/feed and manually paged player query modes need additional support.
- Exact show ID/alias and episode ID/show/alias binding. Acast-native IDs are distinguished from imported RSS GUIDs, which can be opaque external identifiers. Public publisher website links are metadata rather than Acast route identities; actual Acast links must agree with their declared aliases.
- Explicit public/private/premium state, trusted open Sphinx MP3 route bound to the declared show/episode, provided duration/MIME/title/description/author/artwork and stable episode-ID archives. Unsupported access, delivery/container, metadata or identity shapes fail explicitly. Opaque show/player settings and signature data are not decoded or used to invent enclosure URLs.
- Bounded show paging with declared counts, stable show/alias/revision, requested offsets, unique episode IDs and configured limits. Premature empty pages, changed counts/revisions, oversized/repeated inventories and mismatched entries are refused. Premium positions remain unavailable; source numbering is preserved.
- Selected episodes resolve freshly. Inspected snapshots preserve stable child identities/positions and avoid refreshing unrelated show/episode data. GUID and known selected-child archives skip metadata/playback discovery.
- Native progressive audio transfer, full local MP3 decoding/duration/media-role checks, original retention, metadata/artwork/info JSON, audio conversion, unique completion reporting and atomic publication. Verification failures preserve prior files and archives. Public sessions isolate caller cookies/authorization.
- Advertised `contentLength` is informational, not exact delivered byte verification: current open delivery can change bitrate, ads or encoding. Actual media transfer remains checked by response handling/full decoding and declared episode duration. Broader dynamic-ad/source-change guarantees remain pending.

## Verification on October 9, 2026

Thirteen deterministic checks cover stable identities/imported GUIDs/publisher links, old/current/embed aliases, private token routes, access state, public enclosure ownership, complete paging/count/order, premium positions, snapshots, archives, credential isolation, cancellation and API denial/redirects. Five generated-media checks verify complete MP3/metadata/originals, selected show output, inspected selection, audio conversion and incomplete-source rejection. Every successful generated file and retained original is fully decoded; exact original bytes and copied MP3 packet payload/order are verified.

Live native inventories:

| Public show | Complete declared entries | Verification |
| --- | ---: | --- |
| [Shameless Careers](https://shows.acast.com/shamelesscareers) | 59 | Complete native count; also matched the public RSS inventory; inspected trailer selection at source position 59 |
| [No Such Thing As A Fish](https://shows.acast.com/no-such-thing-as-a-fish) | 228 | Complete native multi-page enumeration using stable show ID/count; imported RSS GUID and external publisher website layout retained |

Live ordinary `NativeEngine.DownloadAsync` on the [public Shameless Careers trailer](https://shows.acast.com/shamelesscareers/episodes/trailer), episode `65f0e03aedbd610016916b2b`:

| Workflow | Processed / original bytes | Complete audio |
| --- | ---: | --- |
| Episode alias | 961,500 / 961,124 MP3 | All 2,299 decoded MP3 frames; processed 60.055510s / source 60.055500s |
| GUID embed alias | 961,500 / 961,124 MP3 | Same complete source/audio |
| Inspected show entry 59 | 961,500 / 961,124 MP3 | Same complete source/audio; source position/count 59 preserved |
| M4A conversion | 1,141,223 AAC M4A / 961,124 retained MP3 | All 2,587 AAC frames; 60.055510s; source MP3 unchanged |

All **8** saved audio files passed strict full decoding. Independent packet inspection confirmed all 2,299 encoded MP3 packet payloads/order match between original and metadata-processed trailer. Artwork and info JSON were saved. Each first workflow reported two unique media completions; immediate archive retries reported zero. The advertised source length was 2,400,960 bytes while this session delivered a complete 961,124-byte stream; the provider did not reject valid audio using an incorrect exact-size assumption.

The publisher's documented GUID embed example also inspected natively as `609e55b9f69c17229c1bdcd4`, “Swedish Domination,” with 2,034-second metadata and a ready supplied open MP3. That longer episode was not downloaded in this stage.

## Remaining scope

This is partial coverage of the catalog's `acast` and `acast:channel` keys. External hosting/delivery and other CDN/API/image shapes, premium/private feeds/account tokens, custom/playlist embed contracts, subscriber previews versus full content, metadata/transcripts/chapters/captions, dynamic ad composition/source-refresh/resume, other aliases/query/pagination and full Acast parity need further work. Public count/available descriptors do not establish private subscriber access.

Production WPF still uses yt-dlp. Full catalog/option coverage, native cutover, removal of the old dependency and standalone packaging remain unfinished.

## Primary references and local evidence

- [Acast's documented show/episode GUID embed form](https://learn.acast.com/en/articles/5790046-how-can-i-automatically-generate-embed-players-for-my-website)
- [Acast public embed player API](https://learn.acast.com/en/articles/4485460-embed-player-api)
- [Public trailer page](https://shows.acast.com/shamelesscareers/episodes/trailer)
- [Public 59-entry show](https://shows.acast.com/shamelesscareers)
- [Public imported/paginated show](https://shows.acast.com/no-such-thing-as-a-fish)
- Current Acast-hosted embed scripts and public feeder/audio responses inspected directly. These establish observed protocol behavior, not API stability.

Implementation: `src/YoutubeDl.Engine/Providers/Acast/`. Tests: `AcastChecks.cs` and `FfmpegChecks.Acast.cs`. Local live evidence: `work/acast-probe/live-probe.log`, `live-inventory.log`, `packet-verification.json`, verification JSON and saved audio under its Release output. Scratch public responses/settings/feed/reference material are not checked-in fixtures or runtime dependencies.
