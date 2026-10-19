using System.Net;
using YoutubeDl.Engine;

internal static partial class FfmpegChecks
{
    private static async Task PracticalFacebookLifecycle(string ffmpeg,string mode)
    {
        using var files=new Files();using var cancel=new CancellationTokenSource();
        var source=Path.Combine(files.Directory,"source.mp4");await MixedSource(ffmpeg,source);var bytes=await File.ReadAllBytesAsync(source);
        var fixture=new PracticalFacebookChecks.Fixture{Bytes=bytes};var attempts=0;var resumed=false;var restarted=false;
        fixture.MediaResponse=(request,ct)=>
        {
            var range=request.Headers.Range?.Ranges.Single();if(range?.To is not null)return null;
            attempts++;
            PracticalFacebookChecks.Assert(request.Headers.AcceptEncoding.Any(value=>value.Value=="identity"),"The media transfer changed byte coding.");
            if(attempts==1)
            {
                PracticalFacebookChecks.Assert(range is null,"A new recording started from an undeclared prefix.");
                var response=new HttpResponseMessage(HttpStatusCode.OK){Content=new StreamContent(new InterruptedPrefix(bytes[..512],mode=="cancel"?cancel:null))};
                response.Content.Headers.ContentLength=bytes.Length;
                if(mode!="no-validator")response.Headers.ETag=new("\"owned\"");
                return response;
            }
            if(request.RequestUri!.Query.EndsWith("g=1",StringComparison.Ordinal))return new(HttpStatusCode.Forbidden);
            var offset=range?.From??0;var content=new ByteArrayContent(bytes[(int)offset..]);
            var result=new HttpResponseMessage(range is null?HttpStatusCode.OK:HttpStatusCode.PartialContent){Content=content};
            result.Headers.ETag=new(mode=="validator"?"\"next\"":"\"owned\"");
            if(range is not null)
            {
                resumed=true;
                PracticalFacebookChecks.Assert(offset==512&&request.Headers.IfRange?.EntityTag?.Tag=="\"owned\"","Renewal discarded the validated partial-transfer headers.");
                content.Headers.ContentRange=new(offset,bytes.Length-1,bytes.Length);
            }
            else restarted=true;
            return result;
        };
        var request=fixture.Request(Path.Combine(files.Directory,"output")) with{FfmpegPath=ffmpeg,Retries=1,KeepOriginal=true,DownloadArchiveFile=Path.Combine(files.Directory,"archive.txt")};
        var callbacks=new List<string>();
        if(mode=="cancel")
        {
            await ProviderFamiliesChecks.Expect<OperationCanceledException>(()=>fixture.Engine.DownloadAsync(request,null,null,cancel.Token,callbacks.Add));
            PracticalFacebookChecks.Assert(attempts==1&&fixture.Generation==1&&callbacks.Count==0&&!File.Exists(request.DownloadArchiveFile),"Canceled partial transfer retried, renewed or recorded completion.");return;
        }
        var download=await fixture.Engine.DownloadAsync(request,null,null,default,callbacks.Add);
        PracticalFacebookChecks.Assert(download.Succeeded&&download.OutputPath is not null&&File.ReadAllBytes(download.OutputPath).SequenceEqual(bytes),"Resumed Facebook playback lost or duplicated source bytes.");
        PracticalFacebookChecks.Assert(fixture.Generation==2&&attempts==(mode=="validator"?4:3)&&resumed==(mode!="no-validator")&&restarted==(mode!="resume"),"Resume validation or bounded renewal followed the wrong path.");
        await Run(ffmpeg,["-v","error","-xerror","-i",download.OutputPath!,"-map","0:v:0","-map","0:a:0","-f","null","-"]);
        var completed=callbacks.Count;var archived=await fixture.Engine.DownloadAsync(request,null,null,default,callbacks.Add);
        PracticalFacebookChecks.Assert(archived.Succeeded&&archived.Skipped&&attempts==(mode=="validator"?4:3)&&callbacks.Count==completed,"Recovered Facebook playback repeated transfer or completion.");
    }
    private sealed class InterruptedPrefix(byte[] bytes,CancellationTokenSource? cancel):MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken ct=default)
        {
            if(Position<Length)return base.ReadAsync(buffer,ct);
            if(cancel is not null){cancel.Cancel();ct.ThrowIfCancellationRequested();}
            throw new IOException("Owned interruption after a validated media prefix.");
        }
    }
}
