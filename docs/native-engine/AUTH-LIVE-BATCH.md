# Authentication and live-streaming batch checkpoint

Final version 0.118.0, October 11, 2026. All existing working changes remain uncommitted.

- Added provider-scoped authentication, read-only netrc, PeerTube account/two-factor/video-password protocols, bounded API refresh and isolated job-owned sessions. Five previously unmapped settings now have partial provider-specific support; 13 globally unmapped settings remain.
- Added near-edge or exposed EVENT/from-start HLS recording, bounded complete-segment windows, moving-window capture, encrypted/fragmented media, full local verification and protected cancellation/failure cleanup.
- Passed the Release build with zero warnings/errors, all 1,800 native checks (1,331 core +469 media) and all 74 app checks (70 regular +4 browser). The final netrc and live timing refinements separately passed all 21 focused authentication and 16 focused live/core checks. Added 34 native checks and five app checks overall.
- Five external workflows produced seven independently fully decoded files: one public PeerTube source and four advancing live HLS recording modes. Account/password headers stayed on the API; archive repeats published nothing. All four live archive repeats skipped before HTTP. The short PeerTube alias needed one discovery session.
- Real account/private verification remains pending by the user's explicit choice after no saved test account was found. Generated protected playback and protocol tests passed; public playback is not counted as account-login evidence.

See [authentication/session details](AUTHENTICATION-SESSIONS.md), [live recording details](LIVE-RECORDING.md) and the [full backlog](COVERAGE-BACKLOG.md).

Next shared workstreams are synchronized separate live audio/video, live DASH and private playback-token refresh, followed by more providers' account/password workflows and the YouTube/Vimeo matrix. The original 1,731-key/126-app-option/276-long-option scope remains intact. No full extractor/option parity is inferred from these milestones. Production WPF still starts the original engine; cutover, dependency removal and packaged-app verification remain blocked by required coverage.
