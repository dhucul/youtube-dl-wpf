using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Download;

internal static partial class FfmpegChecks
{
    private static async Task LiveCaptionMedia(string ffmpeg,string mode,string? recovery=null,string? workerDirectory=null,bool worker=false)
    {
        using var files=new Files(workerDirectory,!worker);var original=Path.Combine(files.Directory,"source.mpd");var url=new Uri("https://live.example/"+(mode.StartsWith("hls")?"master.m3u8":"manifest.mpd"));
        if(!worker)await Run(ffmpeg,["-v","error","-y","-f","lavfi","-i","testsrc2=size=128x72:rate=25","-f","lavfi","-i","sine=frequency=590:sample_rate=48000","-t","8","-c:v","libx264","-preset","ultrafast","-g","50","-bf","0","-c:a",mode=="dash-lossless"?"alac":"aac","-f","dash","-seg_duration","2","-use_template","1","-use_timeline","1","-init_seg_name","init-$RepresentationID$.m4s","-media_seg_name","chunk-$RepresentationID$-$Number%05d$.m4s",original]);
        var sourceXml=await File.ReadAllTextAsync(original);var source=DashManifestParser.Parse(sourceXml,new("https://live.example/source.mpd"));
        var assets=Directory.EnumerateFiles(files.Directory).ToDictionary(path=>Path.GetFileName(path)!,File.ReadAllBytes);var video=source.Representations.Single(r=>r.IsVideo);var audio=source.Representations.Single(r=>r.IsAudio);
        var timings=source.Representations.ToDictionary(r=>r.Id,r=>Timing(r.Id));var ttml=mode=="dash-stpp";byte[]? init=null;var captionParts=new List<byte[]>();
        for(var i=0;i<8;i++)
        {
            var sample=ttml?Encoding.UTF8.GetBytes("<tt xmlns=\"http://www.w3.org/ns/ttml\"><body><div><p begin=\"0s\" end=\"8s\">Continuing live cue.</p></div></body></tt>"):DashCaptionChecks.Sample("Continuing live cue.");
            var movie=DashCaptionChecks.Movie([sample],out var offset,checked((ulong)i*1000),ttml:ttml);init??=movie[..offset];var part=movie[offset..];captionParts.Add(part);assets["caption-"+(i*1000)+".m4s"]=part;
        }
        assets["caption-init.mp4"]=init!;assets["captions.mp4"]=init!.Concat(captionParts.SelectMany(p=>p)).ToArray();
        var header="WEBVTT\nX-TIMESTAMP-MAP=LOCAL:00:00:00.000,MPEGTS:0\n\n";assets["caption-header.vtt"]=Encoding.UTF8.GetBytes(header);
        var key=Enumerable.Range(1,16).Select(i=>(byte)i).ToArray();assets["caption-key"]=key;
        for(var i=0;i<4;i++)
        {
            var payload=(mode=="hls-reset"?"cue"+i/2+"\n00:00:00.500 --> 00:00:04.500\nPeriod "+i/2+" caption.\n\n":"cue\n00:00:00.500 --> 00:00:07.500\nContinuing live cue.\n\n");var bytes=Encoding.UTF8.GetBytes(payload);
            if(mode=="hls-encrypted"){using var aes=Aes.Create();aes.Key=key;var iv=new byte[16];BinaryPrimitives.WriteUInt64BigEndian(iv.AsSpan(8),checked((ulong)(700+i)));bytes=aes.EncryptCbc(bytes,iv);}
            assets["caption-"+i+".vtt"]=bytes;
        }
        var hlsParts=Enumerable.Range(0,4).Select(i=>assets["caption-"+i+".vtt"]).ToArray();assets["captions.vtt"]=Encoding.UTF8.GetBytes(header).Concat(hlsParts.SelectMany(p=>p)).ToArray();
        var vp=0;var ap=0;var mp=0;var cp=0;var capReads=0;var mediaReads=0;var callbacks=0;var resumed=false;var failed=mode is "hls-gap" or "dash-gap" or "dash-corrupt" or "dash-cancel"||recovery is "corrupt" or "payload" or "denied" or "missing";
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(45));if(mode=="dash-cancel")timeout.CancelAfter(500);
        var engine=new NativeEngine(httpFactory:_=>AuthenticationBatchChecks.Http(async(message,ct)=>
        {
            var name=Path.GetFileName(message.RequestUri!.AbsolutePath);
            if(worker&&name.StartsWith("chunk-")&&CaptionGate(files.Directory,!mode.StartsWith("hls"),mode is "dash-video" or "dash-audio"?1:2))await PauseDurableWorker(files.Directory,ct);
            if(resumed&&recovery=="denied"&&name.StartsWith("caption"))return AuthenticationBatchChecks.Json("denied",HttpStatusCode.Forbidden);
            if(name=="manifest.mpd")return AuthenticationBatchChecks.Json(Snapshot(Math.Min(Interlocked.Increment(ref mp)-1,2)));
            if(name=="master.m3u8")return AuthenticationBatchChecks.Json("#EXTM3U\n#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID=\"a\",NAME=\"Audio\",DEFAULT=YES,URI=\"audio.m3u8\"\n#EXT-X-MEDIA:TYPE=SUBTITLES,GROUP-ID=\"c\",NAME=\"English\",LANGUAGE=\"en\",URI=\"captions.m3u8\"\n#EXT-X-STREAM-INF:BANDWIDTH=100000,RESOLUTION=128x72,CODECS=\"avc1.42c00a,mp4a.40.2\",AUDIO=\"a\",SUBTITLES=\"c\"\nvideo.m3u8\n");
            if(name is "video.m3u8" or "audio.m3u8"){var isVideo=name=="video.m3u8";var poll=isVideo?Interlocked.Increment(ref vp):Interlocked.Increment(ref ap);return AuthenticationBatchChecks.Json(Hls(isVideo?video:audio,Math.Min(poll,3),isVideo));}
            if(name=="captions.m3u8")return AuthenticationBatchChecks.Json(HlsCaptions());
            if(name.StartsWith("caption")||name=="captions.mp4")
            {Interlocked.Increment(ref capReads);if(mode=="dash-cancel")await Task.Delay(Timeout.Infinite,ct);if(mode=="dash-corrupt"&&name=="caption-4000.m4s")return AuthenticationBatchChecks.Json("broken caption");}
            else Interlocked.Increment(ref mediaReads);
            if(resumed&&recovery is not null&&(name=="caption-0.vtt"||name=="caption-0.m4s"||mode!="hls-reset"&&name.EndsWith("00001.m4s")))throw new InvalidOperationException("Retry requested a dropped beginning.");
            var bytes=assets[name];
            if(resumed&&recovery=="payload"&&name=="caption-1.vtt")bytes=Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("Continuing","Changed"));
            if(resumed&&recovery=="payload"&&name=="caption-3000.m4s"){var changed=DashCaptionChecks.Movie([DashCaptionChecks.Sample("Changed live cue.")],out var offset,3000);bytes=changed[offset..];}
            if(message.Headers.Range is { } range)
            {var bounds=range.Ranges.Single();var first=bounds.From!.Value;var last=bounds.To!.Value;var response=new HttpResponseMessage(HttpStatusCode.PartialContent){Content=new ByteArrayContent(bytes.AsSpan(checked((int)first),checked((int)(last-first+1))).ToArray())};response.Content.Headers.ContentRange=new(first,last,bytes.Length);response.Headers.ETag=new("\"fixture-version\"");return response;}
            return new(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)};
        }));
        var destination=Path.Combine(files.Directory,mode=="dash-audio"?"published.m4a":"published.mp4");var sidecar=Path.Combine(files.Directory,"published.en.vtt");var archive=Path.Combine(files.Directory,"archive.txt");
        if(!worker){await File.WriteAllTextAsync(destination,"prior media");await File.WriteAllTextAsync(sidecar,"prior captions");await File.WriteAllTextAsync(archive,"prior archive\n");}
        var selected=mode!="dash-unselected";var request=new NativeDownloadRequest{ SubtitleSleepSeconds = PublicSubtitleFixtureDelay.Value, Url=url,OutputDirectory=files.Directory,OutputTemplate="published.%(ext)s",FfmpegPath=ffmpeg,LiveDurationSeconds=mode=="hls-static"?4:8,LiveFromStart=!mode.StartsWith("hls"),KeepOriginal=true,
            WriteSubs=selected,EmbedSubs=selected&&mode!="dash-audio",SubLanguages="en",ConvertSubs=mode=="hls-srt"?"srt":"",AudioOnly=mode=="dash-audio",VideoOnly=mode is "dash-video" or "hls-static",DownloadArchiveFile=archive,Retries=0,FragmentRetries=0};
        if(recovery is not null&&!worker)
        {
            await KillDurableWorker(mode,files.Directory,ffmpeg);
            AdaptiveRecoveryChecks.Assert(callbacks==0&&await File.ReadAllTextAsync(destination)=="prior media"&&await File.ReadAllTextAsync(sidecar)=="prior captions"&&await File.ReadAllTextAsync(archive)=="prior archive\n");
            AdaptiveRecoveryChecks.Assert(Directory.GetFiles(files.Directory,"captions.json",SearchOption.AllDirectories).Length==1);
            if(recovery is "corrupt" or "missing")DamageCaptionSnapshot(files.Directory,recovery=="missing");
            foreach(var file in Directory.GetFiles(files.Directory,"captions.json",SearchOption.AllDirectories)){var text=await File.ReadAllTextAsync(file);AdaptiveRecoveryChecks.Assert(!text.Contains("https://")&&!text.Contains("caption-key"));}
            resumed=true;vp=ap=mp=cp=0;
        }
        try
        {
            var result=await engine.DownloadAsync(request,null,null,timeout.Token,_=>callbacks++);if(failed)throw new InvalidOperationException("Invalid live captions published success.");
            if(!result.Succeeded||callbacks!=result.OutputPaths.Count||result.OutputPaths.Count<2)throw new InvalidOperationException("Live captions lost source retention or publication callbacks.");
            foreach(var path in result.OutputPaths)await Run(ffmpeg,["-v","error","-xerror","-i",path,"-map","0:v?","-map","0:a?","-f","null","-"],true);
            if(recovery is not null&&mode!="dash-audio")
            {
                using var details=JsonDocument.Parse(await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!,"ffprobe.exe"),["-v","error","-select_streams","v:0","-count_frames","-show_entries","stream=nb_read_frames","-of","json",result.OutputPath!]));
                AdaptiveRecoveryChecks.Assert(details.RootElement.GetProperty("streams")[0].GetProperty("nb_read_frames").GetString()=="200","Process recovery lost complete video frames.");
            }
            if(recovery is not null)AdaptiveRecoveryChecks.Assert(Directory.GetDirectories(files.Directory,".native-download-*").Length==0,"Caption recovery staging survived successful publication.");
            if(selected)
            {
                var saved=await File.ReadAllTextAsync(Path.ChangeExtension(sidecar,mode=="hls-srt"?"srt":"vtt"));
                if(mode=="hls-reset"){if(!saved.Contains("00:00:00.500 --> 00:00:04.000")||!saved.Contains("00:00:04.500 --> 00:00:07.979"))throw new InvalidOperationException("HLS clock-reset captions did not remain within their periods and common ended window: "+saved);}
                else if(!saved.Contains(mode=="hls-srt"?"00:00:00,500":mode.StartsWith("hls")?"00:00:00.500":"00:00:00.000")||!saved.Contains("Continuing live cue.")||saved.Split("Continuing live cue.").Length!=2)throw new InvalidOperationException("Live captions lost timing or repeated a continuing cue.");
                if(request.EmbedSubs)
                {var extracted=await Run(ffmpeg,["-v","error","-i",result.OutputPath!,"-map","0:s:0","-f","webvtt","-"]);if(!extracted.Contains("caption.")&&!extracted.Contains("live cue."))throw new InvalidOperationException("Live subtitle embedding lost captured text.");}
            }
            else if(capReads!=0)throw new InvalidOperationException("Unselected live captions were fetched.");
            using var probe=JsonDocument.Parse(await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!,"ffprobe.exe"),["-v","error","-count_frames","-show_entries","stream=codec_type,nb_read_frames:format=duration","-of","json",result.OutputPath!]));
            var streams=probe.RootElement.GetProperty("streams").EnumerateArray().ToArray();if(mode!="dash-audio"&&int.Parse(streams.Single(s=>s.GetProperty("codec_type").GetString()=="video").GetProperty("nb_read_frames").GetString()!)!=(mode=="hls-static"?100:200))throw new InvalidOperationException("Live boundary handling dropped or repeated video frames.");
            var duration=double.Parse(probe.RootElement.GetProperty("format").GetProperty("duration").GetString()!,CultureInfo.InvariantCulture);if(duration<request.LiveDurationSeconds-0.1||duration>request.LiveDurationSeconds+0.25)throw new InvalidOperationException("Live clipped output changed its complete duration.");
            if(mode=="dash-video")
            {
                var reference=Path.Combine(files.Directory,"reference-video.mp4");await File.WriteAllBytesAsync(reference,assets[Path.GetFileName(video.Initialization!.AbsolutePath)].Concat(video.Segments.SelectMany(s=>assets[Path.GetFileName(s.Url.AbsolutePath)])).ToArray());
                static string[] Hashes(string output)=>output.Split('\n').Where(line=>!line.StartsWith('#')&&line.Contains(',')).Select(line=>line.Split(',')[^1].Trim()).ToArray();
                var expected=Hashes(await Run(ffmpeg,["-v","error","-i",reference,"-map","0:v:0","-f","framemd5","-"]));var actual=Hashes(await Run(ffmpeg,["-v","error","-i",result.OutputPath!,"-map","0:v:0","-f","framemd5","-"]));
                if(expected.Length!=200||!expected.SequenceEqual(actual))throw new InvalidOperationException("Mid-GOP clipping changed or repeated decoded source video pixels.");
            }
            if(mode=="dash-lossless")
            {
                var reference=Path.Combine(files.Directory,"reference-audio.mp4");await File.WriteAllBytesAsync(reference,assets[Path.GetFileName(audio.Initialization!.AbsolutePath)].Concat(audio.Segments.SelectMany(s=>assets[Path.GetFileName(s.Url.AbsolutePath)])).ToArray());
                var expected=Path.Combine(files.Directory,"reference.pcm");var actual=Path.Combine(files.Directory,"clipped.pcm");
                await Run(ffmpeg,["-v","error","-y","-i",reference,"-map","0:a:0","-t","8","-f","s16le",expected]);await Run(ffmpeg,["-v","error","-y","-i",result.OutputPath!,"-map","0:a:0","-f","s16le",actual]);
                var golden=await File.ReadAllBytesAsync(expected);var decoded=await File.ReadAllBytesAsync(actual);if(golden.Length!=768000||!golden.SequenceEqual(decoded))throw new InvalidOperationException("Clipping changed or repeated decoded lossless audio samples.");
            }
            var before=mediaReads+capReads;var repeat=await engine.DownloadAsync(request,null,null,default,_=>callbacks++);if(!repeat.Skipped||mediaReads+capReads!=before||callbacks!=result.OutputPaths.Count)throw new InvalidOperationException("Live captions repeated HTTP or callbacks after an archive match.");
        }
        catch(Exception e)when(failed&&e is InvalidDataException or NotSupportedException or OperationCanceledException or IOException or HttpRequestException)
        {if(await File.ReadAllTextAsync(destination)!="prior media"||await File.ReadAllTextAsync(sidecar)!="prior captions"||await File.ReadAllTextAsync(archive)!="prior archive\n"||callbacks!=0)throw new InvalidOperationException("Failed captions changed prior media, sidecars or archives.");}
        if(Directory.EnumerateDirectories(files.Directory,".hls*").Any()||Directory.EnumerateFiles(files.Directory,".live-pair-*").Any())throw new InvalidOperationException("Live captions left owned capture resources.");

        (long Start,long Duration,long Scale,Uri Url)[] Timing(string id)
        {
            var doc=XDocument.Parse(sourceXml);var rep=doc.Descendants().Single(e=>e.Name.LocalName=="Representation"&&(string?)e.Attribute("id")==id);var template=rep.Descendants().Single(e=>e.Name.LocalName=="SegmentTemplate");var scale=long.Parse((string)template.Attribute("timescale")!);var values=new List<(long,long)>();long time=0;
            foreach(var s in template.Descendants().Where(e=>e.Name.LocalName=="S")){time=long.Parse((string?)s.Attribute("t")??time.ToString());var length=long.Parse((string)s.Attribute("d")!);for(var i=0;i<=int.Parse((string?)s.Attribute("r")??"0");i++){values.Add((time,length));time+=length;}}
            return source.Representations.Single(r=>r.Id==id).Segments.Zip(values,(seg,value)=>(value.Item1,value.Item2,scale,seg.Url)).ToArray();
        }
        string Snapshot(int poll)
        {
            var root=new XElement("MPD",new XAttribute("type",poll==2?"static":"dynamic"),new XAttribute("availabilityStartTime","2026-01-01T00:00:00Z"),new XAttribute("publishTime","2026-01-01T00:00:"+(poll==0?"04":poll==1?"06":"10")+".100Z"),new XAttribute("minimumUpdatePeriod","PT0.25S"),new XAttribute("timeShiftBufferDepth","PT30S"));if(poll==2)root.SetAttributeValue("mediaPresentationDuration","PT8S");
            if(poll<2&&!(resumed&&recovery is not null))root.Add(Period("p0",0));if(poll>0||resumed)root.Add(Period("p1",3));return root.ToString();
            XElement Period(string id,int start)
            {
                var period=new XElement("Period",new XAttribute("id",id),new XAttribute("start","PT"+start+"S"));
                foreach(var representation in source.Representations)
                {
                    var adaptation=new XElement("AdaptationSet",new XAttribute("id",representation.IsAudio?"a":"v"),new XAttribute("mimeType",representation.MimeType));var rep=new XElement("Representation",new XAttribute("id",representation.Id),new XAttribute("codecs",representation.Codecs!));if(representation.IsVideo)rep.SetAttributeValue("height",72);
                    var times=timings[representation.Id];var template=new XElement("SegmentTemplate",new XAttribute("timescale",times[0].Scale),new XAttribute("presentationTimeOffset",start*times[0].Scale),new XAttribute("initialization",Path.GetFileName(representation.Initialization!.AbsolutePath)),new XAttribute("media","chunk-$RepresentationID$-$Number%05d$.m4s"));var timeline=new XElement("SegmentTimeline");foreach(var item in times)timeline.Add(new XElement("S",new XAttribute("t",item.Start),new XAttribute("d",item.Duration)));template.Add(timeline);rep.Add(template);adaptation.Add(rep);period.Add(adaptation);
                }
                var cap=new XElement("AdaptationSet",new XAttribute("id","c"),new XAttribute("contentType","text"),new XAttribute("lang","en"));var caption=new XElement("Representation",new XAttribute("id","caption"),new XAttribute("mimeType","application/mp4"),new XAttribute("codecs",ttml?"stpp":"wvtt"));
                var addressing=new XElement(mode=="dash-range"?"SegmentList":"SegmentTemplate",new XAttribute("timescale",1000),new XAttribute("presentationTimeOffset",start*1000));
                if(mode=="dash-range")addressing.Add(new XElement("Initialization",new XAttribute("sourceURL","captions.mp4"),new XAttribute("range","0-"+(init!.Length-1))));
                else{addressing.SetAttributeValue("initialization","caption-init.mp4");addressing.SetAttributeValue("media","caption-$Time$.m4s");}
                var ct=new XElement("SegmentTimeline");long position=init!.Length;
                for(var i=0;i<8;i++){if(mode=="dash-gap"&&i==4)continue;ct.Add(new XElement("S",new XAttribute("t",i*1000),new XAttribute("d",1000)));if(mode=="dash-range")addressing.Add(new XElement("SegmentURL",new XAttribute("media","captions.mp4"),new XAttribute("mediaRange",position+"-"+(position+captionParts[i].Length-1))));position+=captionParts[i].Length;}
                addressing.Add(ct);caption.Add(addressing);cap.Add(caption);period.Add(cap);return period;
            }
        }
        string Hls(DashRepresentation representation,int poll,bool isVideo)
        {
            var values=timings[representation.Id];var count=mode=="hls-static"?4:poll==1?2:poll==2?3:4;var skip=resumed&&recovery is not null?1:0;var text="#EXTM3U\n#EXT-X-TARGETDURATION:3\n#EXT-X-MEDIA-SEQUENCE:"+((isVideo?100:500)+skip)+"\n#EXT-X-MAP:URI=\""+Path.GetFileName(representation.Initialization!.AbsolutePath)+"\"\n";double time=0;
            for(var i=0;i<count;i++){var item=values[mode=="hls-reset"?i%2:i];if(i<skip){time+=item.Duration/(double)item.Scale;continue;}if(mode=="hls-reset"&&i==2)text+="#EXT-X-DISCONTINUITY\n";text+="#EXT-X-PROGRAM-DATE-TIME:"+DateTimeOffset.Parse("2026-01-01T00:00:00Z").AddSeconds(time).ToString("O")+"\n#EXTINF:"+(item.Duration/(double)item.Scale).ToString(CultureInfo.InvariantCulture)+",\n"+Path.GetFileName(item.Url.AbsolutePath)+"\n";time+=item.Duration/(double)item.Scale;}
            return text+(mode=="hls-static"||poll==3?"#EXT-X-ENDLIST\n":"");
        }
        string HlsCaptions()
        {
            var poll=Interlocked.Increment(ref cp);var first=resumed&&recovery is not null?1:mode=="hls-moving"?Math.Clamp(poll-1,0,2):0;var last=mode=="hls-moving"?Math.Min(first+2,4):4;
            var text="#EXTM3U\n#EXT-X-TARGETDURATION:2\n#EXT-X-MEDIA-SEQUENCE:"+(700+first)+"\n#EXT-X-MAP:URI=\""+(mode=="hls-range"?"captions.vtt":"caption-header.vtt")+"\""+(mode=="hls-range"?",BYTERANGE=\""+Encoding.UTF8.GetByteCount(header)+"@0\"":"")+"\n";var position=Encoding.UTF8.GetByteCount(header);
            position+=hlsParts.Take(first).Sum(part=>part.Length);
            if(mode=="hls-encrypted")text+="#EXT-X-KEY:METHOD=AES-128,URI=\"caption-key\"\n";
            for(var i=first;i<last;i++){if(mode=="hls-reset"&&i==2)text+="#EXT-X-DISCONTINUITY\n";if(mode=="hls-gap"&&i==2)continue;text+="#EXT-X-PROGRAM-DATE-TIME:2026-01-01T00:00:"+(i*2).ToString("00")+"Z\n#EXTINF:2,\n";if(mode=="hls-range")text+="#EXT-X-BYTERANGE:"+hlsParts[i].Length+"@"+position+"\ncaptions.vtt\n";else text+="caption-"+i+".vtt\n";position+=hlsParts[i].Length;}
            return text+(mode!="hls-moving"||poll>=4?"#EXT-X-ENDLIST\n":"");
        }
    }
}
