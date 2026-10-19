# Native network preferences

Engine 0.57.0 adds native source-address binding, IPv4/IPv6 selection, custom HTTP headers and the explicit certificate-check override. The WPF native adapter maps these saved settings and applies global/job extra arguments in order. The production application still uses its existing backend pending full replacement coverage.

## Connection behavior

`SourceAddress` accepts a local IP literal. `IpVersion` uses 0 for automatic, 4 for IPv4, and 6 for IPv6. Invalid values and conflicting source/version choices fail before extraction. IPv4-mapped IPv6 source literals are normalized to IPv4. With no explicit preference, the standard .NET connection path remains in use.

With a preference, the engine resolves the connection host for the selected family, binds each candidate socket when requested, and connects with cancellation. Failed sockets are disposed before trying another address. A forced family never falls back to the other family. DNS and connection attempts remain inside the HTTP handler's connection timeout. A successful connection transfers socket ownership to its network stream.

For a proxy, these preferences apply to the connection to the proxy. Ordinary HTTP proxy requests and HTTPS CONNECT tunnels are tested. Proxy-side DNS and the proxy's onward connection are not controlled by the client's local binding.

Certificate verification remains enabled by default. `NoCheckCertificate` changes only that job's HTTP handler, including derived job sessions. It does not change global TLS policy or install trusted certificates. A new default session still rejects an untrusted certificate after an override session succeeds.

## Header behavior and options

Saved custom headers use one `Name: Value` per line. Repeated names replace earlier values without regard to case. Values retain colons, case and percent escapes. Names, control characters, individual lengths and total header sizes are validated without including secret values in diagnostic messages. Explicitly empty ordinary headers are retained. User-Agent and Referer are normalized into the typed request so provider defaults respect them, and later job options can override saved values.

Request headers are applied through the managed HTTP client. `Content-*` headers are applied to the actual body; supplied framing headers such as Content-Length and Transfer-Encoding are explicitly unsupported. Custom header dictionaries are copied before overrides and excluded from request JSON serialization. Existing provider-scoped headers and anonymous credential removal continue to apply.

Supported extra arguments now include `--source-address`, `--force-ipv4`/`-4`, `--force-ipv6`/`-6`, `--add-headers`/`--add-header`, `--no-check-certificates` (also singular), the explicit check-certificates inverse, and integer `--socket-timeout`. The previously supported user-agent, referer and proxy arguments remain available. Existing timeout bounds still apply.

## Verification

Twelve native checks use real TCP/HTTP/TLS loopback connections to verify:

- An observed IPv4 peer address of `127.0.0.2` when explicitly bound.
- IPv6 `::1` operation, wrong-family failure, and IPv4 localhost DNS selection.
- Source binding and destination preservation through HTTP proxy requests and HTTPS CONNECT/TLS tunnels.
- Default rejection of an untrusted local certificate, success with the explicit override, then rejection by a separate default session.
- Connection preferences surviving scoped headers, anonymous sessions and redirect-controlled clients; credentials and scoped headers remain isolated.
- Header replacement, literal values, empty headers and unchanged POST bodies with custom content types.
- Invalid-setting rejection before discovery, argument precedence, cancellation and exact transferred bytes through a complete native download.

The test server creates a temporary self-signed certificate and imports its key for Windows Schannel use; it does not add a certificate to any trust store. One additional WPF regression check verifies saved-setting mapping, job overrides and preservation of saved values. The app test runner now reports its actual executed count instead of a fixed number.

On October 9, 2026, a native Bluesky original download completed with forced IPv4, an explicit wildcard IPv4 bind, custom headers/User-Agent and normal certificate validation. It saved a 6,689,099-byte processed MP4 and the 6,688,967-byte original. Both fully decoded as H.264 1920 × 1080/AAC, retaining 892 video and 1,394 audio frames over 29.737 seconds. Two completion callbacks and the expected archive record were emitted. Specific local binding is established by the loopback peer check; the live wildcard bind does not establish a particular external interface.

## Remaining verification and parity

Public IPv6 downloads, multiple physical interfaces, unusual DNS failures, authenticated/SOCKS proxies, proxy-side address-family semantics, content headers outside the currently mapped Content-* family, empty standard User-Agent/Referer overrides, complete legacy timeout/header grammar and all option interactions remain pending. Custom request framing remains explicitly unsupported. These checks are not evidence of full network-option or site parity.

Implementation uses the [.NET connection callback](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.socketshttphandler.connectcallback?view=net-10.0), [cancelable sockets](https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.socket.connectasync?view=net-10.0), and [per-handler TLS options](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.socketshttphandler.ssloptions?view=net-10.0), without a new package dependency.
