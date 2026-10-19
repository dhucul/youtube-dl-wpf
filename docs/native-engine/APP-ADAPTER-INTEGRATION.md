# Native app adapter integration verification

This checkpoint continues the archived **Inspect youtube-dl-wpf** session from native engine 0.95.0. The repository had 541 uncommitted files before this continuation; those files and their implementation history were preserved. Native engine 0.96.0 adds pure description of already resolved provider metadata; the WPF adapter and its verification now use that description.

The native adapter remains a migration component. Production WPF still starts `YtDlpEngine`; full native site coverage, option parity, removal of the original backend and packaged application verification remain unfinished.

## Analysis and format selection

Quick and full analysis no longer accidentally expand a video URL's linked playlist. Playlist mode explicitly requests the collection and retains the native inspection snapshot. Quick and full currently share the native discovery path; differentiated availability checks and latency parity remain pending.

An optional `SettingsService` constructor argument supplies a fresh settings snapshot for each analysis. Proxy, source address, IP family, certificate policy, headers, user-agent, referrer, timeout and retry settings reach native discovery. Saved cookies are used when analysis options are omitted; explicit empty/none cookie choices continue to disable them. Download-only metadata, conversion and output choices are excluded from the analysis request. Supported extraction/connection extra arguments use the existing analysis whitelist and native typed mapper. Unsupported extraction arguments fail before discovery without reporting their values. Complete whitelist semantics remain pending.

Format listing uses the requested single-video or collection scope independently of the analysis selector. Collections resolve the first original-position entry and then its first nested leaf. Empty or unavailable first entries, missing addresses, cycles and more than 16 nested inspections fail explicitly. Cancellation drains the current child inspection. Every child inherits the selected connection/authentication request; later linked playlists are not automatically expanded. Already resolved entries are described without reopening a parent page or requesting media. This preserves original provider format IDs, tracks and availability, including inline children with no separate address. Resolved reference cycles and explicit protected-media refusals remain bounded errors.

## Download preflight

Per-job retry counts are passed through and validated over the native engine's supported 0–1,000 range; the adapter no longer silently clamps them to ten. An empty per-job subtitle language selection inherits saved languages. Explicit job selectors and all-language preferences keep their existing precedence.

Invalid download modes, quality presets and empty custom selectors fail before discovery. Thirty-five currently unmapped configured options also fail before any HTTP request, output directory creation, archive modification or file-completion callback. Diagnostics list property names and exclude configured values, including credentials and command text.

The refused options cover partial-file/time/cache overrides, geographic verification settings, chunk/buffer/external-downloader/sleep settings, file-size/date/view/filter/age selection, non-cookie authentication and advanced post-processing. This refusal does **not** implement their behavior or make those compatibility entries complete. Default geographic/fragment/cache behavior, remaining settings interactions and the full 126-option contract still require verification and implementation.

## Verification

The Release solution build passed with zero warnings and zero errors. The unchanged native library passed all 1,398 checks, including 342 optional media checks. The updated app passed all 49 checks: 45 core and four browser transport checks.

Nine new app checks exercise mode/snapshot behavior, saved connection and explicit authentication choices, analysis argument precedence, original-position nested format selection, unavailable/cycle/depth boundaries, child cancellation, all 35 refusal cases with archive/output/value protection, valid/invalid retry values, saved subtitle language fallback and invalid download choices.

Live verification used the same public Mixcloud show as the preceding session and its creator inventory. Single analysis and direct format listing each exposed three formats. The complete creator inventory contained 14 uploads; its first original entry exposed three formats through the native adapter.

The adapter downloaded and processed the complete audio while retaining its original:

| File | Bytes | Decoded AAC frames | Duration | Encoded packets |
| --- | ---: | ---: | ---: | ---: |
| Processed M4A | 491,993 | 2,581 | 59.925011 s | 2,583 |
| Original M4A | 491,988 | 2,581 | 59.925011 s | 2,583 |

Independent complete decoding and packet hashing confirmed identical decoded PCM and preservation of every encoded audio payload in its original order. The processed title tag matched the current public episode metadata. The initial job reported two completions; an archive repeat succeeded without files or completion callbacks.

The probe uses the native C# adapter directly with ffmpeg. It does not invoke Python, yt-dlp or the composite fallback. It is evidence for this adapter workflow, **not** verification of the final packaged WPF application.

Scratch evidence is in `work/adapter-live-probe` and the test logs in the October 10 Codex workspace. Existing user settings/history and managed engines were not modified. The test settings, output and archive are isolated scratch data. No Git commit or push was performed.
