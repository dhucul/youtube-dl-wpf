using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Media;

namespace YoutubeDl.Engine.Download;

internal sealed record PairedLiveResult(string Path,HlsPlaylist Video,HlsPlaylist Audio,double DurationSeconds,IReadOnlyList<decimal> VideoClocks,DateTimeOffset End);
internal static class PairedLiveRecorder
{
    internal static async Task<PairedLiveResult> DownloadAsync(MediaHttp http,HlsPlaylist video,HlsPlaylist audio,string destination,
        NativeDownloadRequest request,ByteRateLimiter limiter,IProgress<DownloadProgress>? progress,CancellationToken ct,
        Func<CancellationToken,Task<HlsPlaylist>>? refreshVideo=null,Func<CancellationToken,Task<HlsPlaylist>>? refreshAudio=null,
        Func<LiveCapturedSegment,CancellationToken,Task>? observeVideo=null)
    {
        var window=await LiveWindow.RecoverSharedAsync(http,video,audio,destination,request,ct).ConfigureAwait(false);
        FfmpegTool.RequireProbe(request.FfmpegPath,"synchronize and verify separate live tracks");
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct);
        var directory=Path.GetDirectoryName(Path.GetFullPath(destination))!;Directory.CreateDirectory(directory);
        var prefix=Path.Combine(directory,".live-pair");
        var videoPath=prefix+(video.MediaSegments[0].Initialization is null?".video.ts":".video.mp4");
        var audioPath=prefix+(audio.MediaSegments[0].Initialization is null?".audio.ts":".audio.m4a");
        var extension=string.IsNullOrWhiteSpace(request.Container)?"mp4":request.Container;var merged=prefix+".merged."+extension;
        try
        {
            async Task<HlsLiveResult> Capture(HlsPlaylist playlist,string path,Func<CancellationToken,Task<HlsPlaylist>>? refresh)
            {try{return await new HlsLiveRecorder(http){Window=window,Refresh=refresh,Observe=ReferenceEquals(playlist,video)?observeVideo:null}.DownloadAsync(playlist,path,request,progress,limiter,linked.Token).ConfigureAwait(false);}catch{linked.Cancel();throw;}}
            var v=Capture(video,videoPath,refreshVideo);var a=Capture(audio,audioPath,refreshAudio);
            await Task.WhenAll(v,a).ConfigureAwait(false);
            var vr=await v.ConfigureAwait(false);var ar=await a.ConfigureAwait(false);
            var coverage=LiveWindow.Coverage(vr.Playlist,ar.Playlist,window);
            var videoPeriods=vr.Playlist.MediaSegments.GroupBy(segment=>segment.DiscontinuitySequence).ToArray();
            var audioPeriods=ar.Playlist.MediaSegments.GroupBy(segment=>segment.DiscontinuitySequence).ToArray();
            var offset=LiveWindow.Offset(vr.Playlist,ar.Playlist);
            for(var index=1;index<videoPeriods.Length;index++)
            {
                var vd=(videoPeriods[index].First().ProgramDateTime!.Value-videoPeriods[0].First().ProgramDateTime!.Value).TotalSeconds;
                var ad=(audioPeriods[index].First().ProgramDateTime!.Value-audioPeriods[0].First().ProgramDateTime!.Value).TotalSeconds;
                if(Math.Abs(vd-ad)>0.05)throw new NotSupportedException($"Separate live tracks change their clock relationship across discontinuities: video elapsed={vd:0.########}, audio elapsed={ad:0.########}.");
            }
            if(!string.IsNullOrWhiteSpace(request.MergeOutputFormat))
            {extension=await FfmpegTool.SelectMergeExtensionAsync(request.FfmpegPath!,videoPath,audioPath,request.MergeOutputFormat,extension,ct).ConfigureAwait(false);merged=prefix+".merged."+extension;}
            var earliest=vr.Playlist.MediaSegments[0].ProgramDateTime!.Value<ar.Playlist.MediaSegments[0].ProgramDateTime!.Value?vr.Playlist.MediaSegments[0].ProgramDateTime!.Value:ar.Playlist.MediaSegments[0].ProgramDateTime!.Value;
            var end=LiveWindow.EndOf(vr.Playlist.MediaSegments[^1]);var ae=LiveWindow.EndOf(ar.Playlist.MediaSegments[^1]);if(ae<end)end=ae;
            await FfmpegTool.MergeLiveAsync(request.FfmpegPath!,videoPath,audioPath,merged,offset,(end-earliest).TotalSeconds,ct,request.LiveDashSelection?.TransitionAudioCodec is "flac" or "opus").ConfigureAwait(false);
            await FfmpegTool.VerifyLivePairAsync(request.FfmpegPath!,merged,extension,coverage,ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();destination=Path.ChangeExtension(destination,extension);File.Move(merged,destination,true);
            if(request.RetainedMedia is { } retained)
            {
                await retained.CaptureAsync(videoPath,"video",video.MediaSegments[0].Initialization is null?"ts":"mp4",null,ct).ConfigureAwait(false);
                await retained.CaptureAsync(audioPath,"audio",audio.MediaSegments[0].Initialization is null?"ts":"m4a",null,ct).ConfigureAwait(false);
            }
            var clocks=vr.Timing.OriginalPeriodStarts.Count>0?vr.Timing.OriginalPeriodStarts:[await FfmpegTool.ReadStartTimeAsync(request.FfmpegPath!,videoPath,ct).ConfigureAwait(false)];
            return new(destination,vr.Playlist,ar.Playlist,coverage,clocks,end);
        }
        finally{Delete(videoPath);Delete(audioPath);Delete(merged);}
    }
    private static void Delete(string path){try{File.Delete(path);}catch(IOException){}catch(UnauthorizedAccessException){}}
}
