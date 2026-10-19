# Managed YouTube player runtime

`YoutubeDl.Engine.PlayerRuntime` supplies the optional `IYouTubePlayerRuntime` implementation. The extraction, HTTP, transport and download code remains independently implemented in C#. This separate module uses Jint 4.17.0, a general-purpose managed ECMAScript interpreter, and its Acornima 1.7.0 parser. It does not use yt-dlp, Python, EJS, Node.js, an external JavaScript process, or a browser profile. The base `YoutubeDl.Engine` library still has no package references.

## Implemented and tested

The runtime fetches the public player script through the same isolated HTTP session as the watch page. Script sources and final redirect addresses must retain the expected HTTPS YouTube player path. Discovery identifies a URL-building helper by its structure, rather than hard-coding the current minified function name. Ambiguous and unknown layouts fail explicitly.

Every extraction owns a separate interpreter session. Only strings cross the interpreter boundary. CLR interop, filesystem access, network APIs, browser credentials, modules and running timers are absent. Initialization and each transformation have an eight-second host deadline, cancellation, two-million-statement limit, 100-level recursion limit, 256 MiB allocation limit, 250 ms regular-expression timeout, and three-MiB script limit. Faulted sessions are discarded.

The public player's helper applies an encrypted signature and its throttling transformation together. Signature-bearing URLs therefore execute once; already transformed throttling parameters are not transformed again. Clear signatures are preserved. The native parser validates the media origin/path, signature result, and changed nonempty throttling value before exposing a ready format. Failed transformations retain metadata and clear formats while leaving affected formats unavailable.

Inject the runtime explicitly when using the library:

```csharp
var engine = new NativeEngine(playerRuntime: new ManagedYouTubePlayerRuntime());
```

The unused WPF native adapter supplies this runtime. The production application still uses its original backend while full replacement coverage remains incomplete.

## Live evidence

On October 7, 2026, the managed implementation initialized public player `f2999a12` and transformed the throttling parameter of a fresh SABR endpoint for `aqz-KE-bpKQ`. A 1,590-byte native playback request received HTTP 200 with 730,515 bytes of interleaved UMP media/control data. The response included playback protection status 2: authorization is still required. A comparison request omitting the throttling parameter returned empty HTTP 403.

This establishes a working transformation and initial protocol response. It does not establish a complete YouTube download, a valid playback authorization token, or support for every player version. Live encrypted-signature playback has not been established; signature handling is independently fixture-tested.

## Attestation foundation

`YouTubeAttestationParser` reads escaped `ytAtN` page JSON directly in C#, without executing page code. `YouTubeAttestationClient` can use that page material or request an unbound challenge with the current client version. Programs, response text, names, duplicate fields, Google interpreter addresses, redirects, and downloaded script sizes are validated. Inline interpreter text can also be retained without execution.

Both page extraction and the unbound API path were observed live through these native components. A scratch-only managed interpreter experiment initialized Google's 63,584-character challenge interpreter and obtained a snapshot response. It did not receive a WebPO minter callback or usable playback token. That experiment is not a shipped authorization implementation; the player module's limits above apply to URL transformations, not the larger scratch challenge experiment.

The current public player uses a separate WAA request key for WebPO. `YouTubeWaaClient` independently implements its `Create` and `GenerateIT` RPCs, plain/scrambled challenge readers, wrapped inline/URL interpreters, base64 token bytes, lifetime and refresh metadata. Automatic redirects are refused and replies are bounded. Request keys and message IDs remain separate. Fallback candidates are represented separately from integrity tokens and do not imply playback authorization.

Native WAA creation was verified live. A scratch managed snapshot sent through the native integrity client received zero integrity-token bytes, a 77-byte fallback candidate and a 43,200-second estimated lifetime. Supplying the service's fallback to a fresh SABR request still produced protection status 2. The generic page/unbound challenges and this WAA path must not be treated as interchangeable. Function-source retention and a larger bounded allocation budget were needed for further scratch interpreter investigation; no experimental DOM additions or larger challenge limits have been promoted to the player module.

## Remaining work

- Support additional player layouts and reuse bounded parsed scripts across separate sessions.
- Complete the attestation environment, integrity-token exchange, content-bound token minting, expiration and refresh, and prove an authorized live playback session.
- Extend the integrated SABR downloader beyond the limited IOS short-video workflow described in `YOUTUBE.md`; this path does not establish managed attestation or Web-client authorization.
- Verify live signature-cipher playback and all affected account, caption, playlist, live and regional workflows.

Jint and Acornima licenses are included in the module's `Licenses` folder and copied into build/publish output. Research references: [Jint](https://github.com/sebastienros/jint), [Acornima](https://github.com/adams85/acornima), [published attestation protocol research](https://github.com/LuanRT/BgUtils#research), and [stream protection schema](https://github.com/LuanRT/googlevideo/blob/main/protos/video_streaming/stream_protection_status.proto). No third-party extraction or token-generation implementation is included.
