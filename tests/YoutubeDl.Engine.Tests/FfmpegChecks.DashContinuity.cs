using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Xml.Linq;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Download;

internal static partial class FfmpegChecks
{
    private static async Task DashContinuityMedia(string ffmpeg,string mode,bool dropped=false)
    {
        using var files=new Files();var url=new Uri("https://periods.example/live.mpd");
        var source=new Dictionary<string,DashManifest>();
        foreach(var id in new[]{"p0","p1"})
        {
            var directory=Path.Combine(files.Directory,id);Directory.CreateDirectory(directory);var mpd=Path.Combine(directory,"source.mpd");
            await Run(ffmpeg,["-v","error","-y","-f","lavfi","-i","testsrc2=size="+(mode=="decoder"&&id=="p1"?"160x90":"128x72")+":rate=25","-f","lavfi","-i","sine=frequency="+(id=="p0"?"440":"880")+":sample_rate=48000","-t","4","-map","0:v:0","-map","1:a:0","-c:v","libx264","-preset","ultrafast","-g","50","-c:a","aac","-f","dash","-seg_duration","2","-use_template","1","-use_timeline","1","-init_seg_name","init-$RepresentationID$.m4s","-media_seg_name","chunk-$RepresentationID$-$Number%05d$.m4s",mpd]);
            source.Add(id,DashManifestParser.Parse(await File.ReadAllTextAsync(mpd),new("https://periods.example/"+id+"/source.mpd")));
            foreach(var representation in source[id].Representations)
            {
                var combined=new List<byte>(await File.ReadAllBytesAsync(Path.Combine(directory,Path.GetFileName(representation.Initialization!.AbsolutePath))));
                foreach(var segment in representation.Segments)combined.AddRange(await File.ReadAllBytesAsync(Path.Combine(directory,Path.GetFileName(segment.Url.AbsolutePath))));
                await File.WriteAllBytesAsync(Path.Combine(directory,"all-"+representation.Id+".mp4"),combined.ToArray());
            }
        }
        var polls=0;var reads=0;var callbacks=0;var replay=false;var missingReads=0;var failed=mode is "codec" or "gap" or "missing" or "cancel" or "init" or "decoder" or "renamed";
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));if(mode=="cancel")timeout.CancelAfter(500);
        var engine=new NativeEngine(httpFactory:_=>AuthenticationBatchChecks.Http(async(message,ct)=>
        {
            if(message.RequestUri!.AbsolutePath=="/live.mpd")return AuthenticationBatchChecks.Json(Snapshot(Math.Min(Interlocked.Increment(ref polls)-1,2)));
            if(dropped&&!replay&&message.RequestUri.AbsolutePath.Contains("/p1/chunk-")&&message.RequestUri.AbsolutePath.Contains("00002"))
            {await Task.Delay(300,ct);return AuthenticationBatchChecks.Json("interrupted",HttpStatusCode.ServiceUnavailable);}
            if(dropped&&!replay&&mode=="range"&&message.RequestUri.AbsolutePath.Contains("/p1/all-"))
            {
                var id=Path.GetFileNameWithoutExtension(message.RequestUri.AbsolutePath)["all-".Length..];
                var length=new FileInfo(Path.Combine(files.Directory,"p1","init-"+id+".m4s")).Length;
                if(message.Headers.Range?.Ranges.Single().From>length){await Task.Delay(300,ct);return AuthenticationBatchChecks.Json("interrupted",HttpStatusCode.ServiceUnavailable);}
            }
            if(dropped&&replay&&message.RequestUri.AbsolutePath.Contains("/p0/")){Interlocked.Increment(ref missingReads);return AuthenticationBatchChecks.Json("gone",HttpStatusCode.Gone);}
            if(mode=="cancel"&&message.RequestUri.AbsolutePath.Contains("/p1/"))await Task.Delay(Timeout.Infinite,ct);
            Interlocked.Increment(ref reads);var path=Path.Combine(files.Directory,message.RequestUri.AbsolutePath.TrimStart('/').Replace('/',Path.DirectorySeparatorChar));
            var bytes=await File.ReadAllBytesAsync(path,ct);
            if(message.Headers.Range is { } range)
            {
                var bounds=range.Ranges.Single();var start=bounds.From!.Value;var end=bounds.To!.Value;
                var response=new HttpResponseMessage(HttpStatusCode.PartialContent){Content=new ByteArrayContent(bytes.AsSpan(checked((int)start),checked((int)(end-start+1))).ToArray())};
                response.Content.Headers.ContentRange=new(start,end,bytes.Length);response.Headers.ETag=new("\"fixture-version\"");return response;
            }
            return new(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)};
        }));
        var destination=Path.Combine(files.Directory,mode=="audio"?"published.m4a":"published.mp4");var archive=Path.Combine(files.Directory,"archive.txt");
        await File.WriteAllTextAsync(destination,"prior output");await File.WriteAllTextAsync(archive,"prior archive\n");
        var request=new NativeDownloadRequest{Url=url,OutputDirectory=files.Directory,OutputTemplate="published.%(ext)s",FfmpegPath=ffmpeg,LiveDurationSeconds=8,LiveFromStart=mode!="late",
            AudioOnly=mode=="audio",VideoOnly=mode=="video",KeepOriginal=true,DownloadArchiveFile=archive,Retries=0,FragmentRetries=0};
        if(dropped)
        {
            try{var result=await engine.DownloadAsync(request,null,null,timeout.Token,_=>callbacks++);if(result.Succeeded)throw new InvalidOperationException("Dropped-period interruption published success.");}
            catch(Exception error)when(error is HttpRequestException or IOException or OperationCanceledException){}
            if(callbacks!=0||await File.ReadAllTextAsync(destination)!="prior output"||await File.ReadAllTextAsync(archive)!="prior archive\n")throw new InvalidOperationException("Dropped-period interruption changed protected outputs.");
            replay=true;polls=1;reads=0;
        }
        try
        {
            var result=await engine.DownloadAsync(request,null,null,timeout.Token,_=>callbacks++);
            if(failed)throw new InvalidOperationException("An invalid DASH transition published success.");
            if(!result.Succeeded||callbacks!=result.OutputPaths.Count||result.OutputPaths.Count<(mode is "audio" or "video"?1:3)||polls<3)
                throw new InvalidOperationException("DASH period capture lost sources, callbacks or refreshes.");
            foreach(var path in result.OutputPaths)await Run(ffmpeg,["-v","error","-xerror","-i",path,"-f","null","-"],true);
            using var probe=JsonDocument.Parse(await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!,"ffprobe.exe"),["-v","error","-count_frames","-show_entries","stream=codec_type,nb_read_frames:format=duration","-of","json",result.OutputPath!]));
            var streams=probe.RootElement.GetProperty("streams").EnumerateArray().ToArray();
            if(streams.Length!=(mode is "audio" or "video"?1:2))throw new InvalidOperationException("DASH period output changed selected roles.");
            if(mode!="audio"&&int.Parse(streams.Single(s=>s.GetProperty("codec_type").GetString()=="video").GetProperty("nb_read_frames").GetString()!)!=200)
                throw new InvalidOperationException("DASH period transition dropped or repeated source video frames.");
            var duration=double.Parse(probe.RootElement.GetProperty("format").GetProperty("duration").GetString()!,CultureInfo.InvariantCulture);
            if(duration<7.9||duration>8.2)throw new InvalidOperationException("DASH transition output has an invalid complete duration: "+duration);
            var before=reads;var repeat=await engine.DownloadAsync(request,null,null,default,_=>callbacks++);
            if(!repeat.Skipped||reads!=before||callbacks!=result.OutputPaths.Count)throw new InvalidOperationException("DASH archive repeated resources or callbacks.");
            if(dropped&&missingReads!=0)throw new InvalidOperationException("DASH retry fetched a removed period.");
        }
        catch(Exception ex)when(failed&&ex is InvalidDataException or NotSupportedException or OperationCanceledException or HttpRequestException or IOException)
        {
            if(await File.ReadAllTextAsync(destination)!="prior output"||await File.ReadAllTextAsync(archive)!="prior archive\n"||callbacks!=0)
                throw new InvalidOperationException("Failed DASH transition changed prior output or archive.");
        }
        if(Directory.EnumerateDirectories(files.Directory,".hls*").Any()||Directory.EnumerateFiles(files.Directory,".live-pair-*").Any())
            throw new InvalidOperationException("DASH period capture left owned session resources.");

        string Snapshot(int poll)
        {
            var root=new XElement("MPD",new XAttribute("type",poll==2?"static":"dynamic"),new XAttribute("availabilityStartTime","2026-01-01T00:00:00Z"),
                new XAttribute("publishTime",DateTimeOffset.Parse("2026-01-01T00:00:00Z").AddSeconds((poll==0?4.1:poll==1?6.1:8.1)+(mode=="late"?4:0)).ToString("O")),
                new XAttribute("timeShiftBufferDepth","PT30S"),new XAttribute("minimumUpdatePeriod","PT0.25S"));
            if(poll==2)root.SetAttributeValue("mediaPresentationDuration",mode=="late"?"PT12S":"PT8S");
            if(poll<2&&!(dropped&&replay))root.Add(Period("p0",mode=="late"?4:0,poll>0));if(poll>0)root.Add(Period("p1",mode=="late"?8:mode=="gap"&&(!dropped||replay)?4.5:4,poll==2));return root.ToString();
            XElement Period(string id,double start,bool closed)
            {
                var period=new XElement("Period",new XAttribute("id",mode=="renamed"&&replay&&id=="p1"?"changed":id),new XAttribute("start","PT"+start.ToString(CultureInfo.InvariantCulture)+"S"));
                if(closed)period.SetAttributeValue("duration","PT4S");
                foreach(var representation in source[id].Representations)
                {
                    if(mode=="missing"&&id=="p1"&&representation.IsAudio)continue;
                    var adaptation=new XElement("AdaptationSet",new XAttribute("id",representation.IsAudio?"a":"v"),new XAttribute("mimeType",representation.MimeType));
                    var rep=new XElement("Representation",new XAttribute("id",representation.Id),new XAttribute("codecs",mode=="codec"&&id=="p1"&&representation.IsVideo&&(!dropped||replay)?"hvc1":representation.Codecs!));
                    if(representation.IsVideo)rep.SetAttributeValue("height",72);
                    var init=id+"/"+Path.GetFileName(representation.Initialization!.AbsolutePath);
                    if(mode=="init"&&id=="p0"&&poll>0)init=id+"/changed.m4s";
                    if(mode is "list" or "range")
                    {
                        var list=new XElement("SegmentList",new XAttribute("timescale",1),new XAttribute("duration",2));
                        var initLength=new FileInfo(Path.Combine(files.Directory,id,Path.GetFileName(representation.Initialization.AbsolutePath))).Length;
                        var all=id+"/all-"+representation.Id+".mp4";list.Add(new XElement("Initialization",new XAttribute("sourceURL",mode=="range"?all:init)));
                        if(mode=="range")list.Element("Initialization")!.SetAttributeValue("range","0-"+(initLength-1));
                        var position=initLength;foreach(var segment in representation.Segments)
                        {
                            var name=Path.GetFileName(segment.Url.AbsolutePath);var length=new FileInfo(Path.Combine(files.Directory,id,name)).Length;
                            var element=new XElement("SegmentURL",new XAttribute("media",mode=="range"?all:id+"/"+name));
                            if(mode=="range")element.SetAttributeValue("mediaRange",position+"-"+(position+length-1));list.Add(element);position+=length;
                        }
                        rep.Add(list);
                    }
                    else
                    {
                        var template=new XElement("SegmentTemplate",new XAttribute("timescale",1),new XAttribute("duration",2),new XAttribute("initialization",init),new XAttribute("media",id+"/chunk-$RepresentationID$-$Number%05d$.m4s"));
                        if(mode=="timeline"){template.Attribute("duration")!.Remove();template.Add(new XElement("SegmentTimeline",new XElement("S",new XAttribute("t",0),new XAttribute("d",2),new XAttribute("r",1))));}
                        rep.Add(template);
                    }
                    adaptation.Add(rep);period.Add(adaptation);
                }
                return period;
            }
        }
    }
}
