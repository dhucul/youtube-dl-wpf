using System.Net;
using System.Text;
using System.Xml.Linq;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;

internal static partial class FfmpegChecks
{
    internal static IEnumerable<(string Name,Func<Task> Test)> LiveInventoryMediaChecks(string ffmpeg)=>
        new[]{"hls-ts","hls-fmp4","hls-paired","dash-paired","dash-video","dash-audio","nested","main-only","missing","post-original","post-main","corrupt-record","changed-duration","upgrade-retained","changed-template","archive","concurrent"}
            .Select(mode=>("Live inventory media recording "+mode,(Func<Task>)(()=>InventoryMedia(ffmpeg,mode))))
        .Concat(new[]{"mp4","flv","audio","pair","template","missing-part","missing-original","post-original"}
            .Select(mode=>("Live inventory media sequence "+mode,(Func<Task>)(()=>InventoryMedia(ffmpeg,"seq-"+mode)))))
        .Concat(new[]{"hls-single","hls-paired","hls-fmp4","dash-pair","dash-audio","dash-template","hls-caption"}
            .Select(mode=>("Live inventory media reuse "+mode,(Func<Task>)(()=>InventoryMedia(ffmpeg,"reuse-"+mode)))));
    private static async Task InventoryMedia(string ffmpeg,string mode)
    {
        using var files=new Files();var sequence=mode.StartsWith("seq-",StringComparison.Ordinal);var reuse=mode.StartsWith("reuse-",StringComparison.Ordinal);var dash=mode.Contains("dash",StringComparison.Ordinal);var flv=mode=="seq-flv";
        var audioOnly=mode.EndsWith("audio",StringComparison.Ordinal);var videoOnly=mode=="dash-video";
        var paired=mode is "hls-paired" or "dash-paired" or "seq-pair" or "reuse-hls-paired" or "reuse-dash-pair" or "reuse-dash-template";
        var source=Path.Combine(files.Directory,"source.mp4");await Run(ffmpeg,["-v","error","-y","-f","lavfi","-i","testsrc2=size=128x72:rate=10","-f","lavfi","-i","sine=frequency=550:sample_rate=48000","-t","1","-c:v","libx264","-g","5","-c:a","aac",source]);
        var assets=new Dictionary<string,byte[]>();PageMetadata page;
        if(sequence)
        {
            for(var i=0;i<2;i++)
            {
                var path=Path.Combine(files.Directory,"part"+i+(flv?".flv":".mp4"));await Run(ffmpeg,["-v","error","-y","-i",source,"-map",paired?"0:v:0":"0","-c","copy",path]);assets[Path.GetFileName(path)]=await File.ReadAllBytesAsync(path);
            }
            if(paired){var path=Path.Combine(files.Directory,"audio.m4a");await Run(ffmpeg,["-v","error","-y","-i",source,"-map","0:a:0","-c","copy",path]);assets["audio.m4a"]=await File.ReadAllBytesAsync(path);}
            page=LiveInventoryChecks.SequencePage(flv?"flv":"mp4",paired,duration:flv?1.05:1);
        }
        else
        {
            var fragmented=dash||mode is "hls-fmp4" or "reuse-hls-fmp4";
            if(dash)
            {
                var mpd=Path.Combine(files.Directory,"original.mpd");await Run(ffmpeg,["-v","error","-y","-i",source,"-c","copy","-f","dash","-seg_duration","0.5","-use_template","1","-use_timeline","1","-init_seg_name","init-$RepresentationID$.m4s","-media_seg_name","chunk-$RepresentationID$-$Number%05d$.m4s",mpd]);
                var document=XDocument.Load(mpd);if(!reuse){document.Root!.SetAttributeValue("type","dynamic");document.Root.SetAttributeValue("availabilityStartTime","2026-01-01T00:00:00Z");document.Root.SetAttributeValue("publishTime","2026-01-01T00:00:10Z");document.Root.SetAttributeValue("minimumUpdatePeriod","PT1S");document.Root.SetAttributeValue("timeShiftBufferDepth","PT20S");}
                assets["manifest.mpd"]=Encoding.UTF8.GetBytes(document.ToString());
                foreach(var path in Directory.GetFiles(files.Directory,"*.m4s"))assets[Path.GetFileName(path)]=await File.ReadAllBytesAsync(path);
            }
            else
            {
                async Task HlsTrack(string name,bool audio)
                {
                    var playlist=Path.Combine(files.Directory,name+".m3u8");var args=new List<string>{"-v","error","-y","-i",source,"-c","copy"};
                    if(paired)args.AddRange(["-map",audio?"0:a:0":"0:v:0"]);args.AddRange(["-f","hls","-hls_time","0.5","-hls_list_size","0","-hls_segment_filename",Path.Combine(files.Directory,name+"%d."+(fragmented?"m4s":"ts"))]);
                    if(fragmented)args.AddRange(["-hls_segment_type","fmp4","-hls_fmp4_init_filename",name+"-init.mp4"]);args.Add(playlist);await Run(ffmpeg,args);
                    var text=await File.ReadAllTextAsync(playlist);if(!reuse)text=text.Replace("#EXT-X-TARGETDURATION:0","#EXT-X-TARGETDURATION:1").Replace("#EXT-X-ENDLIST","").Replace("#EXTM3U","#EXTM3U\n#EXT-X-PLAYLIST-TYPE:EVENT\n#EXT-X-PROGRAM-DATE-TIME:2026-01-01T00:00:00Z");assets[name+".m3u8"]=Encoding.UTF8.GetBytes(text);
                }
                await HlsTrack("video",false);if(paired)await HlsTrack("audio",true);
                assets["master.m3u8"]=Encoding.UTF8.GetBytes(paired?LiveInventoryChecks.Hls(paired:true):"#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=100000,RESOLUTION=128x72,CODECS=\"avc1.4d401e,mp4a.40.2\"\nvideo.m3u8\n");
                foreach(var path in Directory.GetFiles(files.Directory).Where(path=>Path.GetExtension(path) is ".ts" or ".m4s"||Path.GetFileName(path).EndsWith("-init.mp4",StringComparison.Ordinal)))assets[Path.GetFileName(path)]=await File.ReadAllBytesAsync(path);
            }
            if(mode=="reuse-hls-caption")assets["captions.vtt"]=Encoding.UTF8.GetBytes("WEBVTT\n\n00:00.000 --> 00:00.900\nFixture caption\n");
            page=new(){Id="fixture",Extractor="live-fixture",Title="Fixture",Subtitles=mode=="reuse-hls-caption"?[new(){Url=new("https://cdn.example/captions.vtt"),Language="en"}]:[],Media=[new(){Id="manifest",Url=new("https://cdn.example/"+(dash?"manifest.mpd":"master.m3u8")),Kind=dash?MediaKind.Dash:MediaKind.Hls,Extension=dash?"mpd":"m3u8",HasVideo=true,HasAudio=true,Height=72,VideoCodec="h264",AudioCodec="aac"}]};
        }
        var reads=new Dictionary<string,int>();var media=0;
        NativeEngine Engine()=>new([new AdaptiveNoOverwriteChecks.Provider(page)],_=>AdaptiveAccountChecks.Http((message,_)=>{
            var name=Path.GetFileName(message.RequestUri!.AbsolutePath);lock(reads)reads[name]=reads.GetValueOrDefault(name)+1;
            if(!name.EndsWith(".m3u8")&&!name.EndsWith(".mpd")&&!name.EndsWith(".vtt"))Interlocked.Increment(ref media);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(assets[name])});
        }));
        var request=AdaptiveNoOverwriteChecks.Request(Path.Combine(files.Directory,"out")) with {NoOverwrites=false,FfmpegPath=ffmpeg,AddMetadata=true,AudioOnly=audioOnly,AudioFormat=sequence?"mp3":"best",VideoOnly=videoOnly,
            LiveDurationSeconds=sequence||reuse?null:1,KeepOriginal=mode!="upgrade-retained",OutputTemplate=mode is "nested" or "seq-template" or "reuse-dash-template"?"%(format_id)s/%(ext)s/%(title)s.%(ext)s":"",
            EmbedSubs=mode=="reuse-hls-caption",SubLanguages="en",DownloadArchiveFile=mode=="archive"?Path.Combine(files.Directory,"archive.txt"):""};
        var first=await Engine().DownloadAsync(request,null,null,default);WriteCleanupChecks.Check(first.Succeeded&&first.OutputPath is not null&&media>0,"Initial inventory media failed.");
        var originals=first.OutputPaths.Where(path=>path!=first.OutputPath).ToArray();var missing=originals.LastOrDefault();
        if(mode=="seq-missing-part")missing=originals.First(path=>path.Contains(".part."));
        if(mode=="seq-missing-original")missing=originals.First(path=>path.Contains(".original."));
        if(reuse||mode is "missing" or "seq-missing-part" or "seq-missing-original")File.Delete(missing!);
        if(mode=="corrupt-record")foreach(var path in Directory.GetFiles(request.OutputDirectory,".native-output-inventory-*.json",SearchOption.AllDirectories))await File.WriteAllTextAsync(path,"corrupt inventory");
        var hashes=first.OutputPaths.Where(File.Exists).ToDictionary(path=>path,path=>System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
        reads.Clear();media=0;var callbacks=new List<string>();var repeat=request with {NoOverwrites=mode is not ("post-original" or "seq-post-original" or "post-main"),NoPostOverwrites=mode is "post-original" or "seq-post-original" or "post-main",
            KeepOriginal=mode is not ("main-only" or "post-main"),LiveDurationSeconds=mode=="changed-duration"?0.5:request.LiveDurationSeconds,OutputTemplate=mode=="changed-template"?"new.%(ext)s":request.OutputTemplate};
        var fallback=reuse||mode is "missing" or "corrupt-record" or "changed-duration" or "upgrade-retained" or "changed-template" or "post-original" or "seq-missing-part" or "seq-missing-original" or "seq-post-original";
        if(!fallback)repeat=repeat with {FfmpegPath=Path.Combine(files.Directory,"unused-ffmpeg.exe"),SleepIntervalSeconds=86400};
        if(mode=="concurrent")
        {
            var results=await Task.WhenAll(Enumerable.Range(0,6).Select(_=>Engine().DownloadAsync(repeat,null,null,default,_=>throw new InvalidOperationException("Unexpected inventory callback."))));WriteCleanupChecks.Check(results.All(result=>result.Succeeded&&result.Skipped)&&media==0,"Concurrent recording inventory transferred.");
        }
        else
        {
            var result=await Engine().DownloadAsync(repeat,null,null,default,callbacks.Add);WriteCleanupChecks.Check(result.Succeeded,"Inventory repeat failed.");
            if(fallback){WriteCleanupChecks.Check(media>0,"An incomplete or changed inventory was falsely skipped.");if(reuse||mode is "missing" or "seq-missing-part" or "seq-missing-original")WriteCleanupChecks.Check(callbacks.SequenceEqual([missing!]),"The missing retained file was not filled alone.");}
            else WriteCleanupChecks.Check(result.Skipped&&media==0&&callbacks.Count==0&&(mode=="archive"||result.NewOutputPaths?.Count==0),"Complete inventory transferred again.");
        }
        if(reuse)WriteCleanupChecks.Check(reads.Where(row=>row.Key.EndsWith(".m3u8")||row.Key.EndsWith(".mpd")).All(row=>row.Value==1),"An initial manifest was not handed off to transfer.");
        if(!sequence&&!reuse&&!fallback&&mode!="archive")WriteCleanupChecks.Check(reads.Count==0,"A matching recorded inventory re-read its manifest.");
        foreach(var (path,hash) in hashes)WriteCleanupChecks.Check(hash.AsEnumerable().SequenceEqual(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(path))),"A protected media file changed.");
        foreach(var path in first.OutputPaths)await Run(ffmpeg,["-v","error","-xerror","-i",path,"-map","0:v?","-map","0:a?","-f","null","-"],true);
    }
}
