# Native Panopto recordings

Engine 0.67.0 adds an independent C# Panopto provider. Engine 0.68.0 adds [folder/playlist workflows and exact chapter-gap preservation](PANOPTO-COLLECTIONS.md). Public complete podcast encodes are verified live; valid single-source HLS is covered by generated media. Panopto and the overall replacement remain partial. The main WPF app still uses its original backend pending full coverage.

## Recording and session binding

The provider accepts HTTPS `/Panopto/Pages/Viewer.aspx?id=<GUID>` and `/Panopto/Pages/Embed.aspx?id=<GUID>` addresses, including custom institution hosts. It uses the supplied server's player page and bounded form POSTs to `/Panopto/Pages/Viewer/DeliveryInfo.aspx`. The site's current player scripts were inspected as the protocol reference. No third-party downloader/extractor code is invoked or imported.

The page's final recording/server, delivery public ID, session public ID, session file ID and top-level session ID must agree. Media paths must belong to that session and delivery. Offered signed access queries are preserved literally. The archive identity is `panopto:delivery <server>/<delivery-GUID>`, preventing unrelated servers with matching GUIDs from sharing an archive key. Playback uses an anonymous media session while discovery and caption requests share their owned site session.

Live broadcasts, unavailable/purged encodes, reference-copy layouts, declared DRM, incomplete or offset timelines and unsupported encodings fail explicitly. A source requiring edit composition or several independent views is not exposed as a complete single-source download. Completed podcast encodes can still represent those recordings. Folder and playlist workflows are implemented separately in 0.68.0; see PANOPTO-COLLECTIONS.md for their verified scope and remaining boundaries.

Complete MP4/M4A/MP3 podcast formats are exposed when their corresponding encode-complete flag is present. An ordinary complete single source may expose HLS. Unknown pixel dimensions are not invented, so a progressive format with no supplied height can be excluded by a height constraint. Broader format facts and source layouts remain pending.

## Captions, chapters and saved files

The delivery response lists caption languages. The provider maps the current player's 32 language IDs, fetches only selected languages through bounded `getCaptions` POSTs and converts the event timing to WebVTT. It preserves literal text, checks session IDs and timing bounds, and avoids blank lines accidentally ending a cue. At most sixteen selected languages are fetched per request. Unknown language IDs are reported and excluded. The disclaimer flag does not establish authoring provenance; automatic-caption classification remains unverified.

Table-of-contents events supply chapter titles and times. Ordered primary/slide events become non-overlapping chapter ranges ending at the recording duration. Standalone captions need no media or ffmpeg. Ordinary media downloads support related-file saving, caption/chapter embedding, original retention and archive integration.

Live testing exposed two shared pipeline issues that are fixed in this version:

* New MP4 caption clocks could make ffmpeg choose a very large movie timescale. Long chapter durations then overflowed its chapter sample field, leaving references to a missing chapter track. Embedded or preserved chapters now explicitly use a millisecond movie clock. Files without chapters retain their prior precision. A generated 12-second source with existing captions, a 90 kHz video clock, 44.1 kHz audio and a ten-second chapter verifies the replacement captions, chapter references and complete frames without ffprobe errors. This uses ffmpeg's documented [movie timescale option](https://ffmpeg.org/ffmpeg-formats.html).
* Successful media publication now reports committed caption, image and metadata paths through `SidecarPaths` and `NewSidecarPaths`. Media paths and publication callbacks retain their existing meaning. Files are reported after atomic publication succeeds.

## Live verification on October 9, 2026

Public samples were taken from Panopto's own [LTI announcement](https://community.panopto.com/discussion/2010/what-039-s-new-panopto-releases-new-lms-integrations-with-lti-1-3-support) and [captions announcement](https://community.panopto.com/discussion/2050/what-039-s-new-a-new-captions-api-for-partners).

| Recording ID | Processed MP4 | Original MP4 | Preserved playback | Related content |
| --- | --- | --- | --- | --- |
| `8fb9da28-c640-46c7-80ca-affc01684ba4` | 9,445,718 bytes | 9,419,941 bytes | H.264 1280×720, 1,858 video and 3,195 AAC frames; 74.263 s processed | 12 caption cues, 1,546-byte VTT, four embedded chapters, JPEG and info JSON |
| `f5e49aba-90de-40b8-b950-b021012b6d49` | 5,699,188 bytes | 5,685,322 bytes | H.264 1280×720, 971 video and 1,665 AAC frames; 38.736 s processed | Five caption cues, 803-byte VTT, two embedded chapters, JPEG and info JSON |

All four MP4 files fully decoded, with no probe/decoder diagnostics. Embedded and saved caption cue counts matched. Each fresh job emitted two media callbacks and recorded its server-scoped archive identity. No Python, yt-dlp, personal account cookies or manually supplied playback capture was used.

The offered fragmented HLS sources for both samples did not pass strict validation. The first source's original encoded stream had two non-increasing decoded presentation timestamps despite monotonically increasing packet decode timestamps. Its 1,878 video frames and 75.085474-second manifest also differ from the complete podcast encode. The second showed the same timestamp class of failure. Native verification rejected them before final-file publication and archive updates. These are recorded failures, not verified HLS playback or a reason to suppress media-tool errors. Other native HLS layouts still need broader live Panopto verification.

Eleven deterministic checks cover protocol fields, literal queries, identity/origin/session binding, limits and access boundaries, edited/multi-view selection, requested caption languages and timing, archives, standalone captions and native delegation from generic embedded pages. Four provider generated-media checks cover complete MP4/HLS transfers, full frame/audio preservation, original bytes, caption/chapter embedding, committed related-file reporting, authenticated-discovery isolation, incomplete-output refusal and the long-chapter regression.

Four additional checks validate bounded MP4 chapter-reference parsing, track identities, malformed references and file reading/cancellation. A further generated-media check preserves chapter/caption text and timing through later metadata processing and MOV remuxing, compares every encoded audio/video packet hash, checks all video frames, and fully decodes the outputs. QuickTime chapter data tracks are removed from ordinary stream mapping only after their actual `chap` references bind them to present track IDs; ffmpeg recreates their checked chapter metadata. Unidentified text-data layouts fail explicitly. This prevents duplicate chapter tracks and the invalid data-tag copy that previously failed MOV remuxing.

Remaining Panopto work includes broader collection parity, account/invitation and SSO flows, edit and multi-view composition, live broadcasts, alternate/on-premises media paths, additional encodings and formats, caption provenance, richer metadata and complete compatibility. All-site coverage, dependency removal, packaging and final WPF cutover remain outstanding.
