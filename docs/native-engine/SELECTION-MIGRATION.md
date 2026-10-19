# Live DASH selection, caption conversion and checkpoint compatibility — 0.170.0

This batch continues the native C# replacement. Full replacement remains open; production startup still uses `YtDlpEngine`.

## Selection policy

The typed native request accepts an optional `LiveDashSelection` policy. Its ordered audio language and role preferences select among initially offered audio tracks, with exact language tags preferred over regional descendants within each preference. Caption role preferences apply to requested subtitle languages. Tokens are bounded, duplicates and invalid entries fail validation, and role values are matched only under the DASH role scheme. If offered audio tracks cannot satisfy an explicit audio preference, or a requested caption language cannot satisfy its role preference, selection is refused.

`FollowQuality` can choose the best or worst declared bitrate in each new period, including when the previous representation ID is still offered. It retains the selected codec, dimensions, frame rate, language, role, channel configuration, sample rate, viewpoint and accessibility profile. The original explicit adaptation takes precedence over another compatible adaptation. Unknown bitrate facts and tied successors remain conservative; accepted group, representation and profile facts are pinned within a period. Literal format/selector intent suppresses proactive quality changes.

```csharp
request = request with {
    LiveDashSelection = new() {
        AudioLanguages = ["fr", "en"],
        AudioRoles = ["main", "commentary"],
        CaptionRoles = ["subtitle"],
        FollowQuality = true,
        ConvertCaptionCodecs = true
    }
};
```

The policy is opt-in and scoped to live DASH. Null keeps the previous request serialization, selection and recovery scope, allowing compatible older jobs to resume. Other transports and ordinary VOD reject this policy. WPF controls and a full selector/default/locale policy remain unfinished; this adds a native API contract without claiming new app-option parity.

## Caption conversion

With `ConvertCaptionCodecs`, supported clear fragmented `wvtt` and `stpp` variants can alternate at period boundaries. Each period is decoded with its actual initialization and sample codec into the existing WebVTT cue model, then aligned, clipped, saved and embedded through the existing subtitle pipeline. This preserves the selected language and role. Codec changes within an already accepted period, unsupported codecs, mismatched initialization, changed timing and payload mutation are refused.

Mixed-codec snapshots record each period's codec and bind accepted track identities separately from the normalized conversion intent. A new session can restore an earlier WebVTT period even when the current manifest begins with TTML, or the reverse. Fresh overlap bytes, initialization, caption payload/timing and saved-prefix digests remain mandatory. Different conversion or preference policies have distinct scopes.

## Cross-version recovery

Timeline and caption envelopes now write schema 2 with writer-version and minimum-reader metadata. The reader accepts compatible schema-1 envelopes emitted by the verified 0.168.0 engine, then applies all existing ownership, source, metadata, timeline and byte checks. A successful subsequent snapshot write upgrades the envelope atomically. Merely reading, rejecting or canceling recovery leaves the prior snapshot bytes intact.

Tests load the frozen 0.168.0 engine and fixture assemblies in an isolated worker, verify the engine version, wait for two durable media tracks and caption periods, and forcibly stop that process. The current engine resumes those actual schema-1 WebVTT/TTML prefixes with new sessions and earlier periods removed. Additional old-worker cases corrupt the journal, remove prefix bytes, request cancellation or advertise an unsupported schema; prior media, captions, archive and completion callbacks remain protected.

Unknown schemas, unsupported minimum-reader requirements and malformed writer metadata are refused. The existing v2 logical source bindings retain their meaning. Older DASH scopes that used different identity semantics are not guessed or silently rebound. Downgrading a schema-2 checkpoint to an older reader, arbitrary historical schemas, hostile local edits and hardware power-loss guarantees are outside this verified scope.

## Verification

All **3,071 native checks** (2,146 core + 925 media), **76 app checks** and **79 focused checks** passed. Release build has zero warnings/errors. Two public mixed-caption workflows independently verify 14 decoded files, four exact ordered main/original packet inventories and all 41 saved cue payloads in extracted embedded captions. Public and final engine binary hashes match.

The batch adds 54 core checks and 18 generated-media checks. These cover ordered preferences, regional languages, role schemes, quality intent, ambiguity and profile guards; both caption conversion directions, mixed retired-prefix recovery, within-period changes and initialization mismatch; schema compatibility, atomic upgrade on successful write and refusal/cancellation preservation. Media checks verify the transferred preferred audio paths and captured bitrate bindings, full decoding, exact decoded video frame inventories, saved/embedded captions, publication protection and archive repeats. Seven new worker processes are forcibly stopped, including six using the frozen older engine.

Two public workflows alternate real DASH-IF WebVTT and TTML assets across controlled renamed periods. Each uses the explicit quality/language/role policy, saves both codecs in schema-2 prefixes, injects a transfer failure and starts a fresh session after earlier periods are removed. Delivery URLs and manifest changes are fixtures; no private issuer or saved account is accessed. Independent full decoding, ordered main/original coded-packet comparisons and saved/embedded caption payload checks verify publication. Public archive repeats make no HTTP requests or new callbacks.

Evidence is retained in `work/selection-migration-batch`, the frozen `legacy-0.168.0` binaries, and `work/selection-migration-probe/bin/Release/net10.0/verified-0.170.0`. Earlier diagnostics are retained.

## Remaining work

Changing decoder configurations or dimensions, broader adaptive quality ladders, language/role switching during a recording, external caption transports, richer styling/codec conversion, WPF policy controls and full selector/default/locale semantics remain open. Broader historical checkpoint migration, longer private renewed sessions and additional crash/write interleavings also remain open. Provider/player/proof/signing/login contracts, VOD/SABR handoff, full catalog/options acceptance, inventory/storage gaps and packaged production acceptance stay on the backlog. Real-account verification remains user-deferred. No complete-parity claim is added.
