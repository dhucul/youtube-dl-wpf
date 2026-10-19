# WPF policy integration and decoder/resolution transitions — 0.172.0

This batch continues the native C# replacement. Full replacement remains open. Default startup still constructs `YtDlpEngine`; a saved, explicit **Use native live DASH policies** setting selects the built-in engine for analysis, format inspection and new download jobs. This is an opt-in native path, not a full production cutover.

## WPF integration

The Formats & Audio settings tab exposes ordered audio languages and roles, caption roles, quality following, WebVTT/TTML caption conversion, decoder/resolution conversion, adaptive ladder selection and converted video height. Settings persist through the existing validated draft/save path. Disabling transition conversion also disables its dependent ladder option. The new controls use the same styles and scrolling behavior as the existing settings.

The adapter maps saved preferences into `LiveDashSelection`. Download routing uses each queued job's settings snapshot; changing the current checkbox does not silently move a queued job to another backend. Analysis uses the current saved backend choice. Active native analysis and jobs do not require an external yt-dlp installation, and unsupported native requests propagate their refusal without falling back to an external transfer. Engine status follows the saved choice. The default route and explicit external-engine maintenance remain available.

The checkbox applies to new jobs using this policy and requires a live DASH source. Ordinary VOD and other transports retain the explicit native-policy boundary. Nine native-only settings are tracked separately from the frozen 126 classic app-option entries; no upstream option parity is claimed for these extensions.

## Adaptive selection

`LiveDashSelectionPolicy` adds `NormalizeTransitions`, `AdaptiveQualityLadder` and `TransitionHeight`. Their default values are omitted from JSON, retaining the previous 0.170.0 policy serialization when the new features are unused. Adaptive resolution selection requires explicit conversion and a bounded, even output height.

With the ladder enabled, best/worst selection ranks known height, frame rate, optional free-codec preference and bitrate across new periods. Requested input height, frame-rate and codec limits still constrain each period. Literal format/selector intent suppresses proactive reselection. The selected language, role, channel/sample layout, viewpoint and accessibility facts remain guarded; ambiguous, unknown and changed-within-period tracks are refused.

The logical recovery intent includes the conversion profile and selection limits, rather than the first period's codec, dimensions or frame rate. Physical accepted bindings retain the actual profile. A later session can therefore begin with another compatible rung while still verifying the saved overlap, initialization and prefix bytes. Changing policy limits or an accepted overlap profile cannot silently reuse another recording.

## Conversion contract

The opt-in converter accepts supported clear single-track MP4 periods. Video can move between H.264, HEVC, VP9 and AV1 decoder configurations and dimensions. Supported input is square-pixel, unrotated SDR 8-bit 4:2:0 with unchanged color facts. Unsupported HDR, pixel/aspect layouts and media roles are refused. Audio conversion supports AAC, ALAC, FLAC and Opus with unchanged supported mono/stereo channel and sample-rate facts.

Converted video uses an explicitly selected 16:9 H.264 canvas at 30 fps. Scaling preserves source aspect ratio with padding. Each period is decoded, clipped to its visible interval and normalized through lossless intermediate video before joining; audio uses ALAC. The output is fully decoded and checked before publication. Input quality limits select source formats; the separately selected conversion height and fixed cadence define the converted output.

Keep original retains every captured source period before conversion, alongside the existing assembled/original and component outputs. These period files preserve the original coded packets, including data outside a clipped visible boundary. Conversion does not claim to preserve original coded packets in the normalized main stream. Caption timing stays aligned with the existing recording window, and mixed WebVTT/TTML captions remain saved and embedded.

The existing segment-boundary recording behavior is retained: a requested duration can include the final complete segment. Public captures in this batch cover approximately 20.03 and 22 seconds for a 20-second target; their decoded frame counts match their actual 30-fps timelines.

## Verification

All **3,123 native checks** (2,178 core + 945 media), **88 app checks** and **59 focused checks** passed. Release build has zero warnings/errors. The batch adds 32 core, 20 generated-media and 12 WPF integration checks, including one stopped worker. Both public workflows pass independent source/output/caption checks and match the final engine binary.

The batch adds 32 core selection/validation/recovery-identity checks, 20 generated-media checks and 12 WPF integration checks. Generated recordings cover all four video codecs, changed dimensions and cadence, best/worst and bounded ladders, FLAC/ALAC transitions, clipping, mixed captions, fresh-session recovery and one forcibly stopped worker. Independent scaled reference frame sequences match exactly: 240 frames for complete layouts and 210 for the clipped layout. Lossless generated audio matches decoded reference samples. All four retained source-period packet inventories match their corresponding inputs. Negative cases preserve prior media, captions, archive state and zero callbacks.

WPF checks load the compiled control, change bindings, save and reload settings, verify queued snapshot/backend routing and status changes, and render the actual Formats & Audio tab. The preview is retained as `work/wpf-ladders-batch/wpf-policy.png`.

Two public workflows use the [DASH-IF multi-resolution test asset](https://livesim2.dashif.org/assets), alternating actual 360p/720p sources and WebVTT/TTML captions across controlled renamed periods. The fixture labels language-free beep audio `zxx` for explicit preference selection. After durable prefixes, an injected transfer failure is followed by a fresh session with new delivery URL generations and older periods removed. Manifest transformations, metadata labels and interruption are fixtures; video dimensions and source media remain the actual publisher assets.

Independent checks fully decode 20 files, verify actual retained source heights of 360 and 720, confirm 640×360 H.264 at 30 fps in both main outputs, match four ordered main/original coded-packet inventories and find all 43 saved cue payloads in extracted embedded captions. Recovery makes zero retired-period requests; archive repeats make zero HTTP requests or new callbacks. Final engine and probe binary hashes match. No saved account or private issuer was accessed.

Evidence is retained in `work/wpf-ladders-batch`, `work/wpf-ladders-probe/bin/Release/net10.0/verified-0.172.0-ladders-zxx`, and `work/verify-wpf-ladders.ps1`. Earlier diagnostic attempts are preserved.

## Remaining work

Broader conversion targets and audio layouts, HDR/color/aspect/rotation handling, richer caption styling/transports, in-recording language/role switching and broader quality/default/locale/selector semantics remain open. Ladder selection follows explicit quality intent at period boundaries; throughput-driven switching inside a period is not added. Extend historical checkpoint migration, crash/write interleavings, long private renewed sessions, real provider/player/proof/signing/login contracts, VOD/SABR handoff, inventory/storage coverage and full catalog/options/packaged acceptance. Real-account verification remains user-deferred, and no complete-parity claim is added.
