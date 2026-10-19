using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Http;
using YoutubeDlModern.Services;

internal static partial class Program
{
    private static async Task NativeVocarooAnalysis()
    {
        const string id="1AbCd234Ef56";var calls=new List<HttpMethod>();
        var adapter=new NativeEngineAdapter(new NativeEngine(httpFactory:_=>new MediaHttp(new VocarooAnalysisHandler(request=>
        {
            calls.Add(request.Method);
            if(request.Method==HttpMethod.Head)
            {
                var response=new HttpResponseMessage(HttpStatusCode.OK){RequestMessage=request,Content=new ByteArrayContent([])};
                response.Content.Headers.ContentType=new("audio/mpeg");response.Content.Headers.ContentLength=77013;
                response.Content.Headers.ContentDisposition=new("attachment"){FileName="Vocaroo "+id+".mp3"};return response;
            }
            if(request.RequestUri!.Host!="vocaroo.com")throw new InvalidOperationException("Vocaroo analysis fetched media.");
            return new(HttpStatusCode.OK){RequestMessage=request,Content=new StringContent("<script>var process={env:{\"MEDIA_MP3_FILE_URL\":\"https://media.vocaroo.com/mp3/\",\"MEDIA_MP3_FILE_URL1\":\"https://media1.vocaroo.com/mp3/\"}}</script>",System.Text.Encoding.UTF8,"text/html")};
        }))));
        var info=await adapter.GetVideoInfoAsync("https://voca.ro/"+id,default);var formats=await adapter.ListFormatsAsync("https://vocaroo.com/embed/"+id+"?autoplay=0",default);
        Assert(info.Id==id&&info.Title=="Vocaroo "+id&&formats.Single().FormatId=="mp3"&&calls.SequenceEqual(new[]{HttpMethod.Get,HttpMethod.Head,HttpMethod.Get,HttpMethod.Head}),"Vocaroo app analysis lost recording/format facts or fetched audio.");
    }
    private sealed class VocarooAnalysisHandler(Func<HttpRequestMessage,HttpResponseMessage> run):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){ct.ThrowIfCancellationRequested();return Task.FromResult(run(request));}}
}
