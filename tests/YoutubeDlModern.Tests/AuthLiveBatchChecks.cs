using System.IO;
using System.Net;
using System.Net.Http;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Http;
using YoutubeDlModern.Models;
using YoutubeDlModern.Services;

internal static partial class Program
{
    private const string AuthBatchId="e9c1c3e4-479f-4cf8-a80d-33d81d62f430";
    private static readonly string AuthBatchUrl="https://account.example/videos/watch/"+AuthBatchId;
    private sealed class AuthBatchHandler:HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            var path=request.RequestUri!.AbsolutePath;
            var text=path.EndsWith("/local")?"{\"client_id\":\"c\",\"client_secret\":\"s\"}":path.EndsWith("/users/token")?"{\"access_token\":\"a\",\"refresh_token\":\"r\",\"token_type\":\"Bearer\",\"expires_in\":3600}":path.EndsWith("/token")?"{\"files\":{\"token\":\"f\",\"expires\":\"2099-01-01T00:00:00Z\"}}":path.EndsWith(AuthBatchId)?"{\"uuid\":\""+AuthBatchId+"\",\"name\":\"Fixture\",\"privacy\":{\"id\":1},\"files\":[{\"fileUrl\":\"https://account.example/file.mp4\",\"hasAudio\":true,\"hasVideo\":true}]}":"{}";
            return Task.FromResult(new HttpResponseMessage(path.EndsWith("/captions")||path.EndsWith("/chapters")?HttpStatusCode.NotFound:HttpStatusCode.OK){Content=new StringContent(text)});
        }
    }
    private static NativeEngine AuthBatchEngine(List<NativeDownloadRequest> requests)=>new(httpFactory:request=>{requests.Add(request);return new(new AuthBatchHandler(),new(){AllowAutoRedirect=false});});
    private static async Task AuthBatchSavedAnalysis()
    {
        var service=new SettingsService(Path.Combine(TestRoot,"auth-batch-analysis"));var saved=new AppSettings();saved.Auth.Username="saved-user";saved.Auth.Password="saved-value";saved.Auth.TwoFactor="123456";saved.Auth.VideoPassword="video-value";saved.General.ExtraArgs="--username=override --password=replaced";service.Save(saved);var requests=new List<NativeDownloadRequest>();
        await new NativeEngineAdapter(AuthBatchEngine(requests),service).GetVideoInfoAsync(AuthBatchUrl,default);var actual=requests.Single().Authentication;
        Assert(actual.Username=="override"&&actual.Password=="replaced"&&actual.TwoFactor=="123456"&&actual.VideoPassword=="video-value"&&service.CreateSnapshot().Auth.Password=="saved-value","Saved account fields/analysis overrides changed or mutated settings.");
    }
    private static async Task AuthBatchJobOrder()
    {
        var requests=new List<NativeDownloadRequest>();var saved=new AppSettings();saved.Auth.Username="saved";saved.Auth.Password="saved-value";saved.General.ExtraArgs="--password=global";
        await new NativeEngineAdapter(AuthBatchEngine(requests)).DownloadAsync(AuthBatchUrl,new(){OutputDirectory=TestRoot,ExtraArgs="--password=job --simulate"},saved,null,new Progress<ProgressUpdate>(),_=>{},default);
        Assert(requests.Single().Authentication.Password=="job"&&saved.Auth.Password=="saved-value","Job authentication order or saved settings changed.");
    }
    private static async Task AuthBatchNetrc()
    {
        var path=Path.Combine(TestRoot,"account.netrc");await File.WriteAllTextAsync(path,"machine account.example login file-user password file-value");var service=new SettingsService(Path.Combine(TestRoot,"auth-batch-netrc"));var saved=new AppSettings();saved.Auth.UseNetrc=true;saved.General.ExtraArgs="--netrc-location \""+path+"\"";service.Save(saved);var requests=new List<NativeDownloadRequest>();
        await new NativeEngineAdapter(AuthBatchEngine(requests),service).GetVideoInfoAsync(AuthBatchUrl,default);
        Assert(requests.Single().Authentication.Username=="file-user"&&requests.Single().Authentication.Password=="file-value"&&saved.Auth.Password==""&&await File.ReadAllTextAsync(path)=="machine account.example login file-user password file-value","Netrc settings were not read-only or failed to resolve.");
    }
    private static async Task AuthBatchTelevisionBoundary()
    {
        var saved=new AppSettings();saved.Auth.ApPassword="tv-value";var requests=new List<NativeDownloadRequest>();try{await new NativeEngineAdapter(AuthBatchEngine(requests)).DownloadAsync(AuthBatchUrl,new(),saved,null,new Progress<ProgressUpdate>(),_=>{},default);throw new InvalidOperationException("TV authentication was accepted.");}catch(NativeEngineUnsupportedException ex){Assert(ex.Message.Contains("Auth.ApPassword")&&!ex.Message.Contains("tv-value")&&requests.Count==0,"TV boundary leaked credentials or opened HTTP.");}
    }
    private static async Task LiveBatchJobPolicy()
    {
        var requests=new List<NativeDownloadRequest>();var saved=new AppSettings();saved.General.ExtraArgs="--live-from-start --live-duration=2";await new NativeEngineAdapter(AuthBatchEngine(requests)).DownloadAsync("https://live.example/live.m3u8",new(){OutputDirectory=TestRoot,ExtraArgs="--no-live-from-start --live-duration=3 --simulate"},saved,null,new Progress<ProgressUpdate>(),_=>{},default);
        Assert(!requests.Single().LiveFromStart&&requests.Single().LiveDurationSeconds==3&&saved.General.ExtraArgs.Contains("=2"),"Live job overrides or saved settings changed.");
    }
}
