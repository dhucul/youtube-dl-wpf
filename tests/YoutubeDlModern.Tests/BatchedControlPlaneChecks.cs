using System.IO;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Http;
using YoutubeDlModern.Models;
using YoutubeDlModern.Services;

internal static partial class Program
{
    private static async Task NativeBatchedAnalysisControls()
    {
        var service=new SettingsService(Path.Combine(TestRoot,"batch-analysis"));var saved=new AppSettings();saved.General.ExtraArgs="--exec \"--user-agent\" --force-ipv6 --no-check-certificates --user-agent Batch/1 --no-cookies --no-cookies-from-browser --audio-format=flac";service.Save(saved);
        var requests=new List<NativeDownloadRequest>();var adapter=new NativeEngineAdapter(AdapterEngine((request,_)=>{requests.Add(request);return Task.FromResult(AdapterVideo());}),service);
        await adapter.GetVideoInfoAsync("https://media.example/watch",default);var actual=requests.Single();Assert(actual.IpVersion==6&&actual.NoCheckCertificate&&actual.UserAgent=="Batch/1"&&actual.CookieFile is null&&actual.BrowserCookies is null&&!actual.AudioOnly,"Shared analysis controls were dropped or contaminated by download-only values.");
    }
    private static async Task NativeBatchedAuthenticationPreflight()
    {
        var service=new SettingsService(Path.Combine(TestRoot,"batch-auth"));var saved=new AppSettings();saved.General.ExtraArgs="--password private-fixture-value";service.Save(saved);var calls=0;var adapter=new NativeEngineAdapter(AdapterEngine((_,_)=>{calls++;return Task.FromResult(AdapterVideo());}),service);
        try{await adapter.GetVideoInfoAsync("https://media.example/watch",default);throw new InvalidOperationException("Unsupported authentication was silently discarded.");}catch(NativeEngineUnsupportedException ex){Assert(calls==0&&!ex.Message.Contains("private-fixture-value"),"Unsupported analysis auth reached discovery or exposed values.");}
    }
    private static async Task NativeBatchedJobOrder()
    {
        var requests=new List<NativeDownloadRequest>();var adapter=new NativeEngineAdapter(new NativeEngine(httpFactory:request=>{requests.Add(request);return new MediaHttp(new NativeCookieMappingHandler());}));var saved=new AppSettings();saved.General.ExtraArgs="-x --audio-format=flac --audio-quality=0 --buffer-size=4K";var options=new DownloadOptions{OutputDirectory=TestRoot,AddMetadata=false,EmbedChapters=false,ExtraArgs="--simulate --audio-format=mp3 --audio-quality=128K --no-keep-video --http-chunk-size=0"};
        await adapter.DownloadAsync("https://media.example/file.mp3",options,saved,null,new Progress<ProgressUpdate>(),_=>{},default);var actual=requests.Single();Assert(actual.AudioOnly&&actual.AudioFormat=="mp3"&&actual.AudioQuality=="128K"&&actual.BufferSize=="4K"&&actual.HttpChunkSize=="0"&&saved.General.ExtraArgs.Contains("flac"),"Saved/job CLI order changed settings or lost typed policies.");
    }
}
