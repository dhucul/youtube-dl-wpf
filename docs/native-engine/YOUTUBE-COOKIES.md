# Selected cookies in YouTube browser sessions

**0.65.0 update:** the tested short-browser capture gap described below is now fixed; public and generated-cookie runs passed. See `YOUTUBE-SHORT-CAPTURE.md`. Private-account verification remains open.

Engine 0.64.0 connects selected native cookie state to the Windows browser capture source and carries relevant browser updates back to the C# transport. The browser still uses its own temporary profile; it does not open or modify the selected personal browser profile.

## State ownership and scope

The YouTube provider passes an owned snapshot of the existing discovery session to `IYouTubePlaybackSource`. This includes the selected file/browser-cookie import and applicable response updates, avoiding a second, potentially different import during the browser handoff. Direct calls to the Windows capture source can still load explicitly selected cookies with the existing native importer. Cookie files retain precedence over browser selection.

Only YouTube domains, GoogleVideo domains, and the associated google.com/www.google.com/accounts.google.com identity domains enter the snapshot. Unrelated account cookies and other Google product host cookies are excluded. Snapshot conversion retains host-only versus domain scope, path, Secure, HttpOnly, empty values, session lifetime and expiry. The scope is observed from the original cookie jar's matching policy rather than inferred solely from a leading dot. Versioned and port-bound cookies remain unsupported.

The Windows source imports the snapshot before navigation and reads the cookie manager back to verify the supported fields. Sets exceeding browser size/count limits fail explicitly. SDK errors are wrapped without cookie values. When the caller selected cookies, applicable browser updates are exported before closing capture and used by a new owned native HTTP session. Caller Authorization and literal Cookie headers are removed from that session so they cannot replace the captured state.

Snapshots and metadata cookie properties are excluded from JSON serialization. Diagnostic representations expose only safe counts. Cookies are not copied into ordinary capture logs or saved info JSON. Temporary profile cleanup continues to apply on success, cancellation and timeout.

Legacy capture-source implementations that do not accept scoped cookie state fail explicitly for requested account-cookie use instead of silently dropping it.

## Verification

Five native cookie checks verify field/scope preservation, exclusion and ownership, secret-free diagnostics, file precedence and actual HTTP transport behavior through an owned local proxy. A source-integration check verifies that discovery's cookie snapshot reaches the scoped capture overload and remains independent of later caller changes. An optional actual-WebView2 check verifies import/export fields, browser updates and isolation from a subsequent profile. It uses generated cookies and visits no account page.

A live public download of `aqz-KE-bpKQ` used a generated Netscape file containing a dummy YouTube cookie and a dummy unrelated-domain cookie. The native source/transport pipeline completed with four callbacks and removed its temporary profile. All four output/source files were SHA-256 identical to previously fully decoded references:

- Processed MP4: 19,891,245 bytes.
- Original merged MP4: 19,891,126 bytes.
- Original H.264 video: 9,323,483 bytes, 19,037 frames at 426 × 240.
- Original AAC audio: 10,271,972 bytes, 27,331 frames.

Merged duration is 634.624580 seconds. The dummy cookie value was absent from saved info JSON. No personal browser cookies were read for this live check.

## Open verification

This establishes selected-cookie transport and profile isolation; it does not prove private, paid, age-restricted or other authenticated YouTube playback. Those workflows need appropriate account-owned live evidence. SameSite/partitioned-cookie parity and more exceptional cleanup cases remain pending. Imported browser-cookie decryption has the previously documented limits in `AUTH-COOKIES.md`.

A live test of short `jNQXAC9IVRw` timed out in the browser capture path despite playback being observable. The polling loop's repeated-play behavior was removed, but a usable post-startup session was still not established for that sample. An unverified startup-session fallback was not retained. Timeout cleanup removed the temporary profile. The previously verified mobile short-video path remains separate; the native adapter's short-clip capture gap needs further work.

Normal first-party browser playback can perform service bookkeeping under an imported account. No claim of account-side-effect equivalence with the legacy downloader is made. Source/navigation controls and normal server permission/completion checks remain in force. Full site/options coverage and production WPF cutover remain unfinished.
