# YoutubeDL Modern

YoutubeDL Modern is a Windows desktop downloader with a built-in C# engine. It keeps the familiar youtube-dl workflow while providing a native WPF interface for analyzing links, choosing formats, managing a download queue, and reviewing completed downloads.

> Only download media that you are allowed to save. This project is not affiliated with youtube-dl, yt-dlp, or ffmpeg.

Version 1.1.7 includes the Facebook static DASH playback and session renewal fixes, with all 3,713 core engine checks, 204 application checks and 109 focused Facebook checks passing. It keeps the best available video and original audio defaults without re-encoding. Automatic container selection preserves higher-quality audio, and the former MP3/192K default migrates on startup. It includes the earlier reliability fixes and retains the YouTube, public Facebook and non-explicit common-site workflows. Other sites and advanced legacy options have partial coverage; see [release scope and verification](docs/native-engine/PRACTICAL-RELEASE.md).

## Features

- Analyze a video or playlist before downloading, with thumbnail and metadata preview
- Paste several URLs at once or import a youtube-dl-style batch file
- Download video with audio, video only, or extracted audio
- Default to the best available video and original audio without re-encoding; automatic containers preserve both streams
- Choose a quality preset, a site-provided format, or a supported custom format selector
- Select playlist ranges and individual playlist items
- Download, convert, and embed subtitles
- Save or embed thumbnails, metadata, and chapters
- Authenticate with a cookies file or cookies from a supported browser
- Pause, resume, cancel, remove, and run downloads in parallel
- Keep a persistent download history with shortcuts to the file and folder
- Configure advanced youtube-dl/yt-dlp options for files, formats, networking, selection, authentication, and post-processing
- Use the built-in downloader; locate or install ffmpeg when media processing needs it

## Getting started

1. Launch YoutubeDL Modern.
2. The downloader is built in. Install ffmpeg from **About** when combining streams or converting audio requires it.
3. Paste a video or playlist URL and select **Analyze**. You can also press **Enter** in the URL box.
4. Choose the download type, quality, output folder, and any optional settings.
5. Select **Add to queue**, then use the queue controls to pause, resume, or cancel work.

Multiple space- or newline-separated URLs are queued directly. Use **Batch file...** to import URLs from a text file. Press **F1** anywhere in the app to open the built-in help page.

No yt-dlp or Python installation is required. ffmpeg is required for tasks such as combining separate video and audio streams, extracting audio, converting media, and embedding subtitles or artwork. It can be installed from the About page.

## Detailed usage

### Analyze a link

Use **Analysis options** before selecting **Analyze**:

- **Quick** skips some format-availability checks and is the fastest choice for ordinary links.
- **Full** reads normal metadata and format inventory; fresh playback is checked when downloading.
- **Playlist** explicitly requests playlist metadata so entries can be previewed and selected.

The preview shows the title, uploader, duration, site, thumbnail, and known subtitle languages. **Cancel** stops a slow lookup, **Discard** clears the result, and **Choose format** loads the exact streams reported by the site.

Analysis and downloading are separate engine operations. For private media, make sure authentication is available during analysis and in **Advanced download options**. An analyzed item initially copies its analysis authentication into the download choices.

### Add multiple links or a batch

Space- or newline-separated URLs are added directly using the current saved defaults because one preview cannot represent several items. Check the default output folder, quality, authentication, and parallel-download count first.

A batch text file uses one URL per line. Blank lines and comment lines beginning with `#`, `;`, or `]` are ignored, and duplicates are imported once. Batch files are limited to 10 MB and 1,000 unique URLs. The queue must have room for the entire import.

### Choose video and audio

- **Video + audio** selects streams matching the quality, codec, and frame-rate limits, then uses ffmpeg when separate streams must be merged.
- **Video only** downloads a video stream without sound.
- **Audio only** extracts or converts the best audio to the selected format. For MP3 VBR, `0` is the highest quality; values such as `192K` request a fixed bitrate.
- Resolution presets such as 1080p are maximums, allowing the downloader to select a lower offered height when the exact height is unavailable.
- **Choose format** selects a specific site-provided stream. **Custom** accepts supported `-f` expressions such as `bestvideo[height<=1080]+bestaudio`.

Leaving the output container on **auto** lets the downloader choose a compatible container. A selected container may require remuxing, and not every video/audio codec combination is valid in every container.

### Playlists

Leave **Download playlist** enabled for all entries. To select positions, enter comma-separated indexes and ranges such as `1-3,7,10-12`. The start/end controls limit a continuous range. Clear **Download playlist** to download only the linked item.

The global **Never download playlists** option is under **Settings > General**. Playlist order and selection filters are available under the Network and Selection settings tabs.

### Subtitles, artwork, and metadata

- **Download subs** requests creator-provided captions for languages such as `en,de` or `en.*`.
- **Automatic captions** requests machine-generated captions.
- **Embed subtitles** also downloads the captions and requires ffmpeg plus a compatible container.
- **Save thumbnail** writes a separate image; **Embed thumbnail** places artwork in the media file when supported.
- **Metadata** writes common tags and **Chapters** preserves chapter markers.

