# Shared hosting, delegation and playback renewal

Version 0.132.0 adds primary public Business Insider/JW and Al Jazeera/Brightcove article delegation, signed DASH graph expiry checks and bounded automatic selection of other issuer-offered Kaltura profiles before transfer. TS metadata/remux preserves picture presentation timing when supplied decode clocks conflict. All 2,064 native and 74 app checks passed, including 38 new checks. Four complete public workflows produced fourteen fully decoded media/source files with fourteen exact packet inventories; both Kaltura DASH profiles passed current playback. Two exact consumer keys gained partial evidence. Real private evidence remains user-deferred; full provider/option/catalog and production coverage remain open. See [consumer and DASH verification](CONSUMER-DASH.md).

Native engine 0.130.0, verified October 11, 2026. This batch adds 35 core and 15 generated-media checks across Wistia, JW, Brightcove, Kaltura and shared HTML delegation. All 2,026 native checks (1,483 core and 543 media) and 74 app checks passed. The Release build has zero warnings and errors. Existing uncommitted work and user data are preserved. Production startup still uses the original yt-dlp backend while full native coverage is completed.

## Implemented behavior

All four hosts now use one job-owned issuer/playback context. Clear progressive files, captions and declared HLS/DASH graphs renew on expiry or bounded resource rejection using the shared 0.128 transport. The refreshed page must preserve publisher/media identity and format facts. Reordered Wistia/JW/Brightcove source arrays retain their initial format selectors through unique fact matching; ambiguous or changed formats fail. Kaltura retains its publisher flavor IDs. Issuer tokens, passwords, policy keys and signing secrets are excluded from media headers.

Materialized JW/Brightcove playlists retain existing per-entry snapshots and source positions without fetching each entry again during ordinary discovery. Each selected entry now carries a renewal lease tied to membership in its original playlist. Renewal rereads the authorized parent and requires exactly one matching selected member. Complete archive identities are attached to entries. Disappeared members and incomplete/paged lists remain explicit boundaries.

Hosting credentials declare an exact provider, account where required, and media identity. Native extensions are `--hosting-provider`, `--hosting-account`, `--hosting-media`, `--hosting-access-token`, `--hosting-signing-key` and `--hosting-token-lifetime`. They preserve ordered overrides and analysis arity. Credential fields are excluded from request JSON and diagnostics. These extensions do not change the frozen upstream-option inventory.

Generic HTML can carry these explicit hosted credentials through exactly one matching declared embed. The article receives no hosting token; a different host/account/media or ambiguous matching context fails before issuer requests. Actual redirected article referrers survive delegation unless the caller specifies a referrer. Unrelated inline trailers do not replace the selected authenticated host. Flat literal JW `setup({playlist: ...})` declarations now delegate supported endpoints without executing JavaScript; computed/nested layouts remain partial.

## Provider access paths

- Wistia: supplied read-scoped Data API bearer token at the exact identified `api.wistia.com/v2/medias/{id}.json` endpoint. Ready, matching media and offered containers are required. Public embed JSON remains supported. Offered explicit HLS/DASH containers can use the native adaptive pipeline. Viewer-password, Channel and further API/caption layouts remain pending.
- JW: supplied resource JWT or a publisher-provided signing secret for declared delivery endpoints. Native HMAC-SHA256 JWTs bind the exact resource path and bounded expiry; accepted query values are included in the signed payload. A publisher signing credential can resolve signed player aliases to the matching media endpoint. No signing key is acquired from a website or built into the app. Public and signed playlist snapshots retain selected renewal.
- Brightcove: existing player policy stays on the Playback API; a supplied exact account/media JWT can authorize that API request. Account/player/media identity and actual referrer/Origin are preserved. Current legacy Playback API is covered; enhanced playback authorization, token provisioning, regional and more domain/account variants remain partial.
- Kaltura: supplied scoped KS, existing literal player KS, public widget sessions, or native username/password/read-only netrc login with an optional initial OTP. The documented login and session-info APIs bind partner, user, type and expiry. Expired account sessions can relogin within a three-login job limit without reusing an OTP; changed users fail. Expired supplied sessions require a fresh authorized KS and never switch to guest access. KS/password/OTP remain in exact API bodies, and restrictions still gate direct flavors. Opaque injected account handlers must explicitly disable automatic redirects.

