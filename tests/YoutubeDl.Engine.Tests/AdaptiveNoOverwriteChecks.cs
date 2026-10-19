using System.Net;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Media;
using YoutubeDl.Engine.Protocols.Sabr;
using YoutubeDl.Engine.Providers;

internal static class AdaptiveNoOverwriteChecks
{
    internal static IEnumerable<(string Name,Func<Task> Test)> All=>
        new[]{"aac","alac","mp3","opus","vorbis","flac","pcm_s16le","pcm_f64le","ac3","unknown","webm","copy","template","missing"}.Select(mode=>("Adaptive no overwrite audio "+mode,(Func<Task>)(()=>Audio(mode))))
        .Concat(new[]{"pair","single","audio","video","missing","context","selector","constraint","no-original","template"}.Select(mode=>("Adaptive no overwrite SABR "+mode,(Func<Task>)(()=>Sabr(mode)))))
        .Concat(new[]{"pair","single","audio","video","webm","missing","dynamic","drm","unsupported","selector","template","caption","caption-unselected","caption-sidecar","expired-profile"}.Select(mode=>("Adaptive no overwrite DASH "+mode,(Func<Task>)(()=>Dash(mode)))))
        .Concat(new[]{"locators","ancestors"}.Select(mode=>("Adaptive no overwrite recovery race "+mode,(Func<Task>)(()=>RecoveryRace(mode)))));
    internal sealed class Provider(PageMetadata page):IMediaProvider
    {public MediaProviderInfo Info=>new("adaptive-no-overwrite","Adaptive no overwrite",0);public bool CanHandle(Uri uri)=>true;public Task<PageMetadata> ExtractAsync(NativeDownloadRequest request,YoutubeDl.Engine.Http.MediaHttp http,CancellationToken ct)=>Task.FromResult(page);}
    internal static NativeDownloadRequest Request(string directory)=>new(){Url=new("https://adaptive-no-overwrite.example/fixture"),OutputDirectory=directory,FileName="result",KeepOriginal=true,NoOverwrites=true,Retries=0};
    private static string DirectoryFor()=>Path.Combine(AppContext.BaseDirectory,"test-data",Guid.NewGuid().ToString("N"));
    internal static async Task Put(string path,string value="existing"){Directory.CreateDirectory(Path.GetDirectoryName(path)!);await File.WriteAllTextAsync(path,value);}
    private static async Task RecoveryRace(string mode)
    {
        var directory=DirectoryFor();Directory.CreateDirectory(directory);var source=AdaptiveFragmentCheckpoint.Hash(directory);var recipe=AdaptiveFragmentCheckpoint.Hash("fixture");using var done=new CancellationTokenSource();
        try
        {
            var readers=Enumerable.Range(0,3).Select(reader=>Task.Run(()=>{while(!done.IsCancellationRequested){if(mode=="locators")_ = RecoveryIndex.Find(directory,source).ToArray();else PublicationTransaction.PlainPath(Path.Combine(directory,"changing","output.mp4"));_ = RecoveryIndex.LegacyRoots(directory).ToArray();}})).ToArray();
            try
            {
                for(var i=0;i<150;i++)
                {
                    if(mode=="ancestors"){var folder=Path.Combine(directory,"changing");Directory.CreateDirectory(folder);Directory.Delete(folder);continue;}
                    var scope=AdaptiveFragmentCheckpoint.Hash(directory+"\n"+i);var root=Path.Combine(directory,".native-download-"+scope[..24]);Directory.CreateDirectory(root);
                    var file=await RecoveryIndex.SaveAsync(directory,root,scope,recipe,source,default);Directory.Delete(root);RecoveryIndex.Delete(new(root,scope,file));
                }
            }
            finally{done.Cancel();await Task.WhenAll(readers);}
            WriteCleanupChecks.Check(!RecoveryIndex.Find(directory,source).Any(),"A completed locator was returned as pending recovery.");
        }
        finally{Directory.Delete(directory,true);}
    }
    private static async Task Audio(string mode)
    {
        var directory=DirectoryFor();Directory.CreateDirectory(directory);
        try
        {
            var codec=mode is "webm"?"opus":mode is "copy" or "template" or "missing"?"aac":mode;
            var raw=mode=="webm"?"webm":"mp4";var expected=AudioOutputPlan.AutomaticExtension(codec,raw,mode=="copy"?"copy":"best");
            var page=new PageMetadata{Id="fixture",Title="Fixture",Media=[new(){Id="file",Url=new("https://cdn.example/source."+raw),Kind=MediaKind.Progressive,Extension=raw,HasAudio=true,HasVideo=true,AudioCodec=codec}]};
            var request=Request(directory) with {AudioOnly=true,AudioFormat=mode=="copy"?"copy":"best",OutputTemplate=mode=="template"?"%(ext)s/%(title)s.%(ext)s":""};
            var main=YoutubeDl.Engine.Download.NativeOutputTemplate.Render(request,page,page.Media[0],expected);var original=YoutubeDl.Engine.Download.NativeOutputTemplate.Render(request,page,page.Media[0],raw);
            await Put(main);if(mode!="missing")await Put(main==original?Path.ChangeExtension(original,"original."+raw):original);
            var bodies=0;var callbacks=0;var engine=new NativeEngine([new Provider(page)],_=>AdaptiveAccountChecks.Http((_,_)=>{bodies++;throw new HttpRequestException("Body required.");}));
            var failure=mode is "unknown" or "missing";var failed=false;
            try{var result=await engine.DownloadAsync(request,null,null,default,_=>callbacks++);WriteCleanupChecks.Check(!failure&&result.Skipped&&result.OutputPaths.Count==2&&result.NewOutputPaths?.Count==0,"Automatic audio was not resolved.");}
            catch(Exception e)when(failure&&e is IOException or InvalidOperationException or HttpRequestException){failed=true;}
            WriteCleanupChecks.Check(failed==failure&&bodies==0&&callbacks==0&&await File.ReadAllTextAsync(main)=="existing","Automatic audio changed existing media.");
        }
        finally{Directory.Delete(directory,true);}
    }
    internal static PageMetadata SabrPage(string mode="pair")
    {
        var session=new SabrPlaybackSession(SabrFixture.VideoId,SabrFixture.Endpoint,5,"fixture",[1]);
        MediaCandidate Candidate(bool audio)=>new(){Id=audio?"140":"137",Url=SabrFixture.Endpoint,Extension=audio?"m4a":"mp4",Kind=MediaKind.Sabr,HasVideo=!audio,HasAudio=audio,
            Height=audio?null:72,VideoCodec=audio?null:mode=="unknown"?"unknown":"h264",AudioCodec=audio?mode=="unknown"?"unknown":"aac":null,Sabr=new(audio&&mode=="context"?session with {ClientVersion="other"}:session,audio?SabrFixture.Audio:SabrFixture.Video)};
        return new(){Id=SabrFixture.VideoId,Extractor="youtube",Title="Fixture",Media=mode=="single"?[Candidate(false)]:[Candidate(false),Candidate(true)]};
    }
    private static async Task Sabr(string mode)
    {
        var directory=DirectoryFor();Directory.CreateDirectory(directory);
        try
        {
            var page=SabrPage(mode);var request=Request(directory) with {AudioOnly=mode=="audio",AudioFormat="copy",VideoOnly=mode is "video" or "single",KeepOriginal=mode!="no-original",
                FormatSelector=mode=="selector"?"137+140":"",MaximumHeight=mode=="constraint"?48:null,OutputTemplate=mode=="template"?"%(format_id)s/%(ext)s/%(title)s.%(ext)s":""};
            var primary=mode=="audio"?page.Media[1]:page.Media[0];var ext=mode=="audio"?"m4a":"mp4";
            var main=YoutubeDl.Engine.Download.NativeOutputTemplate.Render(request,page,primary,ext);await Put(main);
            if(mode=="audio")await Put(Path.ChangeExtension(main,"original.m4a"));
            if(mode is not ("audio" or "video" or "single" or "no-original"))
            {
                await Put(YoutubeDl.Engine.Download.NativeOutputTemplate.Render(request,page,page.Media[0],"mp4").Replace(".mp4",".video.mp4"));
                if(mode!="missing")await Put(YoutubeDl.Engine.Download.NativeOutputTemplate.Render(request,page,page.Media[1],"m4a").Replace(".m4a",".audio.m4a"));
            }
            var bodies=0;var callbacks=0;var engine=new NativeEngine([new Provider(page)],_=>AdaptiveAccountChecks.Http((_,_)=>{bodies++;throw new HttpRequestException("SABR POST required.");}));
            var failure=mode is "missing" or "context" or "constraint";var failed=false;
            try{var result=await engine.DownloadAsync(request,null,null,default,_=>callbacks++);WriteCleanupChecks.Check(!failure&&result.Succeeded&&result.Skipped&&result.NewOutputPaths?.Count==0,"SABR inventory was not skipped.");}
            catch(Exception e)when(failure&&e is InvalidOperationException or NotSupportedException or HttpRequestException){failed=true;}
            WriteCleanupChecks.Check(failed==failure&&bodies==0&&callbacks==0&&await File.ReadAllTextAsync(main)=="existing","SABR skip sent media or changed data.");
        }
        finally{Directory.Delete(directory,true);}
    }
    internal static string Manifest(string mode="pair")
    {
        var webm=mode=="webm";var ext=webm?"webm":"mp4";var vc=webm?"vp09.00.10.08":"avc1.4d401e";var ac=webm?"opus":"mp4a.40.2";
        return "<MPD xmlns=\"urn:mpeg:dash:schema:mpd:2011\" type=\""+(mode=="dynamic"?"dynamic":"static")+"\" mediaPresentationDuration=\"PT1S\"><Period>"+
            "<AdaptationSet mimeType=\"video/"+ext+"\">"+(mode=="drm"?"<ContentProtection schemeIdUri=\"urn:uuid:edef8ba9-79d6-4ace-a3c8-27dcd51d21ed\"/>":"")+"<Representation id=\"v\" height=\"72\" bandwidth=\"100000\" codecs=\""+vc+"\"><BaseURL>video."+ext+"</BaseURL>"+(mode=="unsupported"?"<SegmentTemplate/>":"")+"</Representation></AdaptationSet>"+
            (mode=="single"?"":"<AdaptationSet mimeType=\"audio/"+ext+"\"><Representation id=\"a\" bandwidth=\"10000\" codecs=\""+ac+"\"><BaseURL>audio."+(webm?"webm":"m4a")+"</BaseURL></Representation></AdaptationSet>")+
            (mode is "caption" or "caption-unselected"?"<AdaptationSet mimeType=\"application/mp4\" lang=\"en\"><Representation id=\"c\" codecs=\"wvtt\"><SegmentTemplate initialization=\"text-init.mp4\" media=\"text-$Number$.m4s\" duration=\"1\"/></Representation></AdaptationSet>":mode=="caption-sidecar"?"<AdaptationSet mimeType=\"text/vtt\" lang=\"en\"><Representation id=\"c\"><BaseURL>captions.vtt</BaseURL></Representation></AdaptationSet>":"")+"</Period></MPD>";
    }
    private static async Task Dash(string mode)
    {
        var directory=DirectoryFor();Directory.CreateDirectory(directory);
        try
        {
            var page=new PageMetadata{Id="fixture",Title="Fixture",Media=[new(){Id="dash",Url=new("https://cdn.example/manifest.mpd"),Kind=MediaKind.Dash,Extension="mpd",HasVideo=true,HasAudio=true}]};
            if(mode=="expired-profile")page=page with {Extractor="kaltura",Media=[
                new(){Id="dash",Url=new("https://cdn.example/manifest.mpd"),Kind=MediaKind.Dash,Extension="mpd",HasVideo=true,HasAudio=true,Height=72},
                new(){Id="fresh",Url=new("https://cdn.example/fresh.mpd"),Kind=MediaKind.Dash,Extension="mpd",HasVideo=true,HasAudio=true,Height=36}]};
            if(mode=="caption-unselected")page=page with {Subtitles=[new(){Url=new("https://cdn.example/french.vtt"),Language="fr"}]};
            var request=Request(directory) with {AudioOnly=mode=="audio",AudioFormat="best",VideoOnly=mode=="video",EmbedSubs=mode.StartsWith("caption",StringComparison.Ordinal),SubLanguages="en",FormatId=mode=="selector"?"dash":""};
            var main=Path.Combine(directory,"result."+(mode=="audio"?"m4a":mode=="webm"?"webm":"mp4"));await Put(main);
            if(mode=="audio")await Put(Path.Combine(directory,"result.original.m4a"));
            if(mode=="video")await Put(Path.Combine(directory,"result.original.mp4"));
            if(mode is not ("audio" or "video" or "single")){await Put(Path.Combine(directory,"result.video."+(mode=="webm"?"webm":"mp4")));if(mode!="missing")await Put(Path.Combine(directory,"result.audio."+(mode=="webm"?"webm":"m4a")));}
            var bodies=0;var manifests=0;var callbacks=0;var engine=new NativeEngine([new Provider(page)],_=>AdaptiveAccountChecks.Http((message,_)=>{
                if(message.RequestUri!.AbsolutePath.EndsWith(".mpd")){manifests++;return Task.FromResult(AdaptiveAccountChecks.Text(mode=="expired-profile"?(message.RequestUri.AbsolutePath.EndsWith("fresh.mpd")?Manifest():Manifest().Replace("video.mp4","video.mp4?expire=1")):Manifest(mode)));}bodies++;throw new HttpRequestException("DASH body required.");
            }));
            var failure=mode is "missing" or "dynamic" or "drm" or "unsupported" or "expired-profile"||mode.StartsWith("caption",StringComparison.Ordinal);var failed=false;
            try{var result=await engine.DownloadAsync(request,null,null,default,_=>callbacks++);WriteCleanupChecks.Check(!failure&&result.Skipped&&result.NewOutputPaths?.Count==0,"DASH inventory was not skipped.");}
            catch(Exception e)when(failure&&e is IOException or InvalidDataException or InvalidOperationException or NotSupportedException)
            {if(mode=="expired-profile"&&e is YoutubeDl.Engine.Http.ExpiredPlaybackResourcesException)throw new InvalidOperationException("Existing-file preflight blocked alternate-profile recovery.",e);failed=true;}
            WriteCleanupChecks.Check(failed==failure&&callbacks==0&&manifests<4&&(failure||bodies==0)&&await File.ReadAllTextAsync(main)=="existing","DASH preflight altered outputs or fetched media.");
            if(mode=="expired-profile")WriteCleanupChecks.Check(manifests==3&&bodies==0,"Expired preflight did not reach the fresh alternate profile.");
        }
        finally{Directory.Delete(directory,true);}
    }
}
