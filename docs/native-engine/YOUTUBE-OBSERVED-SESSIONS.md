# YouTube playback using an observed browser session

**0.60.0 update:** automatic capture and native-adapter integration are now implemented; see `YOUTUBE-BROWSER-RUNTIME.md`. The observations and integration checklist below record the earlier 0.59.0 handoff milestone.

Engine 0.59.0 adds a bounded reader for a matching public player response and observed SABR request. The C# transport completed a full 634.6-second video using session data from an isolated WebView2 prototype. Automatic capture is not yet part of the default YouTube provider or WPF adapter. The normal mobile path still encounters the longer-video authorization denial described below.

## Core implementation

`YouTubeObservedPlayback.Parse` checks the requested video identity and playability through the normal player parser. It requires the request's playback configuration to match the supplied player response exactly. It validates the Web client identity, GoogleVideo endpoint, bounded proof, and every initialized/preferred/buffered format reference against the video's offered format IDs, revisions and extra tags. An ad or another video's format/configuration cannot be substituted merely because the browser was displaying the requested watch page.

The returned SABR session retains the exact observed client information and proof bytes. Those arrays are copied from the input and excluded from JSON serialization. The request encoder checks that retained client information agrees with its declared client name/version, and forwards the bytes without discarding additional client fields. `SabrDownloader` supplies them throughout the normal request/control loop. Browser buffered state is not treated as media already downloaded: native sinks start empty and obtain the complete tracks.

Server denials, reload requirements, redirects, track identities, continuity, declared endings and incomplete-session failures remain enforced. A nonempty proof is not treated as proof of authorization. Observed playback uses the native anonymous scope rather than forwarding unrelated caller credentials.

## October 9, 2026 evidence

1. A fresh native mobile session for `aqz-KE-bpKQ` received eight responses, reaching 62,200 ms of video and 69,893 ms of audio, before an explicit authorization denial. No completed file was published.
2. A fresh bounded Jint challenge run used the current first-party interface. It produced a snapshot but no integrity token, only a 77-byte fallback candidate. This did not establish sustained authorization. A debugger-enabled diagnostic exceeded its memory bound and was not integrated.
3. The installed WebView2 runtime was tested through a separate C# WinForms prototype using its own anonymous profile, muted playback, no visible/activated window, denied permission requests and blocked popups. It did not import the user's browser profile. Startup requests contained 10-byte proof material; a later observed request contained 87 bytes. No proof or signed endpoint value was written to diagnostic output.
4. The C# transport used the matching observed configuration/client information/proof. It received authorization status 1, completed both selected tracks in 32 requests, and retained every declared segment through the final track endings. It did not cycle denied sessions or concatenate fragments from failed sessions.
5. The normal `NativeEngine.DownloadAsync` pipeline then completed the same long video with a supplied observed-media provider, metadata processing, original retention, callbacks and archive publication.

Native-pipeline outputs:

| Output | Bytes | Streams and complete frame counts | Duration |
| --- | ---: | --- | ---: |
| Processed Matroska | 36,181,583 | AV1 854 × 480: 19,037 frames; Opus: 31,730 frames | 634.601 s |
| Original merged Matroska | 36,181,492 | Same video/audio frame counts | 634.601 s |
| Original video MP4 | 25,998,574 | AV1: 19,037 frames | 634.566667 s |
| Original audio WebM | 10,258,925 | Opus: 31,730 frames | 634.601 s |

All four files fully decoded. The job emitted four file-completion callbacks and archive record `youtube aqz-KE-bpKQ`. The browser was closed before the independent C# transfer. No Python or yt-dlp process/library was used.

This is a verified browser-assisted session handoff and native media workflow, not proof that the default provider now automatically downloads every long YouTube video.

## Tests and boundaries

Seven deterministic checks cover exact context preservation, video/configuration/endpoint identity, format revisions/tags, input ownership, secret serialization, invalid/duplicate/oversized client fields, cancellation, missing proof and explicit server denial without publication. One generated-media check verifies the supplied context reaches normal native transfer and that merged/original media remain complete. The full solution passed 820 native checks, including 163 media-tool checks, and 24 app checks with no Release build warnings or errors.

The WebView2 prototype remains outside the production solution. Its SDK reference is experimental and does not add a core engine dependency. The prototype has a WinForms/WPF SDK reference warning that must be resolved when creating the intended WPF runtime module.

## Remaining integration

- Implement a maintained C# browser-session source and wire it into native YouTube discovery, with cancellation, timeout, session ownership, navigation/permission/download controls and cleanup tests.
- Obtain current matching player metadata and request observations automatically; validate video/configuration/format binding before returning a candidate. Avoid persisting live proof material in normal diagnostics.
- Respect requested user-agent/network/account options or report unsupported combinations explicitly. The verified prototype uses a separate public profile; authenticated workflows remain pending.
- Validate fresh sessions across more videos, format selections, expiry/reload conditions and concurrent/canceled jobs. A working browser-backed path does not complete the independent managed challenge runtime.
- Verify runtime availability/packaging, then complete the broader site/options coverage before switching WPF and removing yt-dlp.

Primary evidence came from the current [public watch page](https://www.youtube.com/watch?v=aqz-KE-bpKQ) and its first-party player script. The prototype uses Microsoft's documented [request observation](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2.webresourcerequested), [request content](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2webresourcerequest.content), and [response content](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2webresourceresponseview.getcontentasync) APIs. No other extractor's implementation was used.