Hosting media redirects remain bounded and provider scoped. Public Kaltura inspection established an HTTPS redirect from `cdnapisec.kaltura.com` to the exact host `kalhlspd-a.akamaihd.net`; that host is now supported, while unrelated Akamai hosts are refused. This does not establish permission for every resource served there.

## Verification

The 35 protocol checks cover typed/redacted credential scopes, JWT signature/resource/expiry, issuer isolation, ambiguous/mismatched delegation, stable reordered formats, selected playlist membership, changed/denied/cancelled renewal, Kaltura password/OTP/session lifecycle and exact CDN redirects. Fifteen generated-media cases cover progressive and adaptive access for all four hosts, delegated Wistia/JW, Kaltura login, selected JW playlists, protected captions and failure/cancellation.

Positive generated recordings retain all 36 video frames and every coded video/audio packet in source order. Retained media fully decode; Brightcove captions are saved/embedded; callbacks and archives preserve job identity. Revoked access, changed formats and cancellation leave previous files and archives intact. Direct known IDs skip before HTTP on archive repeats. Delegated articles and collections still require bounded identity/inventory metadata lookup; repeats transfer no media and produce no completion callback.

Four actual public workflows completed through the native WPF adapter. All ten output/source files independently fully decoded, and all ten ordered main/original or main/component packet inventories matched exactly:

| Workflow | Main bytes | Original bytes | Decoded seconds | Video frames | Audio frames |
| --- | ---: | ---: | ---: | ---: | ---: |
| Wistia `e4a27b971d` progressive | 1,411,923 | 1,411,804 | 16.509 | 395 | 710 |
| JW `9iV4GEIm` HLS | 2,403,204 | 2,210,880 | 64.275 | 1,927 | 2,769 |
| Brightcove account `1752604059001`, video `4029697544001`, DASH | 451,198 | 451,163 | 8.568 | 203 | 369 |
| Kaltura partner `309`, entry `1_rcit0qgs`, direct `1_cull45rt` | 6,979,892 | 6,979,543 | 124.138 | 2,975 | 5,347 |

Brightcove also retained 253,003-byte video and 198,693-byte audio components, giving four decoded files for that workflow. Each direct public archive repeat opened no HTTP session. Public checks use no account token or publisher signing secret; the Brightcove policy and public Kaltura widget session remain issuer scoped.

Kaltura's offered public DASH manifest redirected successfully after the exact-host correction, but its fragments were denied by the CDN. Bounded renewal stopped without completed publication. Its explicitly selected offered direct flavor succeeded separately. The failed attempt is retained as a limitation, not counted as successful adaptive playback.

Real-account verification remains explicitly user-deferred. Authentication, publisher signing and forced renewal are verified through controlled protocol/generated-media tests; public recordings do not establish real private access. No saved account was inspected or changed.

## Remaining backlog

- Real permitted Wistia/JW/Brightcove/Kaltura private authentication, publisher signing and sustained remote renewal evidence remains deferred.
- Additional Wistia API/caption/password/Channel variants, JW current site-playback/paged feeds, Brightcove enhanced authorization/live/region/domain/paged playlists, and Kaltura MediaSpace/custom/regional/SSO/live/collection/action workflows.
- More provider consumers, advanced CDN/addressing/changed-track contracts and durable recording/session recovery.
- Exact consumer extractor-key specifications and live acceptance. Generic/shared hosting capability alone does not prove consumer site parity; no new exact-key parity claim is made.
- Full option/default/catalog coverage, production cutover, dependency removal and standalone packaged acceptance.

Primary protocol references: [Wistia Data API usage](https://docs.wistia.com/docs/uploader), [Wistia embed construction](https://docs.wistia.com/docs/construct-a-wistia-embed-code), [JW delivery signing](https://docs.jwplayer.com/platform/reference/protect-your-content-with-signed-urls), [Brightcove Playback API authentication](https://apis.support.brightcove.com/playback/getting-started/overview-playback-api.html), [Kaltura user login](https://www.kaltura.com/api_v3/testmeDoc/actions/user.loginByLoginId.html), and [Kaltura session information](https://www.kaltura.com/api_v3/testmeDoc/actions/session.get.html). Implementation is independently authored C#, with no yt-dlp extractor code, Python runtime or fallback.
