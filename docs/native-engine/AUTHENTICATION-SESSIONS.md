# Native authentication and session handling

Version 0.130.0 adds shared Wistia/JW/Brightcove/Kaltura playback renewal, explicitly scoped hosted credentials and HTML delegation, publisher-provided JW signing, Wistia/Brightcove token access and Kaltura account/session handling. All 2,026 native and 74 app checks passed, including 50 new checks. Four public workflows produced ten fully decoded media/source files with ten exact packet inventories. Current Kaltura public DASH fragments were denied; its offered direct flavor passed. Real private/signing evidence remains user-deferred; consumer-key/full provider/option/catalog and production coverage remain open. See [shared hosting verification](SHARED-HOSTING-RENEWAL.md).

Version 0.128.0 adds HLS/DASH signed-resource graph renewal, external-browser OAuth consent with Google PKCE/account refresh and Vimeo scoped code exchange, authorized YouTube API playlists and Vimeo adaptive API/captions. All 1,976 native and 74 app checks passed; 52 new checks verify the batch. Three public native-adapter workflows produced nine fully decoded files with six exact ordered source-packet inventories and network-free archives. Google API login still requires separate cookies for private playback. Real account/grant verification remains user-deferred; broader provider/layout/default/catalog and production coverage remain open. See [adaptive renewal and login verification](ADAPTIVE-ACCOUNT-RENEWAL.md).

Version 0.126.0 adds YouTube cookie-selected account/private Web playback, Vimeo video-password/API file access, progressive/caption signed-link renewal and matching SABR context renewal. All 1,924 native and 74 app checks passed; final account review passed all 1,406 core and 37 batch checks again. Public YouTube playback/archive behavior passed with three fully decoded media/source files and exact coded packet retention. Real private-account verification remains user-deferred; interactive login/OAuth grants, adaptive resource-graph renewal and wider provider parity remain pending. See [account and renewal verification](ACCOUNT-RENEWAL.md).

Version0.120.0 now adds synchronized separate live HLS, dynamic DASH and job-owned private playback-token renewal. Full regression and public live workflows passed; real private-account verification remains deferred. See [adaptive live/renewal verification](ADAPTIVE-LIVE-RENEWAL.md).

Scoped batch 0.117.0, included in final version 0.118.0. Verified October 11, 2026. This is partial provider-specific support, not universal account parity.

## Implemented

The provider contract declares account/password, two-factor and video-password capabilities. Unsupported providers reject configured credentials before HTTP creation; credentials cannot follow embedded or collection discovery to another origin. Typed credentials are excluded from request JSON and record diagnostics. Each top-level job owns its HTTP session and token state; disposal clears references and closes the controlled account transport.

`Auth.Username`, `Auth.Password`, `Auth.TwoFactor`, `Auth.UseNetrc` and `Auth.VideoPassword` now map through saved settings and ordered extra arguments for PeerTube. Other providers still reject these fields when their declared capabilities do not support them. The three TV-provider settings remain unimplemented. The global count of unmapped configured app settings drops from 18 to 13; this does not mean the five scoped authentication settings work on every site.

Arguments include `--username`/`-u`, `--password`/`-p`, `--twofactor`, `--video-password`, `--netrc`/`-n`, `--no-netrc` and `--netrc-location`. Analysis preserves these discovery controls; download-only values cannot inject them. Saved settings are not mutated by job overrides.

The bounded read-only netrc reader selects one exact host and optional explicit username. It supports UTF-8/BOM, comments, quoted/escaped values, `.netrc`/`_netrc` and selected file/directory locations; macro bodies are ignored and never executed. Missing, incomplete, duplicated or ambiguous entries fail without exposing contents. Explicit account fields override matching netrc values. Default entries and upstream provider-machine aliases are not treated as permission to share one account across arbitrary servers; full upstream netrc semantics remain pending.

PeerTube uses its documented local OAuth client and password grant, with two-factor codes only on the initial grant. API access tokens refresh before expiry or once after a 401; refresh requests omit the password and two-factor code. Account/video-password API requests stay on the selected HTTPS API origin and cannot follow redirects. Password/private metadata obtains a short-lived file token and replays it only for exact declared progressive resources or an identified video's HLS directory. Account bearer and password headers are not sent to media hosts. Full UUID archives can skip before HTTP; short aliases still require identity discovery.

Private offered progressive containers require fixed local full decoding, declared byte counts when available and the program-duration floor before publication. Cross-origin/custom-port private resources, large token-scope inventories and additional unsupported containers remain explicit boundaries.

Protocol references: [PeerTube REST quick start](https://docs.joinpeertube.org/api/rest-getting-started), [official API schema](https://raw.githubusercontent.com/Chocobozzz/PeerTube/develop/support/doc/api/openapi.yaml). The implementation is independent C#; no yt-dlp extraction source or runtime is used.

## Verification

Fourteen core checks cover capabilities, HTTPS/origin boundaries, login/two-factor headers, bounded refresh, independent jobs, video-password/file-token scopes, redirect rejection, malformed tokens, redacted diagnostics, netrc grammar/selection/read-only overrides, argument order and cancellation. The final netrc keyword/duplicate-field improvements also passed all focused authentication checks after the full suite run.

Generated protected MP4 playback completed with full decoding, exact retained source bytes, two completion callbacks and an archive repeat without media retrieval. Five new app checks cover saved analysis, job override order, netrc resolution, TV boundaries and live recording overrides.

A public PeerTube recording also completed through the native adapter with a configured video-password header restricted to API requests. Its 384,417-byte MP4 fully decoded: 222 H.264 frames, 433 AAC frames, 9.25 seconds. This public recording does not establish account login or private access. The short alias's archive repeat opened one discovery session and published nothing.

At the user's request, current app settings and their backup were inspected read-only for a saved test account. No account/password/video-password/netrc/cookie source was configured. The user then chose to continue without real-account verification. Real permitted account login, two-factor and password-protected playback evidence therefore remain pending; no credentials or user settings were changed.

## Remaining

- Real YouTube/Vimeo consent/account evidence and further credential/cookie/collection/layout protocols; current external-browser OAuth and adaptive API/renewal subset is documented in ADAPTIVE-ACCOUNT-RENEWAL.md.
- Real permitted account/private/region evidence, account revocation/logout and concurrent legacy-server session behavior.
- Additional providers using private-media file-token refresh, cross-origin/custom-port private playback and protected caption/artwork resource scopes.
- Complete netrc/default/alias, account-option interactions, migrations and TV-provider authentication.
- Full catalog/option parity, native production startup and standalone packaged verification.
