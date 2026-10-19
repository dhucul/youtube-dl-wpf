# Native Mastodon public status attachments

Native engine 0.78 implements public Mastodon status discovery in C#. It uses the instance's documented read-only status API and the offered attachment URL, with no Python, yt-dlp, browser capture or login. ffmpeg remains responsible for verification and media processing.

## Implemented scope

- HTTPS status links on federated DNS origins: `/@user/id`, cached `/@user@domain/id`, `/users/user/statuses/id`, `/statuses/id`, `/web/statuses/id`, and supported embed/activity aliases. Tracking queries do not alter the API identity. Profiles, timelines, tags and arbitrary ActivityPub objects are not claimed as status URLs.
- Bounded anonymous JSON discovery validates the requested local ID, author handle, original ActivityPub status URI, author origin and canonical web link. Remote cache IDs may differ from the original status ID. Private/direct statuses and unavailable accounts fail explicitly. Only the supplied public resource is fetched; `remote_url` is not substituted for the instance's offered original.
- Public/unlisted video, silent `gifv` animation and audio attachments. MP4/MOV/WebM video and supported MP4/M4A/MP3/Ogg/Opus/FLAC/WAV audio use the existing native progressive transport and forced local demuxer/full decoding. Video audio remains unknown until probing; an animation is declared silent. Audio does not inherit preview/video dimensions.
- Original dimensions, rational frame rate, bitrate and duration come from attachment metadata, not its scaled preview. Missing facts remain unknown. The post text, content warning, author name and attachment description are retained. Exposed video/animation artwork can be saved with info JSON and original media.
- More than one ready audio/video attachment becomes a collection. Original positions include images and unavailable attachments; selected media have independent archives based on original status URI plus attachment ID. Inspected selections refresh the parent inventory by attachment identity and retain their original positions after reordering. A single ready attachment keeps the original post archive identity.
- Boost activity records resolve their original status with bounded identity checks. Recursive/repeated chains, conflicting wrapper media, malformed/repeated JSON fields, duplicate attachment IDs and incomplete inventories fail before playback. This workflow is fixture-verified; a live boost download is not established.
- The normal publication, cancellation, duration/role verification, original retention and archive behavior applies. Incomplete source files cannot replace existing output or record archive success.

## Verification on October 9, 2026

Thirteen deterministic provider checks cover aliases, local/remote identity, boosts, privacy, malformed records, original dimensions/media roles, inventory limits, changed attachment order, credential isolation, archive scope, cancellation and API failures. Five generated-media checks cover complete video, silent animation, audio, selected multi-attachment publication and incomplete-source rejection. Every successful generated output and retained original is fully decoded.

Live checks used ordinary `NativeEngine.DownloadAsync` with no saved-response substitution or manual capture:

| Public sample | Processed / original bytes | Verified playback |
| --- | ---: | --- |
| `https://techhub.social/@OldRustBucket/117413649531132189` | 141,995 / 141,897 | H.264 320×240; all 125 video frames; no audio; 5.000 seconds |
| `https://techhub.social/@vizou/117413531406081740` | 48,019,933 / 48,019,732 | H.264 1080×1920; all 818 video and 1,599 AAC audio frames; 34.110667 seconds |

All four saved files passed strict full decoding. Each first download reported exactly two new media completions; an immediate archive retry reported zero. Artwork and info JSON were saved. A later animation probe reused the completed archive and therefore saved nothing; its scratch probe expected a new download and reported that expectation failure. The first successful download and decoding evidence remain recorded separately.

Fosstodon public status metadata was inspected, but subsequent native probes encountered HTTP rate limiting. Those attempts do not establish successful downloads on that instance. Other tested servers refused public timeline discovery. Live audio, multi-attachment, cached-remote and boost playback remain unverified even though their generated/fixture checks pass.

## Remaining work

Account/token and restricted-instance workflows, account/timeline pagination, tags, threads, cards/quotes/external delegation, image downloading, arbitrary ActivityPub or other Fediverse implementations, numeric `/ap` identities, differing federation/web domains, additional containers and richer captions/chapters/metadata need further work. This is partial public Mastodon coverage, not complete Fediverse support or full yt-dlp parity.

The frozen upstream catalog has no dedicated Mastodon extractor key. This work improves the broader generic-page compatibility surface; it does not add an implemented key to the 1,731-key baseline. Production WPF still uses its existing yt-dlp backend while full coverage and migration remain pending.

## Primary references

- [Read a status](https://docs.joinmastodon.org/methods/statuses/#get)
- [Status entity](https://docs.joinmastodon.org/entities/Status/)
- [MediaAttachment entity](https://docs.joinmastodon.org/entities/MediaAttachment/)
- [Account entity](https://docs.joinmastodon.org/entities/Account/)
- [Official ActivityPub identity generation](https://github.com/mastodon/mastodon/blob/main/app/lib/activitypub/tag_manager.rb)

Implementation: `src/YoutubeDl.Engine/Providers/Mastodon/`. Checks: `tests/YoutubeDl.Engine.Tests/MastodonChecks.cs` and `FfmpegChecks.Mastodon.cs`. Local live evidence: `work/mastodon-probe/live-probe.log`, `live-second.log`, and saved media/verification JSON under that probe's Release output. Scratch live responses and downloaded media are not runtime dependencies or repository fixtures.
