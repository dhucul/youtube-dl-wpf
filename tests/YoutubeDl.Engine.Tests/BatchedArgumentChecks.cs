using YoutubeDl.Engine;

internal static class BatchedArgumentChecks
{
    internal static IEnumerable<(string Name,Func<Task> Test)> All=>
    [
        ("Batched arguments audio extraction resets conflicting video processing in order",Audio),
        ("Batched arguments inline audio and transfer values preserve ordered overrides",Inline),
        ("Batched arguments ffmpeg directory resolves a structured local executable",Location),
        ("Batched arguments original chapter metadata and filename policies remain ordered",Flags),
        ("Batched arguments force overwrite includes restart and preserves later overrides",Overwrite),
        ("Batched arguments thumbnail disable clears all-variant requests",Thumbnails),
        ("Batched arguments archive and cookie resets preserve independent policy",Resets),
        ("Batched analysis preserves boolean network and cookie choices without output options",Analysis),
        ("Batched analysis opaque command values cannot inject connection flags",Opaque),
        ("Batched analysis unsupported authentication remains explicit and value-free",Unsupported),
        ("Batched analysis malformed and oversized argument lists fail before discovery",Invalid),
        ("Batched arguments invalid audio requests fail before network and output",Preflight)
    ];
    private static NativeDownloadRequest Request()=>new(){Url=new("https://argument.example/watch"),OutputDirectory="."};
    private static Task Audio()
    {var original=Request()with{VideoOnly=true,RecodeVideo="webm",Container="mkv"};var mapped=NativeExtraArguments.Apply(original,["-x","--audio-format","flac","--audio-quality","0"]);Assert(mapped.AudioOnly&&!mapped.VideoOnly&&mapped.RecodeVideo==""&&mapped.Container==""&&mapped.AudioFormat=="flac"&&original.VideoOnly,"Extraction conflicted with video processing or mutated source options.");return Task.CompletedTask;}
    private static Task Inline()
    {var mapped=NativeExtraArguments.Apply(Request(),["--extract-audio","--audio-format=mp3","--audio-format","m4a","--audio-quality=128K","--buffer-size=4K","--http-chunk-size=16K"]);Assert(mapped.AudioFormat=="m4a"&&mapped.AudioQuality=="128K"&&mapped.BufferSize=="4K"&&mapped.HttpChunkSize=="16K","Inline/shared argument ordering failed.");return Task.CompletedTask;}
    private static Task Location()
    {var mapped=NativeExtraArguments.Apply(Request(),["--ffmpeg-location",AppContext.BaseDirectory]);Assert(mapped.FfmpegPath==Path.Combine(Path.GetFullPath(AppContext.BaseDirectory),OperatingSystem.IsWindows()?"ffmpeg.exe":"ffmpeg"),"ffmpeg directory was not resolved as a file.");return Task.CompletedTask;}
    private static Task Flags()
    {var mapped=NativeExtraArguments.Apply(Request()with{KeepOriginal=true,AddMetadata=true,EmbedChapters=true,RestrictFilenames=true},["--no-keep-video","--no-add-metadata","--no-add-chapters","--no-restrict-filenames","--add-chapters","--no-abort-on-error"]);Assert(!mapped.KeepOriginal&&!mapped.AddMetadata&&mapped.EmbedChapters&&!mapped.RestrictFilenames&&mapped.IgnoreErrors,"Inverse/alias policies lost order.");return Task.CompletedTask;}
    private static Task Overwrite()
    {var mapped=NativeExtraArguments.Apply(Request()with{NoOverwrites=true,NoPostOverwrites=true},["--force-overwrites"]);Assert(!mapped.NoOverwrites&&!mapped.NoPostOverwrites&&mapped.NoContinue,"Force overwrite did not include restart.");mapped=NativeExtraArguments.Apply(mapped,["--continue","-w"]);Assert(!mapped.NoContinue&&mapped.NoOverwrites,"Later independent overwrite/resume policies were dropped.");return Task.CompletedTask;}
    private static Task Thumbnails()
    {var mapped=NativeExtraArguments.Apply(Request(),["--write-all-thumbnails","--no-write-thumbnail"]);Assert(!mapped.WriteThumbnail&&!mapped.WriteAllThumbnails,"Disabled thumbnails retained all-image requests.");return Task.CompletedTask;}
    private static Task Resets()
    {var mapped=NativeExtraArguments.Apply(Request()with{DownloadArchiveFile="old",ForceWriteArchive=true,CookieFile="old",BrowserCookies="firefox"},["--no-download-archive","--no-cookies","--no-cookies-from-browser"]);Assert(mapped.DownloadArchiveFile==""&&mapped.ForceWriteArchive&&mapped.CookieFile is null&&mapped.BrowserCookies is null,"Reset altered unrelated policy or retained credentials.");return Task.CompletedTask;}
    private static Task Analysis()
    {var selected=NativeAnalysisArguments.Select(["--extract-audio","--audio-format=flac","--force-ipv6","--no-check-certificates","--no-cookies","--cookies-from-browser","firefox","--no-cookies-from-browser","--socket-timeout=17","--write-info-json"]);var mapped=NativeExtraArguments.Apply(Request(),selected);Assert(mapped.IpVersion==6&&mapped.NoCheckCertificate&&mapped.TimeoutSeconds==17&&mapped.CookieFile is null&&mapped.BrowserCookies is null&&!mapped.AudioOnly&&!mapped.WriteInfoJson,"Analysis dropped connection controls or leaked output behavior.");return Task.CompletedTask;}
    private static Task Opaque()
    {var selected=NativeAnalysisArguments.Select(["--exec","--user-agent","--user-agent","Actual/1","--postprocessor-args=--proxy","--force-ipv4"]);var mapped=NativeExtraArguments.Apply(Request(),selected);Assert(mapped.UserAgent=="Actual/1"&&mapped.Proxy is null&&mapped.IpVersion==4,"Opaque download values changed analysis connection controls.");return Task.CompletedTask;}
    private static Task Unsupported()
    {foreach(var flag in new[]{"--password","--extractor-args","--geo-bypass-country"}){try{var mapped=NativeExtraArguments.Apply(Request(),NativeAnalysisArguments.Select([flag,"private-fixture-value"]));new NativeEngine().ValidateAuthenticationOptions(mapped);throw new InvalidOperationException("Unsupported extraction controls were discarded.");}catch(Exception ex) when(ex is NotSupportedException or ArgumentException){Assert(!ex.Message.Contains("private-fixture-value"),"Unsupported analysis exposed configured values.");}}return Task.CompletedTask;}
    private static Task Invalid()
    {foreach(var args in new[]{new[]{"--user-agent"},new[]{"--force-ipv4=1"},new[]{"--unknown-analysis-option","private"},Enumerable.Repeat("--force-ipv4",1001).ToArray()}){try{NativeAnalysisArguments.Select(args);throw new InvalidOperationException("Invalid analysis arguments accepted.");}catch(Exception ex)when(ex is ArgumentException or NotSupportedException){}}return Task.CompletedTask;}
    private static async Task Preflight()
    {var engine=new NativeEngine(httpFactory:_=>throw new InvalidOperationException("Invalid argument reached HTTP."));foreach(var args in new[]{new[]{"-x","--audio-format=unknown"},new[]{"-x","--audio-quality=invalid"}}){var request=NativeExtraArguments.Apply(Request(),args);try{await engine.DownloadAsync(request,null,null,default);throw new InvalidOperationException("Invalid audio request accepted.");}catch(Exception ex)when(ex is ArgumentException or NotSupportedException){}}}
    private static void Assert(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
