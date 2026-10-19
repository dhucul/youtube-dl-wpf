# Native Bluesky post videos

Engine 0.56.0 implements a tested public-video subset of the captured `Bluesky` target. Complete provider parity and production cutover remain pending.

## Implemented flow

The provider accepts public `bsky.app/profile/<actor>/post/<key>` links using a handle, DID PLC, or hostname-level DID Web identity. It resolves handles with the public AppView, reads the requested post with zero reply/parent expansion, and verifies its account and record identity. A handle is also confirmed against its DID document, including when its post only quotes another post.

Direct video embeds and a post's own video inside `recordWithMedia` expose two native formats:

- `hls`: the supplied playlist, with native quality selection, complete-manifest verification and local decoding.
- `original`: the MP4 blob referenced by the post, fetched from the personal data server in its DID document. Its declared byte count and complete local decoding must pass before publication.

Caption blobs become WebVTT tracks through the same public blob endpoint. Known language and size bounds are enforced. The supplied thumbnail is retained. Aspect-ratio values are not reported as pixel dimensions. Silent originals are complete video resources and do not require a separate audio track.

The post's original blob CID, displayed video CID, playlist account/CID and thumbnail account/CID must agree. Signed query parameters are retained exactly. Account/post identity, supported blob types, declared byte bounds, duplicate caption languages, DID documents and blob server origins are validated. This is identity/size validation, not a cryptographic reimplementation of AT Protocol repository or CID verification.

A quote-only post follows its available referenced post using fresh metadata and checks the referenced record revision. Blocked, detached or unavailable quote views are not followed. A post's own video takes precedence over a quote. Traversal has cycle/depth limits and never selects an unrelated reply or parent.

Discovery and playback use anonymous sessions; caller authorization and cookies are not forwarded across the AppView, DID and federated blob origins. This implementation uses only public GET requests. Account-required records and HTTP denials fail explicitly. Archives use `bluesky:post <did>/<post-key>`, keeping authors distinct even when post keys coincide and remaining stable across handle aliases.

## Verification

Eleven deterministic checks cover metadata/resources, DID variants, URL boundaries, handle/post/blob/resource identity failures, access failures, quote revisions/restrictions/cycles, own-video precedence, blob/caption descriptors, credential isolation, author-specific archives and cancellation. Four generated-media checks cover original MP4, HLS, silent MP4, and wrong-size rejection with previous output preserved. They verify full video frame counts, sound state, original bytes, captions, callbacks and archive skips.

On October 9, 2026, native inspection and downloading of the public Bluesky announcement post `3l3vgf77uco2g` succeeded in both modes:

| Mode | Saved output | Retained original | Video | Frames | Duration |
| --- | ---: | ---: | --- | --- | --- |
| Original | 6,689,706-byte MP4 | 6,688,967-byte MP4 | H.264 1920 × 1080 | 892 video / 1,394 AAC | 29.737 s |
| HLS at 360p | 1,825,311-byte MP4 | 1,970,052-byte TS | H.264 640 × 360 | 892 video / 1,395 AAC | 29.760 s MP4; 29.754667 s TS |

All four files fully decoded. Each processed output retained its own source's audio/video frame counts. Both jobs saved the exact 139-byte English caption file and embedded its two cues with matching normalized text and timestamps. Each reported two file callbacks and the same DID/post archive identity. This live sample establishes neither full site coverage nor every account/quote workflow.

## Remaining work

Additional CDN/player layouts, live DID Web and quoted-video verification, alternate AppViews, standalone AT-URI/embed URLs, profile/feed collections, external-player delegation, authenticated workflows, fuller metadata, thumbnail variants, CID content hashing, URL refresh and complete option/archive parity remain pending. Original format dimensions/codecs are unknown before transfer; constrained requests use supported manifest facts or fail rather than inventing original quality facts.

## Primary references

- [Bluesky video lexicon](https://github.com/bluesky-social/atproto/blob/main/lexicons/app/bsky/embed/video.json): original blob, captions and public playlist fields.
- [Record-with-media lexicon](https://github.com/bluesky-social/atproto/blob/main/lexicons/app/bsky/embed/recordWithMedia.json) and [quoted-record lexicon](https://github.com/bluesky-social/atproto/blob/main/lexicons/app/bsky/embed/record.json): distinct own media and quote views.
- [AT Protocol DID specification](https://atproto.com/specs/did): DID resolution, handle confirmation and personal data server discovery.
- [Public blob endpoint](https://github.com/bluesky-social/atproto/blob/main/lexicons/com/atproto/sync/getBlob.json) and [post-thread endpoint](https://github.com/bluesky-social/atproto/blob/main/lexicons/app/bsky/feed/getPostThread.json).
- [Verified public post](https://bsky.app/profile/bsky.app/post/3l3vgf77uco2g).

No third-party extractor implementation or Python package was used.
