using System.IO;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDlModern.Models;
using YoutubeDlModern.Services;

internal static partial class Program
{
    private static async Task NativeMetadataSelectionSettings()
    {
        var requests=new List<NativeDownloadRequest>();
        var adapter=new NativeEngineAdapter(new NativeEngine(httpFactory:request=>{requests.Add(request);return new MediaHttp(new NativeCookieMappingHandler());}));
        var settings=new AppSettings();settings.Selection.Date="20260131";settings.Selection.DateBefore="20250101";settings.Selection.DateAfter="20200101";
        settings.Selection.MinViews=100;settings.Selection.MaxViews=500;settings.Selection.AgeLimit=12;settings.Selection.MatchFilter="duration>?20";
        var options=new DownloadOptions{OutputDirectory=TestRoot,AddMetadata=false,EmbedChapters=false,ExtraArgs="--simulate"};
        await adapter.DownloadAsync("https://media.example/metadata.mp4",options,settings,null,new Progress<ProgressUpdate>(),_=>{},default);
        var saved=requests[^1];Assert(saved.Date=="20260131"&&saved.MinimumViews==100&&saved.MaximumViews==500&&saved.MaximumAge==12&&saved.MatchFilter=="duration>?20","Saved metadata filters did not reach the native request.");
        options.ExtraArgs="--simulate --date 20260130 --min-views 200 --max-views 300 --age-limit 0 --no-match-filters --match-filter !is_live";
        await adapter.DownloadAsync("https://media.example/metadata.mp4",options,settings,null,new Progress<ProgressUpdate>(),_=>{},default);
        var overridden=requests[^1];Assert(overridden.Date=="20260130"&&overridden.MinimumViews==200&&overridden.MaximumViews==300&&overridden.MaximumAge==0&&overridden.MatchFilter==""&&overridden.MatchFilters.Single()=="!is_live","Job overrides lost metadata filter precedence.");
        Assert(settings.Selection.Date=="20260131"&&settings.Selection.MinViews==100&&settings.Selection.AgeLimit==12,"Job filters modified persisted settings.");
    }
    private static async Task NativeMetadataSelectionEffects()
    {
        var directory=Path.Combine(TestRoot,"metadata-filter-effects");var archive=Path.Combine(directory,"archive.txt");var calls=0;var callbacks=0;
        var page=AdapterVideo("metadata") with{UploadDate=new(2026,1,31),ViewCount=250,AgeLimit=12,DurationSeconds=75};
        var adapter=new NativeEngineAdapter(new NativeEngine([new NativePreviewProvider(page)],_=>new MediaHttp(new NativeCookieMappingHandler())));
        var settings=new AppSettings();settings.Selection.MinViews=300;settings.General.DownloadArchive=archive;settings.General.WriteInfoJson=true;
        var options=new DownloadOptions{OutputDirectory=directory,AddMetadata=false,EmbedChapters=false};
        var rejected=await adapter.DownloadAsync("https://media.example/watch",options,settings,null,new Progress<ProgressUpdate>(),_=>{},default,_=>callbacks++);
        Assert(rejected.ExitCode==0&&rejected.FinalFilePaths.Count==0&&callbacks==0&&!Directory.Exists(directory),"Rejected metadata created output, archive or history callbacks.");
        var inspected=await adapter.GetVideoInfoAsync("https://media.example/watch",default);
        Assert(inspected.UploadDate=="20260131"&&inspected.ViewCount==250,"Native preview discarded date/view facts.");
        settings.Selection.MinViews=200;settings.Selection.MaxViews=300;settings.Selection.AgeLimit=12;settings.Selection.Date="20260131";settings.Selection.MatchFilter="duration>60";
        var accepted=await adapter.DownloadAsync("https://media.example/watch",options,settings,null,new Progress<ProgressUpdate>(),_=>calls++,default,_=>callbacks++);
        Assert(accepted.ExitCode==0&&accepted.FinalFilePaths.Count==1&&callbacks==1&&File.Exists(archive),"Accepted native filters failed to publish their media and archive.");
        using var json=JsonDocument.Parse(await File.ReadAllTextAsync(Directory.GetFiles(directory,"*.info.json").Single()));
        Assert(json.RootElement.GetProperty("view_count").GetInt64()==250&&json.RootElement.GetProperty("upload_date").GetString()=="20260131","Published native metadata did not match inspected filter facts.");
    }
}
