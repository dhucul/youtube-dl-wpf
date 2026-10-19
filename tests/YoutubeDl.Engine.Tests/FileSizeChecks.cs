using System.Net;
using System.Net.Http.Headers;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;

internal static class FileSizeChecks
{
    internal static IEnumerable<(string Name,Func<Task> Test)> All=>
    [
        ("File-size limits parse binary byte quantities without overflow",Parse),
        ("File-size limits known response totals refuse before body or partial-file changes",Known),
        ("File-size limits unknown totals stop before writing bytes beyond the maximum",UnknownMaximum),
        ("File-size limits unknown small bodies cannot become published files",UnknownMinimum),
        ("File-size limits inclusive boundaries permit complete exact-sized media",Boundary),
        ("File-size limits resumptions use complete range size rather than remaining body",Resume),
        ("File-size limits refusals are terminal and never consume retry loops",NoRetry),
        ("File-size limits native refusals preserve existing media archive and callbacks",NativeEffects),
        ("File-size limits accepted media record stable archives and completed files",NativeAccepted),
        ("File-size limits invalid values and argument overrides fail before discovery",Arguments)
    ];
    private static Task Parse()
    {
        Assert(NativeFileSizeLimits.Parse("1K")==1024&&NativeFileSizeLimits.Parse("1.5M")==1572864&&NativeFileSizeLimits.Parse("0")==0,"Byte parsing changed.");
        foreach(var input in new[]{"-1","bad","1e999","999999999999999999Y"})ExpectSync<NativeSelectionException>(()=>NativeFileSizeLimits.Parse(input));
        ExpectSync<NativeSelectionException>(()=>new NativeFileSizeLimits(10,9));return Task.CompletedTask;
    }
    private static async Task Known()
    {
        using var files=new Files();var output=Path.Combine(files.Path,"out.mp4");var part=output+".part";await File.WriteAllTextAsync(part,"preserved partial");var reads=0;
        using var http=new MediaHttp(new Handler(_=>new(HttpStatusCode.OK){Content=new UnknownContent(new string('x',20),()=>reads++,20)}));
        await Expect<NativeFileSizeException>(()=>new FileDownloader(http){MaximumBytes=10}.DownloadAsync(new("https://size.example/file"),output,null,null,default,0));
        Assert(reads==0&&await File.ReadAllTextAsync(part)=="preserved partial"&&!File.Exists(output),"Known size refusal changed partial/body state.");
    }
    private static async Task UnknownMaximum()
    {
        using var files=new Files();var output=Path.Combine(files.Path,"out.mp4");
        using var http=new MediaHttp(new Handler(_=>new(HttpStatusCode.OK){Content=new UnknownContent(new string('x',100),()=>{})}));
        await Expect<NativeFileSizeException>(()=>new FileDownloader(http){MaximumBytes=10}.DownloadAsync(new("https://size.example/file"),output,null,null,default,0));
        Assert(!File.Exists(output)&&new FileInfo(output+".part").Length<=10,"Unknown maximum wrote/published excessive bytes.");
    }
    private static async Task UnknownMinimum()
    {
        using var files=new Files();var output=Path.Combine(files.Path,"out.mp4");
        using var http=new MediaHttp(new Handler(_=>new(HttpStatusCode.OK){Content=new UnknownContent("small",()=>{})}));
        await Expect<NativeFileSizeException>(()=>new FileDownloader(http){MinimumBytes=10}.DownloadAsync(new("https://size.example/file"),output,null,null,default,0));
        Assert(!File.Exists(output)&&await File.ReadAllTextAsync(output+".part")=="small","Small unknown source was published or lost.");
    }
    private static async Task Boundary()
    {
        using var files=new Files();using var http=new MediaHttp(new Handler(_=>new(HttpStatusCode.OK){Content=new StringContent("exact")}));var output=Path.Combine(files.Path,"out.mp4");
        await new FileDownloader(http){MinimumBytes=5,MaximumBytes=5}.DownloadAsync(new("https://size.example/file"),output,null,null,default,0);
        Assert(await File.ReadAllTextAsync(output)=="exact","Inclusive file size failed.");
    }
    private static async Task Resume()
    {
        using var files=new Files();var output=Path.Combine(files.Path,"out.mp4");var url=new Uri("https://size.example/file");var calls=0;
        using var http=new MediaHttp(new Handler(request=>
        {
            calls++;if(calls==1){var broken=new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("abc")};broken.Content.Headers.ContentLength=6;broken.Headers.ETag=new("\"same\"");return broken;}
            Assert(request.Headers.Range?.Ranges.Single().From==3,"Partial resume was not requested.");var resumed=new HttpResponseMessage(HttpStatusCode.PartialContent){Content=new StringContent("def")};resumed.Content.Headers.ContentRange=new(3,5,6);resumed.Headers.ETag=new("\"same\"");return resumed;
        }));
        await Expect<IOException>(()=>new FileDownloader(http).DownloadAsync(url,output,null,null,default,0));
        await Expect<NativeFileSizeException>(()=>new FileDownloader(http){MaximumBytes=5}.DownloadAsync(url,output,null,null,default,0));
        Assert(!File.Exists(output)&&await File.ReadAllTextAsync(output+".part")=="abc","Resume bounds used only remaining bytes or changed the valid partial.");
    }
    private static async Task NoRetry()
    {
        using var files=new Files();var calls=0;using var http=new MediaHttp(new Handler(_=>{calls++;return new(HttpStatusCode.OK){Content=new StringContent("too large")};}));
        await Expect<NativeFileSizeException>(()=>new FileDownloader(http){MaximumBytes=1}.DownloadAsync(new("https://size.example/file"),Path.Combine(files.Path,"out"),null,null,default,10));
        Assert(calls==1,"Size refusal was retried.");
    }
    private static async Task NativeEffects()
    {
        using var files=new Files();var old=Path.Combine(files.Path,"size.mp4");await File.WriteAllTextAsync(old,"prior complete file");var archive=Path.Combine(files.Path,"archive.txt");await File.WriteAllTextAsync(archive,"other preserved\n");var callbacks=0;
        await Expect<NativeFileSizeException>(()=>Engine().DownloadAsync(Request(files.Path)with{MaximumFileSize="4",DownloadArchiveFile=archive,WriteInfoJson=true},null,null,default,_=>callbacks++));
        Assert(await File.ReadAllTextAsync(old)=="prior complete file"&&await File.ReadAllTextAsync(archive)=="other preserved\n"&&callbacks==0&&!File.Exists(Path.Combine(files.Path,"size.info.json")),"Native size refusal changed prior media/archive/sidecars.");
    }
    private static async Task NativeAccepted()
    {
        using var files=new Files();var archive=Path.Combine(files.Path,"archive.txt");var request=Request(files.Path)with{MinimumFileSize="12",MaximumFileSize="12",DownloadArchiveFile=archive};var callbacks=0;var engine=Engine();
        var result=await engine.DownloadAsync(request,null,null,default,_=>callbacks++);Assert(result.Succeeded&&callbacks==1&&new FileInfo(result.OutputPath!).Length==12,"Accepted byte bounds changed publication.");
        var repeat=await engine.DownloadAsync(request,null,null,default,_=>callbacks++);Assert(repeat.Succeeded&&repeat.Skipped&&callbacks==1,"Archived bounded media repeated completion.");
    }
    private static async Task Arguments()
    {
        var request=NativeExtraArguments.Apply(Request(Path.GetTempPath())with{MinimumFileSize="1K",MaximumFileSize="2K"},["--min-filesize","5","--max-filesize","10"]);
        Assert(request.MinimumFileSize=="5"&&request.MaximumFileSize=="10","File-size argument precedence changed.");
        var engine=new NativeEngine(httpFactory:_=>throw new InvalidOperationException("Invalid file bounds contacted network."));
        await Expect<NativeSelectionException>(()=>engine.DownloadAsync(request with{MinimumFileSize="bad"},null,null,default));
        await Expect<NativeSelectionException>(()=>engine.DownloadAsync(request with{MinimumFileSize="20"},null,null,default));
    }
    private static NativeDownloadRequest Request(string output)=>new(){Url=new("https://size.example/watch"),OutputDirectory=output,OutputTemplate="size.%(ext)s",DownloadPlaylist=false};
    private static NativeEngine Engine()=>new([new Provider()],_=>new MediaHttp(new Handler(_=>new(HttpStatusCode.OK){Content=new ByteArrayContent("fixture data"u8.ToArray())})));
    private sealed class Provider:IMediaProvider
    {
        public MediaProviderInfo Info{get;}=new("size","Size fixture",1000);public bool CanHandle(Uri url)=>true;
        public DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request)=>new("size","fixture");
        public Task<PageMetadata> ExtractAsync(NativeDownloadRequest request,MediaHttp http,CancellationToken ct)=>Task.FromResult(new PageMetadata{Id="fixture",Extractor="size",Title="Size",Media=[new(){Url=new("https://size.example/file.mp4"),Extension="mp4",HasAudio=true,HasVideo=true} ]});
    }
    private sealed class UnknownContent(string text,Action read,long? length=null):HttpContent
    {
        private readonly byte[] _bytes=System.Text.Encoding.UTF8.GetBytes(text);
        protected override bool TryComputeLength(out long result){result=length??0;return length is not null;}
        protected override Task SerializeToStreamAsync(Stream stream,TransportContext? context){read();return stream.WriteAsync(_bytes).AsTask();}
        protected override Task<Stream> CreateContentReadStreamAsync(){read();return Task.FromResult<Stream>(new MemoryStream(_bytes));}
    }
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> run):HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){ct.ThrowIfCancellationRequested();return Task.FromResult(run(request));}}
    private sealed class Files:IDisposable{internal string Path{get;}=System.IO.Path.Combine(AppContext.BaseDirectory,"test-data","size-"+Guid.NewGuid().ToString("N"));internal Files()=>Directory.CreateDirectory(Path);public void Dispose()=>Directory.Delete(Path,true);}
    private static void Assert(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private static void ExpectSync<T>(Action action)where T:Exception{try{action();}catch(T){return;}throw new InvalidOperationException("Expected "+typeof(T).Name);}
    private static async Task Expect<T>(Func<Task> action)where T:Exception{try{await action();}catch(T){return;}throw new InvalidOperationException("Expected "+typeof(T).Name);}
}
