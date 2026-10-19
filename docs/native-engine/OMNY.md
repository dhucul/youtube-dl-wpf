# Native Omny verification

Native engine 0.99.0 independently implements Omny public clip, show and playlist workflows in C#. Page/JSON/embed and immutable ID aliases, clear supplied audio/video resources, selected snapshots, archives and static WebVTT are covered. The frozen `Omnyfm`, `OmnyfmPlaylist` and `OmnyfmShow` keys remain partial. Full site/option parity and production native WPF cutover are unfinished.

## Public protocol

The [documented consumer API](https://api.omny.fm/docs/consumer-api/index.html) provides read-only published metadata. Native discovery uses this API and does not use management endpoints, account tokens, Python, yt-dlp extraction/runtime code or fallback. Program and clip slugs resolve to immutable organization/program/clip IDs; selected playback uses immutable clip endpoints. Sources and canonical URLs must agree with those identities. Private/restricted/unpublished/processing/upcoming or conflicting records fail explicitly.

Literal supplied MP3, MP4 and HLS URLs and queries are retained. Local fixed-demuxer decoding checks complete transfer, source role and minimum program duration before publication. Ad/intro/outro delivery may contain additional material. Published base-audio size is enforced only when the audio delivery is static. A shared engine fix carries provider minimum duration through HLS verification; generated short HLS cannot be accepted merely because its short manifest ended normally.

Shows use declared-count cursor pagination. Playlists use the manual-order v2 API with `AfterExclusive` navigation from the last original clip. Every page retains organization/program or playlist membership, stable IDs and source positions. Duplicates, missing counts/navigation, changed first pages/owner revisions, premature endings and configured limits fail rather than truncate. Unavailable entries retain their original positions. Inspected selections refresh only chosen immutable clip IDs; `omnyfm` GUID archives skip direct and selected repeats.

Static published transcripts use the documented `WebVTT` export. Static chapter positions are validated and pass through the shared embedding pipeline. Dynamic audio timing is refused when captions/chapters cannot yet be aligned reliably. A live example showed that `HasPreRollVideoAd` is a video-only flag: it does not make an otherwise static audio source dynamic. The [publisher chapter guidance](https://help.tritondigital.com/adding-chapter-markers-to-your-podcast) describes how ad insertion can affect marker timing; broader alignment remains pending.

## Verification

Fourteen new core checks cover identities/facts, URL aliases/boundaries, access/publication states, conflicting media, video/HLS descriptors, static/dynamic timing, counted shows, manual playlists, incomplete/changing collections, unavailable positions, inspected selections, archives, cancellation, denial and common credential isolation.

Ten generated-media checks fully decode audio, selected/inspected sources, conversion, dynamic longer audio, progressive video, static HLS, caption/chapter embedding and short audio/HLS refusals. They preserve original bytes and unconverted decoded samples, verify source IDs/positions/archives and protect prior outputs on failure.

The Release solution build passed with zero warnings/errors. All **1,460 native checks** passed: 1,100 core and 360 optional media checks. All **51 app checks** passed, including four browser checks.

Live testing used the public [Proof In Trial trailer](https://omny.fm/shows/proof-in-trial/proof-in-trial-trailer) and the documented public [transcript example](https://api.omny.fm/orgs/ec561129-d0bd-4e28-99b0-a4ff00357c00/clips/e6dec256-6f49-4da0-b419-a96a004fba87). The show and named playlist each completed 13 records; the trailer retained position 13 in both.

| Workflow | Processed output | Retained original | Decoded frames |
| --- | ---: | ---: | ---: |
| Trailer single, embed, inspected show and inspected playlist | 808,827-byte MP3 each | 810,373-byte MP3 each | 1,842 each |
| Trailer conversion | 1,179,220-byte AAC M4A | 810,373-byte MP3 | 2,073 AAC / 1,842 MP3 |
| Static transcript example | 108,535-byte MP3 | 110,028-byte MP3 | 195 each |

All twelve files independently decoded completely. Unconverted processed/original decoded PCM hashes matched exactly. MP3 container durations differ slightly after metadata remux while the decoded samples are unchanged: trailer 48.117500/48.143673 seconds and short example 5.093875/5.120000 seconds. Converted trailer duration was 48.117551 seconds. The saved 173-byte `en-US` WebVTT equaled the publisher export. Each initial job completed two files; archive repeats produced no files or callbacks.

Five completed live workflows were reused after the transcript timing fix; their archive state and artifacts were revalidated rather than redownloaded. The corrected transcript workflow then completed. Scratch API captures, OpenAPI data and evidence are under `work/omny-probe` in the October 10 Codex workspace and are not runtime dependencies. User settings, history, media and managed engines were preserved. No Git commit or push was performed.

## Remaining work

Live video/HLS coverage, additional media origins/layouts, dynamic ad/intro/outro timing, restricted/account/access-token workflows, large mutable/shared/promo inventories and complete site/option parity remain pending. Static video/HLS/caption/chapter integration is generated-media verified; the twelve live files establish the public audio/transcript subset above.

Production WPF still uses yt-dlp. Installer/runtime/fallback removal and final packaged application verification without Python or yt-dlp remain unfinished. The native probe is not a packaged WPF test.
