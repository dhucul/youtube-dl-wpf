using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;

internal static partial class FfmpegChecks
{
    internal static IEnumerable<(string Name,Func<Task> Test)> WpfLadderMediaChecks(string ffmpeg)=>
        new[]{"h264-dim","hevc","vp9","av1","audio-codec","ladder-best","ladder-worst","height","fps","clip","caption","restart","kill","corrupt","disabled","within-period","hdr","pixel","aspect","cancel"}.Select(mode=>("WPF ladders media "+mode,(Func<Task>)(()=>WpfLadderMedia(ffmpeg,mode))));
    internal static async Task<int> WpfLadderWorker(string ffmpeg,string directory,string mode){await WpfLadderMedia(ffmpeg,mode,directory,true);return 1;}
    private static async Task WpfLadderMedia(string ffmpeg,string mode,string? existing=null,bool worker=false)
    {
        using var files=new Files(existing,!worker);var directory=files.Directory;var assets=Path.Combine(directory,"assets");var output=Path.Combine(directory,"out");Directory.CreateDirectory(output);
        var restart=mode is "restart" or "kill" or "corrupt" or "cancel";var captions=mode is "caption" or "restart" or "kill" or "corrupt" or "cancel";var variants=mode is "ladder-best" or "ladder-worst" or "height" or "fps";var boundary=mode=="clip";var invalid=mode is "corrupt" or "disabled" or "within-period" or "hdr" or "pixel" or "aspect" or "cancel";
        var reconstructed=mode.StartsWith("reconstruct-",StringComparison.Ordinal);var transformed=mode.StartsWith("transform-",StringComparison.Ordinal);var spatial=mode.StartsWith("spatial-",StringComparison.Ordinal);var metadata=mode.StartsWith("metadata-",StringComparison.Ordinal);var gamut=mode.StartsWith("gamut-",StringComparison.Ordinal);var extended=reconstructed||transformed||spatial||metadata||gamut||mode.StartsWith("convert-",StringComparison.Ordinal);var conversion=reconstructed?mode[12..]:transformed?mode[10..]:spatial?mode[8..]:metadata?mode[9..]:gamut?mode[6..]:extended?mode[8..]:"";
        restart|=extended&&conversion is "recover" or "kill" or "corrupt" or "cancel";captions|=extended&&conversion is "caption" or "recover" or "kill" or "corrupt" or "cancel";boundary|=conversion=="clip-color";invalid|=extended&&conversion is "unknown-color" or "hdr-disabled" or "geometry-disabled" or "audio-disabled" or "bad-rotation" or "corrupt" or "cancel";
        if(reconstructed)invalid|=conversion is "selector-projective-soft" or "selector-clipped" or "selector-default" or "source-missing-peak" or "limited-ids" or "bad-crop" or "singular" or "horizon" or "window-perspective" or "missing-reference" or "fel-missing";
        if(transformed)invalid|=conversion is "oblique-window" or "inset-oblique" or "fel" or "missing-layer" or "bad-matrix" or "bad-cdf";
        if(spatial)invalid|=conversion is "legacy" or "invalid-window" or "plus-pad" or "nonuniform" or "rotation" or "incompatible" or "enhancement" or "profile7" or "missing-rpu" or "missing-master";
        if(metadata){boundary|=conversion=="dv-clip";invalid|=conversion is "dv-geometry" or "dv-profile" or "dv-enhancement" or "plus-bframes" or "plus-window" or "plus-version" or "legacy";}
        if(gamut)invalid|=conversion is "static-change" or "static-late" or "mixed-transfer" or "dynamic-late" or "dynamic-tone" or "rgb-missing" or "rgb-disabled";
        if(!worker)
        {
            foreach(var period in new[]{"p0","p1"})foreach(var rung in variants?new[]{"high","low"}:new[]{"high"})
            {
                var folder=Path.Combine(assets,period,rung);Directory.CreateDirectory(folder);if(extended){if(reconstructed)await ReconstructAssets(ffmpeg,folder,period,conversion);else if(transformed)await TransformAssets(ffmpeg,folder,period,conversion);else if(spatial)await SpatialAssets(ffmpeg,folder,period,conversion);else if(metadata)await MetadataAssets(ffmpeg,folder,period,conversion);else if(gamut)await HdrGamutAssets(ffmpeg,folder,period,conversion);else await ColorAudioAssets(ffmpeg,folder,period,conversion);continue;}var size=rung=="low"?period=="p0"?"64x36":"96x54":period=="p0"?"128x72":"160x90";var fps=mode=="fps"?(rung=="low"?10:30):period=="p0"?10:15;var codec=period=="p1"?mode switch{"hevc"=>"libx265","vp9"=>"libvpx-vp9","av1"=>"libaom-av1",_=>"libx264"}:"libx264";
                var args=new List<string>{"-v","error","-y","-f","lavfi","-i","testsrc2=size="+size+":rate="+fps,"-f","lavfi","-i","sine=frequency="+(period=="p0"?440:880)+":sample_rate=48000","-t","4","-c:v",codec,"-threads","1","-g",(fps*2).ToString(),"-bf","0"};
                if(codec=="libx264")args.AddRange(["-preset","ultrafast"]);if(codec=="libx265")args.AddRange(["-preset","ultrafast","-x265-params","pools=1:frame-threads=1:log-level=error"]);if(codec=="libvpx-vp9")args.AddRange(["-deadline","realtime","-cpu-used","8"]);if(codec=="libaom-av1")args.AddRange(["-cpu-used","8","-lag-in-frames","0"]);
                if(period=="p1"&&mode=="pixel")args.AddRange(["-pix_fmt","yuv420p10le"]);else args.AddRange(["-pix_fmt","yuv420p"]);if(period=="p1"&&mode=="hdr")args.AddRange(["-color_trc","smpte2084","-color_primaries","bt2020","-colorspace","bt2020nc"]);if(period=="p1"&&mode=="aspect")args.AddRange(["-vf","setsar=2/1"]);
                args.AddRange(["-c:a",period=="p1"&&mode=="audio-codec"?"flac":"alac","-strict","-2","-f","dash","-dash_segment_type","mp4","-seg_duration","2","-use_timeline","0","-init_seg_name","init-$RepresentationID$.m4s","-media_seg_name","chunk-$RepresentationID$-$Number%05d$.m4s",Path.Combine(folder,"source.mpd")]);await Run(ffmpeg,args);
            }
            if(captions)foreach(var period in new[]{"p0","p1"})for(var index=0;index<4;index++){var ttml=period=="p1";var text=ttml?"Second caption.":"First caption.";var payload=ttml?Encoding.UTF8.GetBytes("<tt xmlns=\"http://www.w3.org/ns/ttml\"><body><div><p begin=\"0s\" end=\"4s\">"+text+"</p></div></body></tt>"):DashCaptionChecks.Sample(text);var movie=DashCaptionChecks.Movie([payload],out var offset,(ulong)index*1000,ttml:ttml);await File.WriteAllBytesAsync(Path.Combine(assets,period,"high","caption-"+index+".m4s"),movie[offset..]);if(index==0)await File.WriteAllBytesAsync(Path.Combine(assets,period,"high","caption-init.mp4"),movie[..offset]);}
        }
        var target=Path.Combine(output,"recording.mp4");var sidecar=Path.Combine(output,"recording.en.vtt");var archive=Path.Combine(directory,"archive.txt");if(!worker){await File.WriteAllTextAsync(target,"previous media");await File.WriteAllTextAsync(sidecar,"previous captions");await File.WriteAllTextAsync(archive,"previous archive\n");}
        var resumed=false;var reads=0;var manifestReads=0;var oldReads=0;var callbacks=0;
        var request=new NativeDownloadRequest{Url=new("https://ladder.example/"+Path.GetFileName(directory)+"/watch"),OutputDirectory=output,FileName="recording",FfmpegPath=ffmpeg,KeepOriginal=true,AddMetadata=true,LiveFromStart=true,LiveDurationSeconds=mode=="within-period"?10:boundary?7:8,WriteSubs=captions,EmbedSubs=captions,SubLanguages="en",DownloadArchiveFile=archive,Retries=0,FragmentRetries=0,Quality=mode=="ladder-worst"?QualityPreference.Worst:QualityPreference.Best,MaximumHeight=mode=="height"?60:null,MaximumFrameRate=mode=="fps"?15:0,LiveDashSelection=mode=="disabled"?new():new(){NormalizeTransitions=true,AdaptiveQualityLadder=true,TransitionHeight=90}};
        if(metadata&&conversion is "dolby-dvh1" or "dolby-dvhe")request=request with{VideoCodec="h265"};
        if(extended)request=request with{LiveDashSelection=reconstructed?ReconstructPolicy(conversion):transformed?TransformPolicy(conversion):spatial?SpatialPolicy(conversion):metadata?MetadataPolicy(conversion):gamut?HdrGamutPolicy(conversion):ColorAudioPolicy(conversion)};
        NativeEngine Engine()
        {
            var page=new PageMetadata{Id="fixture",Extractor="ladder-fixture",Title="Fixture",IsLive=true,Media=[new(){Id="master",Url=new("https://ladder.example/live.mpd"),Kind=MediaKind.Dash,HasVideo=true,HasAudio=true,Extension="mpd"}]};
            return new([new AdaptiveNoOverwriteChecks.Provider(page)],_=>AdaptiveAccountChecks.Http(async(message,ct)=>
            {
                var uri=message.RequestUri!;if(uri.AbsolutePath.EndsWith("live.mpd",StringComparison.Ordinal)){manifestReads++;return AdaptiveAccountChecks.Text(Manifest());}
                var parts=uri.AbsolutePath.TrimStart('/').Split('/');if(resumed&&parts[1]=="p0"){oldReads++;return AdaptiveAccountChecks.Text("Retired",HttpStatusCode.Gone);}
                if(restart&&!resumed&&parts[1]=="p1"&&parts[^1].StartsWith("chunk-",StringComparison.Ordinal)&&parts[^1].EndsWith("00002.m4s",StringComparison.Ordinal))
                {
                    if(worker){while(!LadderReady(directory))await Task.Delay(20,ct);await File.WriteAllTextAsync(Path.Combine(directory,"ladder-ready"),Environment.ProcessId.ToString(),ct);await Task.Delay(Timeout.Infinite,ct);}
                    for(var attempt=0;attempt<500&&!LadderReady(directory);attempt++)await Task.Delay(20,ct);
                    WriteCleanupChecks.Check(LadderReady(directory),"Both tracks and caption periods must be durable before the injected interruption.");
                    return AdaptiveAccountChecks.Text("Interrupted",HttpStatusCode.ServiceUnavailable);
                }
                reads++;return new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(await File.ReadAllBytesAsync(Path.Combine(assets,parts[1],parts[2],parts[^1]),ct))};
            }));
        }
        if(worker){await Engine().DownloadAsync(request,null,null,default);throw new Exception("Ladder worker escaped its checkpoint gate.");}
        if(restart)
        {
            if(mode=="kill"||conversion=="kill")
            {
                var start=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(var arg in new[]{typeof(FfmpegChecks).Assembly.Location,"--wpf-ladder-worker",ffmpeg,directory,mode})start.ArgumentList.Add(arg);using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();try{for(var i=0;i<800&&!File.Exists(Path.Combine(directory,"ladder-ready"))&&!process.HasExited;i++)await Task.Delay(20);WriteCleanupChecks.Check(File.Exists(Path.Combine(directory,"ladder-ready")),"Ladder worker failed before a durable prefix: "+(process.HasExited?await stderr:"timeout"));process.Kill(true);await process.WaitForExitAsync();}finally{if(!process.HasExited){process.Kill(true);await process.WaitForExitAsync();}await stdout;await stderr;}
            }
            else{try{await Engine().DownloadAsync(request,null,null,default,_=>callbacks++);throw new Exception("Interrupted ladder recording succeeded.");}catch(Exception error)when(error is IOException or HttpRequestException){} }
            WriteCleanupChecks.Check(await File.ReadAllTextAsync(target)=="previous media"&&await File.ReadAllTextAsync(archive)=="previous archive\n"&&callbacks==0,"Interrupted ladder recording published output.");
            if(mode=="corrupt"||conversion=="corrupt"){var path=Directory.GetFiles(directory,"timeline.json",SearchOption.AllDirectories).First();var prefix=Path.Combine(Path.GetDirectoryName(path)!,"fragment-000001.bin");var bytes=await File.ReadAllBytesAsync(prefix);bytes[^1]^=1;await File.WriteAllBytesAsync(prefix,bytes);}
            resumed=true;manifestReads=0;
        }
        using var cancel=new CancellationTokenSource();if(mode=="cancel"||conversion=="cancel")cancel.Cancel();var failed=false;
        try
        {
            var result=await Engine().DownloadAsync(request,null,null,cancel.Token,_=>callbacks++);WriteCleanupChecks.Check(!invalid&&result.Succeeded&&callbacks==result.OutputPaths.Count,"Converted ladder recording did not publish complete files.");foreach(var path in result.OutputPaths)await Run(ffmpeg,["-v","error","-xerror","-i",path,"-map","0:v?","-map","0:a?","-f","null","-"],true);
            if(!reconstructed){
            var expectedFrames=new List<string>();foreach(var period in new[]{"p0","p1"}){var rung=mode is "ladder-worst" or "height" or "fps"?"low":"high";var source=Path.Combine(assets,period,rung,extended?"reference-v.mp4":"source.mpd");var duration=period=="p0"&&boundary?3:4;var transform=transformed?TransformReferenceVideo(conversion,period):spatial?SpatialReferenceVideo(conversion,period):metadata?MetadataReferenceVideo(conversion):gamut?HdrGamutReferenceVideo(conversion,period):extended?ColorAudioReferenceVideo(conversion,period):"scale=160:90:force_original_aspect_ratio=decrease:force_divisible_by=2,pad=160:90:(ow-iw)/2:(oh-ih)/2,setsar=1,fps=30";expectedFrames.AddRange(FrameHashes(await Run(ffmpeg,["-v","error","-filter_threads","1",..(transformed?new[]{"-noautorotate"}:Array.Empty<string>()),"-i",source,"-map","0:v:0","-vf","setpts=PTS-STARTPTS,trim=duration="+duration+",setpts=PTS-STARTPTS,"+transform,"-threads","1","-pix_fmt",transformed||spatial||metadata||gamut&&HdrGamutTen(conversion)?"yuv420p10le":"yuv420p","-f","framemd5","-"])));}
            var actualFrames=FrameHashes(await Run(ffmpeg,["-v","error","-i",result.OutputPath!,"-map","0:v:0","-threads","1","-pix_fmt",transformed||spatial||metadata||gamut&&HdrGamutTen(conversion)?"yuv420p10le":"yuv420p","-f","framemd5","-"]));WriteCleanupChecks.Check(expectedFrames.Count==(boundary?210:240)&&expectedFrames.SequenceEqual(actualFrames),"Decoder/dimension conversion dropped, repeated or changed the reference frame sequence.");
            }
            if(reconstructed)await ReconstructVerify(ffmpeg,assets,result,conversion,boundary?7:8);
            else if(transformed)await TransformVerify(ffmpeg,assets,result,conversion,boundary?7:8);
            else if(spatial)await SpatialVerify(ffmpeg,assets,result,conversion,boundary?7:8);
            else if(metadata)await MetadataVerify(ffmpeg,assets,result,conversion,boundary?7:8);
            else if(gamut)await HdrGamutVerify(ffmpeg,assets,result,conversion,boundary?7:8);
            else if(extended)await ColorAudioVerifyAudio(ffmpeg,assets,result,request.LiveDashSelection!,boundary?7:8);
            else{var p0=Path.Combine(assets,"p0","high","source.mpd");var p1=Path.Combine(assets,"p1","high","source.mpd");var expectedAudio=await Run(ffmpeg,["-v","error","-i",p0,"-i",p1,"-filter_complex","[0:a]asetpts=PTS-STARTPTS,atrim=duration="+(boundary?3:4)+",asetpts=PTS-STARTPTS[a0];[1:a]asetpts=PTS-STARTPTS,atrim=duration=4,asetpts=PTS-STARTPTS[a1];[a0][a1]concat=n=2:v=0:a=1[out]","-map","[out]","-c:a","pcm_f64le","-f","hash","-"]);WriteCleanupChecks.Check(expectedAudio.Trim()==(await AudioHash(ffmpeg,result.OutputPath!)).Trim(),"Converted periods lost or changed decoded audio samples.");}
            if(captions){var text=await File.ReadAllTextAsync(sidecar);var embedded=await Run(ffmpeg,["-v","error","-i",result.OutputPath!,"-map","0:s:0","-f","webvtt","-"]);WriteCleanupChecks.Check(text.Contains("First caption.")&&text.Contains("Second caption.")&&embedded.Contains("First caption.")&&embedded.Contains("Second caption."),"Converted video lost mixed-codec saved/embedded captions.");}
            var partFiles=result.OutputPaths.Where(path=>Path.GetFileName(path).StartsWith("recording.part",StringComparison.Ordinal)).ToArray();WriteCleanupChecks.Check(partFiles.Length==4,"Transition conversion did not retain all four original source periods.");var partInventories=new List<string>();foreach(var part in partFiles)partInventories.Add(await PacketHashes(part,null));foreach(var period in new[]{"p0","p1"})foreach(var role in new[]{"v","a"}){var rung=role=="v"&&mode is "ladder-worst" or "height" or "fps"?"low":"high";var packets=await PacketHashes(Path.Combine(assets,period,rung,extended?"reference-"+role+".mp4":"source.mpd"),extended?null:role);WriteCleanupChecks.Check(partInventories.Contains(packets),"Retained period changed the selected original coded packets.");}
            WriteCleanupChecks.Check(oldReads==0,"Converted recovery requested a retired source period.");var before=reads;var callbackCount=callbacks;var repeat=await Engine().DownloadAsync(request with {FfmpegPath=Path.Combine(directory,"missing.exe")},null,null,default,_=>callbacks++);WriteCleanupChecks.Check(repeat.Skipped&&reads==before&&callbacks==callbackCount,"Converted archive repeat transferred media or callbacks.");
        }
        catch(Exception error)when(invalid&&error is InvalidDataException or NotSupportedException or IOException or OperationCanceledException){failed=true;}
        WriteCleanupChecks.Check(failed==invalid,"Ladder conversion refusal changed: "+mode);if(invalid)WriteCleanupChecks.Check(await File.ReadAllTextAsync(target)=="previous media"&&await File.ReadAllTextAsync(sidecar)=="previous captions"&&await File.ReadAllTextAsync(archive)=="previous archive\n"&&callbacks==0,"Invalid conversion changed prior outputs/captions/archive.");
        async Task<string> PacketHashes(string path,string? role){var probe=Path.Combine(Path.GetDirectoryName(ffmpeg)!,"ffprobe.exe");var args=new List<string>{"-v","error"};if(role is not null)args.AddRange(["-select_streams",role+":0"]);args.AddRange(["-show_packets","-show_data_hash","sha256","-show_entries","packet=data_hash","-of","json",path]);using var doc=JsonDocument.Parse(await Run(probe,args));return string.Join('\n',doc.RootElement.GetProperty("packets").EnumerateArray().Select(row=>row.GetProperty("data_hash").GetString()));}
        string Manifest()
        {
            var root=new XElement("MPD",new XAttribute("type",restart&&!resumed||mode=="within-period"?"dynamic":"static"),new XAttribute("availabilityStartTime","2026-01-01T00:00:00Z"),new XAttribute("publishTime",mode=="within-period"&&manifestReads>1?"2026-01-01T00:00:08.200Z":"2026-01-01T00:00:08.100Z"),new XAttribute("minimumUpdatePeriod","PT0.25S"),new XAttribute("mediaPresentationDuration",boundary?"PT7S":"PT8S"));
            foreach(var period in resumed?new[]{"p1"}:new[]{"p0","p1"})
            {
                var item=new XElement("Period",new XAttribute("id",period),new XAttribute("start",period=="p0"?"PT0S":boundary?"PT3S":"PT4S"),new XAttribute("duration",period=="p0"&&boundary?"PT3S":"PT4S"));root.Add(item);
                foreach(var role in new[]{"video","audio"})
                {
                    var group=new XElement("AdaptationSet",new XAttribute("id",period+"-"+role),new XAttribute("contentType",role),new XAttribute("mimeType",role+"/mp4"),new XAttribute("lang","en"),new XAttribute("audioSamplingRate",48000),new XElement("Role",new XAttribute("schemeIdUri","urn:mpeg:dash:role:2011"),new XAttribute("value","main")));item.Add(group);
                    foreach(var rung in variants&&role=="video"?new[]{"high","low"}:new[]{"high"})
                    {
                        var original=XDocument.Load(Path.Combine(assets,period,rung,"source.mpd"));var source=original.Descendants().Single(element=>element.Name.LocalName=="AdaptationSet"&&(string?)element.Attribute("contentType")==role);var representation=new XElement(source.Descendants().First(element=>element.Name.LocalName=="Representation"));var raw=representation.Attribute("id")!.Value;var template=representation.Elements().FirstOrDefault(element=>element.Name.LocalName=="SegmentTemplate")??new XElement(source.Elements().Single(element=>element.Name.LocalName=="SegmentTemplate"));if(template.Parent is null)representation.Add(template);
                        foreach(var attribute in template.Attributes().Where(attribute=>attribute.Name.LocalName is "initialization" or "media"))attribute.Value="https://ladder.example/g"+(resumed?2:0)+"/"+period+"/"+rung+"/"+attribute.Value.Replace("$RepresentationID$",raw,StringComparison.Ordinal);
                        foreach(var name in new[]{"frameRate","width","height","codecs"})if(representation.Attribute(name) is null&&source.Attribute(name) is { } fact)representation.SetAttributeValue(name,fact.Value);
                        if(role=="video"&&period=="p1"&&mode=="hevc"&&string.IsNullOrEmpty((string?)representation.Attribute("codecs")))representation.SetAttributeValue("codecs","hev1");
                        if(extended&&role=="video"&&string.IsNullOrEmpty((string?)representation.Attribute("codecs")))representation.SetAttributeValue("codecs","hev1");
                        representation.SetAttributeValue("id",period+"-"+role+"-"+rung);if(role=="audio")representation.SetAttributeValue("codecs",period=="p1"&&mode=="audio-codec"?"flac":"alac");if(mode=="within-period"&&manifestReads>1&&period=="p0"&&role=="video")representation.SetAttributeValue("width",320);group.Add(representation);
                        if(extended&&role=="audio"&&period=="p1"&&conversion=="eac3")representation.SetAttributeValue("codecs","ec-3");
                    }
                }
                if(captions)item.Add(new XElement("AdaptationSet",new XAttribute("id",period+"-captions"),new XAttribute("contentType","text"),new XAttribute("mimeType","application/mp4"),new XAttribute("lang","en"),new XElement("Role",new XAttribute("schemeIdUri","urn:mpeg:dash:role:2011"),new XAttribute("value","subtitle")),new XElement("Representation",new XAttribute("id",period+"-text"),new XAttribute("codecs",period=="p0"?"wvtt":"stpp"),new XElement("SegmentTemplate",new XAttribute("timescale",1),new XAttribute("duration",1),new XAttribute("initialization","https://ladder.example/g"+(resumed?2:0)+"/"+period+"/high/caption-init.mp4"),new XAttribute("media","https://ladder.example/g"+(resumed?2:0)+"/"+period+"/high/caption-$Time$.m4s")))));
            }
            return root.ToString();
        }
    }
    private static bool LadderReady(string directory)
    {
        try
        {
            var counts=Directory.GetFiles(directory,"timeline.json",SearchOption.AllDirectories).Select(path=>Read(path,root=>root.GetProperty("Records").EnumerateArray().Count(row=>row.GetProperty("Metadata").GetString()!.Contains("\"Kind\":\"media\"")))).ToArray();
            return counts.Length==2&&counts.All(count=>count>=3)&&Directory.GetFiles(directory,"captions.json",SearchOption.AllDirectories).Any(path=>Read(path,root=>root.GetProperty("Entries").EnumerateArray().Count(row=>{using var description=JsonDocument.Parse(row.GetProperty("Description").GetString()!);return description.RootElement.GetProperty("Kind").GetString()=="period";}))==2);
        }
        catch(IOException){return false;}
        static int Read(string path,Func<JsonElement,int> count)
        {
            // The recorder atomically replaces these receipts while this observer polls them.
            using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);using var document=JsonDocument.Parse(input);return count(document.RootElement);
        }
    }
}






