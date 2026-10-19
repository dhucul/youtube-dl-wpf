# Native Wikimedia media files

Engine 0.63.0 implements a public-file subset of the captured `wikimedia.org` provider. It uses MediaWiki's published `videoinfo` response rather than extracting one convenient URL from page markup.

## Current coverage

English `File:`/`Image:` file-page URLs, mobile-host aliases, index.php title URLs and direct-file special-page aliases are recognized on the supported Wikimedia project host families. File-page lookup follows bounded API normalization/redirect chains. A Wikipedia file backed by Commons is resolved to its authoritative repository before assigning its archive identity. Direct-file aliases expose the original resource only. Historical/resized/edited/clipped requests are rejected instead of being silently replaced with the current file.

Metadata is bound to namespace 6, the requested/normalized title, canonical file name, repository host, file description and upload path. Missing or hidden files fail explicitly. Originals and supplied transcodes retain their literal URL queries. Dimensions, codecs, bandwidth, original size and available original frame-rate facts are reported without inventing derivative facts.

Original audio/video and complete soundless video are supported. A video-only alternative to a source with audio is not mislabeled as complete: default selection prefers an available complete rendition, while explicit video-only selection can use the video-only file. Fixed local demuxers verify complete decoding and original byte counts where supplied. Ogg, MP3, WAV and FLAC were added to the opt-in verifier's accepted local formats.

Supplied VTT/SRT tracks retain their original caption URLs and validated file, language and API action. The observed `zh-cn` → `zh-Hans-CN` and `zh-tw` → `zh-Hant-TW` label aliases are recognized without changing those URLs. Other mismatched language declarations still fail. Metadata-only caption saving/conversion uses the existing native path without requesting video.

Thumbnail URLs accept the observed upload and thumbnail CDN hosts while retaining file/repository binding. Audio's supplied generic PNG file-type icon is accepted only from the same wiki's known asset path. Display dimensions and responsive ratios are not presented as physical image dimensions: a live requested 640-pixel preview returned a 960-pixel CDN image. HTML display metadata is reduced to text. Archives use `wikimedia:page <repository-host>/<page-id>`, so referring-wiki aliases do not create conflicting file identities.

## Verification

Eleven deterministic checks cover formats/roles, literal queries, thumbnails, URL aliases, shared repositories, title redirects, domain/credential boundaries, resource identity, hidden/historical files, captions, selection, archives and cancellation. Four generated-media checks verify complete video, silent video, Ogg audio and standalone caption conversion, including full frames, exact originals and embedded/saved captions.

Live checks on October 9, 2026:

- A Wikipedia link to Commons file `Big buck bunny 720p 5mb.webm` resolved repository page 158250589. A maximum-height request of 360 selected the complete 240p VP9/Opus rendition instead of the video-only 360p alternative. Saved output was 1,083,269 bytes and the exact original rendition 1,083,087 bytes. Both fully decoded with 739 video frames at 426 × 240 and 1,479 six-channel Opus frames. Durations were 29.581/29.576 seconds. Two supplied JPEG previews were also saved.
- Commons `En-us-hello.ogg`, page 268421, saved a 9,058-byte processed file and 9,036-byte exact original. Both fully decoded as mono Vorbis at 44.1 kHz, 23 audio frames and 0.487619 seconds. Repository/page archive records were verified.
- A captions-only request for `Big Buck Bunny 4K.webm` saved a 1,844-byte English SRT from the supplied 1,851-byte VTT. All 32 cues matched in normalized text and timing. The generated fixture separately verifies that this path makes no media request.

These samples do not establish every Wikimedia project or file layout. Articles/categories/collections, localized input namespace names, revision selection, additional language aliases, additional codec/manifest layouts, authenticated files and full metadata/license/option/archive parity remain pending. The production WPF backend is unchanged.

Primary references: [TimedMediaHandler API](https://www.mediawiki.org/wiki/Extension:TimedMediaHandler/API), [verified video file](https://commons.wikimedia.org/wiki/File:Big_buck_bunny_720p_5mb.webm), [audio file](https://commons.wikimedia.org/wiki/File:En-us-hello.ogg), and [captioned file](https://commons.wikimedia.org/wiki/File:Big_Buck_Bunny_4K.webm).
