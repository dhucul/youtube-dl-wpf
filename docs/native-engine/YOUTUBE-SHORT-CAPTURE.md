# Short-video browser session acquisition

Engine 0.65.0 fixes the verified short-clip gap in the C# Windows capture path. Public `jNQXAC9IVRw` now completes through automatic browser acquisition and native transfer, including with generated selected-cookie state.

## Failure and resulting behavior

The short clip filled its browser buffer using startup requests, so no subsequent request contained both the later proof and explicit format references. The watch page could also move to another video after playback ended. The reader correctly refused unrelated metadata, while its requirement for format references in every candidate prevented a control-only refresh from becoming usable.

The capture buffer now recognizes startup observations and permits at most one same-video player reload after a six-second wait when no usable candidate is available. The fixed browser call checks the currently playing video and excludes ad playback before invoking `loadVideoById` for the requested ID. It stays in the same temporary browser session. It does not reload after an explicit denial or accumulate fragments across failed native sessions.

Control-only requests still require matching player configuration, client identity, expected video ID and offered formats. They additionally require prior evidence: either an earlier request that passed the normal reader, or format declarations from a correlated browser media response. Every response declaration must name the requested video, and its revisions/tags must appear in current player metadata. Missing declarations, changed formats, another video and explicit authorization denial fail. Native transfer starts with empty sinks and retains its complete-track checks.

Request/response observations and evidence remain bounded. Validated evidence is cached until the metadata/startup inputs change, rather than reparsing all history on every poll. New playback denials clear observations. Temporary debugging output and capture-dump hooks used during diagnosis were removed from production code.

## Verification

Two native checks verify control-only history requirements, changed-format/video rejection, response video/revision declarations and authorization denial. One app check verifies the startup wait, single-refresh budget and denial behavior. Existing identity, secret, cookie, transport and output tests remain applicable.

Live public and fixture-cookie runs produced identical files:

| Output | Bytes | Complete decoded frame counts | Duration |
| --- | ---: | --- | ---: |
| Processed MP4 | 744,478 | H.264 320 × 240: 284; AAC: 821 | 19.063583 s |
| Original merged MP4 | 744,412 | Same counts | 19.063583 s |
| Original video MP4 | 433,081 | H.264: 284 | 18.933333 s |
| Original audio M4A | 309,288 | AAC: 821 | 19.063583 s |

All four files fully decoded. Each run emitted four callbacks, recorded the video archive identity, and left zero temporary profiles. The selected-cookie run used generated fixture data, not personal browser cookies. The different video/audio end times reflect the source tracks; neither was inferred from the other.

This closes the tested short-browser workflow, not every YouTube layout or account condition. Further samples, playback reload/expiry policies, live streams, private-account evidence, captions and broader site/options parity remain pending. The production WPF backend still uses yt-dlp until the full replacement requirements are met.

The player operation is described in the first-party [YouTube player API reference](https://developers.google.com/youtube/iframe_api_reference#loadVideoById). Its availability on the watch-page player was also observed directly; future layout changes require verification.
