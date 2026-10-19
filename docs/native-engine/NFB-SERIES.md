# Native NFB series and episodes

The C# NFB provider now handles `/series/<slug>/`, explicit `/season<number>/` selections and episode links using either an episode slug or `episode<number>`. It reads the series declaration from the public page and the same public v5 series endpoint used by the site's application. No site JavaScript is executed and no account-history or purchase operation is requested.

## Implemented behavior

- The page's series ID, slug, type, public API endpoint and requested season are checked. API identity, category and canonical series resource must agree.
- Declared season and episode counts must match the returned nested inventory. Season numbers and episode numbers/registry IDs/slugs cannot repeat. Each episode must explicitly belong to the requested series, and its supplied film URL must match its slug.
- The series root preserves all seasons in API order. An explicit season has its own collection identity and positions. An explicit episode resolves only that film after checking the displayed registry and episode index. Site redirects from film URLs to matching series-episode pages are supported.
- Public availability, geoblocking and coming-soon state determine which entries are currently usable. Unavailable entries retain their positions. Only selected entries fetch fresh film/player metadata and signed playback URLs; listing discovery does not retrieve episode media.
- Supplied media templates provide thumbnails. User-specified collection limits apply to the requested listing; direct episode resolution does not fail merely because other seasons exist. Absolute response/inventory limits still apply.
- Entries can declare an expected archive identity. Known archived episodes skip their film/player requests. Freshly resolved media and resolved snapshots must match that identity before publication. Video archives remain separate from the series identity.

The normal playlist session, selection, ordering, file-template, caption, metadata, original-retention and atomic-publication flows apply.

## Verification

Nine deterministic series checks cover whole-series and season scope, numeric/slug episode links, identity/count/membership/URL errors, unavailable positions, limits, selected-only lookup, archive skips, resolved-registry mismatches, HTTP failure and cancellation. A generated-media series check retains every media frame, saves and embeds captions, preserves selected positions and skips archived episodes without additional player requests. A separate core check rejects a resolved snapshot that contradicts its advertised identity.

Live native inspection of [The Great List of Everything](https://www.nfb.ca/series/the-great-list-of-everything-series/) matched all 26 episode registry IDs in the captured complete API inventory. Season 2 returned its 13 entries. The specific episode link for *The Camera* resolved registry 1020020.

A live root-series job selected position 5 and produced a 5,911,713-byte MP4 and a 6,442,572-byte original TS. Both fully decoded with 3,082 H.264 480x270 video frames and 3,013 AAC audio frames, approximately 128.633211 seconds. All 54 saved and embedded caption intervals preserve visible text and timing. The file retained source index 5, selected-order number 1 and collection count 26; its archive entry is `nfb:registry 1020020`. Two media completion callbacks were reported.

## Remaining work

Additional series/locale/layout verification, standalone collections, unusual or changing season inventories, account/rental workflows, larger/lazy inventories, more metadata/chapters, source expiry refresh and complete option/archive parity remain pending. The overall yt-dlp replacement is not complete.
