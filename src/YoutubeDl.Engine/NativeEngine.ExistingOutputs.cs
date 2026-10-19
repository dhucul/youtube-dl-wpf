using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Media;

namespace YoutubeDl.Engine;

public sealed partial class NativeEngine
{
    private sealed record ExistingLayout(string Raw,bool Merged,IReadOnlyList<(string Role,string Extension,MediaCandidate? Format)> Components);
    private static async Task<NativeDownloadResult?> ExistingOutputsAsync(NativeDownloadRequest request,PageMetadata page,MediaDownloadPlan plan,MediaHttp http,
        string? expectedExtension,IReadOnlyList<string> protectedPaths,Action<string>? log,CancellationToken ct,ManifestPreparation manifests)
    {
        if(!request.NoOverwrites&&(!request.NoPostOverwrites||request.KeepOriginal))return null;
        if(request.NoOverwrites&&!request.KeepOriginal&&expectedExtension is not null)
            return await ExistingSetAsync([NativeOutputTemplate.Render(request,page,plan.Primary,expectedExtension)],protectedPaths,log,ct).ConfigureAwait(false);
        ExistingLayout? layout=null;
        if(plan.Primary.Kind is MediaKind.Progressive or MediaKind.Sabr or MediaKind.HttpSequence&&(plan.Audio is null||plan.Primary.Kind!=MediaKind.Sabr&&plan.Audio.Kind is MediaKind.Progressive or MediaKind.HttpSequence||plan.Audio.Kind==plan.Primary.Kind))
        {
            if(plan.Primary.Kind==MediaKind.Sabr)
            {
                if(plan.Primary.Sabr is null)throw new InvalidDataException("The SABR format has no playback context.");
                if(plan.Audio is not null&&(plan.Audio.Sabr is null||plan.Audio.Sabr.Session!=plan.Primary.Sabr.Session))throw new NotSupportedException("A SABR pair must share one playback context.");
            }
            layout=new(plan.NeedsMerge?plan.Extension:plan.Primary.Extension??"mp4",plan.NeedsMerge,
                plan.Audio is null?[]:[("video",plan.Primary.Extension??"mp4",plan.Primary),("audio",plan.Audio.Extension??"m4a",plan.Audio)]);
            if(plan.Primary.Kind==MediaKind.Progressive&&plan.Primary.NormalizeMp3ClipBoundary)layout=layout with {Components=layout.Components.Append(("audio","mp3",plan.Primary)).ToArray()};
            var sequenceParts=new List<(string Role,string Extension,MediaCandidate? Format)>();
            foreach(var track in new[]{plan.Primary,plan.Audio}.Where(track=>track?.Kind==MediaKind.HttpSequence))
            {
                HttpSequenceDownloader.ValidateInventory(track!);
                foreach(var part in track!.Parts)sequenceParts.Add((sequenceParts.Count==0?"part":"part-"+(sequenceParts.Count+1),part.Extension,null));
            }
            if(sequenceParts.Count>0)layout=layout with {Components=sequenceParts.Concat(layout.Components).ToArray()};
            if(plan.NeedsMerge&&!MergeOutputFormats.CanPredict(request.MergeOutputFormat,plan.Primary.VideoCodec,plan.Audio!.AudioCodec))
                layout=await ResolveLocalMergeAsync(request,page,plan.Primary,layout,ct).ConfigureAwait(false);
        }
        else if(plan.Primary.Kind==MediaKind.Hls&&plan.Audio is null)
        {
            // Avoid extra playlist reads on a first download with no plausible existing main.
            var candidates=new[]{"ts","mp4","m4a","mp3","mkv","webm"}.Concat(expectedExtension is null?[]:new[]{expectedExtension})
                .Select(extension=>NativeOutputTemplate.Render(request,page,plan.Primary,extension));
            if(!candidates.Any(File.Exists))return null;
            layout=await ExistingHlsLayoutAsync(request,page,plan.Primary,http,ct,manifests).ConfigureAwait(false);
        }
        else if(plan.Primary.Kind==MediaKind.Dash&&plan.Audio is null)
        {
            var candidates=new[]{"mp4","mkv","webm","m4a","mp3","mka","opus","ogg"}.Concat(expectedExtension is null?[]:[expectedExtension]).Select(extension=>NativeOutputTemplate.Render(request,page,plan.Primary,extension));
            if(!candidates.Any(File.Exists))return null;
            DashManifest manifest;
            try{manifest=await manifests.PrepareDashAsync(http,plan.Primary.Url,ct,request.LiveDurationSeconds is not null||request.LiveFromStart).ConfigureAwait(false);}
            catch(ExpiredPlaybackResourcesException)when(page.Extractor=="kaltura"&&string.IsNullOrWhiteSpace(request.FormatSelector))
            {return null;} // Preserve the downloader's publisher-offered alternate-profile recovery.
            if(manifest.IsDynamic||request.LiveDurationSeconds is not null||request.LiveFromStart)return null;
            var selected=DashDownloader.SelectPlan(manifest,request);
            layout=new(selected.Extension,selected.NeedsMerge,selected.Audio is null?[]:
                [("video",selected.Primary.Extension??"mp4",selected.Primary),("audio",selected.Audio.Extension??"m4a",selected.Audio)]);
            if(selected.NeedsMerge&&!MergeOutputFormats.CanPredict(request.MergeOutputFormat,selected.Primary.VideoCodec,selected.Audio!.AudioCodec))
                layout=await ResolveLocalMergeAsync(request,page,plan.Primary,layout,ct).ConfigureAwait(false);
            if(request.EmbedSubs&&SubtitleSelection.Select(page.Subtitles,request).Count==0&&
                (manifest.CaptionTracks.Count>0||manifest.Subtitles.Count>0||manifest.HasSegmentedSubtitles))return null;
            if(request.AudioOnly)
            {
                // The actual selected representation supplies the audio/container facts.
                var finalAudio=await ExistingAudioExtensionAsync(request,page,plan.Primary,layout?.Raw,selected.Primary,ct).ConfigureAwait(false);
                if(layout is null||finalAudio is null)return null;
                return await ExistingLayoutSetAsync(request,page,plan.Primary,layout,finalAudio,protectedPaths,log,ct).ConfigureAwait(false);
            }
        }
        if(layout is null)return null;
        var final=request.AudioOnly?await ExistingAudioExtensionAsync(request,page,plan.Primary,layout.Raw,plan.Primary,ct).ConfigureAwait(false):ProcessedExtension(request,page,layout.Raw,null);
        if(final is null)return null;
        return await ExistingLayoutSetAsync(request,page,plan.Primary,layout,final,protectedPaths,log,ct).ConfigureAwait(false);
    }
    private static async Task<NativeDownloadResult?> ExistingLayoutSetAsync(NativeDownloadRequest request,PageMetadata page,MediaCandidate primary,ExistingLayout layout,string final,
        IReadOnlyList<string> protectedPaths,Action<string>? log,CancellationToken ct)
    {
        var modified=WouldModify(request,page,layout.Raw,final,layout.Merged?true:primary.HasAudio);
        if(!request.NoOverwrites&&!layout.Merged&&!modified)return null;
        var main=NativeOutputTemplate.Render(request,page,primary,final);var paths=new List<string>{main};
        if(request.KeepOriginal)
        {
            if(modified){var original=NativeOutputTemplate.Render(request,page,primary,layout.Raw);paths.Add(SamePath(original,main)?AddOutputSuffix(original,"original"):original);}
            foreach(var component in layout.Components)
                paths.Add(AddOutputSuffix(NativeOutputTemplate.Render(request,page,component.Format??primary,component.Extension),component.Role));
        }
        return await ExistingSetAsync(paths,protectedPaths,log,ct).ConfigureAwait(false);
    }
    private static async Task<NativeDownloadResult?> ExistingSetAsync(IReadOnlyList<string> paths,IReadOnlyList<string> protectedPaths,Action<string>? log,CancellationToken ct)
    {
        var comparer=OperatingSystem.IsWindows()?StringComparer.OrdinalIgnoreCase:StringComparer.Ordinal;
        if(paths.Distinct(comparer).Count()!=paths.Count)return null;
        foreach(var path in paths){PublicationTransaction.PlainPath(path);if(protectedPaths.Any(protectedPath=>SamePath(protectedPath,path)))throw new IOException("An existing output conflicts with the download archive.");}
        using var publication=await DestinationLock.AcquirePublicationsAsync(paths.Select(path=>Path.GetDirectoryName(path)!),ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();if(paths.Any(path=>!File.Exists(path)))return null;
        foreach(var path in paths){PublicationTransaction.PlainPath(path);log?.Invoke("[native] already saved "+path);}
        return new(){Succeeded=true,Skipped=true,OutputPath=paths[0],OutputPaths=paths.ToArray(),NewOutputPaths=[]};
    }
    private static async Task<ExistingLayout?> ExistingHlsLayoutAsync(NativeDownloadRequest request,PageMetadata page,MediaCandidate descriptor,MediaHttp http,CancellationToken ct,ManifestPreparation manifests)
    {
        var playlist=await manifests.PrepareHlsAsync(http,descriptor.Url,ct).ConfigureAwait(false);CheckHlsFeatures(playlist);
        HlsAudioRendition? rendition=null;HlsVariant? variant=null;var master=playlist;
        if(playlist.IsMaster)
        {
            variant=SelectVariant(playlist,descriptor.IsAudioOnly?request with {AudioOnly=true,MaximumHeight=null}:request);
            if(variant.AudioGroup is not null)rendition=playlist.AudioRenditions.Where(audio=>audio.Group==variant.AudioGroup).OrderByDescending(audio=>audio.Default).FirstOrDefault()
                ??throw new InvalidDataException("The HLS master playlist refers to a missing audio group.");
            playlist=await manifests.PrepareHlsAsync(http,variant.Url,ct).ConfigureAwait(false);CheckHlsFeatures(playlist);
        }
        if(playlist.IsMaster)throw new NotSupportedException("Nested HLS master playlists are not supported yet.");
        if(!playlist.IsComplete||request.LiveDurationSeconds is not null||request.LiveFromStart||playlist.MediaSegments.Count==0||!UniformMaps(playlist))return null;
        if(!master.IsMaster&&!request.AudioOnly&&!descriptor.IsAudioOnly&&
            (request.MaximumHeight>0&&!(descriptor.Height<=request.MaximumHeight)||request.MaximumFrameRate>0&&!(descriptor.FramesPerSecond<=request.MaximumFrameRate)||!FormatFacts.CodecMatches(descriptor.VideoCodec,request.VideoCodec)))
            throw new InvalidOperationException("The HLS media playlist does not expose the requested format facts.");
        // A rendition caption may change the final container. Keep transfer-time resolution for that case.
        if(request.EmbedSubs&&master.SubtitleRenditions.Any(track=>track.Url is not null)&&SubtitleSelection.Select(page.Subtitles,request).Count==0)return null;
        var videoRaw=playlist.MediaSegments[0].Initialization is not null?(descriptor.IsAudioOnly?"m4a":"mp4"):descriptor is {IsAudioOnly:true,AudioCodec:"mp3"}?"mp3":"ts";
        if(rendition?.Url is null||request.VideoOnly)return new(videoRaw,false,[]);
        var audio=await manifests.PrepareHlsAsync(http,rendition.Url,ct).ConfigureAwait(false);CheckHlsFeatures(audio);
        if(audio.IsMaster||!audio.IsComplete||audio.MediaSegments.Count==0||!UniformMaps(audio))return null;
        var audioRaw=audio.MediaSegments[0].Initialization is null?"ts":"m4a";
        if(request.AudioOnly)return new(audioRaw,false,[]);
        if(!MergeOutputFormats.CanPredict(request.MergeOutputFormat,variant?.VideoCodec??descriptor.VideoCodec,null))
            return await ResolveLocalMergeAsync(request,page,descriptor,new(string.IsNullOrWhiteSpace(request.Container)?"mp4":request.Container,true,[("video",videoRaw,descriptor),("audio",audioRaw,null)]),ct).ConfigureAwait(false);
        var merged=MergeOutputFormats.Select(request.MergeOutputFormat,string.IsNullOrWhiteSpace(request.Container)?"mp4":request.Container,variant?.VideoCodec??descriptor.VideoCodec,null);
        return new(merged,true,[("video",videoRaw,descriptor),("audio",audioRaw,null)]);
    }
    private static bool UniformMaps(HlsPlaylist playlist)=>playlist.MediaSegments.All(segment=>(segment.Initialization is not null)==(playlist.MediaSegments[0].Initialization is not null));
    private static async Task<ExistingLayout?> ResolveLocalMergeAsync(NativeDownloadRequest request,PageMetadata page,MediaCandidate primary,ExistingLayout layout,CancellationToken ct)
    {
        var tracks=layout.Components.Where(component=>component.Role is "video" or "audio").ToArray();
        if(!HasLocalProbe(request)||tracks.Length!=2)return null;
        var files=tracks.Select(component=>AddOutputSuffix(NativeOutputTemplate.Render(request,page,component.Format??primary,component.Extension),component.Role)).ToArray();
        foreach(var file in files){PublicationTransaction.PlainPath(file);if(!File.Exists(file))return null;}
        try{return layout with {Raw=await FfmpegTool.SelectMergeExtensionAsync(request.FfmpegPath!,files[0],files[1],request.MergeOutputFormat,layout.Raw,ct).ConfigureAwait(false)};}
        catch(Exception e)when(e is IOException or InvalidDataException or InvalidOperationException or NotSupportedException){ct.ThrowIfCancellationRequested();return null;}
    }
    private static async Task<string?> ExistingAudioExtensionAsync(NativeDownloadRequest request,PageMetadata page,MediaCandidate primary,string? raw,MediaCandidate facts,CancellationToken ct)
    {
        if(AudioOutputPlan.ExtensionFor(request.AudioFormat) is { } explicitFormat)return explicitFormat;
        if(raw is null)return null;
        var source=NativeOutputTemplate.Render(request,page,primary,raw);
        if(HasLocalProbe(request))
        {
            var matches=new List<string>();
            foreach(var candidate in new[]{source,AddOutputSuffix(source,"original")})
            {
                PublicationTransaction.PlainPath(candidate);if(!File.Exists(candidate))continue;
                try
                {
                    var plan=await FfmpegTool.PlanAudioAsync(request.FfmpegPath!,candidate,request.AudioFormat,request.AudioQuality,ct,localMediaOnly:true).ConfigureAwait(false);
                    var target=NativeOutputTemplate.Render(request,page,primary,plan.Extension);var expected=SamePath(source,target)?AddOutputSuffix(source,"original"):source;
                    if(SamePath(candidate,expected))matches.Add(plan.Extension);
                }
                catch(Exception e)when(e is IOException or InvalidDataException or InvalidOperationException or NotSupportedException){ct.ThrowIfCancellationRequested();}
            }
            if(matches.Count==1)return matches[0];if(matches.Count>1)return null;
        }
        var codec=facts.AudioCodec?.ToLowerInvariant();if(codec?.StartsWith("mp4a",StringComparison.Ordinal)==true)codec="aac";
        if(string.IsNullOrWhiteSpace(codec)||codec=="unknown")return null;
        // Only declared elementary/standard containers establish the automatic result.
        if(raw is not ("mp4" or "m4a" or "mov" or "mkv" or "webm" or "mp3" or "aac" or "opus" or "ogg" or "flac" or "wav" or "ts"))return null;
        return AudioOutputPlan.AutomaticExtension(codec,raw=="mkv"?"matroska":raw,AudioOutputPlan.Normalize(request.AudioFormat));
    }
    private static bool HasLocalProbe(NativeDownloadRequest request)=>!string.IsNullOrWhiteSpace(request.FfmpegPath)&&File.Exists(request.FfmpegPath)&&File.Exists(Path.Combine(Path.GetDirectoryName(request.FfmpegPath)!,"ffprobe.exe"));
}
