# Structured media discovery

The generic C# provider now reads Schema.org JSON-LD as structured metadata rather than treating every URL in a script as an alternative download. It supports common compact forms of [VideoObject](https://schema.org/VideoObject) and [AudioObject](https://schema.org/AudioObject), including document arrays, `@graph`, local identifier references, schema contexts and fully qualified type names. It does not execute scripts or fetch remote contexts.

## Implemented behavior

- Page/article `mainEntity` relationships select the declared media and exclude unrelated recommendations and loose page URLs. A URL fragment matching a declared media identifier selects that object. Missing main references and cycles do not produce a partial selection.
- `encoding` and `associatedMedia` describe alternatives of one media object. Encoding-specific facts take precedence over parent facts for the same resource. Distinct media objects become a playlist with stable source positions and selected-only transfers, rather than being mixed as quality options.
- JSON string escaping is decoded correctly; relative and protocol-relative URLs resolve against the retrieved page. Query spelling and case-sensitive access variants are preserved. MIME declarations support extensionless content URLs when the format is known. Player `embedUrl` values delegate to registered native providers.
- Names, descriptions, creator names/references, thumbnails, fixed-length ISO 8601 durations, pixel dimensions, frame-size strings and exact byte-size declarations are retained when supported. Unspecified facts are not inferred from quality labels. Machine-readable caption objects support VTT, SRT and TTML/DFXP URLs with declared language strings.
- Media identities are scoped to the publisher and declared object. Blank-node and fallback identities also retain their document context. Native output templates, metadata, captions, originals and archives use the normal pipeline.
- Malformed JSON-LD, image-only declarations, HTML comments, templates and textarea examples do not fall through into loose URL scanning. Conflicting identifiers relevant to selected media fail; unrelated image/organization metadata does not suppress ordinary HTML discovery. Empty URLs never become requests for the HTML page itself.
- Explicit subscription/access restrictions, live broadcast declarations and unsupported clip ranges do not silently download a different page URL or preview. This public structured-data path does not establish authenticated entitlement or implement clipping/recording.

The reader is bounded to 64 script blocks, 2 MiB per block, 10,000 indexed objects, bounded graph traversal, 256 encodings/captions/thumbnails per media object and the configured playlist-entry limit. It is a purpose-built media reader, not a complete JSON-LD/RDF processor.

## Verification

Twelve deterministic checks cover literal metadata and MIME-only URLs, graph references, main entities, encodings, multiple videos and explicit fragments, audio roles, embed delegation, excluded/inert data, context boundaries, access states, conflicts/cycles, identity scoping, case-sensitive URLs and inventory limits. Three generated-media checks verify encoding selection, audio playback and selected-video collections, including complete frames, exact originals, caption text, metadata and archive skips.

A captured published Rumble JSON-LD document was replayed through a local HTTP page and the default native engine. Its player URL delegated to the Rumble provider and fetched fresh remote HLS media. The saved 16,320,527-byte MP4 and 17,603,756-byte TS were SHA-256 identical to the previously fully decoded reference files: 7,616 H.264 video frames and 27,331 AAC audio frames, approximately 634.713 seconds. This verifies real published metadata plus fresh remote playback; it is not a broad audit of third-party structured-data sites.

## Remaining work

Additional publisher verification, custom/remote contexts and aliases, expanded JSON-LD, legal multi-definition graph merging, language maps, additional quantity/unit forms, richer encoding facts, untyped extensionless-resource probing, parent metadata/caption handoff through embedded providers, explicit capture interactions, clip/live/account workflows, and full generic extractor parity remain pending. The overall yt-dlp replacement is still incomplete.
