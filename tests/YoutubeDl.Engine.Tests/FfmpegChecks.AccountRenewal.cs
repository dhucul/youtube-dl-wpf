using System.Buffers.Binary;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.YouTube;
using YoutubeDl.Engine.Protocols.Sabr;

internal static partial class FfmpegChecks
{
    private static async Task AccountMedia(string ffmpeg,string mode)
    {
        using var files=new Files();var source=Path.Combine(files.Directory,"source.mp4");
        await Run(ffmpeg,["-v","error","-y","-f","lavfi","-i","testsrc2=size=128x72:rate=25","-f","lavfi","-i","sine=frequency=650:sample_rate=48000","-t","1","-c:v","libx264","-preset","ultrafast","-c:a","aac",source]);
        var bytes=await File.ReadAllBytesAsync(source);var issuers=0;var media=0;var passwords=0;var callbacks=0;var youtube=mode.StartsWith("youtube");var revoked=mode=="vimeo-revoked";var expired=mode=="vimeo-expiry";
        var jar=youtube?AccountRenewalChecks.Jar():new CookieContainer();
        var engine=new NativeEngine(httpFactory:_=>new MediaHttp(new SabrFixture.Handler(async message=>
        {
            var host=message.RequestUri!.Host;var path=message.RequestUri.AbsolutePath;
            if(host=="api.vimeo.com")
            {
                issuers++;if(message.Headers.Authorization?.Parameter!="fixture-token")throw new InvalidOperationException("Vimeo API lost its token.");
                return AuthenticationBatchChecks.Json(AccountRenewalChecks.ApiJson("https://video.example/"+(issuers==1?"one":"two")+".mp4"+(expired&&issuers==1?"?expire=1":"")));
            }
            if(host=="player.vimeo.com")
            {
                if(message.Method==HttpMethod.Post){passwords++;if(message.Headers.Authorization is not null)throw new InvalidOperationException("Vimeo API token entered the password gate.");return AuthenticationBatchChecks.Json(AccountRenewalChecks.VimeoConfig(view:3));}
                issuers++;return AuthenticationBatchChecks.Json(AccountRenewalChecks.VimeoConfig(view:4));
            }
            if(host=="www.youtube.com")
            {
                issuers++;
                if(path=="/watch")
                {
                    var node=JsonNode.Parse(YouTubeChecks.FixtureJson)!;
                    if(mode=="youtube-cookie")node["playabilityStatus"]=new JsonObject{["status"]="LOGIN_REQUIRED"};else Set(node,"https://r1.googlevideo.com/"+(issuers==1?"one":"two")+".mp4");
                    return AuthenticationBatchChecks.Json(AccountRenewalChecks.Config+"var ytInitialPlayerResponse="+node.ToJsonString()+";");
                }
                if(!message.Headers.Contains("Authorization")||message.Headers.GetValues("X-Goog-AuthUser").Single()!="1")throw new InvalidOperationException("Protected YouTube API requests lost account headers.");
                var player=JsonNode.Parse(YouTubeChecks.FixtureJson)!;Set(player,"https://r1.googlevideo.com/two.mp4");return AuthenticationBatchChecks.Json(player.ToJsonString());
            }
            media++;if(message.Headers.Authorization is not null||message.Headers.Contains("X-Goog-AuthUser"))throw new InvalidOperationException("Account authorization escaped to media.");
            if(revoked||mode.EndsWith("renew")&&path.Contains("one"))return AuthenticationBatchChecks.Json("private response",HttpStatusCode.Forbidden);
            await Task.Yield();return new(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)};
        }),new(){AllowAutoRedirect=false,CookieContainer=jar}));
        var url=new Uri(youtube?"https://www.youtube.com/watch?v=abcdefghijk":"https://vimeo.com/123");var output=Path.Combine(files.Directory,"finished.mp4");var archive=Path.Combine(files.Directory,"archive.txt");await File.WriteAllTextAsync(output,"prior");await File.WriteAllTextAsync(archive,"prior archive\n");
        var request=new NativeDownloadRequest{Url=url,OutputDirectory=files.Directory,OutputTemplate="finished.%(ext)s",FfmpegPath=ffmpeg,KeepOriginal=true,AddMetadata=true,DownloadArchiveFile=archive,
            Authentication=youtube?new(){YouTubeAccountIndex=1}:mode=="vimeo-password"?new(){VideoPassword="fixture-password"}:new(){AccessToken="fixture-token"},Retries=0};
        try
        {
            var result=await engine.DownloadAsync(request,null,null,default,_=>callbacks++);if(revoked)throw new InvalidOperationException("Revoked Vimeo media published success.");
            if(!result.Succeeded||result.OutputPaths.Count<2||callbacks!=result.OutputPaths.Count)throw new InvalidOperationException("Authenticated media lost originals or callbacks.");
            foreach(var path in result.OutputPaths)await Run(ffmpeg,["-v","error","-xerror","-i",path,"-f","null","-"],true);
            using var probe=JsonDocument.Parse(await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!,"ffprobe.exe"),["-v","error","-count_frames","-show_entries","stream=codec_type,nb_read_frames","-of","json",output]));
            var streams=probe.RootElement.GetProperty("streams").EnumerateArray().ToArray();if(streams.Length!=2||streams.Single(s=>s.GetProperty("codec_type").GetString()=="video").GetProperty("nb_read_frames").GetString()!="25")throw new InvalidOperationException("Account renewal media dropped frames or roles.");
            var before=issuers+media;var repeated=await engine.DownloadAsync(request,null,null,default,_=>callbacks++);if(!repeated.Skipped||before!=issuers+media||callbacks!=result.OutputPaths.Count)throw new InvalidOperationException("Account archive repeated HTTP or callbacks.");
            if(mode.EndsWith("renew")&&issuers!=2||expired&&issuers!=2||mode=="vimeo-password"&&passwords!=1)throw new InvalidOperationException("Account media renewal was not bounded to the expected exchange.");
        }
        catch(IOException)when(revoked){if(await File.ReadAllTextAsync(output)!="prior"||await File.ReadAllTextAsync(archive)!="prior archive\n"||callbacks!=0||media!=2)throw new InvalidOperationException("Revoked access changed prior output or archive.");}
        static void Set(JsonNode root,string url){root["streamingData"]!["formats"]=new JsonArray(new JsonObject{["itag"]=18,["mimeType"]="video/mp4; codecs=\"avc1.42c00a,mp4a.40.2\"",["url"]=url,["height"]=72,["width"]=128});root["streamingData"]!["adaptiveFormats"]=new JsonArray();}
    }
    private sealed class RenewCapture: IYouTubePlaybackSource
    {
        internal int Calls;
        public Task<PageMetadata> CaptureAsync(string videoId,NativeDownloadRequest request,CancellationToken ct)=>CaptureAsync(videoId,request,YouTubePlaybackCookies.Empty,ct);
        public Task<PageMetadata> CaptureAsync(string videoId,NativeDownloadRequest request,YouTubePlaybackCookies cookies,CancellationToken ct)
        {ct.ThrowIfCancellationRequested();Calls++;return Task.FromResult(YouTubePlayerParser.ParseResponse(SabrFixture.Player().ToJsonString(),videoId,sabrSession:new(videoId,new("https://r1.googlevideo.com/videoplayback?generation="+Calls),1,"fixture",[checked((byte)Calls)])));}
    }
    private static async Task AccountSabrMedia(string ffmpeg,string mode)
    {
        using var files=new Files();var video=Path.Combine(files.Directory,"v.mp4");var audio=Path.Combine(files.Directory,"a.m4a");
        await Run(ffmpeg,["-v","error","-y","-f","lavfi","-i","testsrc2=size=128x72:rate=10:duration=0.4","-an","-c:v","libx264","-preset","ultrafast","-movflags","frag_keyframe+empty_moov+default_base_moof",video]);
        await Run(ffmpeg,["-v","error","-y","-f","lavfi","-i","sine=frequency=550:sample_rate=44100:duration=0.4","-vn","-c:a","aac","-movflags","frag_keyframe+empty_moov+default_base_moof",audio]);
        var tracks=new Dictionary<SabrFormatId,byte[]>{{SabrFixture.Video,await File.ReadAllBytesAsync(video)},{SabrFixture.Audio,await File.ReadAllBytesAsync(audio)}};var capture=new RenewCapture();var reads=0;var callbacks=0;var jar=AccountRenewalChecks.Jar();
        var engine=new NativeEngine(httpFactory:_=>new MediaHttp(new SabrFixture.Handler(message=>
        {
            if(message.RequestUri!.Host=="www.youtube.com")return Task.FromResult(AuthenticationBatchChecks.Json(AccountRenewalChecks.Config+SabrFixture.Watch));
            reads++;if(message.Headers.Authorization is not null)throw new InvalidOperationException("Account header entered SABR CDN.");
            if(mode=="http"&&reads==1)return Task.FromResult(AuthenticationBatchChecks.Json("",HttpStatusCode.Forbidden));
            using var body=new MemoryStream();SabrFixture.Status(body,mode=="protection"?3U:2U);
            if(mode!="protection")
            {
                byte id=0;foreach(var pair in tracks)
                {
                    if(mode=="reload"&&reads==1&&pair.Key==SabrFixture.Audio)continue;
                    var bytes=pair.Value;var boundary=0;while(boundary+8<bytes.Length&&!bytes.AsSpan(boundary+4,4).SequenceEqual("moof"u8))boundary+=checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(boundary)));
                    SabrFixture.Segment(body,pair.Key,++id,0,bytes[..boundary]);SabrFixture.Segment(body,pair.Key,++id,1,bytes[boundary..],new(0,400,1000));SabrFixture.Metadata(body,pair.Key,1,400);
                }
                if(mode=="reload"&&reads==1)SabrFixture.Part(body,46,[]);
            }
            return Task.FromResult(SabrFixture.Response(body.ToArray()));
        }),new(){AllowAutoRedirect=false,CookieContainer=jar}),youTubePlaybackSource:capture);
        var output=Path.Combine(files.Directory,"sabr.mp4");var archive=Path.Combine(files.Directory,"archive.txt");await File.WriteAllTextAsync(output,"prior");await File.WriteAllTextAsync(archive,"prior archive\n");
        var request=new NativeDownloadRequest{Url=new("https://www.youtube.com/watch?v="+SabrFixture.VideoId),OutputDirectory=files.Directory,FileName="sabr",FfmpegPath=ffmpeg,FormatSelector="137+140:en.0",KeepOriginal=true,Authentication=new(){YouTubeAccountIndex=1},DownloadArchiveFile=archive,Retries=0};
        try
        {
            var result=await engine.DownloadAsync(request,null,null,default,_=>callbacks++);if(mode=="protection")throw new InvalidOperationException("Denied SABR playback published success.");
            if(!result.Succeeded||reads!=2||capture.Calls!=2||callbacks!=result.OutputPaths.Count)throw new InvalidOperationException("SABR renewal did not preserve one playback job.");
            foreach(var path in result.OutputPaths)await Run(ffmpeg,["-v","error","-xerror","-i",path,"-f","null","-"],true);
            using var probe=JsonDocument.Parse(await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!,"ffprobe.exe"),["-v","error","-count_frames","-show_entries","stream=codec_type,nb_read_frames","-of","json",output]));if(probe.RootElement.GetProperty("streams").EnumerateArray().Single(s=>s.GetProperty("codec_type").GetString()=="video").GetProperty("nb_read_frames").GetString()!="4")throw new InvalidOperationException("SABR reload repeated buffered video frames.");
            var count=reads;var repeat=await engine.DownloadAsync(request,null,null,default,_=>callbacks++);if(!repeat.Skipped||reads!=count||callbacks!=result.OutputPaths.Count)throw new InvalidOperationException("Renewed SABR archive repeated callbacks or playback.");
        }
        catch(NotSupportedException)when(mode=="protection"){if(capture.Calls!=1||await File.ReadAllTextAsync(output)!="prior"||await File.ReadAllTextAsync(archive)!="prior archive\n"||callbacks!=0)throw new InvalidOperationException("Explicit SABR denial renewed or changed prior data.");}
    }
}
