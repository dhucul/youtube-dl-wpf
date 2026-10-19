# Native Odysee and LBRY

The C# provider resolves public claims through Odysee's published API and downloads the returned media resource through the native transport pipeline. It does not run a LBRY daemon, Python, yt-dlp or another extraction library.

## Implemented scope

- Odysee and legacy `lbry.tv` stream/channel URLs, colon claim selectors, current embed/download/stream routes, public claim-backed collection links and native `lbry://` addresses with hash selectors. The registry routes LBRY URIs only to its dedicated provider, without enabling arbitrary schemes in generic HTTP discovery.
- Bounded `resolve`, `claim_search` and playback-URL lookup responses. Claim IDs, names, selector prefixes, requested channel signatures, publisher IDs, returned playback claims and stream-descriptor prefixes are checked. The native client preserves explicit connection/user-agent preferences.
- Free published audio/video streams with a supported stream descriptor. Original-file title, description, publisher, thumbnail URL, duration, dimensions and size are retained when declared. Codec/frame-rate/audio-presence facts are not invented. Original container information comes from source metadata when the CDN route's `.mp4` suffix is merely a routing convention.
- Complete public channel upload pagination with declared-count/page checks, duplicate detection, bounds and original positions. Entries with an invalid publisher signature remain unavailable in the inventory rather than disappearing or aborting all other entries. Withheld or inconsistent page results fail instead of appearing complete.
- Public collections preserve their declared order, repeated references and unavailable positions. Claim metadata is retrieved in bounded batches. Playback lookup and media transfer happen only for selected available entries. Nested/non-media entries remain unavailable.
- Native archives use the full stream claim ID. Fresh discovery currently precedes archive skipping; already recorded media is not downloaded again. Literal returned playback parameters are retained, with no fallback URL construction after an API/access failure.

The API's playback operation can initiate purchases in an authenticated wallet context. This public provider therefore uses an isolated anonymous HTTP session for metadata and playback. It strips account headers and uses a fresh cookie jar without clearing the caller's original session. Exact nonzero/unknown fee values, purchase/rental/member/unlisted tags and unreleased scheduled content fail before playback lookup. Paid and authenticated access are not implemented; no account, wallet or purchase operation is issued by the native provider.

## MP4 timecode preservation

The live stream exposed a shared ffmpeg limitation: copying its existing codec-none `tmcd` data stream prevented metadata processing. Native MP4/MOV/M4V processing now inspects a supported single timecode track and its matching video metadata, excludes only that data stream from copying, and lets ffmpeg regenerate it. The processed timecode value, configuration, time base and single-sample layout are verified before publication. Other streams remain mapped. The original file can still be retained unchanged.

This requires the existing ffprobe companion tool. Multiple/unverifiable timecode layouts and other output containers remain pending rather than silently losing data. Generated tests cover normal MP4 metadata processing and drop-frame MP4-to-MOV remuxing, including timecode packet hashes/timing and unchanged media frame counts.

## Verification on October 8, 2026

Thirteen provider/privacy checks cover claim facts and aliases, LBRY URI routing, complete pagination, collection order/repeats/missing entries, selected-only downloads, access boundaries including tiny nonzero fees, identity mismatches, quality/audio selection, errors/cancellation, archives and credential isolation. A real loopback HTTP check verifies that an anonymous session sends no account cookies or authorization headers and leaves the original cookie jar intact. Two generated-media checks cover the timecode processing described above.

Native inspection of [LBRY's public channel](https://odysee.com/@lbry:3f) returned 53 unique uploads. One older entry was marked unavailable because the API reported an invalid channel signature; the other 52 remained available. Inspection of public collection `de6d7bad9ecb9ebccabd3a4a3ad2b52fad210934` retained its two declared entries in exact order. A later repeated collection lookup returned an RPC error and stopped explicitly; stable service availability is not established by these individual checks.

The channel's first selected stream, `7a416c44a6888d94fe045241bbac055c726332aa` (Introducing Odysee: A Short Video), completed with metadata, keep-original and archive recording:

| Output | Bytes | Verification |
| --- | ---: | --- |
| Retained source | 44,532,814 | SHA-384 matched the published source hash |
| Processed MP4 | 44,532,935 | Full video/audio decoding passed |

Both files retained H.264 1920x1080 at 25 fps, 3,594 decoded video frames, 6,739 decoded AAC frames and a 143.760000-second duration. The initial timecode `01:00:00:00`, its single-sample layout, time base and configuration hash were preserved. The operation emitted two file callbacks and recorded `lbry 7a416c44a6888d94fe045241bbac055c726332aa`. Published metadata's rounded 143-second duration did not truncate the file. Hash comparison was performed by the verification probe; the general downloader does not yet enforce the claim's full content hash at runtime.

## Remaining work

Authenticated/paid/membership/unlisted access, alternative storage sources, live playback, reposts, additional URI/selector grammar, channel filtering and volatile-snapshot handling, service retries/URL refresh, additional CDN/format variants, captions/chapters, broader thumbnail workflows, runtime source-hash enforcement, full archive/option equivalence and production WPF integration remain pending. Live audio-only downloading and thumbnail saving were not established in this check. The three captured `lbry`, `lbry:channel` and `lbry:playlist` keys have partial coverage.

Primary references: [Odysee's published frontend](https://github.com/OdyseeTeam/odysee-frontend), [its API gateway](https://github.com/OdyseeTeam/odysee-api), and [LBRY's resolution/claim terminology](https://lbry.tech/glossary/). Current response shapes and playback URLs were observed from the public service. Repository fixtures contain invented claim IDs, hashes and credentials.
