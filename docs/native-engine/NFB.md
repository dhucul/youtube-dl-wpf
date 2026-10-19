# Native NFB and ONF films

The C# provider handles current public film pages and `/embed/` or `/embed/player/` links on NFB.ca and ONF.ca. It reads the supplied player configuration without executing site scripts or requesting purchase, rental or analytics operations. No Python or yt-dlp component is involved.

## Implemented scope

- Film-page registry identity is checked against the player configuration, statistics identity, slug, canonical film URL and embedded page identity. French and English versions retain their own registry IDs. Native archives use `nfb:registry <id>` to avoid confusing registry IDs with legacy slug records.
- Actual supplied signed HLS URLs are retained, including their access parameters and recognized expiry fragment. The currently observed CloudFront distribution and registry-bearing asset layout are validated. Other layouts fail explicitly pending verification.
- The primary source and described-audio source are distinct choices with readable format labels. Default quality selection uses the primary source; the described version is selected explicitly as `hls-described`.
- Metadata includes title, description, film-page director credits, poster, language and catalog duration when available. Rental/buy UI templates are not treated as playback availability facts. Missing sources, trailers, identity mismatches, access/protection flags, errors and unsupported pages do not download a replacement or preview.
- Existing native HLS quality selection, signed child URLs, caption rendition handling, full decoding, metadata processing, originals, archive and atomic publication are used. I-frame/image preview playlists are not selected as the film.

NFB catalog duration is not always the encoded stream duration. The live sample advertises 457 seconds while the supplied media is approximately 461 seconds. NFB therefore uses the complete media playlist's segment-duration sum for playback verification. The shared HLS pipeline now retains that duration as a fallback when a provider does not supply an exact verification duration; explicit provider verification durations still take precedence. Missing or shortened media still fails verification.

## Verification

Ten deterministic checks cover film/embed/French aliases, registry/canonical/resource boundaries, signed URLs, access/trailer states, described selection, generic iframe dispatch, archives, caller headers, HTTP errors and cancellation. Three generated-media checks verify original/described playback, complete media frames and originals, saved/embedded captions, readable format labels, catalog-versus-manifest duration semantics, and rejection of an incomplete declared timeline.

Live inspection verified [The Cat Came Back](https://www.nfb.ca/film/the-cat-came-back/) (registry 213822) and [Le chat colla...](https://www.onf.ca/film/chat_colla/) (registry 152156). English original and described-audio streams both downloaded successfully, with captions saved and embedded. Original output: 21,218,422-byte MP4 and 23,085,648-byte original TS. Described output: 21,221,642-byte MP4 and 23,089,596-byte original TS. Both retain 11,060 H.264 360x270 video frames and 10,811 AAC audio frames and fully decode. Their durations are approximately 461.362245 and 461.382289 seconds. Each job reports two complete files and the registry archive identity. Decoded audio hashes differ between the original and described versions.

The original and described caption tracks contain 40 and 39 source cues respectively. One original-track cue overlaps a later cue; ordinary MP4 subtitle muxing initially shortened it. Native embedding now composes overlapping VTT/SRT text into non-overlapping display intervals, using only temporary copies and retaining the saved source. The corrected original and described outputs each contain 39 display intervals after equivalent adjacent states are merged. Every boundary interval's visible text matches the respective saved caption timeline. A real overlap previously lost 2.628 seconds; that loss is corrected. Source audio language tags are retained. French playback has been inspected, not downloaded in this pass.

Two generated overlap checks verify simultaneous VTT/SRT text through MP4 embedding and unchanged video frames. A bounded-reader check preserves large disjoint documents, rejects excessive dense rendering and honors cancellation. This normalization covers ordinary VTT/SRT timelines for MP4/MOV/M4V; full layout/style fidelity, overlapping inline-timed cues and other subtitle formats remain pending.

## Remaining work

Series, seasons and specific episode selection now have a native implementation and live verification; see `NFB-SERIES.md`. Additional URL/CDN/player layouts, broader live French verification, authenticated/rental/account workflows, standalone HLS captions, additional metadata/chapters, expiry refresh and complete option/archive parity remain pending. This does not complete the overall replacement.
