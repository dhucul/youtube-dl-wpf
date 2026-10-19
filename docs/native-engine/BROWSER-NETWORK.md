# Browser-session network settings

Engine 0.61.0 extends the automatic Windows YouTube session source with unauthenticated HTTP/HTTPS proxy configuration, download-rate limiting and the explicit certificate-check override. These apply to the temporary browser session; the subsequent media transfer retains the corresponding native HTTP settings.

## Implemented behavior

Proxy input is parsed as an HTTP or HTTPS server authority. Paths, queries, fragments, credentials, control characters and unusable ports are rejected. Only parsed URI components become browser arguments. There is no shell interpretation or raw argument concatenation. Explicit proxy routing also removes Chromium's implicit loopback bypass, matching the configured native route. Authenticated and SOCKS browser proxies remain unsupported.

Before navigating the public watch page, the component applies one global browser network rule using `Network.emulateNetworkConditionsByRule`. It shares the requested download throughput across matching requests, leaves upload throughput unlimited and adds no artificial latency. It does not override the browser's reported navigator state. If the runtime cannot apply the requested rule, capture fails instead of proceeding without the limit. The existing native rate limiter controls the later C# media transfer.

Certificate verification remains enabled by default. An explicitly requested override is scoped to the owned browser session. Page certificate errors use the WebView2 certificate event. HTTPS-proxy TLS errors do not reach that event, so an opted-in HTTPS-proxy session also receives the fixed certificate-ignore browser switch. Other sessions retain normal verification; no system trust store or global setting is changed.

The browser's startup/runtime background traffic is not a machine-wide bandwidth policy. The capture deadline remains bounded; very low download rates may prevent session preparation within that deadline.

## Verification

Two additional deterministic app checks cover proxy parsing and injection boundaries, explicit certificate opt-in, accepted network preferences, invalid rates and the aggregate-rule payload. Three optional checks exercise an actual WebView2 runtime against owned local fixtures:

- Both HTTP and HTTPS proxy endpoints receive the expected absolute target URL, including a destination that would otherwise use Chromium's loopback bypass. A strict session rejects the same self-signed HTTPS proxy accepted by the opted-in session.
- Two simultaneous 262,144-byte responses share a 128 KiB/s cap. The final run transferred 524,288 bytes in 4.066 seconds, versus 0.062 seconds without the cap; payload lengths remained exact.
- A normal session rejects a self-signed website, an explicit override accepts it, and another normal session still rejects it. No certificate is added to a trust store.

Run these optional local-browser checks with:

```powershell
dotnet run --project tests/YoutubeDlModern.Tests -c Release -- --browser-tools
```

The app runner reports 31 regular checks, or 34 including these three optional browser checks. The optional checks require the installed WebView2 runtime and do not visit public services or use personal browser profiles.

## Live workflow

An owned loopback HTTP CONNECT proxy forwarded only relevant public service domains. A fresh native request for `aqz-KE-bpKQ` used that proxy and a `1M` rate limit for automatic browser acquisition and the subsequent C# transfer. It completed in 32.13 seconds with four callbacks and no remaining temporary profile.

The proxy observed five YouTube tunnels and fifteen media tunnels by the end of capture; a further media tunnel served the C# download. The processed MP4, original merged MP4, original video and original audio were each SHA-256 identical to their previously fully decoded references: 19,891,245, 19,891,126, 9,323,483 and 10,271,972 bytes respectively. They retain 19,037 H.264 video frames and 27,331 AAC audio frames over 634.624580 seconds in the merged outputs.

This proves the tested proxy/rate workflow, not all browser network or site combinations. Account-cookie imports, authenticated/SOCKS proxies, local-address/IP-family binding, custom headers/Referer, custom browser connection timeouts, wider worker/frame throttling cases and additional runtime versions remain pending. Production WPF cutover and full site/options replacement also remain unfinished.

The implementation follows the first-party [Chromium protocol schema](https://github.com/ChromeDevTools/devtools-protocol/blob/master/json/browser_protocol.json) and [WebView2 browser-argument documentation](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/webview-features-flags).
