using System.Diagnostics;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Media;
using YoutubeDl.Engine.Protocols.Sabr;

namespace YoutubeDl.Engine.Download;

internal static class SabrDownloader
{
    private sealed record SavedSegment(SabrFormatId Format,uint Number,bool Initialization,long StartMs,long DurationMs,SabrTimeRange? TimeRange);
    public static async Task<string> DownloadAsync(MediaHttp http, MediaCandidate primary, MediaCandidate? audio, string destination,
        NativeDownloadRequest request, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var first = primary.Sabr ?? throw new InvalidDataException("The SABR format has no playback context.");
        if (audio is not null && (audio.Sabr is null || audio.Sabr.Session != first.Session)) throw new NotSupportedException("A SABR pair must share one playback context.");
        if (audio is not null && (string.IsNullOrWhiteSpace(request.FfmpegPath) || !File.Exists(request.FfmpegPath))) throw new InvalidOperationException("ffmpeg is required to combine SABR tracks.");
        if (audio is not null && !string.IsNullOrWhiteSpace(request.MergeOutputFormat)) FfmpegTool.RequireProbe(request.FfmpegPath, "choose the SABR merge container");
        using var controlled = http.AllowAutoRedirect ? http.CreateWithoutRedirects() : null;
        var transport = controlled ?? http;
        var directory = Path.GetDirectoryName(Path.GetFullPath(destination))!; Directory.CreateDirectory(directory);
        var prefix = Path.Combine(directory, ".sabr-" + Guid.NewGuid().ToString("N"));
        var primaryPath = prefix + ".primary." + (primary.Extension ?? "mp4"); var audioPath = prefix + ".audio." + (audio?.Extension ?? "m4a");
        var merged = prefix + ".merged." + Path.GetExtension(destination).TrimStart('.');
        var scope=System.Text.Json.JsonSerializer.Serialize(new{request.AdaptiveRecoveryScope,first.Session.VideoId,first.Session.ClientName,first.Session.ClientVersion,Primary=first.Format,Audio=audio?.Sabr?.Format,primary.VideoCodec,primary.AudioCodec,primary.Height,primary.FramesPerSecond});
        using var recovery=!request.NoContinue&&request.AdaptiveRecoveryScope is not null?await TimelineCheckpoint.OpenAsync(transport,destination,scope,ct).ConfigureAwait(false):null;
        var saved=recovery?.Records.ToArray()??[];
        try
        {
            await using (var primaryStream = new FileStream(primaryPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 65536, FileOptions.Asynchronous))
            await using (var audioStream = audio is null ? null : new FileStream(audioPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 65536, FileOptions.Asynchronous))
            {
                var streams = new Dictionary<SabrFormatId, Stream> { [first.Format] = primaryStream };
                if (audio is not null) streams.Add(audio.Sabr!.Format, audioStream!);
                var sink = new SabrTrackSink(streams){ValidateCheckpoint=recovery is null?null:async(segment,token)=>
                {
                    var identity=System.Text.Json.JsonSerializer.Serialize(new{segment.Format,segment.Number,segment.Initialization});
                    var facts=System.Text.Json.JsonSerializer.Serialize(new{segment.StartMs,segment.DurationMs,segment.TimeRange});
                    var metadata=System.Text.Json.JsonSerializer.Serialize(new SavedSegment(segment.Format,segment.Number,segment.Initialization,segment.StartMs,segment.DurationMs,segment.TimeRange));
                    var path=prefix+".checkpoint";try{await recovery.CaptureAsync(identity,facts,first.Session.Endpoint,null,segment.Data.ToArray(),path,async(target,cancel)=>{if(File.Exists(target))File.Delete(target);await AdaptiveFragmentCheckpoint.DurableWriteAsync(target,segment.Data.ToArray(),cancel).ConfigureAwait(false);return null;},token,metadata).ConfigureAwait(false);}finally{try{File.Delete(path);}catch(IOException){}}
                }};
                var restored=false;
                async Task Restore(SabrControlState control,SabrSegmentAssembler assembler,CancellationToken token)
                {
                    if(restored||saved.Length==0||saved.Any(row=>row.Metadata is null))return;
                    // Startup status 2 may expose a short preview; it must never authorize an offline full prefix.
                    if(control.ProtectionStatus is not (0 or 1)||sink.SelectedFormats.Any(format=>sink.LastSegment(format)==0||!control.LastSegments.ContainsKey(format)||!control.EndTimesMs.ContainsKey(format)))return;
                    var rows=saved.Select(row=>(Row:row,Segment:System.Text.Json.JsonSerializer.Deserialize<SavedSegment>(row.Metadata!)??throw new InvalidDataException("The saved SABR descriptor is invalid."))).ToArray();
                    foreach(var format in sink.SelectedFormats)
                    {
                        var track=rows.Where(row=>row.Segment.Format==format).ToArray();
                        if(track.Length==0)continue; // A stopped process may not yet have received this freshly authorized track.
                        if(!track[0].Segment.Initialization||track.Skip(1).Any(row=>row.Segment.Initialization)||
                            track.Skip(1).Select(row=>row.Segment.Number).Where((number,index)=>number!=index+1).Any())
                            throw new InvalidDataException("The saved SABR prefix is not a complete contiguous track.");
                        foreach(var row in track)
                        {
                            var descriptor=row.Segment;
                            var identity=System.Text.Json.JsonSerializer.Serialize(new{descriptor.Format,descriptor.Number,descriptor.Initialization});
                            var facts=System.Text.Json.JsonSerializer.Serialize(new{descriptor.StartMs,descriptor.DurationMs,descriptor.TimeRange});
                            if(AdaptiveFragmentCheckpoint.Hash(identity)!=row.Row.Identity||AdaptiveFragmentCheckpoint.Hash(facts)!=row.Row.Facts||
                                descriptor.Number>control.LastSegments[format]||!descriptor.Initialization&&descriptor.StartMs+ (decimal)descriptor.DurationMs>control.EndTimesMs[format]+1m)
                                throw new InvalidDataException("The saved SABR prefix exceeds or changed the fresh track.");
                            if(descriptor.Initialization||descriptor.Number<=sink.LastSegment(format))continue;
                            var path=prefix+".restore";
                            try
                            {
                                await recovery!.CopySavedAsync(row.Row,path,token).ConfigureAwait(false);
                                if(new FileInfo(path).Length>32*1024*1024)throw new InvalidDataException("The saved SABR segment exceeds its limit.");
                                var bytes=await File.ReadAllBytesAsync(path,token).ConfigureAwait(false);
                                var segment=new SabrSegment(format,descriptor.Number,false,descriptor.StartMs,descriptor.DurationMs,bytes,descriptor.TimeRange);
                                await sink.AcceptAsync(segment,token).ConfigureAwait(false);assembler.RememberRestored(segment);
                            }
                            finally{try{File.Delete(path);}catch(IOException){}}
                        }
                    }
                    if(rows.Any(row=>!sink.SelectedFormats.Contains(row.Segment.Format)))throw new InvalidDataException("The saved SABR prefix contains an unselected track.");
                    restored=true;
                }
                var timer = Stopwatch.StartNew();
                var current=first.Session;
                async Task<Uri> Renew(CancellationToken token){current=await current.Renewal!(current,token).ConfigureAwait(false);return current.Endpoint;}
                await new SabrHttpSession(transport, first.Session.UserAgent) { BufferSize = request.BufferSize, NoResizeBuffer = request.NoResizeBuffer,Renew=current.Renewal is null?null:Renew,RestorePrefix=Restore }.DownloadAsync(first.Session.Endpoint, first.Session.VideoId, sink, (control, _) =>
                {
                    progress?.Report(new DownloadProgress { DownloadedBytes = sink.TotalBytes });
                    control.SeedSession(current.InitialPlaybackCookie, current.InitialContexts);
                    var playback=new SabrRequest{ClientName=current.ClientName,ClientVersion=current.ClientVersion,PlaybackConfiguration=current.Configuration,ProofToken=current.ProofToken,ClientInfo=current.ClientInfo,ClientAbrState=current.ClientAbrState,
                        Width=primary.Width is >0?primary.Width.Value:1280,Height=primary.Height is >0?primary.Height.Value:720,
                        VideoFormats=primary.IsAudioOnly?[]:[first.Format],AudioFormats=primary.IsAudioOnly?[first.Format]:audio is null?[]:[audio.Sabr!.Format],
                        AudioTrack=primary.IsAudioOnly?first.AudioTrack:audio?.Sabr?.AudioTrack};
                    return playback with { PlaybackCookie = control.PlaybackCookie, Contexts = control.OutgoingContexts, ElapsedTimeMs = timer.ElapsedMilliseconds,
                        PlaybackTimeMs = sink.BufferedRanges.Count == 0 ? 0 : sink.BufferedRanges.Min(range => range.StartMs + range.DurationMs) };
                }, ct, allowStartupPlayback: true, retries: request.Retries, limiter: new ByteRateLimiter(ByteRateLimiter.Parse(request.RateLimit))).ConfigureAwait(false);
                await primaryStream.FlushAsync(ct).ConfigureAwait(false); primaryStream.Flush(true);
                if (audioStream is not null) { await audioStream.FlushAsync(ct).ConfigureAwait(false); audioStream.Flush(true); }
                progress?.Report(new DownloadProgress { DownloadedBytes = sink.TotalBytes, TotalBytes = sink.TotalBytes });
            }
            if (audio is not null)
            {
                if (!string.IsNullOrWhiteSpace(request.MergeOutputFormat))
                {
                    var extension = await FfmpegTool.SelectMergeExtensionAsync(request.FfmpegPath!, primaryPath, audioPath, request.MergeOutputFormat, Path.GetExtension(destination).TrimStart('.'), ct).ConfigureAwait(false);
                    destination = Path.ChangeExtension(destination, extension); merged = Path.ChangeExtension(merged, extension);
                }
                await FfmpegTool.MergeAsync(request.FfmpegPath!, primaryPath, audioPath, merged, ct).ConfigureAwait(false);
                if (!File.Exists(merged) || new FileInfo(merged).Length == 0) throw new InvalidDataException("The SABR merge did not produce media.");
                if (request.RetainedMedia is { } retained)
                {
                    await retained.CaptureAsync(primaryPath, "video", primary.Extension ?? "mp4", primary, ct).ConfigureAwait(false);
                    await retained.CaptureAsync(audioPath, "audio", audio.Extension ?? "m4a", audio, ct).ConfigureAwait(false);
                }
            }
            ct.ThrowIfCancellationRequested();
            File.Move(audio is null ? primaryPath : merged, destination, overwrite: true);
            return destination;
        }
        finally
        {
            foreach (var path in new[] { primaryPath, audioPath, merged })
                try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
