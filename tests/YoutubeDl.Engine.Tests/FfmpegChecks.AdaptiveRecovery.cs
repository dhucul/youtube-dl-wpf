using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;
internal static partial class FfmpegChecks
{
    internal static IEnumerable<(string Name,Func<Task> Test)> AdaptiveRecoveryMediaChecks(string ffmpeg)=>
        new[]{"hls","hls-range","hls-aes","hls-no-continue","hls-corrupt","hls-entity","hls-cancel","hls-concurrent","hls-no-validator","hls-caption","dash","dash-cancel","dash-tokens","dash-json","dash-indexed","collection-removed","collection-unavailable","collection-changed","process"}.Select(mode=>("Adaptive recovery media "+mode,(Func<Task>)(()=>AdaptiveRecoveryMedia(ffmpeg,mode))));
    private static async Task AdaptiveRecoveryMedia(string ffmpeg,string mode)
    {
        using var files=new Files();var fixture=await RecoveryFixture.CreateAsync(files.Directory,ffmpeg,mode);var archive=Path.Combine(files.Directory,"archive.txt");var output=Path.Combine(files.Directory,mode.StartsWith("collection-")?"002 - recording.mp4":"recording.mp4");
        await File.WriteAllTextAsync(output,"prior media");await File.WriteAllTextAsync(archive,"prior archive\n");
        var request=RecoveryRequest(fixture,ffmpeg,archive);NativeEngine Engine()=>new([fixture],_=>fixture.Http());var callbacks=new List<string>();
        if(mode.StartsWith("collection-")){var info=await Engine().InspectAsync(request,default);request=request with {PlaylistSnapshot=info.PlaylistSnapshot};}
        using var stop=new CancellationTokenSource();fixture.Stop=mode.EndsWith("cancel")?stop:null;
        if(mode=="process")
        {
            var marker=Path.Combine(files.Directory,"worker-ready");var start=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true};
            foreach(var arg in new[]{typeof(FfmpegChecks).Assembly.Location,"--recovery-worker",files.Directory,ffmpeg})start.ArgumentList.Add(arg);
            using var process=Process.Start(start)!;var ready=false;
            try
            {
                for(var attempt=0;attempt<200&&!process.HasExited;attempt++){try{ready=File.Exists(marker)&&int.TryParse(await File.ReadAllTextAsync(marker),out var pid)&&pid==process.Id;}catch(IOException){}if(ready)break;await Task.Delay(50);}
                AdaptiveRecoveryChecks.Assert(ready,"Worker did not persist a completed fragment before termination.");process.Kill(entireProcessTree:true);await process.WaitForExitAsync();
            }
            finally{if(!process.HasExited){process.Kill(entireProcessTree:true);await process.WaitForExitAsync();}}
            AdaptiveRecoveryChecks.Assert(callbacks.Count==0&&await File.ReadAllTextAsync(output)=="prior media"&&await File.ReadAllTextAsync(archive)=="prior archive\n");
        }
        else
        {
            fixture.Interrupt=true;NativeDownloadResult? failed=null;
            try{failed=await Engine().DownloadAsync(request,null,null,stop.Token,callbacks.Add);}catch(Exception e)when(e is IOException or HttpRequestException or OperationCanceledException){}
            AdaptiveRecoveryChecks.Assert(failed?.Succeeded!=true&&callbacks.Count==0&&await File.ReadAllTextAsync(output)=="prior media"&&await File.ReadAllTextAsync(archive)=="prior archive\n");
        }
        var receipts=Directory.GetFiles(files.Directory,"fragment-*.bin.json",SearchOption.AllDirectories);AdaptiveRecoveryChecks.Assert(mode=="hls-no-validator"||receipts.Length>0,"No durable completed fragment was retained.");
        foreach(var receipt in Directory.GetFiles(files.Directory,"*.json",SearchOption.AllDirectories)){var json=await File.ReadAllTextAsync(receipt);AdaptiveRecoveryChecks.Assert(!json.Contains("https://")&&!json.Contains("fixture-secret"));}
        if(mode.StartsWith("collection-"))
        {
            fixture.MemberChange=mode;fixture.Interrupt=false;fixture.Probes=fixture.Full=0;
            try{var denied=await Engine().DownloadAsync(request,null,null,default,callbacks.Add);AdaptiveRecoveryChecks.Assert(!denied.Succeeded);}catch(InvalidDataException){}
            AdaptiveRecoveryChecks.Assert(fixture.Full==0&&fixture.Probes==0&&callbacks.Count==0&&await File.ReadAllTextAsync(output)=="prior media"&&await File.ReadAllTextAsync(archive)=="prior archive\n");fixture.MemberChange="";
        }
        if(mode=="hls-corrupt"){var data=receipts.First()[..^5];await File.WriteAllTextAsync(data,"corrupted checkpoint");}
        fixture.Interrupt=false;fixture.Stop=null;fixture.Generation=1;fixture.Probes=fixture.Full=fixture.Rejected=0;fixture.CaptionAllowed=true;fixture.Revision=mode=="hls-entity"?1:0;
        request=request with {NoContinue=mode=="hls-no-continue",ConcurrentFragments=mode=="hls-concurrent"?4:1};
        var result=await Engine().DownloadAsync(request,null,null,default,callbacks.Add);
        AdaptiveRecoveryChecks.Assert(result.Succeeded&&callbacks.SequenceEqual(result.NewOutputPaths??result.OutputPaths),"Recovery failed: "+result.Error);
        if(mode is "hls-no-continue" or "hls-no-validator")AdaptiveRecoveryChecks.Assert(fixture.Probes==0);else AdaptiveRecoveryChecks.Assert(fixture.Probes>0,"Saved fragments were not revalidated.");
        if(mode=="hls-entity")AdaptiveRecoveryChecks.Assert(fixture.Rejected>0,"Changed entities reused stale bytes.");
        foreach(var file in result.OutputPaths)await Run(ffmpeg,["-v","error","-xerror","-i",file,"-map","0:v?","-map","0:a?","-f","null","-"],true);
        using var probe=JsonDocument.Parse(await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!,"ffprobe.exe"),["-v","error","-count_frames","-show_entries","stream=codec_type,nb_read_frames","-of","json",output]));
        AdaptiveRecoveryChecks.Assert(probe.RootElement.GetProperty("streams").EnumerateArray().Single(s=>s.GetProperty("codec_type").GetString()=="video").GetProperty("nb_read_frames").GetString()=="36");
        foreach(var role in new[]{"video","audio"})
        {
            var before=await RecoveryPackets(ffmpeg,fixture.References[role],role);var after=await RecoveryPackets(ffmpeg,output,role);AdaptiveRecoveryChecks.Assert(before.Length>0&&before.SequenceEqual(after),"Recovery changed ordered "+role+" packets.");
        }
        AdaptiveRecoveryChecks.Assert(Directory.GetDirectories(files.Directory,".native-download-*").Length==0,"Completed publication retained recovery staging.");
        var full=fixture.Full;var probes=fixture.Probes;var completed=callbacks.Count;var repeat=await Engine().DownloadAsync(request,null,null,default,callbacks.Add);AdaptiveRecoveryChecks.Assert(repeat.Succeeded&&repeat.Skipped&&full==fixture.Full&&probes==fixture.Probes&&completed==callbacks.Count);
    }
    internal static async Task<int> AdaptiveRecoveryWorker(string directory,string ffmpeg)
    {
        var fixture=await RecoveryFixture.LoadHlsAsync(directory,"process");var request=RecoveryRequest(fixture,ffmpeg,Path.Combine(directory,"archive.txt"));
        var engine=new NativeEngine([fixture],_=>fixture.Http());var progress=new RecoveryWorkerProgress(Path.Combine(directory,"worker-ready"));
        await engine.DownloadAsync(request,progress,null,default,_=>throw new InvalidOperationException("Terminated worker published a callback."));return 1;
    }
    private sealed class RecoveryWorkerProgress(string path):IProgress<DownloadProgress>
    {
        private bool _saved;
        public void Report(DownloadProgress value){if(_saved||value.DownloadedBytes<=4096)return;_saved=true;File.WriteAllText(path+".tmp",Environment.ProcessId.ToString());File.Move(path+".tmp",path);Thread.Sleep(Timeout.Infinite);}
    }
    private static NativeDownloadRequest RecoveryRequest(RecoveryFixture fixture,string ffmpeg,string archive)=>new(){Url=new(fixture.Mode.StartsWith("collection-")?"https://recovery.example/list":"https://recovery.example/media"),OutputDirectory=fixture.Directory,OutputTemplate=fixture.Mode.StartsWith("collection-")?"%(playlist_index)03d - recording.%(ext)s":"recording.%(ext)s",FfmpegPath=ffmpeg,Container=fixture.Mode=="hls-aes"?"mp4":"",KeepOriginal=true,AddMetadata=true,DownloadArchiveFile=archive,Retries=0,FragmentRetries=0,ConcurrentFragments=1,PlaylistItems=fixture.Mode.StartsWith("collection-")?"2":"",WriteSubs=fixture.Mode=="hls-caption",EmbedSubs=fixture.Mode=="hls-caption",SubLanguages="en"};
    private static async Task<string[]> RecoveryPackets(string ffmpeg,string file,string role)
    {using var doc=JsonDocument.Parse(await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!,"ffprobe.exe"),["-v","error","-select_streams",role=="video"?"v:0":"a:0","-show_packets","-show_data_hash","sha256","-show_entries","packet=data_hash","-of","json",file]));return doc.RootElement.GetProperty("packets").EnumerateArray().Select(p=>p.GetProperty("data_hash").GetString()!).ToArray();}
    private sealed class RecoveryFixture:IMediaProvider
    {
        internal string Directory="",Mode="",MemberChange="";internal Dictionary<string,byte[]> Assets=new();internal Dictionary<string,string> References=new();internal int Full,Probes,Rejected,Generation,Revision;internal bool Interrupt,CaptionAllowed;internal CancellationTokenSource? Stop;
        public MediaProviderInfo Info=>new("fixture","Recovery fixture",0);public bool CanHandle(Uri url)=>url.Host=="recovery.example";
        public DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request)=>request.Url.AbsolutePath=="/list"?null:new("fixture","video");
        internal MediaHttp Http()=>AdaptiveAccountChecks.Http((message,ct)=>{
            AdaptiveRecoveryChecks.Assert(message.Headers.Authorization is null);var name=Path.GetFileName(message.RequestUri!.AbsolutePath);if(name=="playlist.json")name="manifest.json";
            if(name=="caption.vtt")return Task.FromResult(AdaptiveAccountChecks.Text(CaptionAllowed?PlaylistCaptionChecks.Vtt:"denied",CaptionAllowed?HttpStatusCode.OK:HttpStatusCode.Forbidden));
            if(!Assets.TryGetValue(name,out var bytes))throw new InvalidDataException("Unexpected recovery fixture asset: "+name);
            if(name.EndsWith(".m3u8")||name.EndsWith(".mpd")||name.EndsWith(".json"))return Task.FromResult(AdaptiveAccountChecks.Text(Encoding.UTF8.GetString(bytes).Replace("GEN",Generation.ToString())));
            var range=message.Headers.Range?.Ranges.SingleOrDefault();var validation=message.Headers.IfMatch.Count>0&&range is {From:{ } from,To:{ } to}&&from==to;var tag=new EntityTagHeaderValue("\""+Revision+"-"+Convert.ToHexString(SHA256.HashData(bytes))+"\"");
            if(validation){Probes++;if(message.Headers.IfMatch.Single().ToString()!=tag.ToString()){Rejected++;return Task.FromResult(AdaptiveAccountChecks.Text("changed",HttpStatusCode.PreconditionFailed));}}
            else if(name!="key"){Full++;if(Interrupt&&Mode!="hls-caption"&&Full==(Mode=="dash-indexed"?4:3)){Stop?.Cancel();return Task.FromResult(AdaptiveAccountChecks.Text("interrupted",HttpStatusCode.ServiceUnavailable));}}
            var response=new HttpResponseMessage(range is null?HttpStatusCode.OK:HttpStatusCode.PartialContent){Content=new ByteArrayContent(range is null?bytes:bytes[(int)range.From!.Value..((int)range.To!.Value+1)])};
            if(Mode!="hls-no-validator")response.Headers.ETag=tag;if(range is not null)response.Content.Headers.ContentRange=new(range.From!.Value,range.To!.Value,bytes.Length);return Task.FromResult(response);
        });
        public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request,MediaHttp http,CancellationToken ct)
        {
            if(request.Url.AbsolutePath=="/list")
            {
                var rows=new List<NativePlaylistEntry>{new(1,"other","Other",new("https://recovery.example/item/other"),3,null,true,ExpectedArchiveIdentity:new("fixture","other"),CollectionMemberId:"member-other")};
                if(MemberChange!="collection-removed")rows.Add(new(2,MemberChange=="collection-changed"?"changed":"video","Selected",new("https://recovery.example/item/selected"),3,null,MemberChange!="collection-unavailable",ExpectedArchiveIdentity:new("fixture",MemberChange=="collection-changed"?"changed":"video"),CollectionMemberId:"member-selected"));
                return new(){Id="list",PlaylistId="list",Extractor="fixture",Title="Collection",IsPlaylist=true,Entries=rows,RevalidateCollectionMembership=true};
            }
            var page=Page();var lease=http.SessionState("recovery-fixture",()=>new RenewableMediaAuthorization(page,_=>Task.FromResult(Page()),_=>false,(uri,token)=>http.ReadPlaybackManifestAsync(uri,_=>false,null,token)));
            await Task.CompletedTask;return page with {PlaybackAuthorization=lease};
        }
        private PageMetadata Page()
        {
            var dash=Mode.StartsWith("dash");var url=new Uri(Mode=="dash-json"?"https://vod.example.vimeocdn.com/"+MixedCollectionChecks.Clip+"/v2/playlist/av/primary/playlist.json?signature=fixture-secret-GEN".Replace("GEN",Generation.ToString()):"https://cdn.example/"+(dash?"manifest.mpd":"manifest.m3u8")+"?signature=fixture-secret-"+Generation);
            return new(){Id="video",Extractor="fixture",Title="Recovery fixture",DurationSeconds=3,Media=[new(){Id="adaptive",Url=url,Kind=dash?MediaKind.Dash:MediaKind.Hls,Extension=dash?"mpd":"m3u8",Source="fixture",HasVideo=true,HasAudio=true}],
                Subtitles=Mode=="hls-caption"?[new(){Url=new("https://cdn.example/caption.vtt"),Language="en",Extension="vtt",ResourceIdentity="fixture:caption"}]:[]};
        }
        internal static async Task<RecoveryFixture> LoadHlsAsync(string directory,string mode)
        {
            var fixture=new RecoveryFixture{Directory=directory,Mode=mode};foreach(var path in System.IO.Directory.GetFiles(Path.Combine(directory,"assets")))fixture.Assets[Path.GetFileName(path)]=await File.ReadAllBytesAsync(path);return fixture;
        }
        internal static async Task<RecoveryFixture> CreateAsync(string directory,string ffmpeg,string mode)
        {
            var assetDirectory=Path.Combine(directory,"assets");System.IO.Directory.CreateDirectory(assetDirectory);var source=Path.Combine(directory,"source.mp4");
            await Run(ffmpeg,["-v","error","-y","-f","lavfi","-i","testsrc2=size=128x72:rate=12","-f","lavfi","-i","sine=frequency=620:sample_rate=48000","-t","3","-c:v","libx264","-preset","ultrafast","-g","12","-sc_threshold","0","-c:a","aac",source]);
            var dash=mode.StartsWith("dash");var indexed=mode=="dash-indexed";
            if(dash&&!indexed)await Run(ffmpeg,["-v","error","-y","-i",source,"-c","copy","-f","dash","-seg_duration","1","-use_template","1","-use_timeline","1","-init_seg_name","init-$RepresentationID$.m4s","-media_seg_name","chunk-$RepresentationID$-$Number%05d$.m4s",Path.Combine(assetDirectory,"manifest.mpd")]);
            else if(!indexed)await Run(ffmpeg,mode=="hls-aes"?["-v","error","-y","-i",source,"-c","copy","-f","hls","-hls_time","1","-hls_playlist_type","vod","-hls_segment_filename",Path.Combine(assetDirectory,"chunk%d.ts"),Path.Combine(assetDirectory,"manifest.m3u8")]:["-v","error","-y","-i",source,"-c","copy","-f","hls","-hls_time","1","-hls_playlist_type","vod","-hls_segment_type","fmp4","-hls_fmp4_init_filename","init.mp4","-hls_segment_filename",Path.Combine(assetDirectory,"chunk%d.m4s"),Path.Combine(assetDirectory,"manifest.m3u8")]);
            if(indexed)
            {
                var representations=new List<string>();foreach(var role in new[]{"video","audio"})
                {
                    var file=Path.Combine(assetDirectory,role+".mp4");await Run(ffmpeg,["-v","error","-y","-i",source,"-map",role=="video"?"0:v:0":"0:a:0","-c","copy","-movflags","+frag_keyframe+empty_moov+default_base_moof+global_sidx","-frag_duration","1000000",file]);var bytes=await File.ReadAllBytesAsync(file);var offset=0;
                    while(Encoding.ASCII.GetString(bytes,offset+4,4)!="sidx")offset+=checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset,4)));var length=BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset,4));
                    representations.Add("<AdaptationSet mimeType='"+role+"/mp4'><Representation id='"+role+"' codecs='"+(role=="video"?"avc1.42001e":"mp4a.40.2")+"'><BaseURL>"+role+".mp4?signature=fixture-secret-GEN</BaseURL><SegmentBase indexRange='"+offset+"-"+(offset+length-1)+"'><Initialization range='0-"+(offset-1)+"'/></SegmentBase></Representation></AdaptationSet>");
                }
                await File.WriteAllTextAsync(Path.Combine(assetDirectory,"manifest.mpd"),"<MPD><Period>"+string.Join("",representations)+"</Period></MPD>");
            }
            var fixture=await LoadHlsAsync(directory,mode);
            if(dash)
            {
                var manifest=DashManifestParser.Parse(Encoding.UTF8.GetString(fixture.Assets["manifest.mpd"]),new("https://cdn.example/manifest.mpd"));
                foreach(var track in manifest.Representations)
                {
                    var role=track.IsVideo?"video":"audio";var reference=Path.Combine(directory,"reference-"+role+".mp4");var bytes=track.SegmentBase is not null?fixture.Assets[role+".mp4"]:(track.Initialization is not null?fixture.Assets[Path.GetFileName(track.Initialization.AbsolutePath)]:[]).Concat(track.Segments.SelectMany(s=>fixture.Assets[Path.GetFileName(s.Url.AbsolutePath)])).ToArray();await File.WriteAllBytesAsync(reference,bytes);fixture.References[role]=reference;
                }
                if(mode=="dash-json")
                {
                    object Track(DashRepresentation r)=>new{id=r.Id,base_url=r.IsVideo?"video/":"audio/",mime_type=r.MimeType,codecs=r.Codecs,bitrate=100000,height=72,framerate=12,duration=3,init_segment=Convert.ToBase64String(fixture.Assets[Path.GetFileName(r.Initialization!.AbsolutePath)]),init_segment_url="",segments=r.Segments.Select((s,i)=>new{start=3d*i/r.Segments.Count,end=3d*(i+1)/r.Segments.Count,url=Path.GetFileName(s.Url.AbsolutePath)+"?signature=fixture-secret-GEN",size=fixture.Assets[Path.GetFileName(s.Url.AbsolutePath)].Length}).ToArray()};
                    fixture.Assets["manifest.json"]=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{clip_id=MixedCollectionChecks.Clip,base_url="../../../remux/avf/",video=manifest.Representations.Where(r=>r.IsVideo).Select(Track),audio=manifest.Representations.Where(r=>r.IsAudio).Select(Track)}));
                }
                else fixture.Assets["manifest.mpd"]=Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(fixture.Assets["manifest.mpd"]).Replace(".m4s",".m4s?signature=fixture-secret-GEN"));
            }
            else
            {
                var playlist=HlsPlaylistParser.Parse(Encoding.UTF8.GetString(fixture.Assets["manifest.m3u8"]),new("https://cdn.example/manifest.m3u8"));var plain=(playlist.MediaSegments[0].Initialization is { } init?fixture.Assets[Path.GetFileName(init.Url.AbsolutePath)]:[]).Concat(playlist.MediaSegments.SelectMany(s=>fixture.Assets[Path.GetFileName(s.Url.AbsolutePath)])).ToArray();var raw=Path.Combine(directory,mode=="hls-aes"?"reference.ts":"reference.mp4");await File.WriteAllBytesAsync(raw,plain);
                var reference=Path.Combine(directory,"reference-final.mp4");await Run(ffmpeg,["-v","error","-y","-i",raw,"-c","copy",reference]);fixture.References["video"]=fixture.References["audio"]=reference;
                if(mode=="hls-range")
                {
                    var builder=new StringBuilder("#EXTM3U\n#EXT-X-TARGETDURATION:1\n");var offset=fixture.Assets["init.mp4"].Length;builder.Append("#EXT-X-MAP:URI=\"combined.mp4\",BYTERANGE=\""+offset+"@0\"\n");foreach(var segment in playlist.MediaSegments){var length=fixture.Assets[Path.GetFileName(segment.Url.AbsolutePath)].Length;builder.Append("#EXTINF:"+segment.DurationSeconds+",\n#EXT-X-BYTERANGE:"+length+"@"+offset+"\ncombined.mp4\n");offset+=length;}builder.Append("#EXT-X-ENDLIST\n");fixture.Assets["combined.mp4"]=plain;fixture.Assets["manifest.m3u8"]=Encoding.UTF8.GetBytes(builder.ToString());
                }
                if(mode=="hls-aes")
                {
                    var key=Enumerable.Range(0,16).Select(i=>(byte)i).ToArray();fixture.Assets["key"]=key;foreach(var segment in playlist.MediaSegments){var name=Path.GetFileName(segment.Url.AbsolutePath);using var aes=Aes.Create();aes.Key=key;var iv=new byte[16];BinaryPrimitives.WriteUInt64BigEndian(iv.AsSpan(8),segment.Sequence);aes.IV=iv;using var encrypt=aes.CreateEncryptor();fixture.Assets[name]=encrypt.TransformFinalBlock(fixture.Assets[name],0,fixture.Assets[name].Length);}
                    fixture.Assets["manifest.m3u8"]=Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(fixture.Assets["manifest.m3u8"]).Replace("#EXTM3U","#EXTM3U\n#EXT-X-KEY:METHOD=AES-128,URI=\"key\""));
                }
                // The terminated worker reads these same issuer assets from disk.
                foreach(var pair in fixture.Assets)await File.WriteAllBytesAsync(Path.Combine(assetDirectory,pair.Key),pair.Value);
            }
            return fixture;
        }
    }
}

