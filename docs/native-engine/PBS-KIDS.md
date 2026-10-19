# Native PBS Kids current videos and collections

Native engine 0.82 adds C# extraction for PBS Kids' current `/videos/watch` and `/videos/playlist` pages. It reads the publisher's literal public page data and resolves the provided encodings through PBS's public URS service. No Python, yt-dlp, JavaScript execution, private API keys, browser capture or location changes are used. ffmpeg remains for media verification/processing.

## Implemented scope

- Current watch and collection URLs with their slug and numeric CMS ID, optional property context, `www` aliases and HTTP-to-HTTPS normalization.
- One bounded literal `__NEXT_DATA__` document with route/query/context identity checks, exact requested video/collection record binding and media-asset GUID validation. Related/theme/navigation objects do not become requested media.
- Exposed clear MP4/HLS profiles, deduplicated source URLs, explicit protection state, expiry checks and supplied publisher rendition labels. Protected profiles are not presented as ready clear formats or sent to a licensing service. Unsupported live/audio/profile layouts fail explicitly.
- Provided URS identifiers are resolved without changing access parameters. Public current/legacy PBS CDN origins and declared asset IDs are checked. Signed/nested URL query values remain literal. Denied encoding resolution stops before media transfer; no alternate filenames or location parameters are guessed.
- Native MP4/static-HLS transfer, height constraints, full local decoding and duration checks, original retention, metadata/artwork/info JSON, media completion callbacks and asset-GUID archives. Missing captions are not invented; exposed HLS caption resources use the existing shared transport support.
- Typed public collection entry arrays with their original order/positions, unavailable placeholders, configured limits, duplicate identity checks, declared-count consistency and explicit refusal of unfinished pagination markers. Each selected child is resolved freshly and bound to the collection's original media GUID.
- Inspected collection snapshots retain linked identities and original positions. Known selected-child GUID archives skip child page/encoding lookup. The two current public collection arrays described below are treated as the publisher-served collection inventory; this does not establish complete show/search/trending catalogs or other pagination contracts.
- Public metadata and playback sessions isolate caller cookies/authorization. Identity/access failures, cancellation and incomplete media cannot replace previous output or record archive success.

## Verification on October 9, 2026

Fourteen deterministic provider checks cover current/context/HTTP aliases, requested route and CMS/media identity, malformed/repeated page data, expiry/protection, profile choice/deduplication, collection positions/counts/limits, snapshots, selected-child archives, caller credential isolation, region denial and cancellation. Six generated-media checks verify MP4, HLS, inspected selected MP4/HLS, incomplete-source rejection and denied-access preservation. All successful generated outputs and retained originals/components pass strict full decoding, retain all 48 video frames, preserve original progressive bytes and report each completion once. Archive repeats transfer no media and report no new completions.

Live normal native inspection:

| Public collection | Served entries | Verified native result |
| --- | ---: | --- |
| [Back to School](https://pbskids.org/videos/playlist/back-to-school/1567131) | 43 | All 43 entry identities/positions retained; inspected selection snapshot created |
| [What's New](https://pbskids.org/videos/playlist/whats-new/1590812) | 50 | All 50 entry identities/positions retained; inspected selection snapshot created |

The current collection client renders the supplied `collectionData.entries` array. Native extraction does not silently truncate these arrays. Other collection schemas, hidden server limits or continuation shapes still require verification before a wider inventory claim.

Live access boundaries in the current Canadian session:

- [Welcome to PBS Kids](https://pbskids.org/videos/watch/welcome-to-pbs-kids/35256) exposed CMS ID `35256`, media GUID `c7a4e218-bf77-4775-b7bc-3d181c6557cb`, 11-second metadata and twelve offered MP4/HLS resolver records. Every independently inspected resolver returned an explicit region-denied response. These descriptors are not proof of playable media.
- A normal native single-video download failed with zero media completions, no archive entry and the prior file unchanged.
- An inspected “Back to School” selection for source position 2 (`1561111`, “The Loops Morning Rush”) failed with one structured per-entry failure, zero media files/completions, no archive entry and the prior file unchanged. No unselected video was used as a substitute.
- The central PBS portal corresponding to the first video's legacy media ID returned an unavailable player. It is not used as alternate playback or caption access.

**Successful live PBS Kids media playback is not established.** Generated-media checks prove native transfer/publication behavior for the supported schema; the live checks prove current collection extraction and correct access refusal. No proxy, forged location or other access change was attempted.

## Remaining coverage

This is partial coverage of the catalog's `PBSKids` key. Working live playback in an allowed public session, legacy GUID/video-site aliases, root-home/trending/show/search routes, broader CMS/collection/pagination layouts, podcasts/live/protected workflows, alternate-language/audio/ASL selection, in-band CEA captions and other caption/provenance/metadata/chapters, rendition/URL refresh and complete PBS Kids parity remain pending.

Standalone watch URLs still require fresh page/encoding discovery before the engine knows their GUID archive identity; inspected collection children can skip earlier. Full PBS/site/option parity, production WPF cutover, dependency removal and standalone packaging remain unfinished. Production WPF still uses yt-dlp.

## Primary references and evidence

- [Current PBS Kids video page](https://pbskids.org/videos/watch/welcome-to-pbs-kids/35256)
- [Public Back to School collection](https://pbskids.org/videos/playlist/back-to-school/1567131)
- [Public What's New collection](https://pbskids.org/videos/playlist/whats-new/1590812)
- Current publisher-hosted watch/player/collection scripts and their public page/resolver responses, inspected directly. These are current protocol observations rather than an API stability guarantee.

Implementation: `src/YoutubeDl.Engine/Providers/PbsKids/`; shared public encoding resolution in `PbsProvider.cs`. Tests: `PbsKidsChecks.cs` and `FfmpegChecks.PbsKids.cs`. Local live evidence: `work/pbs-kids-probe/live-probe.log`, page/resolver response snapshots and probe Release `verification.json`. Scratch public responses/scripts are not runtime dependencies or repository fixtures.
