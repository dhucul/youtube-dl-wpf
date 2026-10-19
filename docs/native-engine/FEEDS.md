# Native podcast and media feeds

The C# generic provider now recognizes RSS 2.0 and Atom responses before HTML discovery. It reads feed data using the .NET XML reader with external resource resolution disabled and DTDs prohibited. No script execution or third-party feed/extractor runtime is involved.

## Implemented scope

- RSS enclosures, Atom enclosure links, direct Media RSS content and media groups. Alternative formats retain declared size, dimensions, bitrate and frame rate when supplied. Images are not mistaken for downloadable episodes, and sample/nonstop expressions are not treated as complete media.
- Feed/episode identifiers, titles, descriptions, creators, artwork and durations. Relative addresses honor inherited `xml:base` and the final response address after redirects. Literal signed query values are preserved. Unknown zero enclosure lengths remain unknown.
- Ordered episode snapshots and explicit unavailable positions. The existing playlist selection, partial-success reporting, naming, captions, artwork, processing and archive pipeline downloads selected enclosures without fetching unrelated article pages.
- Atom `rel=next` pagination for RSS/Atom. Unchanged overlaps are deduplicated; page loops, changed collection identities, conflicting episode IDs and item limits fail explicitly. Bounds are 5 MiB per response, 32 MiB combined text, 100 pages, XML depth 64 and the request's item limit (default 1,000, maximum 10,000).
- Timed Podcast Namespace transcript links in VTT, SRT and TTML forms, and inline Podlove Simple Chapters when the episode duration supplies the last chapter end. VTT and inline chapter processing are media-verified; other transcript formats still need dedicated live checks.
- HTTP text decoding honors a declared XML encoding when no HTTP charset is given, with BOM detection and common BOM-less UTF-16/UTF-32 signatures. Latin-1, UTF-16 LE/BE and UTF-32 LE are fixture-verified.
- Resolved playlist snapshots now reuse their discovery HTTP session. Website-set cookies remain available for media and sidecar requests. This also applies to existing providers that return resolved playlist snapshots.
- Feed archives use a provider-specific `native:feed` key derived from feed identity plus episode identity. Public metadata keeps the original GUID/Atom ID. Identical local episode IDs in different feeds cannot skip each other. This does not establish legacy yt-dlp archive equivalence.

## Verification on October 8, 2026

Deterministic checks cover RSS and Atom, Media RSS alternatives, inherited XML bases, missing entries, duration/artwork/transcript/chapter metadata, selected-only transfer, pagination/overlaps, changed IDs and limits, archive scope, non-HTTP addresses, DTD refusal, deep XML, encoding, cancellation and a local HTTP cookie exchange. Generated media passed complete-frame, artwork, caption, chapter-boundary and identity-sidecar checks.

The [public NASA Curious Universe feed](https://www.nasa.gov/feeds/podcasts/curious-universe), listed on [NASA's feed page](https://www.nasa.gov/rss-feeds/), returned 109 playable enclosure entries. Native selection of episode 92, “Introducing Season Three of Curious Universe,” completed as a 3,313,777-byte MP3 with metadata and a 3000×3000 PNG cover. Full decoding passed: stereo MP3 at 44.1 kHz, 3,882 audio frames and a 101.407347-second served-file duration. The feed advertised 88 seconds; the provider retains that metadata value and does not truncate the served file to it. The original episode GUID remained in the saved info JSON. The other 108 media files were not downloaded by this check.

## Remaining work

RSS 1.0/RDF, JSON Feed, feed autodiscovery from HTML, external chapter documents, untimed transcripts, richer podcast extensions, multi-item Media RSS group semantics, additional navigation/archive formats, changing or expiring feeds, legacy archive identity mapping, broader authenticated/live checks and all podcast-site URL integrations remain pending. Inline chapters without a known episode duration are not resolved yet. Generic feed support does not make the dedicated Apple/iHeart/Simplecast and other catalog integrations complete. Production WPF cutover remains pending overall coverage verification.

References: [RSS 2.0](https://www.rssboard.org/rss-specification), [Atom RFC 4287](https://www.rfc-editor.org/rfc/rfc4287), [Media RSS](https://www.rssboard.org/media-rss), [Podcast Namespace transcripts](https://github.com/Podcastindex-org/podcast-namespace/blob/main/docs/1.0.md#transcript), and [Podlove Simple Chapters](https://podlove.org/simple-chapters/).
