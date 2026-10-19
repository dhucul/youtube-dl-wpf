# Adaptive playback renewal and provider login

Version 0.140.0 adds durable completed-fragment recovery for static HLS and segmented/JSON/indexed DASH, fresh provider/track/membership checks, strong server-validator rechecks, local SHA verification and safe publication after retry. All 2,274 native and 74 app checks passed, including 45 new checks. Three public interrupted/recovered adapter workflows produced ten fully decoded files and ten exact ordered packet inventories. Brightcove/JW each reuse three validated network fragments; Vimeo lacks media validators and safely retrieves all 94 media fragments again, reusing only fresh matching initialization. Full coverage, live/SABR and wider recovery, production cutover and user-deferred private evidence remain open. See [adaptive recovery verification](ADAPTIVE-RECOVERY.md).

Verified October 11, 2026, native engine 0.128.0. This batch adds 42 core and 10 generated-media checks. All 1,976 native checks (1,448 core and 528 media) and 74 app checks passed. The Release build has zero errors and warnings. All previous uncommitted work and user settings are preserved. Production startup still uses the existing yt-dlp backend while native coverage is completed.

## Adaptive signed-link renewal

The shared job-owned lease now includes HLS and DASH roots. Each manifest is parsed against its actual response URL before returning canonical URLs to the download pipeline. Fresh signed prefixes and relative URLs therefore resolve at the publisher's current location without changing stored recording identities.

HLS graphs register master variants, selected video/audio/subtitle playlists, media sequences, discontinuities, program times, byte ranges, initialization sections, AES-128 keys and IV facts. DASH graphs register representations, period IDs/start times, templates, timeline/number-addressed segments, initialization/ranges, supported static indexed files and caption resources. Renewal fetches the observed graph from fresh same-video issuer links and matches resources by declared track/timeline facts. It never strips arbitrary URL parameters or infers identity from a filename.

Concurrent rejected requests share the issuer generation. Each resource can replay once after 401/403; eight issuer renewals are allowed per job. Changed encoding/timing/ranges/encryption, lost static resources, ambiguous shared mappings and disappeared selected manifests fail. Moving windows can add resources and retire old ones; a requested retired resource fails rather than advancing silently. Completed recording state remains intact. Graphs are limited to 128 observed manifests, 100,000 entries per manifest, 200,000 retained URL aliases, 8 MiB per document and 32 MiB of documents per renewal. Foreign redirects and unsupported protected/addressing modes remain explicit refusals.

YouTube and Vimeo both consume this shared graph renewal. Issuer account hashes, passwords, OAuth bearer tokens and client secrets are confined to their issuer requests. Adaptive CDN reads retain the job's intended media headers and cookie scoping. Caption URLs can also use stable API track IDs and declared expiry, keeping separate tracks with the same language distinct.

## Broader login flows

Native `--oauth-login` opens consent in the system browser using the caller's registered client. Google uses a random IP-loopback callback, random state, S256 PKCE and read-only YouTube/OpenID scopes. Vimeo uses its registered fixed IP-loopback callback and confidential authorization-code exchange for public/private/file access. Only the exact callback and consent state can exchange a code. Consent is one-use and expires after ten minutes. The bounded loopback listener rejects unrelated requests, closes on cancellation, and returns a plain completion message without callback secrets. Embedded password collection is not used.

Google access sessions support bounded refresh grants, a single replay after API 401, concurrent refresh suppression and stable account-subject verification. Vimeo tokens identify the authorized account through `/me`; expired or revoked sessions require fresh sign-in because Vimeo does not currently offer refresh grants. No user tokens or credentials are automatically saved or migrated by this batch.

Google API sign-in now obtains complete authorized playlist inventories through the read-only API, preserving source order, item count, unavailable positions and native video archive identities. Changed membership, repeated tokens and incomplete inventories fail. **Google API authorization does not grant private YouTube player access.** Private selected playback still requires the existing explicit YouTube cookie source and account selector. Public selected playback continues through the native transport.

Vimeo API login can obtain offered HLS/DASH links as well as clear progressive files. Requested API captions use bounded, complete same-video text-track pagination, active-track selection, automatic provenance and stable track identities. Renewed caption files can be saved and embedded. Extra account/API/domain/file-link layouts remain partial.

