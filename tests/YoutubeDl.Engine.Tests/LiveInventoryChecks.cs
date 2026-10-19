using System.Net;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;

internal static class LiveInventoryChecks
{
    internal static IEnumerable<(string Name,Func<Task> Test)> All=>
        new[]{"mp4","flv","audio","template","pair","missing-part","missing-original","no-original","post-original","post-main","archive","bad-count","bad-duration","bad-container","bad-url"}
            .Select(mode=>("Live inventory sequence "+mode,(Func<Task>)(()=>Sequence(mode))))
        .Concat(new[]{"hls-pair","dash-pair","hls-live","dash-live","hls-aged","dash-aged","hls-denied","dash-invalid"}
            .Select(mode=>("Live inventory manifest reuse "+mode,(Func<Task>)(()=>Reuse(mode)))));
    internal static string Suffix(string path,string role)=>Path.ChangeExtension(path,role+Path.GetExtension(path));
    internal static PageMetadata SequencePage(string extension="mp4",bool pair=false,int count=2,double duration=1,bool invalidUrl=false)=>new(){Id="fixture",Extractor="sequence-fixture",Title="Fixture",Media=
        new[]{new MediaCandidate{Id="v",Url=new("https://sequence.example/watch"),Kind=MediaKind.HttpSequence,Extension=extension,HasVideo=true,HasAudio=!pair,VideoCodec="h264",AudioCodec=pair?null:"aac",
            Parts=Enumerable.Range(0,count).Select(index=>new MediaFilePart(new((invalidUrl?"file:///":"https://cdn.example/")+"part"+index+"."+extension),extension,duration)).ToArray()}}
        .Concat(pair?[new MediaCandidate{Id="a",Url=new("https://cdn.example/audio.m4a"),Kind=MediaKind.Progressive,Extension="m4a",HasAudio=true,HasVideo=false,AudioCodec="aac"}]:[]).ToArray()};
    private static async Task Sequence(string mode)
    {
        var directory=Path.Combine(AppContext.BaseDirectory,"test-data",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var raw=mode=="flv"?"flv":mode=="bad-container"?"webm":"mp4";var page=SequencePage(raw,mode=="pair",mode=="bad-count"?1:2,mode=="bad-duration"?double.NaN:1,mode=="bad-url");
            var request=AdaptiveNoOverwriteChecks.Request(directory) with {AddMetadata=true,AudioOnly=mode=="audio",AudioFormat="mp3",KeepOriginal=mode is not ("no-original" or "post-main"),NoOverwrites=mode is not ("post-original" or "post-main"),NoPostOverwrites=mode is "post-original" or "post-main",
                OutputTemplate=mode=="template"?"%(format_id)s/%(ext)s/%(title)s.%(ext)s":"",DownloadArchiveFile=mode=="archive"?Path.Combine(directory,"archive.txt"):""};
            var main=NativeOutputTemplate.Render(request,page,page.Media[0],mode=="audio"?"mp3":raw);await AdaptiveNoOverwriteChecks.Put(main);
            if(request.KeepOriginal)
            {
                var original=NativeOutputTemplate.Render(request,page,page.Media[0],raw);if(original==main)original=Suffix(original,"original");if(mode!="missing-original")await AdaptiveNoOverwriteChecks.Put(original);
                for(var index=0;index<page.Media[0].Parts.Count;index++)if(mode!="missing-part"||index==0)await AdaptiveNoOverwriteChecks.Put(Suffix(NativeOutputTemplate.Render(request,page,page.Media[0],raw),index==0?"part":"part-"+(index+1)));
                if(mode=="pair"){await AdaptiveNoOverwriteChecks.Put(Suffix(NativeOutputTemplate.Render(request,page,page.Media[0],"mp4"),"video"));await AdaptiveNoOverwriteChecks.Put(Suffix(NativeOutputTemplate.Render(request,page,page.Media[1],"m4a"),"audio"));}
            }
            var bodies=0;var callbacks=0;var engine=new NativeEngine([new AdaptiveNoOverwriteChecks.Provider(page)],_=>AdaptiveAccountChecks.Http((_,_)=>{bodies++;throw new HttpRequestException("Unexpected sequence body.");}));
            var failure=mode is "missing-part" or "missing-original" or "post-original" or "bad-count" or "bad-duration" or "bad-container" or "bad-url";var failed=false;
            try{var result=await engine.DownloadAsync(request,null,null,default,_=>callbacks++);WriteCleanupChecks.Check(!failure&&result.Skipped&&result.NewOutputPaths?.Count==0,"Sequence inventory did not skip.");}
            catch(Exception e)when(failure&&e is IOException or InvalidDataException or InvalidOperationException){failed=true;}
            WriteCleanupChecks.Check(failed==failure&&bodies==0&&callbacks==0&&await File.ReadAllTextAsync(main)=="existing","Sequence preflight altered media.");
            if(mode=="archive")WriteCleanupChecks.Check(File.ReadAllLines(request.DownloadArchiveFile).Single()=="sequence-fixture fixture","Sequence skip did not seed its archive.");
        }
        finally{Directory.Delete(directory,true);}
    }
    internal static string Hls(bool live=false,bool paired=false)=>paired?"#EXTM3U\n#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID=\"a\",NAME=\"Audio\",DEFAULT=YES,URI=\"audio.m3u8\"\n#EXT-X-STREAM-INF:BANDWIDTH=100000,RESOLUTION=128x72,CODECS=\"avc1.4d401e,mp4a.40.2\",AUDIO=\"a\"\nvideo.m3u8\n":
        "#EXTM3U\n#EXT-X-TARGETDURATION:1\n#EXT-X-PLAYLIST-TYPE:EVENT\n#EXT-X-PROGRAM-DATE-TIME:2026-01-01T00:00:00Z\n#EXTINF:1,\nsegment.ts\n"+(live?"":"#EXT-X-ENDLIST\n");
    private static async Task Reuse(string mode)
    {
        var directory=Path.Combine(AppContext.BaseDirectory,"test-data",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var dash=mode.StartsWith("dash",StringComparison.Ordinal);var live=mode.EndsWith("live",StringComparison.Ordinal)||mode.EndsWith("aged",StringComparison.Ordinal);
            var page=new PageMetadata{Id="fixture",Title="Fixture",Media=[new(){Id="manifest",Url=new("https://cdn.example/"+(dash?"manifest.mpd":"master.m3u8")),Kind=dash?MediaKind.Dash:MediaKind.Hls,Extension=dash?"mpd":"m3u8",HasVideo=true,HasAudio=true,VideoCodec="h264",Height=72}]};
            var request=AdaptiveNoOverwriteChecks.Request(directory) with {MergeOutputFormat="webm/mkv",LiveDurationSeconds=live?1:null,SleepIntervalSeconds=mode=="hls-aged"?0.7:mode=="dash-aged"?0.15:null};
            await AdaptiveNoOverwriteChecks.Put(Path.Combine(directory,"result.mp4"));await AdaptiveNoOverwriteChecks.Put(Path.Combine(directory,"result.ts"));
            var reads=new Dictionary<string,int>();var bodies=0;var engine=new NativeEngine([new AdaptiveNoOverwriteChecks.Provider(page)],_=>AdaptiveAccountChecks.Http((message,_)=>{
                var name=Path.GetFileName(message.RequestUri!.AbsolutePath);reads[name]=reads.GetValueOrDefault(name)+1;
                if(mode=="hls-denied")throw new HttpRequestException("Denied",null,HttpStatusCode.Forbidden);
                if(name.EndsWith(".mpd"))return Task.FromResult(AdaptiveAccountChecks.Text(mode=="dash-invalid"?"invalid XML":live?AdaptiveLiveChecks.Mpd().Replace("PT1S\" timeShift","PT0.1S\" timeShift"):AdaptiveNoOverwriteChecks.Manifest()));
                if(name.EndsWith(".m3u8"))return Task.FromResult(AdaptiveAccountChecks.Text(Hls(live,!live&&name=="master.m3u8")));
                bodies++;throw new HttpRequestException("Unexpected media body.");
            }));
            var failed=false;try{await engine.DownloadAsync(request,null,null,default);}
            catch(Exception e)when(e is IOException or InvalidDataException or InvalidOperationException or HttpRequestException or System.Xml.XmlException){failed=true;}
            WriteCleanupChecks.Check(failed&&bodies==0,"Incomplete manifest inventory falsely skipped or fetched media.");
            if(mode is "hls-aged" or "dash-aged")WriteCleanupChecks.Check(reads.Values.Single()==2,"A stale live manifest was reused.");
            else WriteCleanupChecks.Check(reads.Values.All(count=>count==1),"Initial manifest was read twice.");
            WriteCleanupChecks.Check(await File.ReadAllTextAsync(Path.Combine(directory,"result.mp4"))=="existing","Manifest reuse changed an existing file.");
        }
        finally{Directory.Delete(directory,true);}
    }
}
