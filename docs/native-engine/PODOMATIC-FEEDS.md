# Native Podomatic feed migrations

Native engine 0.93 supports named Podomatic RSS addresses through the C# provider. It follows the publisher's feed redirects, reads the resulting public HTTPS feed with the shared bounded RSS/Atom parser, and preserves canonical destination identity, opaque episode GUIDs, original positions, supplied enclosures and archives. The website/API listing remains a separate view.

## Migration evidence

On October 10, 2026, `bestseopodcast.podomatic.com/rss2.xml` returned HTTP 301 to `feeds.buzzsprout.com/2019013.rss`, which resolved to `rss.buzzsprout.com/2019013.rss`. That current feed contains 694 entries and Buzzsprout audio enclosures. Older items retain Podomatic entry URLs as opaque GUIDs; newer items use Buzzsprout GUIDs.

This explains why the old Podomatic public listing's 538 entries and general 544-posting count differ from the 694-item feed. The feed is now hosted elsewhere; those totals must not be combined into a fabricated old-site inventory. The [Podomatic RSS guide](https://www.podomatic.com/help-center/articles/rss-feed-and-podcast-url) documents the original named feed URL. The redirect and current feed are independent live observations.

## Native behavior

The feed route isolates caller credentials for metadata and selected media. It requires a supported HTTPS feed destination and retains the final feed's identity. Selected progressive MP3 enclosures receive fixed local MP3 decoding and program-duration lower-bound checks before publication, alongside complete transfer and ordinary native processing. The provider does not rewrite a legacy GUID into a numeric old-host episode ID or replace a supplied enclosure with a guessed URL.

Inspected selections refresh their parent feed, preserve the original GUID and position when publisher ordering changes, and reject missing or conflicting identities. Originals, metadata, conversion and `native:feed` archives retain the shared feed semantics. Private-token feed requests cannot fall through to generic discovery; unrelated feeds keep their generic handling.

## Ancillary chapter handling

The live feed includes invalid chapter times in an unselected episode: some starts exceed its declared duration. The previous shared parser stopped the entire feed. It now keeps the affected episode's media and reports its chapter error without inventing repaired times. An explicit request to embed those invalid chapters fails before media transfer/publication. Valid chapter parsing is unchanged.

The [Podlove Simple Chapters specification](https://podlove.org/simple-chapters/) defines chapter titles, starts and chronological presentation. The reader still validates chapter values before embedding; isolating an ancillary error does not convert it into valid chapter data.

## Verification

Nine core checks cover redirect/canonical/GUID facts, dispatch, refreshed selection order/resources, archive identities, unsafe/non-feed/XML/limit boundaries, missing or conflicting GUIDs, unmigrated RSS, anonymous metadata, cancellation/denial and chapter-error isolation. Five generated-media checks fully decode selected/snapshot/converted MP3 workflows, retain exact originals and decoded samples, reject short audio, and refuse invalid chapter embedding without changing prior media or archives.

Live native inspection completed all 694 entries through the original Podomatic feed URL. An inspected selection downloaded the oldest episode, “What Is Internet Marketing and SEO?”, retaining its 2009 Podomatic URL GUID and original position 694. Processed MP3 was 24,106,576 bytes; the exact source was 24,107,247 bytes. Both contained 76,806 decoded audio frames and retained the attached JPEG. All 76,806 encoded audio payloads/order and complete decoded PCM matched. Container duration estimates were 2,006.360833 and 2,006.371500 seconds; identical decoded samples confirm no playback was lost.

Conversion produced a 19,973,592-byte AAC M4A with 86,407 decoded audio frames over 2,006.360816 seconds, retaining the original MP3. All four files fully decoded. Each initial job reported two file completions, and archive repeats zero. The snapshot retained all 694 identities without storing stale playback URLs.

Scratch evidence is under `work/podomatic-feed-probe` and `work/podomatic-probe` in the Codex workspace. The probe harness is not a runtime dependency.

## Remaining work

Additional migration destinations/formats, private feed workflows, external chapter formats, immutable publisher inventory guarantees and full site/option parity remain pending. This does not establish complete coverage of every old Podomatic website episode. Production cutover, old dependency removal and packaged verification remain pending.
