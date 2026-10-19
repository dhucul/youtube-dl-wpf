using System.Net;
using System.Net.Http;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Http;
using YoutubeDlModern.Services;

internal static partial class Program
{
    private static async Task NativeClypAnalysis()
    {
        const string id="abcd1234";var calls=new List<HttpMethod>();
        var adapter=new NativeEngineAdapter(new NativeEngine(httpFactory:_=>new MediaHttp(new ClypAnalysisHandler(request=>
        {
            calls.Add(request.Method);
            if(request.Method==HttpMethod.Head){var head=new HttpResponseMessage(HttpStatusCode.OK){RequestMessage=request,Content=new ByteArrayContent([])};head.Content.Headers.ContentType=new("audio/mpeg");head.Content.Headers.ContentLength=1234;return head;}
            if(request.RequestUri!.Host!="api.clyp.it")throw new InvalidOperationException("Clyp analysis fetched audio.");
            return new(HttpStatusCode.OK){RequestMessage=request,Content=new StringContent("{\"Status\":\"DownloadDisabled\",\"AudioFileId\":\"abcd1234\",\"Title\":\"Public track\",\"Description\":\"Published\",\"Duration\":2,\"Url\":\"https://clyp.it/abcd1234\",\"SecureMp3Url\":\"https://a.clyp.it/abcd1234.mp3\",\"DateCreated\":\"2026-01-31T12:30:00Z\"}",System.Text.Encoding.UTF8,"application/json")};
        }))));
        var info=await adapter.GetVideoInfoAsync("https://clyp.it/"+id,default);var formats=await adapter.ListFormatsAsync("https://api.clyp.it/"+id,default);
        Assert(info.Id==id&&info.Title=="Public track"&&info.UploadDate=="20260131"&&formats.Single().FormatId=="mp3"&&calls.SequenceEqual(new[]{HttpMethod.Get,HttpMethod.Head,HttpMethod.Get,HttpMethod.Head}),"Clyp app analysis lost facts or fetched media.");
    }
    private sealed class ClypAnalysisHandler(Func<HttpRequestMessage,HttpResponseMessage> run):HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){ct.ThrowIfCancellationRequested();return Task.FromResult(run(request));}}
}
