using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;

internal static partial class FfmpegChecks
{
    private static async Task MetadataParsingMedia(string ffmpeg, string mode)
    {
        using var files = new Files(); var source = Path.Combine(files.Directory,"source.mp3");
        await Run(ffmpeg,["-v","error","-y","-f","lavfi","-i","sine=frequency=620:sample_rate=44100:duration=2","-c:a","libmp3lame","-b:a","128k","-metadata","title=Source original",source]);
        var bytes = await File.ReadAllBytesAsync(source); var media = 0; var callbacks = 0;
        var page = MetadataParsingChecks.Page with { Media = [new() { Url = MetadataParsingChecks.Page.Media[0].Url, Kind = MediaKind.Progressive, Extension = "mp3", HasAudio = true, HasVideo = false, VerificationDemuxer = "mp3", VerificationDurationSeconds = mode == "failure" ? 10 : 2 }] };
        var request = MetadataParsingChecks.Request(files.Directory,"title:%(artist)s - %(title)s") with { FfmpegPath = ffmpeg, OutputTemplate = "%(title)s.%(ext)s", WriteInfoJson = true, KeepOriginal = true, AddMetadata = mode != "noembed", AudioOnly = mode == "convert", AudioFormat = "m4a", DownloadArchiveFile = Path.Combine(files.Directory,"archive.txt") };
        if (mode == "overrides") request = request with { MetadataParseRules = ["title:%(artist)s - %(title)s", "Embedded title:meta_title", "description:meta_comment", ":(?P<meta_synopsis>)"] };
        if (mode == "snapshot")
        {
            page = page with { IsPlaylist = true, Entries = [new(1,"other","Other",new("https://parse.example/other"),2,null,true,page),new(2,"42",page.Title,new("https://parse.example/watch"),2,null,true,page)] };
            request = request with { DownloadPlaylist = true, PlaylistItems = "2", OutputTemplate = "%(playlist_index)s-%(title)s.%(ext)s" };
        }
        var engine = MetadataParsingChecks.Engine(page,bytes,()=>media++);
        if (mode == "failure")
        {
            var prior = Path.Combine(files.Directory,"Track.mp3"); await File.WriteAllBytesAsync(prior,bytes); await File.WriteAllTextAsync(request.DownloadArchiveFile,"other preserved\n");
            try { await engine.DownloadAsync(request,null,null,default,_=>callbacks++); throw new InvalidOperationException("Incomplete parsed media was published."); } catch (InvalidDataException) { }
            if (!(await File.ReadAllBytesAsync(prior)).SequenceEqual(bytes) || await File.ReadAllTextAsync(request.DownloadArchiveFile) != "other preserved\n" || callbacks != 0) throw new InvalidOperationException("Parsing changed protected output/archive after verification failure."); return;
        }
        if (mode == "snapshot") { var inspected = await engine.InspectAsync(request,default); request = request with { PlaylistSnapshot = inspected.PlaylistSnapshot }; }
        var result = await engine.DownloadAsync(request,null,null,default,_=>callbacks++); var expected = mode == "noembed" ? 1 : 2;
        if (!result.Succeeded || result.OutputPaths.Count != expected || callbacks != expected || media != 1) throw new InvalidOperationException("Parsed media/original/completion count changed.");
        var processed = result.OutputPaths.Single(path=> mode == "convert" ? path.EndsWith(".m4a",StringComparison.OrdinalIgnoreCase) : !Path.GetFileName(path).Contains(".original.",StringComparison.Ordinal));
        foreach (var path in result.OutputPaths) await Run(ffmpeg,["-v","error","-xerror","-err_detect","explode","-i",path,"-f","null","-"],true);
        if (mode != "convert" && await AudioHash(ffmpeg,processed) != await AudioHash(ffmpeg,source)) throw new InvalidOperationException("Metadata parsing changed decoded samples.");
        if (mode != "noembed" && !result.OutputPaths.Any(path=>File.ReadAllBytes(path).SequenceEqual(bytes))) throw new InvalidOperationException("Metadata parsing lost the exact original.");
        var ffprobe = Path.Combine(Path.GetDirectoryName(ffmpeg)!,OperatingSystem.IsWindows()?"ffprobe.exe":"ffprobe");
        using var probe = JsonDocument.Parse(await Run(ffprobe,["-v","error","-show_entries","format_tags","-of","json",processed]));
        var tags = probe.RootElement.GetProperty("format").GetProperty("tags");
        if (tags.GetProperty("title").GetString() != (mode == "noembed"?"Source original":mode == "overrides"?"Embedded title":"Track")) throw new InvalidOperationException("Embedded metadata override or disable policy changed.");
        if (mode != "noembed" && tags.GetProperty("artist").GetString() != "Artist") throw new InvalidOperationException("Parsed artist was not embedded.");
        using var info = JsonDocument.Parse(await File.ReadAllTextAsync(result.SidecarPaths.Single()));
        if (info.RootElement.GetProperty("title").GetString() != "Track" || info.RootElement.GetProperty("artist").GetString() != "Artist" || info.RootElement.GetProperty("id").GetString() != "42") throw new InvalidOperationException("Parsed info JSON changed identity or text.");
        if (mode == "snapshot" && (!Path.GetFileName(processed).StartsWith("2-Track.",StringComparison.Ordinal) || info.RootElement.GetProperty("playlist_index").GetInt32()!=2)) throw new InvalidOperationException("Parsed inspected selection lost its source position.");
        var repeat = await engine.DownloadAsync(request,null,null,default,_=>callbacks++);
        if (!repeat.Skipped || media != 1 || callbacks != expected) throw new InvalidOperationException("Parsed archive repeated media or completion.");
    }
}
