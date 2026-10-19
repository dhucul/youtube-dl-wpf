# Native Pinterest story video assets

Engine 0.75.0 adds independently implemented C# story-video extraction. Offered MP4 and HLS paths are verified live. The implementation downloads exposed video assets; complete rendering of separate page effects/music/interactive overlays is still pending. Full Pinterest/PinterestCollection coverage and production WPF cutover remain unfinished.

## Identified story projections

Current public pin responses can expose different partial story projections in completed Relay declarations. The reader retains those projections separately instead of merging page/block arrays by index without identity checks. Pin/query/global identities remain checked by the existing reader.

The story's typed global ID and any supplied native entity ID must agree. Deleted or unavailable stories fail. Identified pages supply the canonical ordered inventory; other projections must agree on page count, supplied page IDs, block count, block types and layout. Video variants and caption resources bind to the same supplied asset. Conflicting facts/assets, incomplete inventories, excessive counts and mismatched total/clip durations fail.

A current single-page/single-video projection can omit page IDs and the pin's optional `isHidden` field. This bounded case uses the verified story/post identity and offered asset identity; an explicit hidden flag still fails, and the story must declare not-deleted. Multi-page inventories still require identified pages. This handles the observed complete public response without treating missing fields as an authorization grant or inventing page identities.

Video declarations include `videoList`, mobile, HLS-v4, EXP and 720P groups. Partial metadata-only groups are combined with matching complete offered resources; groups lacking a playable URL are not invented as formats. Additional observed `mc/hls` and `mc/expMp4` resource families now use the same origin/shard/asset/type validation. Offered encoded queries are preserved.

Each source video retains its declared dimensions, complete duration, poster, formats and automatic captions. A single video keeps the post's normal `pinterest <pin-ID>` archive identity. Multi-page/multi-block stories expose an ordered asset playlist. Non-video blocks retain unavailable source positions; logical video/archive IDs bind pin, page and asset. Selection and inspected snapshots refresh that same asset when page order changes.

The downloadable inventory is ordered blocks across pages. Separate crop/text/music/interactive overlays are not silently rendered into source clips; detected or unverified rendering context produces a clear warning. Stories containing only still images and full image/carousel/composition workflows remain pending. A multi-video story nested inside a board entry also needs broader nested-playlist handling.

Primary protocol evidence is the public pin's completed response records and supplied playback resources. Pinterest's [pin-type documentation](https://help.pinterest.com/en/article/types-of-pins-on-pinterest) describes video pins containing videos/images and interactive effects. The observed website schema is not a separately documented API contract. No Python, yt-dlp extraction code or JavaScript execution is used by this reader.

## Live verification on October 9, 2026

Public [pin 626281891952504949](https://www.pinterest.com/pin/626281891952504949/) supplied story `5284036561638308316`, one video page, eight offered formats and 14.6-second declared duration. Some fresh public projections omitted optional pin/page fields; the bounded single-video reader handled those actual layouts. Metadata attributes the pin to its supplied creator, without inferring ownership from the content.

| Offered path | Published files | Strict full decoding | Publication and repeat |
| --- | --- | --- | --- |
| `v720P`, no height ceiling | 876,558-byte processed MP4; 876,389-byte original MP4 | H.264 720×1280, 438 video frames; AAC 312 audio frames; 14.600000 s | Thumbnail/info JSON, two callbacks; zero archive-repeat callbacks |
| `vHLSV4`, maximum height 720 | 274,346-byte processed MP4; 274,252-byte merged original; 150,575-byte video component; 121,637-byte audio component | H.264 360×640, 438 video frames; AAC 314 audio frames; 14.600000 s merged | Thumbnail/info JSON, four callbacks; zero archive-repeat callbacks |

All six saved media files fully decoded. The MP4 and HLS audio encodes have different frame counts and are not claimed byte-identical to each other. No personal cookies or manually supplied capture were used.

Ten deterministic checks cover story/post identities, deletion/hidden/optional fields, partial formats, original block positions, archive identities, page/type/asset conflicts, complete durations, limits, overlays, captions and inspected selection after reordering. Four generated-media checks cover complete MP4/HLS frames/audio, captions with original timing, retained originals, selected-only multi-page transfers and incomplete-file refusal without replacing previous output or updating archives. Multi-page asset selection is fixture/media verified; a complete multi-page live sample remains required.

Nested board/story traversal was subsequently added in 0.76.0; see [nested playlists](NESTED-PLAYLISTS.md) for verified media/callback/source-context scope and the outstanding live gap. Remaining work includes complete page rendering/concatenation with separate effects/music, image/carousel downloads, broader story/block/layout schemas, multi-page/nested live verification, accounts/private access, legacy silent-HLS audio parity and complete Pinterest/PinterestCollection parity. All-site/options coverage, old-runtime removal, packaging and WPF switch remain required.
