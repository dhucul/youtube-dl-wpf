using System.IO;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Http;
using YoutubeDlModern.Models;
using YoutubeDlModern.Services;

internal static partial class Program
{
    private static async Task NativeMetadataParsingSettings()
    {
        var requests = new List<NativeDownloadRequest>(); var settings = new AppSettings(); settings.PostProcess.ParseMetadata = "title:artist"; settings.General.ExtraArgs = "--parse-metadata artist:album";
        var adapter = new NativeEngineAdapter(new NativeEngine(httpFactory:request=>{requests.Add(request);return new MediaHttp(new NativeCookieMappingHandler());}));
        var options = new DownloadOptions { OutputDirectory=TestRoot,AddMetadata=false,EmbedChapters=false,ExtraArgs="--simulate --parse-metadata album:meta_artist" };
        await adapter.DownloadAsync("https://media.example/metadata.mp4",options,settings,null,new Progress<ProgressUpdate>(),_=>{},default);
        Assert(requests.Single().MetadataParseRules.SequenceEqual(new[] {"title:artist","artist:album","album:meta_artist"}) && settings.PostProcess.ParseMetadata=="title:artist", "Saved/general/job parsing order changed saved settings.");
    }
    private static async Task NativeMetadataParsingPreflight()
    {
        var calls = 0; var adapter = new NativeEngineAdapter(new NativeEngine(httpFactory:_=>{calls++;throw new InvalidOperationException("Invalid metadata reached HTTP.");}));
        var settings = new AppSettings(); settings.PostProcess.ParseMetadata="fixture-private-value";
        try { await adapter.DownloadAsync("https://media.example/watch",new(){OutputDirectory=Path.Combine(TestRoot,"invalid-metadata")},settings,null,new Progress<ProgressUpdate>(),_=>{},default);throw new InvalidOperationException("Invalid metadata was ignored."); }
        catch (ArgumentException ex) { Assert(!ex.Message.Contains("fixture-private-value") && calls==0 && !Directory.Exists(Path.Combine(TestRoot,"invalid-metadata")), "Invalid parsing exposed values or changed output."); }
    }
    private static async Task NativeMetadataParsingPublication()
    {
        var directory=Path.Combine(TestRoot,"parsed-publication");var page=AdapterVideo("parsed") with {Title="Artist - Track"};
        var adapter=new NativeEngineAdapter(new NativeEngine([new NativePreviewProvider(page)],_=>new MediaHttp(new NativeCookieMappingHandler())));
        var settings=new AppSettings();settings.PostProcess.ParseMetadata="title:%(artist)s - %(title)s";settings.General.WriteInfoJson=true;
        var options=new DownloadOptions {OutputDirectory=directory,OutputTemplate="%(artist)s-%(title)s.%(ext)s",AddMetadata=false,EmbedChapters=false};
        var callbacks=0;var result=await adapter.DownloadAsync("https://media.example/watch",options,settings,null,new Progress<ProgressUpdate>(),_=>{},default,_=>callbacks++);
        using var info=JsonDocument.Parse(await File.ReadAllTextAsync(Directory.GetFiles(directory,"*.info.json").Single()));
        Assert(result.ExitCode==0&&callbacks==1&&Path.GetFileName(result.FinalFilePaths.Single())=="Artist-Track.mp4"&&info.RootElement.GetProperty("artist").GetString()=="Artist"&&settings.PostProcess.ParseMetadata=="title:%(artist)s - %(title)s","Saved parsing did not reach filenames/info JSON or changed settings.");
    }
}
