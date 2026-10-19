using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Authentication;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;

internal static partial class FfmpegChecks
{
    private static async Task AdaptiveAccountMedia(string ffmpeg,string mode)
    {
        using var files=new Files();var original=Path.Combine(files.Directory,"source.mpd");
        await Run(ffmpeg,["-v","error","-y","-f","lavfi","-i","testsrc2=size=128x72:rate=12","-f","lavfi","-i","sine=frequency=590:sample_rate=48000","-t","3","-map","0:v:0","-map","1:a:0","-c:v","libx264","-preset","ultrafast","-g","12","-c:a","aac","-f","dash","-seg_duration","1","-use_template","1","-use_timeline","1","-init_seg_name","init-$RepresentationID$.m4s","-media_seg_name","chunk-$RepresentationID$-$Number%05d$.m4s",original]);
        var xml=await File.ReadAllTextAsync(original);var parsed=DashManifestParser.Parse(xml,new("https://cdn.example/g0/root.mpd"));var video=parsed.Representations.Single(r=>r.IsVideo);var audio=parsed.Representations.Single(r=>r.IsAudio);
        var hls=mode.Contains("hls");var youtube=mode.StartsWith("youtube");var caption=mode=="vimeo-api-caption";var oauth=mode=="vimeo-oauth-dash";var issuers=0;var mediaReads=0;var renewedReads=0;var callbacks=0;
        var engine=new NativeEngine(httpFactory:_=>AdaptiveAccountChecks.Http(async(message,ct)=>
        {
            var url=message.RequestUri!;var name=Path.GetFileName(url.AbsolutePath);
            if(url.Host=="api.vimeo.com")
            {
                if(message.Headers.Authorization?.Parameter!= "token-fixture")throw new InvalidOperationException("The API lost its scoped credential.");
                if(url.AbsolutePath=="/me")return AdaptiveAccountChecks.Text("{\"uri\":\"/users/42\"}");
                if(url.AbsolutePath.EndsWith("/texttracks"))return AdaptiveAccountChecks.Text(JsonSerializer.Serialize(new{total=1,data=new[]{new{uri="/videos/123/texttracks/1",language="en",active=true,link="https://cdn.example/g"+(issuers-1)+"/caption.vtt"}},paging=new{next=(string?)null}}));
                issuers++;return AdaptiveAccountChecks.Text(Api(issuers-1));
            }
            if(url.Host=="www.youtube.com")
            {
                issuers++;var player=JsonNode.Parse(YouTubeChecks.FixtureJson)!;player["videoDetails"]!["lengthSeconds"]="3";player["streamingData"]!["formats"]=new JsonArray();player["streamingData"]!["adaptiveFormats"]=new JsonArray();player["streamingData"]![hls?"hlsManifestUrl":"dashManifestUrl"]="https://r1.googlevideo.com/g"+(issuers-1)+"/root."+(hls?"m3u8":"mpd");return AdaptiveAccountChecks.Text(AccountRenewalChecks.Config+"var ytInitialPlayerResponse="+player.ToJsonString()+";");
            }
            if(message.Headers.Authorization is not null||message.Headers.Contains("X-Goog-AuthUser"))throw new InvalidOperationException("An issuer credential entered adaptive media.");
            if(name=="root.mpd")
            {
                if(mode=="live-dash"){var doc=XDocument.Parse(xml);var root=doc.Root!;root.SetAttributeValue("type","dynamic");root.SetAttributeValue("availabilityStartTime","2026-01-01T00:00:00Z");root.SetAttributeValue("publishTime","2026-01-01T00:00:04Z");root.Attribute("mediaPresentationDuration")?.Remove();return AdaptiveAccountChecks.Text(doc.ToString());}
                return AdaptiveAccountChecks.Text(mode=="changed"&&url.AbsolutePath.Contains("/g1/")?xml.Replace("height=\"72\"","height=\"144\""):xml);
            }
            if(name=="root.m3u8")return AdaptiveAccountChecks.Text("#EXTM3U\n#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID=\"a\",NAME=\"Audio\",DEFAULT=YES,URI=\"audio.m3u8\"\n#EXT-X-STREAM-INF:BANDWIDTH=100000,RESOLUTION=128x72,CODECS=\"avc1.42c01e,mp4a.40.2\",AUDIO=\"a\"\nvideo.m3u8\n");
            if(name is "video.m3u8" or "audio.m3u8")return AdaptiveAccountChecks.Text(Hls(name=="video.m3u8"?video:audio));
            mediaReads++;if(mode=="cancel"){await Task.Delay(Timeout.Infinite,ct);}
            if(mode=="revoked"||caption&&name=="caption.vtt"&&url.AbsolutePath.Contains("/g0/")||!caption&&mediaReads>2&&url.AbsolutePath.Contains("/g0/"))return AdaptiveAccountChecks.Text("expired",HttpStatusCode.Forbidden);
            if(url.AbsolutePath.Contains("/g1/"))renewedReads++;
            if(name=="caption.vtt")return AdaptiveAccountChecks.Text("WEBVTT\n\n00:00.000 --> 00:02.500\ncaption token renewal\n");
            return new(HttpStatusCode.OK){Content=new ByteArrayContent(await File.ReadAllBytesAsync(Path.Combine(files.Directory,name),ct))};
        },youtube?AccountRenewalChecks.Jar():null));
        var output=Path.Combine(files.Directory,"published.mp4");var archive=Path.Combine(files.Directory,"archive.txt");await File.WriteAllTextAsync(output,"prior output");await File.WriteAllTextAsync(archive,"prior archive\n");
        var request=new NativeDownloadRequest{Url=new(youtube?"https://www.youtube.com/watch?v=abcdefghijk":"https://vimeo.com/123"),OutputDirectory=files.Directory,OutputTemplate="published.%(ext)s",FfmpegPath=ffmpeg,KeepOriginal=true,DownloadArchiveFile=archive,Retries=0,FragmentRetries=0,ConcurrentFragments=3,WriteSubs=caption,EmbedSubs=caption,
            Authentication=youtube?new(){YouTubeAccountIndex=1}:oauth?new(){OAuth=new(){Provider=NativeOAuthProvider.Vimeo,AccessToken="token-fixture"}}:new(){AccessToken="token-fixture"}};
        if(mode=="live-dash")request=request with {LiveFromStart=true,LiveDurationSeconds=2.9};
        using var stop=new CancellationTokenSource();if(mode=="cancel")stop.CancelAfter(150);
        try
        {
            var result=await engine.DownloadAsync(request,null,null,stop.Token,_=>callbacks++);if(mode is "revoked" or "changed" or "cancel")throw new InvalidOperationException("Rejected adaptive recording published success.");
            if(!result.Succeeded||callbacks!=result.OutputPaths.Count||issuers!=2||renewedReads==0)throw new InvalidOperationException("Adaptive signed renewal did not complete one bounded job.");
            foreach(var path in result.OutputPaths.Where(p=>Path.GetExtension(p) is ".mp4" or ".m4a"))await Run(ffmpeg,["-v","error","-xerror","-i",path,"-f","null","-"],true);
            using var probe=JsonDocument.Parse(await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!,"ffprobe.exe"),["-v","error","-count_frames","-show_entries","stream=codec_type,nb_read_frames:format=duration","-of","json",output]));
            var streams=probe.RootElement.GetProperty("streams").EnumerateArray().ToArray();var frames=int.Parse(streams.Single(s=>s.GetProperty("codec_type").GetString()=="video").GetProperty("nb_read_frames").GetString()!);if((mode=="live-dash"?frames<34:frames!=36)||!streams.Any(s=>s.GetProperty("codec_type").GetString()=="audio"))throw new InvalidOperationException("Adaptive renewal lost complete video frames or audio.");
            if(mode!="live-dash")
            {
                foreach(var representation in new[]{video,audio})
                {
                    var reference=Path.Combine(files.Directory,representation.IsVideo?"reference-video.mp4":"reference-audio.m4a");
                    await using(var joined=File.Create(reference))foreach(var url in new[]{representation.Initialization!}.Concat(representation.Segments.Select(s=>s.Url))){var bytes=await File.ReadAllBytesAsync(Path.Combine(files.Directory,Path.GetFileName(url.AbsolutePath)));await joined.WriteAsync(bytes);}
                    async Task<string[]> Packets(string path){using var data=JsonDocument.Parse(await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!,"ffprobe.exe"),["-v","error","-select_streams",representation.IsVideo?"v:0":"a:0","-show_packets","-show_data_hash","sha256","-show_entries","packet=data_hash","-of","json",path]));return data.RootElement.GetProperty("packets").EnumerateArray().Select(p=>p.GetProperty("data_hash").GetString()!).ToArray();}
                    var originalPackets=await Packets(reference);var outputPackets=await Packets(output);if(!originalPackets.SequenceEqual(outputPackets))throw new InvalidOperationException("Adaptive renewal changed or lost ordered coded source packets.");
                }
            }
            if(caption&&!Directory.EnumerateFiles(files.Directory,"published*.vtt").Any(p=>File.ReadAllText(p).Contains("caption token renewal")))throw new InvalidOperationException("Account caption renewal lost the selected track.");
            var before=issuers+mediaReads;var repeat=await engine.DownloadAsync(request,null,null,default,_=>callbacks++);if(!repeat.Skipped||before!=issuers+mediaReads||callbacks!=result.OutputPaths.Count)throw new InvalidOperationException("Adaptive account archive repeated HTTP or callbacks.");
        }
        catch(Exception e)when(mode is "revoked" or "changed" or "cancel"&&e is IOException or HttpRequestException or InvalidDataException or NotSupportedException or OperationCanceledException)
        {if(await File.ReadAllTextAsync(output)!="prior output"||await File.ReadAllTextAsync(archive)!="prior archive\n"||callbacks!=0)throw new InvalidOperationException("Failed adaptive renewal changed prior files or archive.");}
        string Api(int generation)=>JsonSerializer.Serialize(new{uri="/videos/123",name="Signed adaptive fixture",duration=3,play=hls?new Dictionary<string,object>{{"hls",new{link="https://cdn.example/g"+generation+"/root.m3u8"}}}:new Dictionary<string,object>{{"dash",new{link="https://cdn.example/g"+generation+"/root.mpd"}}}});
        string Hls(DashRepresentation representation)
        {
            var doc=XDocument.Parse(xml);var rep=doc.Descendants().Single(e=>e.Name.LocalName=="Representation"&&(string?)e.Attribute("id")==representation.Id);var template=rep.Descendants().Single(e=>e.Name.LocalName=="SegmentTemplate");var scale=long.Parse((string?)template.Attribute("timescale")??"1");var durations=new List<double>();
            foreach(var s in template.Descendants().Where(e=>e.Name.LocalName=="S")){var repeat=int.Parse((string?)s.Attribute("r")??"0");for(var i=0;i<=repeat;i++)durations.Add(long.Parse((string)s.Attribute("d")!)/(double)scale);}
            return "#EXTM3U\n#EXT-X-TARGETDURATION:2\n#EXT-X-MEDIA-SEQUENCE:1\n#EXT-X-MAP:URI=\""+Path.GetFileName(representation.Initialization!.AbsolutePath)+"\"\n"+string.Join("",representation.Segments.Zip(durations,(segment,duration)=>"#EXTINF:"+duration.ToString("0.########",CultureInfo.InvariantCulture)+",\n"+Path.GetFileName(segment.Url.AbsolutePath)+"\n"))+"#EXT-X-ENDLIST\n";
        }
    }
}
