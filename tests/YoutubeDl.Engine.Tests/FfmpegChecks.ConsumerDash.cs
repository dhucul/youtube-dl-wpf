using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Authentication;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Media;
internal static partial class FfmpegChecks
{
    private static async Task ConsumerTransportTiming(string ffmpeg,bool embed)
    {
        using var files=new Files();var source=Path.Combine(files.Directory,"source.ts");await Run(ffmpeg,["-v","error","-y","-f","lavfi","-i","testsrc2=size=128x72:rate=12","-f","lavfi","-i","sine=frequency=570:sample_rate=48000","-t","3","-c:v","libx264","-preset","medium","-g","12","-bf","2","-c:a","aac","-f","mpegts",source]);var original=await File.ReadAllBytesAsync(source);
        foreach(var corrupt in new[]{false,true})
        {
            var bytes=(byte[])original.Clone();var changed=0;
            if(corrupt)for(var packet=0;packet+188<=bytes.Length;packet+=188)
            {
                var pid=((bytes[packet+1]&31)<<8)|bytes[packet+2];if(pid!=256||(bytes[packet+1]&64)==0)continue;var offset=packet+4;var adaptation=(bytes[packet+3]>>4)&3;if(adaptation is 0 or 2)continue;if(adaptation==3)offset+=1+bytes[offset];if(offset+19>packet+188||bytes[offset]!=0||bytes[offset+1]!=0||bytes[offset+2]!=1||(bytes[offset+7]&192)!=192)continue;
                var at=offset+14;var dts=((long)(bytes[at]&14)<<29)|((long)bytes[at+1]<<22)|((long)(bytes[at+2]&254)<<14)|((long)bytes[at+3]<<7)|((long)bytes[at+4]>>1);dts=(dts+15000)&0x1ffffffffL;bytes[at]=(byte)(0x11|((dts>>29)&14));bytes[at+1]=(byte)(dts>>22);bytes[at+2]=(byte)(1|((dts>>14)&254));bytes[at+3]=(byte)(dts>>7);bytes[at+4]=(byte)(1|((dts<<1)&254));changed++;
            }
            ConsumerDashChecks.Assert(!corrupt||changed>0);await File.WriteAllBytesAsync(source,bytes);var output=Path.Combine(files.Directory,"published.ts");if(embed)await FfmpegTool.EmbedAsync(ffmpeg,source,output,[],null,new Dictionary<string,string>{{"title","Timing fixture"}},default);else await FfmpegTool.RemuxAsync(ffmpeg,source,output,default);
            await Run(ffmpeg,["-v","error","-xerror","-err_detect","explode","-i",output,"-map","0:v:0","-map","0:a:0","-fps_mode:v","passthrough","-enc_time_base:v","demux","-f","null","-"],true);
            async Task<string[]> ReadPackets(string path,string role){using var doc=JsonDocument.Parse(await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!,"ffprobe.exe"),["-v","error","-select_streams",role=="video"?"v:0":"a:0","-show_packets","-show_data_hash","sha256","-show_entries","packet=pts_time,data_hash","-of","json",path]));var packets=doc.RootElement.GetProperty("packets").EnumerateArray().ToArray();var origin=decimal.Parse(packets[0].GetProperty("pts_time").GetString()!,System.Globalization.CultureInfo.InvariantCulture);return packets.Select(p=>p.GetProperty("data_hash").GetString()+"@"+(decimal.Parse(p.GetProperty("pts_time").GetString()!,System.Globalization.CultureInfo.InvariantCulture)-origin).ToString("F5",System.Globalization.CultureInfo.InvariantCulture)).ToArray();}
            foreach(var role in new[]{"video","audio"}){var before=await ReadPackets(source,role);var after=await ReadPackets(output,role);ConsumerDashChecks.Assert(before.SequenceEqual(after));}
        }
    }
    private static async Task ConsumerDashMedia(string ffmpeg,string mode)
    {
        using var files=new Files();var kaltura=mode.StartsWith("k-");var source=Path.Combine(files.Directory,kaltura?"source.mpd":"source.mp4");
        var arguments=new List<string>{"-v","error","-y","-f","lavfi","-i","testsrc2=size=128x72:rate=12","-f","lavfi","-i","sine=frequency=570:sample_rate=48000","-t","3","-map","0:v:0","-map","1:a:0","-c:v","libx264","-preset","ultrafast","-g","12","-c:a","aac"};
        if(kaltura)arguments.AddRange(["-f","dash","-seg_duration","1","-use_template","1","-use_timeline","1","-init_seg_name","init-$RepresentationID$.m4s","-media_seg_name","chunk-$RepresentationID$-$Number%05d$.m4s"]);arguments.Add(source);await Run(ffmpeg,arguments);
        var xml=kaltura?await File.ReadAllTextAsync(source):"";var data=kaltura?Array.Empty<byte>():await File.ReadAllBytesAsync(source);var parsed=kaltura?DashManifestParser.Parse(xml,new("https://cdn.example/g0/new.mpd")):null;
        var issuers=0;var media=0;var staleMedia=0;var callbacks=0;var rootReads=0;var aj=mode=="aj";var signed=mode=="bi-signed";
        var engine=new NativeEngine(httpFactory:configuration=>AdaptiveAccountChecks.Http(async(message,ct)=>
        {
            var url=message.RequestUri!;var action=Path.GetFileName(url.AbsolutePath);
            if(url.Host=="www.businessinsider.com")return AdaptiveAccountChecks.Text(ConsumerDashChecks.Bi(paid:mode=="bi-paid"));
            if(url.Host=="www.aljazeera.com")return AdaptiveAccountChecks.Text(ConsumerDashChecks.Aj());
            if(url.Host=="players.brightcove.net")return AdaptiveAccountChecks.Text("{\"account_id\":\"12345\",\"video_cloud\":{\"policy_key\":\"policy\"}}");
            if(url.Host is "cdn.jwplayer.com" or "edge.api.brightcove.com")
            {
                issuers++;ConsumerDashChecks.Assert(message.Headers.Referrer?.Host==(aj?"www.aljazeera.com":"www.businessinsider.com"));
                if(signed)ConsumerDashChecks.Assert(url.Query.StartsWith("?token="));var body=JsonNode.Parse(aj?SharedHostingChecks.Brightcove("https://cdn.example/source.mp4"):SharedHostingChecks.Jw("https://cdn.example/source.mp4",id:mode=="bi-switched"?"Other123":"AbCd1234"))!;
                if(aj){body["duration"]=3000;body["sources"]![0]!["size"]=data.LongLength;}else{body["playlist"]![0]!["duration"]=3;body["playlist"]![0]!["sources"]![0]!["filesize"]=data.LongLength;}return AdaptiveAccountChecks.Text(body.ToJsonString());
            }
            if(url.Host=="www.kaltura.com")
            {
                if(action=="startWidgetSession")return AdaptiveAccountChecks.Text("{\"partnerId\":309,\"ks\":\"guest-session\"}");
                if(action=="get"){issuers++;return AdaptiveAccountChecks.Text(SharedHostingChecks.KMedia.Replace("\"duration\":1","\"duration\":3"));}
                ConsumerDashChecks.Assert(action=="getPlaybackContext");var generation=issuers-1;
                return AdaptiveAccountChecks.Text(JsonSerializer.Serialize(new{objectType="KalturaPlaybackContext",actions=Array.Empty<object>(),flavorAssets=Array.Empty<object>(),playbackCaptions=Array.Empty<object>(),sources=new[]{new{format="mpegdash",deliveryProfileId="1",url="https://cdn.example/g"+generation+"/old.mpd",drm=Array.Empty<object>()},new{format="mpegdash",deliveryProfileId="2",url="https://cdn.example/g"+generation+"/new.mpd",drm=Array.Empty<object>()}}}));
            }
            ConsumerDashChecks.Assert(message.Headers.Authorization is null);
            if(action.EndsWith("mpd"))
            {
                rootReads++;var expired=action=="old.mpd"||mode=="k-all-expired";var doc=XDocument.Parse(xml);if(expired)foreach(var template in doc.Descendants().Where(e=>e.Name.LocalName=="SegmentTemplate"))foreach(var attribute in template.Attributes().Where(a=>a.Name.LocalName is "media" or "initialization").ToArray())attribute.Value+="?"+ConsumerDashChecks.Token(true);return AdaptiveAccountChecks.Text(doc.ToString());
            }
            Interlocked.Increment(ref media);if(url.Query.Contains("publisher-signature"))staleMedia++;
            if(mode=="k-denied"||mode=="k-renew"&&url.AbsolutePath.Contains("/g0/"))return AdaptiveAccountChecks.Text("denied",HttpStatusCode.Forbidden);
            await Task.Yield();return new(HttpStatusCode.OK){Content=new ByteArrayContent(kaltura?await File.ReadAllBytesAsync(Path.Combine(files.Directory,action),ct):data)};
        }));
        var output=Path.Combine(files.Directory,"published.mp4");var archive=Path.Combine(files.Directory,"archive.txt");await File.WriteAllTextAsync(output,"prior output");await File.WriteAllTextAsync(archive,"prior archive\n");
        var request=new NativeDownloadRequest{Url=new(kaltura?SharedHostingChecks.Url("kaltura").AbsoluteUri:aj?ConsumerDashChecks.AjUrl:ConsumerDashChecks.BiUrl),OutputDirectory=files.Directory,OutputTemplate="published.%(ext)s",FfmpegPath=ffmpeg,KeepOriginal=true,DownloadArchiveFile=archive,Retries=0,FragmentRetries=0,
            FormatSelector=mode=="k-explicit"?"dash-1":"",Authentication=signed?new(){Hosting=SharedHostingChecks.Credential("jwplatform") with {AccessToken="",SigningKey="publisher-secret"}}:new()};
        try
        {
            var result=await engine.DownloadAsync(request,null,null,default,_=>callbacks++);if(mode is "k-explicit" or "k-all-expired" or "k-denied" or "bi-paid" or "bi-switched")throw new InvalidOperationException("Failed consumer/DASH job published success.");
            ConsumerDashChecks.Assert(result.Succeeded&&callbacks==result.OutputPaths.Count&&staleMedia==0);foreach(var path in result.OutputPaths.Where(p=>Path.GetExtension(p) is ".mp4" or ".m4a"))await Run(ffmpeg,["-v","error","-xerror","-i",path,"-f","null","-"],true);
            using var probe=JsonDocument.Parse(await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!,"ffprobe.exe"),["-v","error","-count_frames","-show_entries","stream=codec_type,nb_read_frames","-of","json",output]));var streams=probe.RootElement.GetProperty("streams").EnumerateArray().ToArray();ConsumerDashChecks.Assert(streams.Single(s=>s.GetProperty("codec_type").GetString()=="video").GetProperty("nb_read_frames").GetString()=="36"&&streams.Any(s=>s.GetProperty("codec_type").GetString()=="audio"));
            foreach(var role in new[]{"video","audio"})
            {
                var reference=source;if(kaltura){var rep=parsed!.Representations.Single(r=>role=="video"?r.IsVideo:r.IsAudio);reference=Path.Combine(files.Directory,"reference-"+role+".mp4");await using var joined=File.Create(reference);foreach(var part in new[]{rep.Initialization!}.Concat(rep.Segments.Select(s=>s.Url))){var bytes=await File.ReadAllBytesAsync(Path.Combine(files.Directory,Path.GetFileName(part.AbsolutePath)));await joined.WriteAsync(bytes);}}
                async Task<string[]> Packets(string file){using var json=JsonDocument.Parse(await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!,"ffprobe.exe"),["-v","error","-select_streams",role=="video"?"v:0":"a:0","-show_packets","-show_data_hash","sha256","-show_entries","packet=data_hash","-of","json",file]));return json.RootElement.GetProperty("packets").EnumerateArray().Select(p=>p.GetProperty("data_hash").GetString()!).ToArray();}
                var before=await Packets(reference);var after=await Packets(output);ConsumerDashChecks.Assert(before.SequenceEqual(after));
            }
            var previous=media;var repeat=await engine.DownloadAsync(request,null,null,default,_=>callbacks++);ConsumerDashChecks.Assert(repeat.Skipped&&previous==media&&callbacks==result.OutputPaths.Count);
        }
        catch(Exception e)when(mode is "k-explicit" or "k-all-expired" or "k-denied" or "bi-paid" or "bi-switched"&&e is IOException or InvalidDataException or NotSupportedException or HttpRequestException)
        {ConsumerDashChecks.Assert(await File.ReadAllTextAsync(output)=="prior output"&&await File.ReadAllTextAsync(archive)=="prior archive\n"&&callbacks==0&&staleMedia==0);if(mode is "k-explicit" or "k-all-expired")ConsumerDashChecks.Assert(media==0);}
    }
}
