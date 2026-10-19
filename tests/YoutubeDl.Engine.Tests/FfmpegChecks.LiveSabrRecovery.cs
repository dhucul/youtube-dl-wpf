using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Protocols.Sabr;
using YoutubeDl.Engine.Providers;
internal static partial class FfmpegChecks
{
    internal static IEnumerable<(string Name,Func<Task> Test)> LiveSabrRecoveryMediaChecks(string ffmpeg)=>new[]{"live","live-cancel","live-payload","live-missing","live-no-continue","sabr","sabr-payload","sabr-facts","sabr-denied","sabr-no-continue"}.Select(mode=>("Live SABR recovery media "+mode,(Func<Task>)(()=>LiveSabrRecoveryMedia(ffmpeg,mode)))).Concat(new[]{"hls","offset","ended","dash"}.Select(mode=>("Live SABR recovery paired "+mode,(Func<Task>)(()=>AdaptiveLiveMedia(ffmpeg,mode,true)))));
    private static async Task LiveSabrRecoveryMedia(string ffmpeg,string mode)
    {
        using var files=new Files();var source=await RecoveryFixture.CreateAsync(files.Directory,ffmpeg,"hls");var sabr=mode.StartsWith("sabr");var provider=new LiveSabrProvider(sabr);var calls=0;var resumed=false;using var stop=new CancellationTokenSource();
        var tracks=new Dictionary<SabrFormatId,byte[]>();var references=new Dictionary<string,string>();
        if(sabr)foreach(var role in new[]{"video","audio"})
        {
            var path=Path.Combine(files.Directory,role+".mp4");await Run(ffmpeg,["-v","error","-y","-f","lavfi","-i",role=="video"?"testsrc2=size=128x72:rate=12":"sine=frequency=720:sample_rate=48000","-t","3","-c",role=="video"?"libx264":"aac","-movflags","+frag_keyframe+empty_moov+default_base_moof",path]);tracks[role=="video"?SabrFixture.Video:SabrFixture.Audio]=await File.ReadAllBytesAsync(path);references[role]=path;
        }
        NativeEngine Engine()=>new([provider],_=>AdaptiveAccountChecks.Http(async(message,ct)=>
        {
            if(sabr)
            {
                calls++;if(!resumed&&calls>1)return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable){Content=new StringContent("interrupted")};
                using var body=new MemoryStream();SabrFixture.Status(body,resumed&&mode=="sabr-denied"?3U:2U);byte id=0;
                foreach(var pair in tracks)
                {
                    if(!resumed&&pair.Key==SabrFixture.Audio)continue;var offset=0;while(!pair.Value.AsSpan(offset+4,4).SequenceEqual("moof"u8))offset+=checked((int)BinaryPrimitives.ReadUInt32BigEndian(pair.Value.AsSpan(offset,4)));
                    SabrFixture.Metadata(body,pair.Key,1,3000);SabrFixture.Segment(body,pair.Key,++id,0,pair.Value[..offset]);var bytes=pair.Value[offset..].ToArray();if(resumed&&mode=="sabr-payload")bytes[^1]^=1;
                    SabrFixture.Segment(body,pair.Key,++id,1,bytes,new(0,resumed&&mode=="sabr-facts"?2999:3000,1000));
                }
                return SabrFixture.Response(body.ToArray());
            }
            var name=Path.GetFileName(message.RequestUri!.AbsolutePath);
            if(name=="manifest.m3u8")
            {
                if(resumed&&mode=="live-missing")return AdaptiveAccountChecks.Text("#EXTM3U\n#EXT-X-MEDIA-SEQUENCE:2\n#EXTINF:1,\nchunk2.m4s\n#EXT-X-ENDLIST\n");
                return AdaptiveAccountChecks.Text(Encoding.UTF8.GetString(source.Assets[name]).Replace("#EXT-X-PLAYLIST-TYPE:VOD","#EXT-X-PLAYLIST-TYPE:EVENT").Replace("#EXT-X-ENDLIST",""));
            }
            calls++;if(!resumed&&calls==3){if(mode=="live-cancel")stop.Cancel();return AdaptiveAccountChecks.Text("interrupted",HttpStatusCode.ServiceUnavailable);}
            var data=source.Assets[name].ToArray();if(resumed&&mode=="live-payload"&&name=="chunk0.m4s")data[^1]^=1;var range=message.Headers.Range?.Ranges.SingleOrDefault();
            var response=new HttpResponseMessage(range is null?HttpStatusCode.OK:HttpStatusCode.PartialContent){Content=new ByteArrayContent(range is null?data:data[(int)range.From!.Value..((int)range.To!.Value+1)])};
            response.Headers.ETag=new("\""+Convert.ToHexString(SHA256.HashData(data))+"\"");if(range is not null)response.Content.Headers.ContentRange=new(range.From!.Value,range.To!.Value,data.Length);await Task.CompletedTask;return response;
        }));
        var output=Path.Combine(files.Directory,"recording.mp4");var archive=Path.Combine(files.Directory,"archive.txt");await File.WriteAllTextAsync(output,"prior media");await File.WriteAllTextAsync(archive,"prior archive\n");
        var request=new NativeDownloadRequest{Url=new("https://recovery-live.example/media"),OutputDirectory=files.Directory,OutputTemplate="recording.%(ext)s",FfmpegPath=ffmpeg,Container="mp4",LiveDurationSeconds=sabr?null:3,LiveFromStart=!sabr,KeepOriginal=true,AddMetadata=true,Retries=0,FragmentRetries=0,DownloadArchiveFile=archive,FormatSelector=sabr?"137+140":""};var callbacks=0;
        try{var failed=await Engine().DownloadAsync(request,null,null,stop.Token,_=>callbacks++);AdaptiveRecoveryChecks.Assert(!failed.Succeeded);}catch(Exception error)when(error is IOException or HttpRequestException or OperationCanceledException){}
        AdaptiveRecoveryChecks.Assert(callbacks==0&&await File.ReadAllTextAsync(output)=="prior media"&&await File.ReadAllTextAsync(archive)=="prior archive\n"&&Directory.GetFiles(files.Directory,"timeline.json",SearchOption.AllDirectories).Length>0);
        resumed=true;calls=0;request=request with {NoContinue=mode.EndsWith("no-continue")};var failure=mode is "live-payload" or "live-missing" or "sabr-payload" or "sabr-facts" or "sabr-denied";
        try
        {
            var result=await Engine().DownloadAsync(request,null,null,default,_=>callbacks++);AdaptiveRecoveryChecks.Assert(!failure&&result.Succeeded);
            foreach(var file in result.OutputPaths)await Run(ffmpeg,["-v","error","-xerror","-i",file,"-map","0:v?","-map","0:a?","-f","null","-"],true);
            foreach(var role in new[]{"video","audio"}){var actual=await RecoveryPackets(ffmpeg,output,role);var expected=await RecoveryPackets(ffmpeg,sabr?references[role]:source.References[role],role);AdaptiveRecoveryChecks.Assert(actual.AsEnumerable().SequenceEqual(expected));}
            AdaptiveRecoveryChecks.Assert(callbacks==result.OutputPaths.Count&&Directory.GetDirectories(files.Directory,".native-download-*").Length==0);
            var before=calls;var done=callbacks;var repeat=await Engine().DownloadAsync(request,null,null,default,_=>callbacks++);AdaptiveRecoveryChecks.Assert(repeat.Succeeded&&repeat.Skipped&&calls==before&&callbacks==done);
        }
        catch(Exception error)when(failure&&error is InvalidDataException or NotSupportedException or IOException){AdaptiveRecoveryChecks.Assert(callbacks==0&&await File.ReadAllTextAsync(output)=="prior media"&&await File.ReadAllTextAsync(archive)=="prior archive\n");}
    }
    private sealed class LiveSabrProvider(bool sabr):IMediaProvider
    {
        public MediaProviderInfo Info=>new("fixture-live","Recovery live fixture",0);public bool CanHandle(Uri url)=>url.Host=="recovery-live.example";
        public DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request)=>new("fixture-live","source");
        public Task<PageMetadata> ExtractAsync(NativeDownloadRequest request,YoutubeDl.Engine.Http.MediaHttp http,CancellationToken ct)
        {
            var session=new SabrPlaybackSession(SabrFixture.VideoId,SabrFixture.Endpoint,5,"fixture",[1,2,3]);return Task.FromResult(new PageMetadata{Id="source",Extractor="fixture-live",Title="Recovered recording",DurationSeconds=3,IsLive=!sabr,
                Media=sabr?[new(){Id="137",Url=SabrFixture.Endpoint,Kind=MediaKind.Sabr,Extension="mp4",HasVideo=true,HasAudio=false,Sabr=new(session,SabrFixture.Video)},new(){Id="140",Url=SabrFixture.Endpoint,Kind=MediaKind.Sabr,Extension="m4a",HasVideo=false,HasAudio=true,Sabr=new(session,SabrFixture.Audio)}]:[new(){Id="hls",Url=new("https://cdn.example/manifest.m3u8"),Kind=MediaKind.Hls,Extension="m3u8",HasVideo=true,HasAudio=true}]});
        }
    }
}



