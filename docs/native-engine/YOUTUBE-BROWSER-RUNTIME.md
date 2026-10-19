# Automatic C# YouTube session capture

Version 0.60.0 adds `YoutubeDl.Engine.BrowserRuntime`, a Windows WPF component implementing `IYouTubePlaybackSource`. It automatically obtains matching public YouTube player metadata and a browser playback request, then returns the validated session to the independent C# download core. No saved capture files, Python, or yt-dlp are required.

The native WPF adapter now configures this source by default. The production app still instantiates its existing yt-dlp backend pending the broader replacement requirements; this is not the final application cutover. Platform-neutral `new NativeEngine()` retains its existing behavior unless a playback source is supplied.

```csharp
var source = new YouTubeBrowserPlaybackSource();
var engine = new NativeEngine(youTubePlaybackSource: source);
```

## Flow and ownership

The source serializes capture requests and creates each browser on a dedicated STA dispatcher thread. It uses a unique temporary profile under its own session directory, separate from the user's browser profiles. The public watch page is muted and hosted without showing or activating a window. Top-level navigation must retain the requested video ID and expected HTTPS watch origin. Popups, permission prompts, basic-auth prompts and browser-initiated downloads are blocked; consent/sign-in controls are not clicked.

Bounded in-memory buffers hold recent player responses and observed playback requests. Startup proof placeholders are not treated as ready capture. Matching candidates pass the core video's identity, playability, configuration, client, endpoint and format-revision checks. A new explicit playback denial clears previously usable observations. No live proof or signed endpoint is written to normal diagnostic files.

The component closes its browser, drains pending operations within bounds, waits for browser exit and removes only its own generated profile directory before completing capture. It preserves cancellation, distinguishes a capture deadline from caller cancellation, and releases queued capture slots after success or failure. Cleanup failure is reported through a page warning, now surfaced by the native download log.

The C# core downloads all media independently after capture. It does not consider browser-prefetched fragments to be downloaded output. Original retention, metadata processing, publication, callbacks, archive handling and server-denial/completion checks use the existing native pipeline.

Ready directly exposed formats and known archive skips do not need browser capture. A fresh watch title that fails a download filter does not initialize browser playback. Simulation and standalone metadata jobs retain metadata-only native discovery instead of starting a browser stream.

## Current capability policy

As of 0.61.0, the component supports a public isolated profile, a requested User-Agent, unauthenticated HTTP/HTTPS proxies, a shared download-rate cap and explicit certificate-check overrides. Version 0.64.0 adds scoped selected-cookie import and native playback updates; see `YOUTUBE-COOKIES.md` for current verification and account limitations. It still rejects authenticated/SOCKS proxies, source-address/IP-version selection, custom browser connection timeouts, custom Referer and other custom HTTP headers. See `BROWSER-NETWORK.md` for current verification. Those options remain available where supported by the ordinary native transports. The browser does not silently ignore them or substitute a different account/session.

Capture has a bounded overall deadline, including queue wait. Missing WebView2 produces a clear runtime-availability error. Broader network/account support and exact option interactions remain pending.

## Dependencies

The module targets `net10.0-windows` with WPF and pins Microsoft's `Microsoft.Web.WebView2` SDK to `1.0.4258.31`. It uses an installed Edge WebView2 runtime; the tested machine had runtime `154.0.4258.62`. The SDK's license and notice are included in the module and copied to app output. The core engine retains its platform-neutral contract and does not reference the browser SDK. The earlier prototype's WinForms/WPF reference warning is absent from the solution build.

Runtime bootstrap/packaging on machines without WebView2, and a final packaged application test without yt-dlp, remain unfinished.

## Verification

Six core checks verify automatic source selection, mismatched/empty/playlist rejection, explicit source failures, directly exposed media/archive skips, cancellation, and title-rejected/simulated/metadata-only paths. Five app-side checks verify capability rejection before launch, serialized acquisition and queued cancellation, timeout recovery, metadata/request binding with startup/denial handling, and watch-page navigation boundaries.

A live early-cancellation run removed its temporary profile. A subsequent fresh-URL download of public YouTube video `aqz-KE-bpKQ` automatically captured a new session and downloaded selected formats `133+140` through `NativeEngine.DownloadAsync`. No manual session data was supplied. It produced:

| Output | Bytes | Complete frame counts | Duration |
| --- | ---: | --- | ---: |
| Processed MP4 | 19,891,245 | H.264 426 × 240: 19,037; AAC: 27,331 | 634.624580 s |
| Original merged MP4 | 19,891,126 | Same video/audio counts | 634.624580 s |
| Original video MP4 | 9,323,483 | H.264: 19,037 | 634.566667 s |
| Original audio M4A | 10,271,972 | AAC: 27,331 | 634.624580 s |

All four files fully decoded, four file-completion callbacks fired, and `youtube aqz-KE-bpKQ` was recorded in the archive. No generated browser profile remained after success or cancellation. The full solution passed 826 native checks, including 163 media-tool checks, and 29 app checks; the Release build has zero warnings/errors.

## Remaining work

Verify additional videos, codecs, captions and age/region/account conditions; implement supported account/network options; exercise more cancellation/failure stages and concurrent real-browser jobs; add session expiry/reload handling; verify runtime packaging and availability; complete the remaining site and app-option coverage before production cutover. A successful long public-video workflow does not establish full YouTube or all-site parity.
