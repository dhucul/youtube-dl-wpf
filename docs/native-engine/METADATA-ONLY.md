# Native simulation and standalone files

The C# engine now supports simulation and jobs that save related files without downloading video/audio. The app's existing global and per-download extra-argument fields can request these modes through the native adapter. This is a typed compatibility parser; it does not launch yt-dlp, Python or a command processor.

## Behavior

- `SkipDownload` saves explicitly requested info JSON, descriptions, thumbnails and standalone authored/automatic subtitles. Media conversion and embedding do not run, and ffmpeg is not required for these standalone outputs.
- `Simulate` performs extraction and format selection but suppresses those file writes. It creates no output workspace or non-forced archive lock. It takes precedence over standalone-file requests.
- Missing media formats still fail by default. `IgnoreNoFormatsError` permits available metadata/captions to be saved even when no downloadable format exists. It does not suppress extraction, caption, file or authorization errors.
- These modes do not record an item as a completed media download. `ForceWriteArchive` explicitly enables a successful archive entry, including for simulation. Failed standalone files never produce a successful archive entry.
- Native results expose standalone paths separately through `SidecarPaths` and `NewSidecarPaths`. Media output paths and media-completion callbacks remain empty. The app logs saved paths without adding fictitious playable media to history.

Standalone publication uses the existing staged, rollback-capable file transaction. No-overwrite applies to each artifact. Archive paths remain protected. Standalone staging uses a distinct workspace identity, so successful metadata work cannot remove a prior resumable media download. Playlist jobs preserve selected positions, retain completed standalone files after a later failure, and report per-item failures.

Filenames use the selected source/container facts when available. Format-specific values are unavailable when formats are absent, and the extension placeholder uses `NA`; no such placeholder media file is created. Complete output-template compatibility still needs further work.

## Extra arguments

Global arguments apply first, then per-download arguments, matching the app's existing override order. Windows quoting is handled by the existing argument tokenizer. Supported switches map into native request fields; unknown options, positional URLs, malformed values and unsupported typed output paths fail before extraction.

Examples for the existing extra-arguments field:

```text
--skip-download --write-info-json --write-subs --sub-langs en,fr
--skip-download --ignore-no-formats-error --write-subs --sub-langs en,fr
--simulate --write-info-json
```

The parser covers these mode flags and their implemented aliases, standalone-file enable/disable flags, all-thumbnail saving, subtitle language selection, supported embedding flags, keep-original, no-overwrite, restricted names, playlist selection, plain output directories/templates, archive paths, cookies, selected connection fields, format selection and media retry/concurrency/rate fields. The code in `NativeExtraArguments.cs` is the exact supported list. This is partial extra-argument compatibility. See THUMBNAILS.md for the available image inventories and formats.

## Caption scope

Exposed complete subtitle documents use the normal validated writer. When no provider caption inventory exists, DASH caption representations can be discovered and their text resources downloaded/converted independently of audio and video. Indexed captions retain existing size, range and entity checks.

Standalone HLS rendition captions still need additional clock support. A request for such a track fails after manifest discovery rather than fetching video to guess its timing. Unrequested authored tracks do not block an automatic-only request. Additional discovery when a provider supplies only a partial caption inventory remains pending.

## Verification on October 8, 2026

Fifteen deterministic checks cover standalone output sets, simulation, explicit no-format handling, forced archive writes, per-file no-overwrite, archive-path protection, rollback, archive-write failure reporting, cancellation, preserved media resume workspaces, playlist partial results, DASH text-only transfers, HLS boundaries and argument parsing. An app regression checks quoted templates and per-job overrides through the native adapter.

The native engine saved the public Vimeo demo's exposed English and French caption documents plus info JSON with `--skip-download --ignore-no-formats-error`. The results were 1,394-byte English and 1,486-byte French VTT files, each with 14 cues, and a 346-byte info JSON. A guarded HTTP handler allowed only the player configuration and those two caption documents; no video/audio request or media callback occurred. Simulation then made only its configuration request and created neither output nor archive directories.

The public Angel One DASH sample saved 1,332-byte English and 1,397-byte French VTT files with 16 cues each. A guard allowed the manifest and declared caption resources only. Both documents were byte-identical to the captions from the previously verified complete media download.

These are live standalone-file checks. They do not establish clear Vimeo video playback or complete site parity. The production WPF backend remains unchanged until broader replacement coverage is complete.

Subtitle-only jobs now honor format preference and conversion settings. Live Vimeo English/French SRT conversion retained all cue text and timing; see `SUBTITLE-FORMATS.md` for verified formats and limitations.

## Remaining work

Standalone HLS clock mapping, remaining subtitle discovery/conversion preferences, remaining thumbnail inventories, additional metadata sidecars, print/dump modes, the remaining extra-argument grammar/options, exact legacy interactions, full app-setting validation and production cutover remain pending. The public CLI descriptions of [simulation and related-file options](https://github.com/yt-dlp/yt-dlp#verbosity-and-simulation-options) informed compatibility semantics; the runtime implementation is independent C#.
