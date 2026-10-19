# Native Bandcamp public streams

The C# provider reads bounded HTML-encoded track/album data exposed by artist pages, without running page scripts. It preserves artist, track identity/title/duration/order, relative track pages, public audio URLs, and available bitrate/container descriptors. Albums retain resolved per-track metadata; unavailable streams report explicit item failures while completed tracks remain available.

Audio-only content can use the default native download mode when no video formats are offered. Explicit video-only requests remain enforced. Album limits, duplicate positions, invalid stream URLs, and ambiguous single-track responses fail explicitly. The engine does not purchase media or claim access to unexposed paid files.

On October 7, 2026, native inspection of `https://monplaisir.bandcamp.com/album/the-agreement` enumerated eight tracks. Selecting its first track downloaded a complete 2,758,947-byte public MP3 stream. Full decoding passed at 128 kbps with a 172.373333-second container duration. Album/full-quality purchases, discographies, custom domains, artwork/tags, embeds, access/subscription workflows, weekly/radio pages, and full upstream parity remain pending.

The live observations came from the public artist page and its exposed data. Fixtures validate selected/partially unavailable album downloads, single tracks, URL boundaries, identity/position handling, and limits. This subset does not establish every Bandcamp workflow.
