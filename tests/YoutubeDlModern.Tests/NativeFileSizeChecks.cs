using System.IO;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDlModern.Models;
using YoutubeDlModern.Services;

internal static partial class Program
{
    private static async Task NativeFileSizeSettings()
    {
        var requests=new List<NativeDownloadRequest>();var adapter=new NativeEngineAdapter(new NativeEngine(httpFactory:request=>{requests.Add(request);return new MediaHttp(new NativeCookieMappingHandler());}));
        var settings=new AppSettings();settings.Selection.MinFilesize="1K";settings.Selection.MaxFilesize="2K";var options=new DownloadOptions{OutputDirectory=TestRoot,AddMetadata=false,EmbedChapters=false,ExtraArgs="--simulate"};
        await adapter.DownloadAsync("https://media.example/size.mp4",options,settings,null,new Progress<ProgressUpdate>(),_=>{},default);
        Assert(requests[^1].MinimumFileSize=="1K"&&requests[^1].MaximumFileSize=="2K","Saved byte bounds were dropped.");
        options.ExtraArgs="--simulate --min-filesize 5 --max-filesize 20";await adapter.DownloadAsync("https://media.example/size.mp4",options,settings,null,new Progress<ProgressUpdate>(),_=>{},default);
        Assert(requests[^1].MinimumFileSize=="5"&&requests[^1].MaximumFileSize=="20"&&settings.Selection.MinFilesize=="1K","Job byte bounds changed settings or lost precedence.");
        var output=Path.Combine(TestRoot,"native-size-rejected");options.OutputDirectory=output;options.ExtraArgs="--min-filesize 0 --max-filesize 2";
        try{await adapter.DownloadAsync("https://media.example/size.mp4",options,settings,null,new Progress<ProgressUpdate>(),_=>{},default,_=>throw new InvalidOperationException("Rejected bytes emitted history completion."));throw new InvalidOperationException("Oversized native bytes were accepted.");}
        catch(NativeFileSizeException){Assert(!Directory.GetFiles(output,"*.mp4").Any(),"Rejected native size published media.");}
    }
}
