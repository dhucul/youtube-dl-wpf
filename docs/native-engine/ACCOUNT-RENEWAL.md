# YouTube and Vimeo accounts and playback renewal

Verified October 11, 2026, native engine 0.126.0. This batch adds provider-specific access and renewal to the existing independent C# engine. Existing user settings and uncommitted work are preserved. Production WPF startup still uses the existing yt-dlp backend while native coverage is completed.

## Implemented behavior

YouTube uses cookies from the existing selected cookie source. Native argument `--youtube-account-index` selects account 0–9 for watch pages, browser navigation, playlists and channel pages. Signed-in configuration must expose the selected index and a stable `DATASYNC_ID` or `USER_SESSION_ID`; brand identity is retained separately. Missing identity, wrong account, identity changes and sign-out fail. Account authorization is signed in C# with the cookie, timestamp and exact YouTube origin, and sent only to the player/browse API. Brand requests carry the corresponding delegated context. Private watch-page denials can request the authenticated Web player; authenticated jobs do not fall back to anonymous mobile playback. API 401/403 permits one same-page refresh with identity validation.

Vimeo supports the saved video-password option and native argument `--vimeo-access-token`. Password submission uses UTF-8 Base64 at the exact identified player gate, retains the job cookie jar, and accepts only the matching unlocked configuration. The API bearer token is sent only to `api.vimeo.com/videos/{id}` (including an unlisted hash when supplied). Returned video identity and file access are required. Clear offered MP4/WebM progressive files retain stable rendition IDs, size/dimension/FPS facts and advertised expiry. Tokens are excluded from serialized requests and session diagnostics. Username/password and interactive OAuth login are not added by this batch. When an API token is supplied, it selects the API file path.

Job-owned resource leases renew exact progressive media and caption URLs before imminent expiry or once after a resource's 401/403. Concurrent requests rejected in the same generation share one issuer refresh. Every registered resource must still belong to the same video and preserve its format/track facts. Expired replacements, changed identity, revoked access and foreign resources fail. Renewal is limited to eight issuer generations per job. Vimeo read-only redirects are limited to approved HTTPS playback/CDN origins, with at most five hops; issuer bearer/password credentials never become media headers.

YouTube SABR can renew an expired endpoint, HTTP 401/403 or explicit player-reload request using a fresh matching playback context. Selected format identity, last-modified/tag identity and client version remain fixed. Completed segment/buffer acknowledgements survive; unfinished buffers are discarded. Two renewal attempts are allowed. Explicit protection status 3 remains terminal, and cannot trigger new sessions to accumulate denied fragments. Cancellation and failed access protect existing files, captions, archives and completion callbacks.

The two arguments are native extensions. The frozen inventory remains 1,731 extractor keys, 126 app options and 276 captured long option names. They do not increase the count of captured upstream handlers or imply full provider parity.

## Verification

The Release solution built with zero errors and warnings. All 1,924 native checks passed: 1,406 core and 518 media checks. This batch adds 27 core and 10 generated-media checks. All 74 app checks passed. After final account-identity, collection-routing and numeric-field review fixes, all 1,406 core checks and all 37 batch checks passed again (the focused runner also executes seven foundation checks, for 44 total).

Protocol checks cover origin-bound account hashes and delegated selection, ambiguous/signed-out/switched/missing account identities, one bounded API retry, UTF-8 password submission, exact issuer-token scope, file permission, stable resource generations, concurrent refresh, expiry, cancellation, safe redirects and SABR renewal/terminal denial. Generated protected workflows fully decode complete H.264/AAC media and verify original retention, callbacks, archives and failed publication. They exercise Vimeo password/token/expiry/renewal/revocation, YouTube cookie/progressive renewal and SABR HTTP/reload/protection boundaries.

Actual public YouTube `jNQXAC9IVRw` completed through the native WPF adapter and skipped before HTTP on an archive repeat. Independent full decoding and ordered packet comparison produced:

| Output | Bytes | Decoded seconds | Video frames | Audio frames |
| --- | ---: | ---: | ---: | ---: |
| VP9/Opus WebM | 440,841 | 19.006 | 284 | 951 |
| Retained video | 185,292 | 18.933 | 284 | 0 |
| Retained audio | 255,427 | 19.006 | 0 | 951 |

Every coded video and audio packet in the combined output matches its retained source in order. Public playback used no account credential. Vimeo's official SDK sample `76979871?h=8272103f6e` exposed nine reported formats and zero ready formats in this observation. No clear external Vimeo playback or private-account renewal is claimed.

The user chose to continue without real-account verification. No saved account was inspected or changed in this batch, and no real private account was used. Controlled protocol/generated-media evidence is recorded separately from public external evidence.

## Remaining backlog

- Real permitted YouTube/Vimeo private playback, account-only collections, logout/revocation and sustained server-driven renewal evidence remain user-deferred.
- Interactive Google/Vimeo login, OAuth token acquisition and refresh grants, TV/account-specific flows, broader account cookie/layout variants and user-facing native credential controls.
- Signed HLS/DASH manifest and segment-graph renewal; the exact-resource lease currently covers progressive files and caption documents. Existing adaptive transfer support does not establish refreshed private graph identities.
- Vimeo adaptive-only API responses, API text tracks, additional password/domain/private variants, supplied-file-link regeneration and collections.
- YouTube additional authorization/challenge/client layouts, broader collection/search/live/translated-caption access, changed-format recovery and durable cross-job resume.
- Shared provider consumers, full option/default/catalog parity, production startup/dependency removal and clean packaged acceptance.

Primary protocol references: [Vimeo API authentication](https://developer.vimeo.com/api/authentication), [Vimeo API file-link scopes and expiry](https://help.vimeo.com/hc/en-us/articles/12427806914577-About-video-file-download-links-from-the-API), [official Vimeo player sample](https://player.vimeo.com/video/76979871?h=8272103f6e), and [YouTube.js account-request protocol reference](https://github.com/LuanRT/YouTube.js/blob/main/src/utils/HTTPClient.ts). Vimeo's current player gate behavior was observed directly. Runtime implementation is independently authored C#, with no yt-dlp extraction source, runtime or fallback.