The native extensions are `--oauth-login`, `--no-oauth-login`, `--oauth-client-id`, `--oauth-client-secret`, `--oauth-redirect-uri`, `--oauth-access-token`, `--oauth-refresh-token` and `--oauth-scopes`. They preserve ordered job overrides and analysis arity, with credential fields excluded from request serialization. They are not additions to the frozen upstream option inventory.

For Google consent, supply a registered desktop client ID and any client secret required by that registration. For Vimeo consent, supply its client ID, client secret and registered callback such as `http://127.0.0.1:32145/callback`. The explicit login flag starts consent for that native job. The public C# `NativeOAuthLogin` transaction also supports callers that already manage a registered HTTPS callback, while automatic desktop listening accepts IP-loopback callbacks only. Existing supplied tokens can use the typed OAuth fields or native arguments. This remains a native-engine path; production WPF cutover is pending the original coverage gates.

## Verification evidence

The 42 protocol/core cases verify HLS segments/keys/init/ranges/relative prefixes, concurrent selected-track renewal, moving windows and retired resources, DASH templates/indexed files/captions/live periods, changed formats, DRM refusal, bounds/replay/cancellation, OAuth state/PKCE/scopes/identity, revoked/switched account refresh, loopback callback privacy/cleanup and engine login integration, authorized playlists and caption pagination. Tests use controlled issuer responses and simulated browser callbacks; no real account was used.

Ten media cases exercise Vimeo HLS/DASH/OAuth/captions, YouTube HLS/DASH, live DASH, revocation, changed representations and cancellation. Static positive recordings retain all 36 generated video frames and every coded video/audio packet in source order, fully decode retained media, preserve callbacks and skip before HTTP on archive repeats. Requested API captions survive saving and embedding. Live DASH completes its requested 2.9-second window. Failed access and cancellation preserve existing files and archives without callbacks.

Three actual public workflows completed through the native WPF adapter, with archive repeats before HTTP. All nine output/component files fully decoded. Six ordered main/component video/audio packet inventories matched exactly:

| Public workflow | Main bytes | Decoded seconds | Video frames | Audio frames |
| --- | ---: | ---: | ---: | ---: |
| YouTube `jNQXAC9IVRw`, VP9/Opus | 440,841 | 19.006 | 284 | 951 |
| Unified Streaming paired live HLS, H.264/AAC | 974,135 | 13.440 | 336 | 630 |
| Unified Streaming paired live DASH, H.264/AAC | 971,347 | 13.440 | 336 | 630 |

Retained YouTube video/audio files contain 185,292/255,427 bytes; HLS components 847,122/126,134 bytes; DASH components 853,165/118,031 bytes. Public checks use no account authorization. Vimeo's public SDK sample remains protected-only, with zero ready formats. Forced signed-link renewal and login grants are verified through controlled protocol/generated-media workflows; public playback does not establish real private access or remote grant acceptance.

Real-account verification remains explicitly user-deferred. No saved test account was inspected or changed in this batch.

## Remaining work

- Real permitted consent/private/account and sustained publisher-driven signed renewal evidence remains deferred.
- Broader YouTube cookie/login/brand/client/challenge, account-only collection/search/live/translated-caption workflows and durable cross-job resume. Google API credentials do not replace playback cookies.
- Vimeo additional API/password/domain/account/collection/file-link layouts, caption metadata/default conventions, automatic credential storage and re-consent UX. Supplied-file-link routes do not regenerate their issuer links through the API.
- Changed selected representations, renamed/disappeared manifests, expired resources outside a current live window, advanced indexed/live/remote/low-latency addressing and further provider renewal consumers.
- Full native option/default/catalog coverage, production startup/dependency removal and clean packaged acceptance.

Primary references: [Google installed-app OAuth](https://developers.google.com/youtube/v3/guides/auth/installed-apps), [Google account identity endpoint](https://developers.google.com/identity/openid-connect/reference), [Vimeo authentication](https://developer.vimeo.com/api/authentication), [Vimeo auth response and refresh-token limitation](https://developer.vimeo.com/api/reference/response/auth), [Vimeo adaptive file links](https://developer.vimeo.com/api/files/video-links), and [Vimeo text-track response](https://developer.vimeo.com/api/reference/response/text-track/3.4). Runtime code is independently authored C# with no yt-dlp extraction source, Python/runtime or fallback.
