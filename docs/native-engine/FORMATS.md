# Native format selection

The C# selector now parses exposed format IDs, quoted IDs, extensions, best/worst media-type aliases, ranked choices, parentheses, slash fallbacks, and plus-separated track choices. Numeric/text filters support comparisons, prefixes/suffixes/contains, bounded regular expressions, and allow-missing markers. Parsing limits expression length, nesting, alternatives, and filters. Unresolved/protected URLs are never advertised as ready candidates.

Single-stream merging retains expression order and ignores later formats that duplicate an already selected stream kind. An explicit single selector does not automatically add audio. Audio/video-only processing remains enforced when an explicit pair is selected. Generated media checks inspect the resulting tracks and decode outputs.

Typed H.264/H.265/VP9/AV1 preferences and frame-rate limits filter progressive formats using available metadata. HLS masters retain codec/frame-rate attributes; static DASH retains inherited codecs and decimal/rational frame rates. Selection enforces these constraints before segment transfer. A leaf HLS playlist with insufficient metadata fails explicitly when constrained. Unknown progressive facts do not silently pass hard limits.

A live PeerTube request for H.264, at most 480p and 30 fps downloaded the 384,417-byte variant. Inspection confirmed H.264 video at 692×480 and 24 fps, with AAC audio and a 9.250008-second duration; the full output decoded successfully.

The experimental WPF adapter now passes custom selectors, codec preferences, and frame-rate limits. Native HTTP sessions retain website-set guest cookies automatically, while caller-supplied cookie files remain supported. A local HTTP check verifies cookie retention without reading browser profiles.

Separate-output/all-format selectors, multiple audio/video streams, full upstream sorting, remaining filter fields/operators, unknown-format probing, and custom filters within opaque master manifests remain pending. This verified subset does not establish complete format-option parity.

Behavioral reference: [upstream format-selection documentation](https://github.com/yt-dlp/yt-dlp#format-selection). The parser and transport selection code are independent C# implementations.
## Video constraints and silent resources

The engine now classifies a source before applying height/codec filters. Filtering every video track out of a video/audio source fails rather than silently downloading its remaining audio track. Explicit audio-only choices and genuine audio-only sources retain their existing behavior. A provider may identify a complete silent resource explicitly; this does not make ordinary video-only adaptive tracks complete. Deterministic checks and a live Reddit DASH maximum-height rejection verify the boundary; see `REDDIT.md`.
