# Native Kaltura provider

Version 0.132.0 adds primary public Business Insider/JW and Al Jazeera/Brightcove article delegation, signed DASH graph expiry checks and bounded automatic selection of other issuer-offered Kaltura profiles before transfer. TS metadata/remux preserves picture presentation timing when supplied decode clocks conflict. All 2,064 native and 74 app checks passed, including 38 new checks. Four complete public workflows produced fourteen fully decoded media/source files with fourteen exact packet inventories; both Kaltura DASH profiles passed current playback. Two exact consumer keys gained partial evidence. Real private evidence remains user-deferred; full provider/option/catalog and production coverage remain open. See [consumer and DASH verification](CONSUMER-DASH.md).

Version 0.130.0 adds shared Wistia/JW/Brightcove/Kaltura playback renewal, explicitly scoped hosted credentials and HTML delegation, publisher-provided JW signing, Wistia/Brightcove token access and Kaltura account/session handling. All 2,026 native and 74 app checks passed, including 50 new checks. Four public workflows produced ten fully decoded media/source files with ten exact packet inventories. At the 0.130 checkpoint Kaltura public DASH fragments were denied; its offered direct flavor passed. Current 0.132 DASH evidence is recorded above. Real private/signing evidence remains user-deferred; consumer-key/full provider/option/catalog and production coverage remain open. See [shared hosting verification](SHARED-HOSTING-RENEWAL.md).

The C# provider resolves public players on the standard Kaltura service. It recognizes current Playkit URLs and legacy widget/iframe player URLs, validates partner/entry identities, and preserves an explicitly supplied `ks` session. Otherwise it obtains a public widget session. No Kaltura SDK, yt-dlp code or Python runtime is used.

The provider reads media metadata and playback context before resolving downloadable flavors. Session values are scoped to API request bodies. Kaltura's JSON API requires a bare `application/json` content type; live testing showed that including a charset caused it to ignore the JSON body and return XML. The shared HTTP helper now permits omitting that optional header parameter for this provider.

## Implemented scope

- Ready direct flavors explicitly offered by clear playback sources, with stable flavor IDs, container, dimensions, codec and frame-rate facts, and bitrate converted from Kbit/s.
- Exact `sizeInBytes` when positive. Rounded KByte sizes and default zero values are not presented as exact sizes.
- Exposed clear HLS and DASH sources, retaining numeric or string delivery-profile identifiers.
- Entry identity, title, description, creator, duration and thumbnail, plus exposed WebVTT captions.
- Iframe embeds, autoembed script URLs with an entry ID, and flat literal `kWidget.embed` declarations. Duplicate, computed and misleading quoted fields are rejected.
- Playback restrictions stop discovery before direct flavor resolution. Protected sources are excluded; malformed protection fields fail explicitly.
- Embedding-page referrers, including the final page after a redirect, reach native provider access checks. An explicit caller referrer remains authoritative.

## Live verification

Public partner `309`, entry `1_rcit0qgs`, “The Official 2012 Kaltura DevConnect Promo,” was inspected and downloaded on October 7, 2026. The provider reported 13 formats: seven direct flavors and six HLS/DASH sources. The sample exposed no captions.

| Path | Bytes | Video | Audio | Container duration |
| --- | ---: | --- | --- | ---: |
| Direct flavor `1_cull45rt` | 6,979,543 | H.264, 480×256, 23.976 fps | AAC | 124.138005 s |
| HLS profile `13942`, capped at 360p | 14,498,560 | H.264, 640×360, 23.976 fps | AAC | 124.165822 s |
| DASH profile `4662`, capped at 360p | 14,106,108 | H.264, 640×360, 23.976 fps | AAC | 124.161905 s |

All three complete outputs passed ffprobe inspection and full ffmpeg decoding. DASH audio/video were merged through the native ffmpeg pipeline. These establish tested public workflows, not complete Kaltura parity. Ephemeral sessions and service responses remain outside repository fixtures.

Ten deterministic checks cover URL/session handling, format facts, captions, restrictions, malformed identities/protection, actual native fixture downloads, API session scope, embed/referrer behavior and cancellation.

## Remaining work

Playlists, on-premise/custom/regional endpoints, MediaSpace portals, complex dynamic embeds, scheduled/live media, broader account/SSO/portal and remote private-session evidence, additional refresh semantics, remaining caption formats, alternate audio, metadata/chapters, access-control action handling and full upstream parity remain pending. `kaltura:` pseudo-URLs are not yet accepted by the HTTP-oriented engine contract. Restricted actions are refused rather than interpreted as permission to download another source.

Protocol references: [public widget sessions](https://www.kaltura.com/api_v3/testmeDoc/actions/session.startWidgetSession.html), [playback context](https://www.kaltura.com/api_v3/testmeDoc/objects/KalturaPlaybackContext.html), [flavor metadata](https://www.kaltura.com/api_v3/testmeDoc/objects/KalturaFlavorAsset.html), [flavor URLs](https://www.kaltura.com/api_v3/testmeDoc/actions/flavorasset.getUrl.html), and [official player embed forms](https://github.com/kaltura/kaltura-player-js/blob/master/docs/embed-types.md).
