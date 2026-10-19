# Native subtitle preferences and conversion

The C# engine now maps the app's preferred subtitle format, conversion format and all-subtitle setting into native requests. Discovery, selection, HTTP transfer, file ownership and publication run in C#. Conversion uses the already retained ffmpeg tools; no Python, yt-dlp or additional subtitle package is involved.

## Selection and settings

`SubFormat` accepts ordered slash-separated preferences such as `ass/srt/best`. Language selection occurs first, then authored captions take precedence over automatic captions for the same language, then the format preference chooses an offered alternative. A format missing from an unselected language does not block a requested language. Only the chosen document is fetched.

`best` retains the provider's first supported preferred document. This preserves existing provider ordering; it is not a complete reproduction of every upstream provider's ranking rules. If none of the requested formats is offered for a selected language, the request fails explicitly instead of saving a different format under a misleading extension. Preferences have bounded length and token counts.

The adapter maps `Subtitles.SubFormat` and `Subtitles.ConvertSubs`. `Subtitles.WriteAllSubs` enables authored-caption saving and selects all languages; automatic captions still require their separate option. Per-job extra arguments override these saved settings. Supported arguments now include `--sub-format`, `--convert-subs` / `--convert-subtitles`, and `--all-subs`, alongside existing language and authored/automatic switches.

## Conversion and publication

`ConvertSubs` supports SRT, VTT, ASS and LRC outputs. The supported converter inputs are VTT, SRT, ASS/SSA and LRC. Supported TTML/DFXP now converts to SRT/VTT directly in C#, or through native timed text into ffmpeg for ASS/LRC. Original XML can also be saved unchanged while its native representation is embedded; see `TTML.md` for timing, text and presentation limits. A same-format request preserves the exact downloaded bytes and does not require ffmpeg.

When a selected source needs conversion, the known dependency is checked before media transfer. Adaptive manifests are checked when their caption inventory becomes available. Simulation requires no converter. Subtitle-only jobs save converted sidecars without downloading audio/video. Temporary source files are removed after success, failure and cancellation; converted files enter the same publication transaction as other requested outputs. Existing published files survive conversion failure, and standalone no-overwrite preserves an existing converted file.

ffmpeg receives a forced text-subtitle demuxer and local file/pipe protocols. Its output is read directly into a bounded buffer, with a 2 MiB output limit and a 30-second conversion deadline. Oversized output, invalid UTF-8, unsuccessful exit, empty output and output without timed cues fail explicitly. Cancellation and conversion failures kill and drain the child process before returning.

SRT/VTT retain millisecond cue timing for the tested documents. ASS stores centiseconds and may reduce timestamp precision. LRC stores cue starts and cannot preserve explicit cue ends or gaps. When converting a full subtitle document to LRC while also embedding captions, the saved sidecar is LRC and embedding uses a temporary copy of the original timed document. This preserves its explicit ends in the media. Styling, positioning, voice labels and other format-specific features may not survive a conversion to a less expressive format.

## Verification on October 8, 2026

Nine deterministic checks cover preference/provenance/language order, fetching only the selected format, byte-identical same-format output, missing tools, invalid options, argument precedence, oversized converter output, child-process cancellation/draining, and preservation of published captions after failed/empty/invalid conversion. A WPF regression check verifies all-subtitle settings and per-job overrides.

Seven generated-media/conversion checks cover all four output formats, ASS input to SRT, no-overwrite, Unicode/multiline text, cue starts/ends/gaps, output extensions, temporary cleanup, and SRT/LRC saving plus MP4 embedding. Embedded outputs retained all 30 generated video frames, English language tags, and both original subtitle end timestamps. ASS fixtures deliberately use centisecond-aligned timing; LRC assertions cover starts only. Full media decoding passed.

A caption-only live run used the exposed documents from Vimeo's public SDK example `76979871?h=8272103f6e`. A guarded HTTP handler allowed only its configuration and VTT resources. Four configurations and six caption requests produced the following files, with no media requests or media-completion callbacks:

| Language / conversion | Source bytes | Output bytes | Verification |
| --- | ---: | ---: | --- |
| English VTT to SRT | 1,394 | 1,387 | All 14 cues retained identical text, starts and ends |
| French VTT to SRT | 1,486 | 1,479 | All 14 cues retained identical text, starts and ends |
| English VTT to ASS | 1,394 | 2,290 | Saved successfully with timed cues |
| English VTT to LRC | 1,394 | 1,031 | Saved successfully with timed lyrics |

This establishes live caption access and conversion only. Clear live Vimeo video playback remains unverified.

Clear MP4-packaged TTML now normalizes into the native WebVTT caption pipeline. Standalone conversion and subsequent format choices are covered in `MP4-TTML.md`.

## Remaining work

Remaining TTML presentation/timing modes and provider-specific JSON/XML conversion, broader styling/positioning fidelity, more encoding formats, original-caption retention options, exact upstream best/fallback rules, additional provider inventories, automatic translations, standalone HLS clock support, broader input-format verification and production cutover remain pending.

Behavioral reference: [upstream subtitle options](https://github.com/yt-dlp/yt-dlp#subtitle-options). Conversion uses [ffmpeg's subtitle formats](https://ffmpeg.org/ffmpeg-formats.html); the native selection and orchestration code is independently implemented in C#.
