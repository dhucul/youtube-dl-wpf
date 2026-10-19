using System.Buffers.Binary;
using System.Net;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;
internal static partial class FfmpegChecks
{
    internal static IEnumerable<(string Name,Func<Task> Test)> MixedCollectionMediaChecks(string ffmpeg)=>
        new[]{"stable","reorder","removed","unavailable","changed","repeat","repeat-count","same-video","materialized","archive","cancel"}.Select(mode=>("Mixed collections media membership "+mode,(Func<Task>)(()=>MixedCollectionMedia(ffmpeg,mode))))
        .Concat(new[]{"json","renew","init-changed","denied","cancel"}.Select(mode=>("Mixed collections media Vimeo JSON "+mode,(Func<Task>)(()=>VimeoJsonMedia(ffmpeg,mode)))))
        .Concat(new[]{"jw","bc","jw-renew","bc-renew","wistia","series"}.Select(mode=>("Mixed collections media inspected provider "+mode,(Func<Task>)(()=>HostingCollectionMedia(ffmpeg,mode,true)))));
    private static async Task MixedCollectionMedia(string ffmpeg,string mode)
    {
        using var files=new Files();var source=Path.Combine(files.Directory,"source.mp4");await MixedSource(ffmpeg,source);var bytes=await File.ReadAllBytesAsync(source);using var stop=new CancellationTokenSource();
        var provider=new MembershipProvider(mode);var media=0;var engine=new NativeEngine([provider],_=>AdaptiveAccountChecks.Http((message,ct)=>{PlaylistCaptionChecks.Assert(message.RequestUri!.AbsolutePath=="/fresh/b.mp4"||mode is "repeat" or "same-video"&&message.RequestUri.AbsolutePath=="/fresh/a.mp4");media++;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)});}));
        var archive=Path.Combine(files.Directory,"archive.txt");var output=Path.Combine(files.Directory,"002 - recording.mp4");await File.WriteAllTextAsync(output,"prior output");await File.WriteAllTextAsync(archive,mode=="archive"?"fixture b\n":"prior archive\n");
        var request=new NativeDownloadRequest{Url=new("https://collection.example/list"),OutputDirectory=files.Directory,OutputTemplate="%(playlist_index)03d - recording.%(ext)s",PlaylistItems="2",DownloadArchiveFile=archive,FfmpegPath=ffmpeg,KeepOriginal=true,AddMetadata=true,Retries=0};
        var info=await engine.InspectAsync(request,default);PlaylistCaptionChecks.Assert(info.PlaylistSnapshot?.Count==2);request=request with {PlaylistSnapshot=info.PlaylistSnapshot};provider.Stop=mode=="cancel"?stop:null;var callbacks=new List<string>();var failure=mode is "removed" or "unavailable" or "changed" or "repeat-count" or "cancel";NativeDownloadResult? result=null;
        try{result=await engine.DownloadAsync(request,null,null,stop.Token,callbacks.Add);}catch(Exception e)when(failure&&e is InvalidDataException or OperationCanceledException){}
        if(failure){PlaylistCaptionChecks.Assert(result?.Succeeded!=true&&media==0&&provider.ChildCalls==0&&callbacks.Count==0&&await File.ReadAllTextAsync(output)=="prior output"&&await File.ReadAllTextAsync(archive)=="prior archive\n");return;}
        if(mode=="archive"){PlaylistCaptionChecks.Assert(result!.Succeeded&&result.Skipped&&media==0&&provider.ChildCalls==0&&callbacks.Count==0);return;}
        PlaylistCaptionChecks.Assert(result!.Succeeded&&media==1&&provider.ListCalls==2&&provider.ChildCalls==(mode=="materialized"?0:1)&&callbacks.SequenceEqual(result.NewOutputPaths??result.OutputPaths));await MixedVerify(ffmpeg,source,output);
        var before=media;var repeat=await engine.DownloadAsync(request,null,null,default,_=>throw new InvalidOperationException("Archived callback."));PlaylistCaptionChecks.Assert(repeat.Succeeded&&repeat.Skipped&&media==before);
    }
    private sealed class MembershipProvider(string mode):IMediaProvider
    {
        internal int ListCalls,ChildCalls;internal CancellationTokenSource? Stop;public MediaProviderInfo Info=>new("fixture","Membership fixture",0);public bool CanHandle(Uri url)=>url.Host=="collection.example";
        public DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request)=>request.Url.AbsolutePath.StartsWith("/item/")?new("fixture",request.Url.AbsolutePath.Split('/')[^1]):null;
        public Task<PageMetadata> ExtractAsync(NativeDownloadRequest request,MediaHttp http,CancellationToken ct)
        {
            if(request.Url.AbsolutePath!="/list"){ChildCalls++;return Task.FromResult(Video(request.Url.AbsolutePath.Split('/')[^1]));}
            ListCalls++;var fresh=ListCalls>1;Stop?.Cancel();var same=mode is "repeat" or "repeat-count" or "same-video";
            var rows=new List<NativePlaylistEntry>{Entry(1,"a","member-a",true,fresh),Entry(2,fresh&&mode=="changed"?"c":same?"a":"b",mode is "repeat" or "repeat-count"?"member-a":"member-b",!fresh||mode!="unavailable",fresh)};
            if(fresh&&mode=="removed")rows.RemoveAt(1);if(fresh&&mode=="repeat-count")rows.RemoveAt(0);if(fresh&&mode=="reorder")rows.Reverse();
            return Task.FromResult(new PageMetadata{Id="list",PlaylistId="list",Extractor="fixture",Title="Fixture",IsPlaylist=true,Entries=rows,SourcePageUrl=request.Url,RevalidateCollectionMembership=true});
        }
        private NativePlaylistEntry Entry(int position,string id,string member,bool available,bool fresh)=>new(position,id,id,new("https://collection.example/item/"+id),3,null,available,mode=="materialized"?Video(id,fresh):null,new("fixture",id),CollectionMemberId:member);
        private static PageMetadata Video(string id,bool fresh=true)=>new(){Id=id,Extractor="fixture",Title=id,DurationSeconds=3,Media=[new(){Id="file",Url=new("https://cdn.example/"+(fresh?"fresh/":"stale/")+id+".mp4"),Kind=MediaKind.Progressive,Extension="mp4",HasVideo=true,HasAudio=true}]};
    }
    private static async Task VimeoJsonMedia(string ffmpeg,string mode)
    {
        using var files=new Files();var source=Path.Combine(files.Directory,"source.mp4");await MixedSource(ffmpeg,source);var parts=new Dictionary<string,(byte[] Init,byte[] Body)>();
        foreach(var role in new[]{"video","audio"})
        {
            var path=Path.Combine(files.Directory,role+".mp4");await Run(ffmpeg,["-v","error","-y","-i",source,"-map",role=="video"?"0:v:0":"0:a:0","-c","copy","-movflags","+frag_keyframe+empty_moov+default_base_moof",path]);var bytes=await File.ReadAllBytesAsync(path);var offset=0;
            while(offset<bytes.Length&&System.Text.Encoding.ASCII.GetString(bytes,offset+4,4)!="moof"){var size=BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset,4));PlaylistCaptionChecks.Assert(size>=8&&size<=bytes.Length-offset);offset+=(int)size;}
            PlaylistCaptionChecks.Assert(offset>0&&offset<bytes.Length);parts[role]=(bytes[..offset],bytes[offset..]);
        }
        var config=0;var manifests=0;var transferred=0;var callbacks=0;using var stop=new CancellationTokenSource();
        var engine=new NativeEngine(httpFactory:request=>new MediaHttp(new MixedHandler((message,ct)=>{
            PlaylistCaptionChecks.Assert(message.Headers.Authorization is null&&message.Headers.Referrer?.AbsoluteUri=="https://publisher.example/story");var uri=message.RequestUri!;
            if(uri.Host=="player.vimeo.com"){if(uri.AbsolutePath.EndsWith("/config"))return Task.FromResult(AdaptiveAccountChecks.Text("not exposed",HttpStatusCode.Forbidden));config++;return Task.FromResult(AdaptiveAccountChecks.Text("<script>window.playerConfig = "+MixedCollectionChecks.Config(MixedCollectionChecks.ManifestUrl.AbsoluteUri+"?generation="+(config-1),true)+";</script>"));}
            if(uri.AbsolutePath.EndsWith("/playlist.json")){manifests++;var init=parts["video"].Init.ToArray();if(mode=="init-changed"&&config>1)init[^1]^=1;return Task.FromResult(AdaptiveAccountChecks.Text(MixedCollectionChecks.JsonManifest(init,parts["audio"].Init,config-1)));}
            var role=uri.AbsolutePath.Contains("/video/")?"video":"audio";if(mode=="cancel")stop.Cancel();
            if(mode is "renew" or "init-changed"&&uri.Query.Contains("generation=0")||mode=="denied")return Task.FromResult(AdaptiveAccountChecks.Text("expired",HttpStatusCode.Forbidden));
            transferred++;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(parts[role].Body)});
        }),new(){AllowAutoRedirect=false,Referer=request.Referer??""}));
        var output=Path.Combine(files.Directory,"recording.mp4");var archive=Path.Combine(files.Directory,"archive.txt");await File.WriteAllTextAsync(output,"prior output");await File.WriteAllTextAsync(archive,"prior archive\n");
        var request=new NativeDownloadRequest{Url=new("https://vimeo.com/12345"),Referer="https://publisher.example/story",OutputDirectory=files.Directory,OutputTemplate="recording.%(ext)s",FfmpegPath=ffmpeg,KeepOriginal=true,AddMetadata=true,DownloadArchiveFile=archive,Retries=0};
        NativeDownloadResult? result=null;var failure=mode is "init-changed" or "denied" or "cancel";try{result=await engine.DownloadAsync(request,null,null,stop.Token,_=>callbacks++);}catch(Exception e)when(failure&&e is InvalidDataException or HttpRequestException or IOException or OperationCanceledException or NotSupportedException){}
        if(failure){PlaylistCaptionChecks.Assert(result?.Succeeded!=true&&callbacks==0&&await File.ReadAllTextAsync(output)=="prior output"&&await File.ReadAllTextAsync(archive)=="prior archive\n");return;}
        PlaylistCaptionChecks.Assert(result!.Succeeded&&transferred==2&&callbacks==(result.NewOutputPaths??result.OutputPaths).Count&&config==(mode=="renew"?2:1)&&manifests==(mode=="renew"?2:1));await MixedVerify(ffmpeg,source,output);
        var before=config;var repeat=await engine.DownloadAsync(request,null,null,default,_=>throw new InvalidOperationException("Archived callback."));PlaylistCaptionChecks.Assert(repeat.Succeeded&&repeat.Skipped&&config==before);
    }
    private static Task<string> MixedSource(string ffmpeg,string source)=>Run(ffmpeg,["-v","error","-y","-f","lavfi","-i","testsrc2=size=128x72:rate=12","-f","lavfi","-i","sine=frequency=700:sample_rate=48000","-t","3","-c:v","libx264","-preset","ultrafast","-c:a","aac",source]);
    private sealed class MixedHandler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> action):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage message,CancellationToken ct)=>action(message,ct);
    }
    private static async Task MixedVerify(string ffmpeg,string source,string output)
    {
        await Run(ffmpeg,["-v","error","-xerror","-i",output,"-map","0:v:0","-map","0:a:0","-f","null","-"],true);var ffprobe=Path.Combine(Path.GetDirectoryName(ffmpeg)!,"ffprobe.exe");using var probe=JsonDocument.Parse(await Run(ffprobe,["-v","error","-count_frames","-show_entries","stream=codec_type,nb_read_frames","-of","json",output]));PlaylistCaptionChecks.Assert(probe.RootElement.GetProperty("streams").EnumerateArray().Single(s=>s.GetProperty("codec_type").GetString()=="video").GetProperty("nb_read_frames").GetString()=="36");
        foreach(var role in new[]{"v:0","a:0"}){var inventories=new List<string[]>();foreach(var path in new[]{source,output}){using var doc=JsonDocument.Parse(await Run(ffprobe,["-v","error","-select_streams",role,"-show_packets","-show_data_hash","sha256","-show_entries","packet=data_hash","-of","json",path]));inventories.Add(doc.RootElement.GetProperty("packets").EnumerateArray().Select(p=>p.GetProperty("data_hash").GetString()!).ToArray());}PlaylistCaptionChecks.Assert(inventories[0].Length>0&&inventories[0].SequenceEqual(inventories[1]));}
    }
}
