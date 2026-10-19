using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Authentication;
using YoutubeDl.Engine.Download;

internal static partial class FfmpegChecks
{
    private static async Task SharedHostingMedia(string ffmpeg,string mode)
    {
        using var files=new Files();var provider=mode.StartsWith("wistia")?"wistia":mode.StartsWith("brightcove")?"brightcove":mode.StartsWith("kaltura")?"kaltura":"jwplatform";
        var hls=mode.Contains("hls");var dash=mode.Contains("dash");var adaptive=hls||dash;var delegated=mode.Contains("delegated");var playlist=mode.Contains("playlist");var login=mode.Contains("login");
        var source=Path.Combine(files.Directory,"source.mp4");var mpd=Path.Combine(files.Directory,"source.mpd");
        if(adaptive)await Run(ffmpeg,["-v","error","-y","-f","lavfi","-i","testsrc2=size=128x72:rate=12","-f","lavfi","-i","sine=frequency=610:sample_rate=48000","-t","3","-map","0:v:0","-map","1:a:0","-c:v","libx264","-preset","ultrafast","-g","12","-c:a","aac","-f","dash","-seg_duration","1","-use_template","1","-use_timeline","1","-init_seg_name","init-$RepresentationID$.m4s","-media_seg_name","chunk-$RepresentationID$-$Number%05d$.m4s",mpd]);
        else await Run(ffmpeg,["-v","error","-y","-f","lavfi","-i","testsrc2=size=128x72:rate=12","-f","lavfi","-i","sine=frequency=610:sample_rate=48000","-t","3","-c:v","libx264","-preset","ultrafast","-c:a","aac",source]);
        var bytes=adaptive?Array.Empty<byte>():await File.ReadAllBytesAsync(source);var xml=adaptive?await File.ReadAllTextAsync(mpd):"";var parsed=adaptive?DashManifestParser.Parse(xml,new("https://cdn.example/g0/root.mpd")):null;
        var issuers=0;var reads=0;var logins=0;var widgets=0;var callbacks=0;var caption=provider=="brightcove"&&!adaptive;var credential=SharedHostingChecks.Credential(provider,playlist);if(mode.Contains("signed"))credential=credential with {AccessToken="",SigningKey="publisher-secret"};
        var url=delegated?new Uri("https://article.example/page"):SharedHostingChecks.Url(provider,playlist);var player=SharedHostingChecks.Url(provider,playlist);
        var engine=new NativeEngine(httpFactory:configuration=>AdaptiveAccountChecks.Http(async(message,ct)=>
        {
            var uri=message.RequestUri!;var name=Path.GetFileName(uri.AbsolutePath);
            if(uri.Host=="article.example"){SharedHostingChecks.Assert(message.Headers.Authorization is null);return AdaptiveAccountChecks.Text("<iframe src='"+player.AbsoluteUri+"'></iframe>");}
            if(uri.Host=="players.brightcove.net"){SharedHostingChecks.Assert(message.Headers.Authorization is null);return AdaptiveAccountChecks.Text("{\"account_id\":\"12345\",\"video_cloud\":{\"policy_key\":\"policy-fixture\"}}");}
            if(uri.Host=="www.kaltura.com")
            {
                using var body=JsonDocument.Parse(await message.Content!.ReadAsStringAsync(ct));var root=body.RootElement;
                if(name=="startWidgetSession"){widgets++;throw new InvalidOperationException("Authenticated Kaltura used a guest session.");}
                if(name=="loginByLoginId"){logins++;SharedHostingChecks.Assert(root.GetProperty("password").GetString()=="password-fixture"&&root.TryGetProperty("otp",out _)==(logins==1));return AdaptiveAccountChecks.Text("\"ks-"+logins+"\"");}
                if(name=="get"&&uri.AbsolutePath.Contains("/session/"))return AdaptiveAccountChecks.Text(JsonSerializer.Serialize(new{partnerId=309,userId="user-fixture",type=0,expiry=DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()}));
                if(name=="get"){issuers++;return AdaptiveAccountChecks.Text(SharedHostingChecks.KMedia.Replace("\"duration\":1","\"duration\":3"));}
                if(name=="getPlaybackContext")
                {
                    var context=JsonNode.Parse(SharedHostingChecks.KContext)!;if(adaptive){context["flavorAssets"]=new JsonArray();context["sources"]=new JsonArray(new JsonObject{["format"]=hls?"applehttp":"mpegdash",["deliveryProfileId"]="20",["url"]=RootUrl(),["drm"]=new JsonArray()});}return AdaptiveAccountChecks.Text(context.ToJsonString());
                }
                SharedHostingChecks.Assert(name=="getUrl");return AdaptiveAccountChecks.Text(JsonSerializer.Serialize(RootUrl()));
            }
            if(uri.Host is "api.wistia.com" or "cdn.jwplayer.com" or "edge.api.brightcove.com")
            {
                if(provider=="jwplatform")SharedHostingChecks.Assert(uri.Query.StartsWith("?token=",StringComparison.Ordinal)&&message.Headers.Authorization is null);else SharedHostingChecks.Assert(message.Headers.Authorization?.Parameter=="token-fixture");
                issuers++;
                if(provider=="wistia")return AdaptiveAccountChecks.Text(JsonSerializer.Serialize(new{hashed_id=SharedHostingChecks.WistiaId,status="ready",name="fixture",duration=3,type="Video",assets=new[]{new{type=adaptive?"hls_video":"hd_mp4_video",url=RootUrl(),container=adaptive?"m3u8":"mp4",contentType=adaptive?"application/x-mpegURL":"video/mp4",width=128,height=72,size=adaptive?(long?)null:bytes.LongLength,codec="h264"}}}));
                if(provider=="jwplatform")
                {
                    var root=JsonNode.Parse(SharedHostingChecks.Jw(RootUrl()))!;var item=root["playlist"]![0]!;item["duration"]=3;var offered=item["sources"]![0]!;offered["type"]=hls?"application/x-mpegURL":"video/mp4";offered["filesize"]=adaptive?null:JsonValue.Create(bytes.LongLength);if(mode=="changed"&&issuers>1)offered["height"]=144;return AdaptiveAccountChecks.Text(root.ToJsonString());
                }
                var bc=JsonNode.Parse(SharedHostingChecks.Brightcove(RootUrl()))!;bc["duration"]=3000;var bsrc=bc["sources"]![0]!;bsrc["type"]=dash?"application/dash+xml":"video/mp4";bsrc["size"]=adaptive?null:JsonValue.Create(bytes.LongLength);
                if(caption)bc["text_tracks"]=new JsonArray(new JsonObject{["kind"]="captions",["srclang"]="en",["src"]="https://cdn.example/g"+(issuers-1)+"/caption.vtt"});return AdaptiveAccountChecks.Text(bc.ToJsonString());
            }
            SharedHostingChecks.Assert(uri.Host=="cdn.example"&&message.Headers.Authorization is null&&message.Headers.Accept.All(a=>!a.ToString().Contains("pk=")));
            if(name=="root.mpd")return AdaptiveAccountChecks.Text(xml);
            if(name=="root.m3u8")return AdaptiveAccountChecks.Text("#EXTM3U\n#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID=\"a\",NAME=\"Audio\",DEFAULT=YES,URI=\"audio.m3u8\"\n#EXT-X-STREAM-INF:BANDWIDTH=100000,RESOLUTION=128x72,CODECS=\"avc1.42c01e,mp4a.40.2\",AUDIO=\"a\"\nvideo.m3u8\n");
            if(name is "video.m3u8" or "audio.m3u8")return AdaptiveAccountChecks.Text(Hls(parsed!.Representations.Single(r=>name=="video.m3u8"?r.IsVideo:r.IsAudio)));
            Interlocked.Increment(ref reads);if(mode=="cancel")await Task.Delay(Timeout.Infinite,ct);
            if(mode=="revoked"||uri.AbsolutePath.Contains("/g0/")&&(!adaptive||reads>2))return AdaptiveAccountChecks.Text("expired",HttpStatusCode.Forbidden);
            if(name=="caption.vtt")return AdaptiveAccountChecks.Text("WEBVTT\n\n00:00.000 --> 00:02.500\nscoped hosting caption\n");
            return new(HttpStatusCode.OK){Content=new ByteArrayContent(adaptive?await File.ReadAllBytesAsync(Path.Combine(files.Directory,name),ct):bytes)};
        }));
        var output=Path.Combine(files.Directory,"published.mp4");var archive=Path.Combine(files.Directory,"archive.txt");await File.WriteAllTextAsync(output,"prior output");await File.WriteAllTextAsync(archive,"prior archive\n");
        var request=new NativeDownloadRequest{Url=url,OutputDirectory=files.Directory,OutputTemplate="published.%(ext)s",FfmpegPath=ffmpeg,KeepOriginal=true,DownloadArchiveFile=archive,Retries=0,FragmentRetries=0,ConcurrentFragments=3,WriteSubs=caption,EmbedSubs=caption,
            Authentication=login?new(){Username="user-fixture",Password="password-fixture",TwoFactor="123456"}:new(){Hosting=credential}};
        using var stop=new CancellationTokenSource();if(mode=="cancel")stop.CancelAfter(150);
        try
        {
            var result=await engine.DownloadAsync(request,null,null,stop.Token,_=>callbacks++);if(mode is "revoked" or "changed" or "cancel")throw new InvalidOperationException("Rejected hosting media published success.");
            SharedHostingChecks.Assert(result.Succeeded&&issuers==2&&widgets==0&&callbacks==result.OutputPaths.Count&&(!login||logins==1));
            foreach(var path in result.OutputPaths.Where(p=>Path.GetExtension(p) is ".mp4" or ".m4a"))await Run(ffmpeg,["-v","error","-xerror","-i",path,"-f","null","-"],true);
            using var probe=JsonDocument.Parse(await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!,"ffprobe.exe"),["-v","error","-count_frames","-show_entries","stream=codec_type,nb_read_frames","-of","json",output]));var streams=probe.RootElement.GetProperty("streams").EnumerateArray().ToArray();SharedHostingChecks.Assert(streams.Single(s=>s.GetProperty("codec_type").GetString()=="video").GetProperty("nb_read_frames").GetString()=="36"&&streams.Any(s=>s.GetProperty("codec_type").GetString()=="audio")&&(!caption||streams.Any(s=>s.GetProperty("codec_type").GetString()=="subtitle")));
            foreach(var role in new[]{"video","audio"})
            {
                var reference=source;if(adaptive){var rep=parsed!.Representations.Single(r=>role=="video"?r.IsVideo:r.IsAudio);reference=Path.Combine(files.Directory,"reference-"+role+".mp4");await using var joined=File.Create(reference);foreach(var part in new[]{rep.Initialization!}.Concat(rep.Segments.Select(s=>s.Url))){var data=await File.ReadAllBytesAsync(Path.Combine(files.Directory,Path.GetFileName(part.AbsolutePath)));await joined.WriteAsync(data);}}
                async Task<string[]> Packets(string path){using var data=JsonDocument.Parse(await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!,"ffprobe.exe"),["-v","error","-select_streams",role=="video"?"v:0":"a:0","-show_packets","-show_data_hash","sha256","-show_entries","packet=data_hash","-of","json",path]));return data.RootElement.GetProperty("packets").EnumerateArray().Select(p=>p.GetProperty("data_hash").GetString()!).ToArray();}
                var originalPackets=await Packets(reference);var actualPackets=await Packets(output);SharedHostingChecks.Assert(originalPackets.SequenceEqual(actualPackets));
            }
            var previousIssuers=issuers;var previousReads=reads;var repeat=await engine.DownloadAsync(request,null,null,default,_=>callbacks++);SharedHostingChecks.Assert(repeat.Skipped&&reads==previousReads&&issuers==previousIssuers+(delegated||playlist?1:0)&&callbacks==result.OutputPaths.Count);
        }
        catch(Exception e)when(mode is "revoked" or "changed" or "cancel"&&e is IOException or InvalidDataException or HttpRequestException or NotSupportedException or OperationCanceledException)
        {SharedHostingChecks.Assert(await File.ReadAllTextAsync(output)=="prior output"&&await File.ReadAllTextAsync(archive)=="prior archive\n"&&callbacks==0);}
        string RootUrl()=>"https://cdn.example/g"+(issuers-1)+"/"+(adaptive?(hls?"root.m3u8":"root.mpd"):"source.mp4");
        string Hls(DashRepresentation rep)
        {
            var doc=XDocument.Parse(xml);var sourceRep=doc.Descendants().Single(e=>e.Name.LocalName=="Representation"&&(string?)e.Attribute("id")==rep.Id);var template=sourceRep.Descendants().Single(e=>e.Name.LocalName=="SegmentTemplate");var scale=long.Parse((string?)template.Attribute("timescale")??"1");var durations=new List<double>();foreach(var s in template.Descendants().Where(e=>e.Name.LocalName=="S")){var repeat=int.Parse((string?)s.Attribute("r")??"0");for(var i=0;i<=repeat;i++)durations.Add(long.Parse((string)s.Attribute("d")!)/(double)scale);}
            return "#EXTM3U\n#EXT-X-TARGETDURATION:2\n#EXT-X-MEDIA-SEQUENCE:1\n#EXT-X-MAP:URI=\""+Path.GetFileName(rep.Initialization!.AbsolutePath)+"\"\n"+string.Join("",rep.Segments.Zip(durations,(segment,duration)=>"#EXTINF:"+duration.ToString("0.########",CultureInfo.InvariantCulture)+",\n"+Path.GetFileName(segment.Url.AbsolutePath)+"\n"))+"#EXT-X-ENDLIST\n";
        }
    }
}
