# JioSaavn, QQ Music, NetEase and output options

Version 0.196.0. Full replacement remains open.

## Implemented batch

Three previously missing provider families add 19 exact frozen-key subsets: six JioSaavn, six QQ Music and seven NetEase routes. Native code reads current first-party JSON contracts, preserves complete ordered inventories and unavailable positions, binds selected metadata/ownership/archive identity, fetches fresh selected playback and uses shared clear-media transfer/renewal. Source decoding and duration/size checks prevent a preview or short transfer from becoming a complete publication. See [JioSaavn](JIOSAAVN.md), [QQ Music](QQ-MUSIC.md) and [NetEase](NETEASE.md) for precise scopes and denials.

Six captured argument gaps gain typed behavior: `--quiet`, `--no-quiet`, `--no-progress`, `--progress`, `--no-warnings` and `--rm-cache-dir`. Quiet controls logger callbacks; progress flags control progress callbacks; warnings can be suppressed from logs while remaining available as truthful inspection data. Results and completion callbacks retain their normal behavior. Saved options, per-job overrides and immutable WPF capture are tested.

Explicit cache cleanup touches only recognized native public-player JSON/temporary files in the configured `native-public-player-v1` child. It does not recurse, follows no reparse paths, leaves unrelated and nested data intact and honors cancellation. With no configured native cache directory it is a no-op. Read-only inspection and analysis ignore the cleanup request. Cleanup and output flags are operational, excluded from request/publication/recovery serialization; defaults match verified 0.194.0. Standalone CLI cache cleanup without a media request, default disk-cache location and broader cache policy remain pending.

## Verification

All **4,051 native checks** (2,836 core + 1,215 optional media), **155 app checks** and **133 focused checks** pass on the frozen candidate. Release has zero warnings/errors. The 90 core/36 media/six app additions pass with the original regression suite and cross-version recovery checks. Default policy/request JSON/publication recipe/video/audio recovery identities match the verified 0.194.0 binary. All 905 baseline files are accounted for; 891 outside 14 edited existing paths remain byte-identical, 26 current source/doc hashes verify and 917 files remain uncommitted. No HEAD/startup reset/stash/commit/push/cutover.

The batch adds 90 core, 36 generated-media and six app cases. All 19 routes have positive contracts; 24 retained positive workflows independently fully decode 48 outputs/originals, match 52 ordered coded stream inventories and 144 video pictures. HLS/DASH audio, three-family renewal and archive repeats are checked. Twelve negative media workflows preserve previous output/archive/callbacks for removed members, changed IDs, truncated audio and failed renewal. Guest/account denials, previews, foreign URLs, wrong owners, count/paging changes, bulk identities and malformed sources have explicit contracts.

Two real public podcast workflows completed through the native engine: a NetEase program and a JioSaavn episode with narrowly scoped Spreaker delegation. Four MP3 files independently fully decode, with exact main/original coded-packet sequences and no new archive-repeat callbacks. Public metadata/inventory inspection succeeded for 18 of 19 sampled routes on the current candidate. The remaining guest album endpoint explicitly required phone binding; sampled QQ music/video and other music grants were denied. No public QQ transfer or real-account success is claimed. First refused observations, changing catalog counts, unavailable-duration rows, episode page-size discovery and the account-bound album recheck are retained.

## Remaining replacement work

The frozen scope remains 1,731 keys, 126 classic settings, 276 captured long names and 18 separate native WPF extensions. Partial key evidence rises from 125 to 144; 1,452 keys still lack key-specific evidence and 135 broken decisions remain pending. There are 66 registered providers and 121 typed long-name handlers. Twelve classic settings still require implementation or explicit app policy: geo controls, external downloader/args, AdobePass fields, fixup, postprocessor args, exec and SponsorBlock. No complete-parity claim, production startup switch or fallback-policy change is made.

Continue missing multi-key provider families alongside those classic option gaps and remaining argument/default/alias controls. Broader music account/region/session, URL/codec/lyrics/tagging, podcast-hosting and catalog-consistency work remains, as do core YouTube/Vimeo player/proof/signing, long transport/recovery/storage/concurrency, conversion and final packaged acceptance. Real-account checks remain user-deferred: do not ask again or inspect saved accounts.

## Evidence

- `work/music-families-batch/`: build/full native/app/focused/probe/independent/public/compatibility logs, original/applied hashes and preservation audit.
- `work/music-families-probe/bin/Release/net10.0/test-data/verified-0.196.0/verification.json`; `work/verify-music-families.py`; `independent-{summary,decodes,inventories}.json`.
- `work/music-families-public/bin/Release/net10.0/public/observed-0.196.0-refined/verification.json`: current metadata/inventory and rights/account outcomes.
- `work/music-families-playback/bin/Release/net10.0/public/observed-0.196.0/verification.json`; `work/verify-music-public.py`; `public-independent-{summary,decodes,comparisons}.json`.
- `compatibility-0.194.json`: separate assembly contexts verify default policy, request JSON, publication recipe and video/audio recovery identity.
- `reference/`: first-party pages, client request definitions and public API/redirect envelopes. Production does not execute these captured scripts, load accounts or depend on Python/yt-dlp.
