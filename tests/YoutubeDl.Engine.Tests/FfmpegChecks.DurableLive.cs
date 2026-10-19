using System.Diagnostics;
using System.Text.Json;
internal static partial class FfmpegChecks
{
    internal static IEnumerable<(string Name,Func<Task> Test)> DurableLiveMediaChecks(string ffmpeg)=>
        new[]{"hls","hls-encrypted","hls-range","hls-reset","hls-srt","dash-wvtt","dash-stpp","dash-range","dash-video","dash-audio"}.Select(mode=>("Durable live killed caption worker "+mode,(Func<Task>)(()=>LiveCaptionMedia(ffmpeg,mode,"process"))))
        .Concat(new[]{"corrupt","payload","denied","missing"}.Select(mode=>("Durable live HLS caption failure "+mode,(Func<Task>)(()=>LiveCaptionMedia(ffmpeg,"hls",mode)))))
        .Concat(new[]{"corrupt","payload","denied","missing"}.Select(mode=>("Durable live DASH caption failure "+mode,(Func<Task>)(()=>LiveCaptionMedia(ffmpeg,"dash-wvtt",mode)))))
        .Concat(new[]{"skip","replay","denied","startup-media","corrupt","partial"}.Select(mode=>("Durable live killed SABR worker "+mode,(Func<Task>)(()=>SabrPrefix(ffmpeg,mode,true)))));
    internal static async Task<int> DurableLiveWorker(string mode,string directory,string ffmpeg)
    {
        if(mode.StartsWith("sabr:"))await SabrPrefix(ffmpeg,mode["sabr:".Length..],true,directory,true);
        else await LiveCaptionMedia(ffmpeg,mode,"process",directory,true);
        throw new InvalidOperationException("The interruption worker escaped its transfer gate.");
    }
    private static async Task KillDurableWorker(string mode,string directory,string ffmpeg)
    {
        var marker=Path.Combine(directory,"worker-ready");var start=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true};
        foreach(var argument in new[]{typeof(FfmpegChecks).Assembly.Location,"--durable-live-worker",mode,directory,ffmpeg})start.ArgumentList.Add(argument);
        using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();var ready=false;
        try
        {
            for(var attempt=0;attempt<400&&!process.HasExited;attempt++)
            {
                try{ready=File.Exists(marker)&&int.TryParse(await File.ReadAllTextAsync(marker),out var pid)&&pid==process.Id;}catch(IOException){}
                if(ready)break;await Task.Delay(50);
            }
            AdaptiveRecoveryChecks.Assert(ready,"Worker failed before durable checkpoint: "+(process.HasExited?await stderr:"timeout"));
            process.Kill(entireProcessTree:true);await process.WaitForExitAsync();
            AdaptiveRecoveryChecks.Assert(process.ExitCode!=0,"Worker exited normally rather than being interrupted.");
        }
        finally{if(!process.HasExited){process.Kill(entireProcessTree:true);await process.WaitForExitAsync();}await stdout;await stderr;}
    }
    private static async Task PauseDurableWorker(string directory,CancellationToken ct)
    {
        var marker=Path.Combine(directory,"worker-ready");await File.WriteAllTextAsync(marker+".tmp",Environment.ProcessId.ToString(),ct);File.Move(marker+".tmp",marker,true);
        await Task.Delay(Timeout.Infinite,ct);
    }
    private sealed class PendingWorkerStream(byte[] bytes,string directory):Stream
    {
        private readonly MemoryStream _input=new(bytes);
        public override bool CanRead=>true;public override bool CanWrite=>false;public override bool CanSeek=>false;
        public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken ct=default)
        {
            var count=await _input.ReadAsync(buffer,ct);if(count==0)await PauseDurableWorker(directory,ct);return count;
        }
        public override Task<int> ReadAsync(byte[] buffer,int offset,int count,CancellationToken ct)=>ReadAsync(buffer.AsMemory(offset,count),ct).AsTask();
        public override int Read(byte[] buffer,int offset,int count)=>ReadAsync(buffer.AsMemory(offset,count)).AsTask().GetAwaiter().GetResult();
        public override void Flush(){}public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();public override void SetLength(long value)=>throw new NotSupportedException();public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
        protected override void Dispose(bool disposing){if(disposing)_input.Dispose();base.Dispose(disposing);}
    }
    private static bool CaptionGate(string directory,bool dash,int expectedTracks)
    {
        var media=Directory.GetFiles(directory,"timeline.json",SearchOption.AllDirectories).Select(file=>
        {
            using var document=JsonDocument.Parse(File.ReadAllText(file));return document.RootElement.GetProperty("Records").EnumerateArray().Select(row=>JsonDocument.Parse(row.GetProperty("Metadata").GetString()!).RootElement.Clone()).Where(row=>row.TryGetProperty("Kind",out var kind)&&kind.GetString()=="media").ToArray();
        }).ToArray();
        if(media.Length!=expectedTracks||media.Any(rows=>dash?!rows.Any(row=>row.GetProperty("Discontinuity").GetInt64()>0):rows.Length<2))return false;
        foreach(var file in Directory.GetFiles(directory,"captions.json",SearchOption.AllDirectories))
        {
            using var document=JsonDocument.Parse(File.ReadAllText(file));var rows=document.RootElement.GetProperty("Entries").EnumerateArray().ToArray();
            if(!dash&&rows.Length>=2)return true;
            if(dash&&rows.Count(row=>JsonDocument.Parse(row.GetProperty("Description").GetString()!).RootElement.GetProperty("Kind").GetString()=="period")>=2)return true;
        }
        return false;
    }
    private static void DamageCaptionSnapshot(string directory,bool remove)
    {
        var file=Directory.GetFiles(directory,"captions.json",SearchOption.AllDirectories).Single();
        if(remove)File.Delete(file);else{var node=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))!;node["Entries"]![0]!["Bytes"]="ZGFtYWdl";File.WriteAllText(file,node.ToJsonString());}
    }
}