Subtitle conversion and preferred subtitle formats are under **Settings > Subtitles**. Most embedding and conversion operations require ffmpeg.

### Queue controls and History

Downloads begin automatically as concurrency slots become available. The configured parallel-download limit is between 1 and 8.

- **Pause** cancels current transfer work but keeps the item. With normal partial-file settings, **Resume** can continue downloaded data.
- **Cancel** stops the item. **Remove** deletes only the queue card.
- **Start all**, **Pause all**, and **Clear completed** apply bulk actions without deleting downloaded media.
- Expand **Engine log** on a card for the downloader output.
- Each finalized file appears in **History**, including files completed before a later playlist failure or cancellation. Use **Play** and **Folder** there to open the result. Removing or clearing history never deletes media.

### Saved settings

Select **Save** before expecting edited settings to apply. New queue items capture a settings snapshot when they are added, so later edits do not change existing items. **Reset to defaults** updates the editable values; save afterward to persist them.

The download archive is distinct from History: it records media IDs for the downloader and can skip previously downloaded items even after visual history is cleared.

### Examples

| Goal | Controls or value |
| --- | --- |
| Compatible 1080p MP4 | Video + audio, 1080p, H.264, 30 fps, mp4 |
| MP3 audio | Audio only, mp3, 192K |
| Selected playlist entries | Playlist items: `1-3,7,10-12` |
| English variants and German subtitles | Subtitle languages: `en.*,de` |
| Limit transfer speed | Speed limit: `4.2M` |
| 1080p/30 fps custom format | `bestvideo[height<=1080][fps<=30]+bestaudio/best[height<=1080]` |
| Numbered playlist filenames | `%(playlist_title)s/%(playlist_index)03d - %(title)s [%(id)s].%(ext)s` |
| Videos under 30 minutes with 10,000+ views | Match filter: `duration < 1800 & view_count >= 10000` |

The in-app **Help > Examples** tab contains step-by-step context, authentication and batch-file examples, fallbacks, and cautions for each recipe.

## Private or age-restricted media

Use cookies only for accounts and content you are authorized to access. Analysis uses saved authentication by default; clear **Use saved authentication** to supply an override. Expand **Analysis options** or **Advanced download options** and select a browser, or browse to a Netscape-format cookies file. A cookies file takes priority over browser cookies.

Authentication defaults can also be saved under **Settings > Authentication**. Stored secrets are protected with the current Windows user account; one-time two-factor codes are never persisted.

## Troubleshooting

- **A site suddenly stops working:** Install a newer application release for downloader fixes. Supported sites change frequently.
- **Merging or audio extraction fails:** Install ffmpeg from **About**, or set its folder under **Settings > Post-processing**.
- **Private media cannot be analyzed:** Supply browser cookies or a cookies file for both analysis and download.
- **A playlist is larger than expected:** Clear **Download playlist**, enter playlist items such as `1-3,7`, or enable the global single-video option in Settings.
- **A download fails without enough detail:** Expand **Engine log** on its queue card. The original URL can be copied from the same card.

## Data locations

| Data | Location |
| --- | --- |
| Settings and history | `%APPDATA%\YoutubeDlModern` |
| Managed media tools | `%LOCALAPPDATA%\YoutubeDlModern` |
| Default downloads | `%USERPROFILE%\Downloads\YoutubeDL Modern` |

Removing history does not delete downloaded media. Settings can be returned to their defaults from the Settings page.

## Build from source

Requirements:

- Windows 10 or later
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

Clone the repository, then run:

```powershell
dotnet restore .\YoutubeDlModern.slnx
dotnet build .\YoutubeDlModern.slnx --configuration Release
dotnet run --project .\src\YoutubeDlModern\YoutubeDlModern.csproj
```

Global extra arguments apply to downloads. Metadata analysis accepts only connection and extraction overrides, ignores external configuration files, and always runs in simulation mode.

The C# engine ships with the app. Engine fixes arrive with application releases. ffmpeg can be downloaded on demand.

### Run regression checks

The checks exercise queue cancellation and shutdown, partial-result persistence, preview initialization, settings recovery, and argument selection using a fake engine. They do not download media or modify your application profile.

```powershell
dotnet run --project .\tests\YoutubeDlModern.Tests\YoutubeDlModern.Tests.csproj --configuration Release
```

### Build the installer

The installer script creates a self-contained Windows x64, single-file publish and packages it with Inno Setup:

```powershell
.\dev-scripts\build-installer.ps1
```

This requires the Inno Setup 7 compiler at `C:\Program Files\Inno Setup 7\ISCC.exe`. The installer is written to `installer\output`.

## Project layout

```text
src/YoutubeDlModern/   WPF application, view models, and services
installer/             Inno Setup definition
dev-scripts/           Packaging and development helpers
```

The app targets `net10.0-windows` and uses the CommunityToolkit.Mvvm package. There is no separate web service: the C# engine and ffmpeg run locally, and settings/history are stored in the current Windows profile.
