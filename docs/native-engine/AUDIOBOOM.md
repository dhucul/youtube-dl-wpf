# Native Audioboom verification

Native engine 0.88 independently implements current public Audioboom posts, episode embeds and count-verified channel feeds in C#. It uses no Python, yt-dlp extraction code, executable or fallback. Coverage of the frozen `AudioBoom` key remains partial. The production WPF app still uses its original backend while full catalog/option replacement is unfinished.

## Supported public workflows

Numeric and slugged `/posts/ID` pages, current `/posts/ID/embed` players on the public or embeds host, `/channels/ID` pages and embeds, and publisher-resolved `/channel/ALIAS` routes dispatch to this provider. Public HTTP/www addresses normalize to HTTPS. Filtered, manually paged, private-token and unsupported legacy routes fail explicitly.

The provider reads one literal `V5DetailPagePlayer` record or `ReactBoomEmbedPlayerV5` data attribute without executing JavaScript. Requested, returned, canonical, selected clip and static-link identities must agree. Related clips cannot replace the requested one. Duplicate attributes, fields, requested records and conflicting canonical identities fail. Comments, script/template/textarea contents do not supply player records.

Playback preserves the exact supplied `/posts/ID.mp3` URL and literal query. The public player must supply a supported playback state and full positive duration. Metadata and playback use anonymous sessions. Full local MP3 decoding, media roles and duration are verified before publication. Stable episode archives, original files, metadata, artwork and audio conversion use the common engine pipeline.

Channel inventories bind the page's explicit episode count to its exact owned RSS link. The complete parsed feed must contain that many entries without continuation or duplicate/foreign episode links. Configured limits fail without truncation. Imported RSS GUIDs can differ from Audioboom post IDs; the public episode link supplies the stable archive identity. Unavailable entries retain source positions. Each selected child refreshes its native post player; inspected selections retain the original child identity and numbering.

The [official episode embed guide](https://support.audioboom.com/hc/en-us/articles/360004974373-Episode-Embed-Player) and [podcast embed guide](https://support.audioboom.com/hc/en-us/articles/360004927114-Podcast-Embed-Player) describe the public players. Current public HTML, player data and RSS documents were inspected on October 9, 2026. A legacy `.json` metadata attempt returned HTTP 406 and is not a runtime dependency.

## Verification

Eleven core checks cover literal player facts, aliases, exact identities, malformed/ambiguous records, related-clip isolation, complete feed counts, imported GUIDs, unavailable positions, snapshots, archives, anonymous metadata, cancellation, denial and redirected identity. Five generated MP3 workflows verify complete source/selected/snapshot/converted files, exact originals, anonymous playback, encoded packet and decoded-sample preservation, and rejection of incomplete audio without changing prior files or recording archive success.

Live downloads used [Podcasting Playbook Trailer, post 8143686](https://audioboom.com/posts/8143686-podcasting-playbook-trailer), through its public episode URL, current embedded player, inspected channel selection and M4A conversion. The native provider completed the [30-entry Playbook channel](https://audioboom.com/channels/5084695) and the [733-entry No Such Thing As A Fish channel](https://audioboom.com/channels/2399216). Both inventories matched the website's declared counts; imported older episode GUIDs were included. The trailer remained at original position 30 in the inspected selection.

All eight files in the initial live workflows decoded successfully, with 2,181 MP3 audio frames. Independent packet hashes showed all 2,181 encoded MP3 payloads preserved in order. This inspection also exposed an encoder-delay issue in the common metadata processor: ffmpeg added a 529-sample initial trim to a source without gapless metadata. The original file was retained unchanged, but the processed decoded audio differed. That behavior is corrected by the shared [MP3 boundary fix](MP3-BOUNDARIES.md), with fresh live workflows verified after the fix.

Fresh downloads after the fix again fully decoded all eight published files. The episode, embed and inspected selection each produced a 913,228-byte MP3 and retained the exact 914,337-byte original, both with 2,181 audio frames. All encoded packet payloads/order and the complete decoded PCM hash match the source. Container duration estimates differ slightly (56.973063 versus 56.981063 seconds); they do not indicate missing decoded samples. Converted AAC output was 1,024,911 bytes with 2,454 decoded frames over 56.973061 seconds. Each initial job reported two file completions; archive repeats reported zero.

Scratch evidence is under `work/audioboom-probe` in the Codex workspace: public documents, initial and corrected live results, packet/PCM comparisons and regression logs. The scratch harness is not a production dependency.

## Remaining work

Older player versions and audioboo routes, arbitrary playlists/users, private/account workflows, supplied transcript/chapter metadata, alternate playback/download layouts and site/option parity remain pending. Feed/page counts do not prove snapshot stability while publishers change their inventories. Production cutover, old dependency removal and packaged verification remain pending.
