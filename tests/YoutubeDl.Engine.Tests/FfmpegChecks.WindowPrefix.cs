using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Protocols;
using YoutubeDl.Engine.Protocols.Sabr;
internal static partial class FfmpegChecks
{
    internal static IEnumerable<(string Name,Func<Task> Test)> WindowPrefixMediaChecks(string ffmpeg)=>
        new[]{"clear","aes","gap","corrupt","denied","caption","overlap-payload","overlap-time"}.Select(mode=>("Window prefix dropped HLS "+mode,(Func<Task>)(()=>DroppedWindow(ffmpeg,mode))))
        .Concat(new[]{"skip","restart","corrupt","denied","late-denied","startup-only","startup-media","complete-cache-startup","changed-init","changed-time","replay"}.Select(mode=>("Window prefix SABR "+mode,(Func<Task>)(()=>SabrPrefix(ffmpeg,mode)))))
        .Concat(new[]{"pair","video","audio","range","timeline","codec","gap","renamed"}.Select(mode=>("Window prefix dropped DASH "+mode,(Func<Task>)(()=>DashContinuityMedia(ffmpeg,mode,true)))))
        .Concat(new[]{"hls","offset","ended"}.Select(mode=>("Window prefix dropped paired HLS "+mode,(Func<Task>)(()=>AdaptiveLiveMedia(ffmpeg,mode,true,true)))));
    private static async Task DroppedWindow(string ffmpeg,string mode)
    {
        using var files=new Files();var fixture=await RecoveryFixture.CreateAsync(files.Directory,ffmpeg,mode=="aes"?"hls-aes":"hls");
        var parsed=HlsPlaylistParser.Parse(Encoding.UTF8.GetString(fixture.Assets["manifest.m3u8"]),new("https://cdn.example/manifest.m3u8"));
        var resumed=false;var bodies=0;var oldReads=0;var calls=0;var callbacks=0;
        NativeEngine Engine()=>new([new LiveSabrProvider(false)],_=>AdaptiveAccountChecks.Http((message,ct)=>
        {
            var name=Path.GetFileName(message.RequestUri!.AbsolutePath);
            if(name=="manifest.m3u8")
            {
                var skip=resumed?(mode=="gap"?2:1):0;var text="#EXTM3U\n#EXT-X-TARGETDURATION:2\n#EXT-X-MEDIA-SEQUENCE:"+skip+"\n"+(resumed?"":"#EXT-X-PLAYLIST-TYPE:EVENT\n");
                if(parsed.MediaSegments[0].Initialization is { } init)text+="#EXT-X-MAP:URI=\""+Path.GetFileName(init.Url.AbsolutePath)+"\"\n";
                var time=DateTimeOffset.Parse("2026-01-01T00:00:00Z");
                foreach(var segment in parsed.MediaSegments)
                {
                    if(segment.Sequence>=(ulong)skip)
                    {
                        if(mode=="aes")text+="#EXT-X-KEY:METHOD=AES-128,URI=\"key"+segment.Sequence+"\"\n";
                        text+="#EXT-X-PROGRAM-DATE-TIME:"+time.ToString("O")+"\n#EXTINF:"+(segment.DurationSeconds!.Value+(resumed&&mode=="overlap-time"&&segment.Sequence==1?0.01:0)).ToString(System.Globalization.CultureInfo.InvariantCulture)+",\n"+Path.GetFileName(segment.Url.AbsolutePath)+"\n";
                    }
                    time=time.AddSeconds(segment.DurationSeconds!.Value);
                }
                return Task.FromResult(AdaptiveAccountChecks.Text(text+(resumed?"#EXT-X-ENDLIST\n":"")));
            }
            if(resumed&&(name=="key0"||name.StartsWith("chunk0"))) {oldReads++;return Task.FromResult(AdaptiveAccountChecks.Text("gone",HttpStatusCode.Gone));}
            if(resumed&&mode=="denied")return Task.FromResult(AdaptiveAccountChecks.Text("denied",HttpStatusCode.Forbidden));
            if(name.StartsWith("key"))return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(fixture.Assets["key"])});
            calls++;if(!resumed&&name.StartsWith("chunk2"))return Task.FromResult(AdaptiveAccountChecks.Text("interrupted",HttpStatusCode.ServiceUnavailable));
            bodies++;var bytes=fixture.Assets[name].ToArray();if(resumed&&mode=="overlap-payload"&&name.StartsWith("chunk1"))bytes[^1]^=1;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)});
        }));
        var output=Path.Combine(files.Directory,"recording.mp4");var archive=Path.Combine(files.Directory,"archive.txt");await File.WriteAllTextAsync(output,"prior");await File.WriteAllTextAsync(archive,"prior\n");
        var request=new NativeDownloadRequest{Url=new("https://recovery-live.example/media"),OutputDirectory=files.Directory,OutputTemplate="recording.%(ext)s",FfmpegPath=ffmpeg,Container="mp4",KeepOriginal=true,AddMetadata=true,LiveFromStart=true,LiveDurationSeconds=3,Retries=0,FragmentRetries=0,DownloadArchiveFile=archive,WriteSubs=mode=="caption"};
        try{var first=await Engine().DownloadAsync(request,null,null,default,_=>callbacks++);AdaptiveRecoveryChecks.Assert(!first.Succeeded);}catch(Exception error)when(error is HttpRequestException or IOException){}
        AdaptiveRecoveryChecks.Assert(callbacks==0&&await File.ReadAllTextAsync(output)=="prior"&&await File.ReadAllTextAsync(archive)=="prior\n");
        if(mode=="corrupt"){var journals=Directory.GetFiles(files.Directory,"timeline.json",SearchOption.AllDirectories);var root=Path.GetDirectoryName(journals.Single())!;var index=parsed.MediaSegments[0].Initialization is null?0:1;await File.WriteAllTextAsync(Path.Combine(root,"fragment-"+index.ToString("D6")+".bin"),"damaged");}
        resumed=true;var failure=mode is not ("clear" or "aes" or "caption");
        var failed=false;try
        {
            var result=await Engine().DownloadAsync(request,null,null,default,_=>callbacks++);if(!result.Succeeded)failed=true;
            else
            {
                AdaptiveRecoveryChecks.Assert(!failure);foreach(var path in result.OutputPaths)await Run(ffmpeg,["-v","error","-xerror","-i",path,"-f","null","-"],true);
                foreach(var role in new[]{"video","audio"}){var actual=await RecoveryPackets(ffmpeg,output,role);var expected=await RecoveryPackets(ffmpeg,fixture.References[role],role);AdaptiveRecoveryChecks.Assert(actual.AsEnumerable().SequenceEqual(expected));}
                AdaptiveRecoveryChecks.Assert(callbacks==result.OutputPaths.Count&&Directory.GetDirectories(files.Directory,".native-download-*").Length==0);
                var count=calls;var repeat=await Engine().DownloadAsync(request,null,null,default,_=>callbacks++);AdaptiveRecoveryChecks.Assert(repeat.Skipped&&calls==count&&callbacks==result.OutputPaths.Count);
            }
        }
        catch(Exception error)when(failure&&error is InvalidDataException or NotSupportedException or HttpRequestException or IOException){failed=true;}
        AdaptiveRecoveryChecks.Assert(failed==failure&&oldReads==0,"Dropped window result: "+mode);
        if(failure)AdaptiveRecoveryChecks.Assert(callbacks==0&&await File.ReadAllTextAsync(output)=="prior"&&await File.ReadAllTextAsync(archive)=="prior\n");
    }
    private static async Task SabrPrefix(string ffmpeg,string mode,bool process=false,string? workerDirectory=null,bool worker=false)
    {
        using var files=new Files(workerDirectory,!worker);var tracks=new Dictionary<SabrFormatId,(byte[] Init,byte[][] Segments,string Path)>();
        foreach(var role in new[]{"video","audio"})
        {
            var path=Path.Combine(files.Directory,role+".mp4");
            if(!worker)await Run(ffmpeg,["-v","error","-y","-f","lavfi","-i",role=="video"?"testsrc2=size=128x72:rate=12":"sine=frequency=720:sample_rate=48000","-t","6","-c",role=="video"?"libx264":"aac","-g","12","-movflags","+empty_moov+default_base_moof","-frag_duration","1000000",path]);
            var bytes=await File.ReadAllBytesAsync(path);var offsets=new List<int>();
            for(var offset=0;offset<bytes.Length;){if(bytes.AsSpan(offset+4,4).SequenceEqual("moof"u8))offsets.Add(offset);offset+=checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset,4)));}
            var chunks=offsets.Select((offset,index)=>bytes[offset..(index+1<offsets.Count?offsets[index+1]:bytes.Length)]).ToArray();
            AdaptiveRecoveryChecks.Assert(chunks.Length>=4);tracks.Add(role=="video"?SabrFixture.Video:SabrFixture.Audio,(bytes[..offsets[0]],chunks,path));
        }
        var resumed=false;var calls=0;var skipped=0;var callbacks=0;var freshPrefixDeclared=false;
        NativeEngine Engine()=>new([new LiveSabrProvider(true)],_=>AdaptiveAccountChecks.Http(async(message,ct)=>
        {
            calls++;var request=ProtobufReader.Read(await message.Content!.ReadAsByteArrayAsync(ct));var ranges=request.Where(field=>field.Number==3).Select(field=>ProtobufReader.Read(field.Bytes)).ToArray();
            if(resumed&&calls==1&&process){var context=ProtobufReader.Read(request.Single(field=>field.Number==19).Bytes);AdaptiveRecoveryChecks.Assert(!context.Any(field=>field.Number is 3 or 5),"The fresh process restored a stale cookie or control context.");}
            var last=ranges.ToDictionary(fields=>SabrControlState.ReadFormat(SabrSegmentAssembler.OptionalBytes(fields,1)),fields=>(uint)SabrSegmentAssembler.RequiredInteger(fields,5));
            if(!resumed&&calls>1){if(worker)await PauseDurableWorker(files.Directory,ct);return AdaptiveAccountChecks.Text("interrupted",HttpStatusCode.ServiceUnavailable);}
            using var stream=new MemoryStream();SabrFixture.Status(stream,resumed&&mode=="denied"?3U:resumed&&mode is not ("startup-only" or "startup-media" or "complete-cache-startup")?1U:2U);byte id=0;
            if(resumed&&mode=="startup-only")return SabrFixture.Response(stream.ToArray());
            if(!resumed&&process)
            {
                var cookie=new ProtobufWriter();cookie.Bytes(7,"process-cookie"u8.ToArray());cookie.String(8,SabrFixture.VideoId);SabrFixture.Part(stream,35,cookie.ToArray());
                var context=new ProtobufWriter();context.UInt64(1,7);context.Bytes(3,"process-context"u8.ToArray());context.UInt64(4,1);SabrFixture.Part(stream,57,context.ToArray());
            }
            foreach(var pair in tracks)
            {
                if(!resumed&&process&&mode=="partial"&&pair.Key==SabrFixture.Audio)continue;
                var total=pair.Value.Segments.Length+(!resumed&&mode=="complete-cache-startup"?1:0);SabrFixture.Metadata(stream,pair.Key,(uint)total,(ulong)total*1000);
                var start=last.GetValueOrDefault(pair.Key);if(resumed&&calls==1)AdaptiveRecoveryChecks.Assert(start==0,"Saved ranges were sent before a fresh bootstrap.");
                if(resumed&&calls>1&&mode is "startup-media" or "complete-cache-startup"){AdaptiveRecoveryChecks.Assert(start==1,"A startup preview imported an offline prefix.");continue;}
                if(start==0){var init=pair.Value.Init.ToArray();if(resumed&&mode=="changed-init")init[^1]^=1;SabrFixture.Segment(stream,pair.Key,++id,0,init);}
                var end=!resumed?(mode=="complete-cache-startup"?pair.Value.Segments.Length:3): calls==1?1:pair.Value.Segments.Length;
                if(resumed&&calls>1&&mode!="restart"){var expected=process&&mode=="partial"&&pair.Key==SabrFixture.Audio?1U:3U;AdaptiveRecoveryChecks.Assert(start==expected,"SABR did not skip its verified saved prefix.");skipped+=(int)start-1;freshPrefixDeclared=true;}
                if(resumed&&calls>1&&mode=="replay")SabrFixture.Segment(stream,pair.Key,++id,2,pair.Value.Segments[1],new(1000,1000,1000));
                for(var number=(int)start+1;number<=end;number++)SabrFixture.Segment(stream,pair.Key,++id,(uint)number,pair.Value.Segments[number-1],new((number-1)*1000,resumed&&mode=="changed-time"?999:1000,1000));
            }
            if(resumed&&mode=="late-denied")SabrFixture.Status(stream,3);
            if(worker&&mode=="partial")
            {
                var bytes=tracks[SabrFixture.Audio].Init;SabrFixture.Part(stream,20,SabrFixture.Header(SabrFixture.Audio,++id,0,bytes.Length));
                SabrFixture.Part(stream,21,new[]{id}.Concat(bytes[..(bytes.Length/2)]).ToArray());
                var response=new HttpResponseMessage(HttpStatusCode.OK){Content=new StreamContent(new PendingWorkerStream(stream.ToArray(),files.Directory))};response.Content.Headers.ContentType=new("application/vnd.yt-ump");return response;
            }
            return SabrFixture.Response(stream.ToArray());
        }));
        var output=Path.Combine(files.Directory,"recording.mp4");var archive=Path.Combine(files.Directory,"archive.txt");if(!worker){await File.WriteAllTextAsync(output,"prior");await File.WriteAllTextAsync(archive,"prior\n");}
        var request=new NativeDownloadRequest{Url=new("https://recovery-live.example/media"),OutputDirectory=files.Directory,OutputTemplate="recording.%(ext)s",FfmpegPath=ffmpeg,Container="mp4",KeepOriginal=true,AddMetadata=true,Retries=0,DownloadArchiveFile=archive,FormatSelector="137+140"};
        if(process&&!worker)await KillDurableWorker("sabr:"+mode,files.Directory,ffmpeg);
        else try{var first=await Engine().DownloadAsync(request,null,null,default,_=>callbacks++);AdaptiveRecoveryChecks.Assert(!first.Succeeded);}catch(Exception error)when(error is HttpRequestException or IOException){}
        AdaptiveRecoveryChecks.Assert(callbacks==0&&await File.ReadAllTextAsync(output)=="prior"&&await File.ReadAllTextAsync(archive)=="prior\n");
        if(process&&!worker)foreach(var file in Directory.GetFiles(files.Directory,"*.json",SearchOption.AllDirectories)){var text=await File.ReadAllTextAsync(file);AdaptiveRecoveryChecks.Assert(!text.Contains("process-cookie")&&!text.Contains("process-context"));}
        if(mode=="corrupt"){var root=Path.GetDirectoryName(Directory.GetFiles(files.Directory,"timeline.json",SearchOption.AllDirectories).Single())!;await File.WriteAllTextAsync(Path.Combine(root,"fragment-000002.bin"),"damaged");}
        resumed=true;calls=0;request=request with {NoContinue=mode=="restart"};var failure=mode is not ("skip" or "restart" or "replay" or "partial");var failed=false;
        try
        {
            var result=await Engine().DownloadAsync(request,null,null,default,_=>callbacks++);failed=!result.Succeeded;
            if(result.Succeeded)
            {
                AdaptiveRecoveryChecks.Assert(!failure);foreach(var path in result.OutputPaths)await Run(ffmpeg,["-v","error","-xerror","-i",path,"-f","null","-"],true);
                foreach(var role in new[]{"video","audio"}){var actual=await RecoveryPackets(ffmpeg,output,role);var expected=await RecoveryPackets(ffmpeg,tracks[role=="video"?SabrFixture.Video:SabrFixture.Audio].Path,role);AdaptiveRecoveryChecks.Assert(actual.AsEnumerable().SequenceEqual(expected));}
                AdaptiveRecoveryChecks.Assert(callbacks==result.OutputPaths.Count&&Directory.GetDirectories(files.Directory,".native-download-*").Length==0);
                AdaptiveRecoveryChecks.Assert(mode=="restart"?!freshPrefixDeclared:skipped==(mode=="partial"?2:4));
            }
        }
        catch(Exception error)when(failure&&error is InvalidDataException or NotSupportedException or HttpRequestException or IOException){failed=true;}
        AdaptiveRecoveryChecks.Assert(failed==failure,"SABR prefix result: "+mode);
        if(failure)AdaptiveRecoveryChecks.Assert(callbacks==0&&await File.ReadAllTextAsync(output)=="prior"&&await File.ReadAllTextAsync(archive)=="prior\n");
    }
}
