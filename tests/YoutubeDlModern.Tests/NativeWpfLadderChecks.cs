using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;
using YoutubeDlModern.Models;
using YoutubeDlModern.Services;
using YoutubeDlModern.ViewModels;

internal static partial class Program
{
    private static string? WpfLadderPreview;
    private static IEnumerable<(string Name,Func<Task> Test)> WpfLadderAppChecks=>
        new[]{"roundtrip","tokens","height","disabled","mapping"}.Select(mode=>("WPF ladders settings "+mode,(Func<Task>)(()=>WpfLadderSettings(mode))))
        .Concat(new[]{"analysis","snapshot","default-snapshot","refusal","cancel"}.Select(mode=>("WPF ladders routing "+mode,(Func<Task>)(()=>WpfLadderRouting(mode)))))
        .Concat(new[]{("WPF ladders controls bind save and render",(Func<Task>)WpfLadderControls),("WPF ladders status follows saved backend selection",(Func<Task>)WpfLadderStatus)});
    private static FormatSettings WpfPolicy()=>new(){NativeLiveDash=true,LiveAudioLanguages="fr, en",LiveAudioRoles="main,commentary",LiveCaptionRoles="subtitle",LiveNormalizeTransitions=true,LiveAdaptiveLadder=true,LiveTransitionHeight=480};
    private static async Task WpfLadderSettings(string mode)
    {
        var service=new SettingsService(Path.Combine(TestRoot,"wpf-policy-"+mode));var saved=new AppSettings{Formats=WpfPolicy()};
        if(mode=="tokens")saved.Formats.LiveAudioLanguages="en,EN";if(mode=="height")saved.Formats.LiveTransitionHeight=481;
        if(mode is "tokens" or "height"){try{service.Save(saved);throw new InvalidOperationException("Invalid native policy was saved.");}catch(ArgumentException){}Assert(!service.Data.Formats.NativeLiveDash,"Rejected policy changed the saved settings.");return;}
        service.Save(saved);var snapshot=service.CreateSnapshot();var draft=service.CreateDraft();draft.Formats.LiveAudioLanguages="de";Assert(snapshot.Formats.LiveAudioLanguages=="fr, en","Draft preferences changed a queued snapshot.");service.Load();Assert(service.Data.Formats.NativeLiveDash&&service.Data.Formats.LiveAdaptiveLadder&&service.Data.Formats.LiveTransitionHeight==480,"Native policy did not survive protected settings persistence.");
        if(mode=="roundtrip"){draft.Formats.LiveNormalizeTransitions=false;Assert(!draft.Formats.LiveAdaptiveLadder&&snapshot.Formats.LiveAdaptiveLadder,"Disabling conversion did not disable its dependent ladder or changed a snapshot.");return;}
        if(mode=="disabled"){snapshot.Formats.NativeLiveDash=false;snapshot.Formats.LiveAudioLanguages="invalid value";}
        var requests=new List<NativeDownloadRequest>();var engine=new NativeEngine([new WpfPolicyProvider()],httpFactory:request=>{requests.Add(request);return new MediaHttp();});
        await new NativeEngineAdapter(engine).DownloadAsync("https://ladder.example/watch",new(){OutputDirectory=TestRoot,AddMetadata=false,EmbedChapters=false,QualityPreset="720",ExtraArgs="--simulate"},snapshot,null,new Progress<ProgressUpdate>(),_=>{},default);
        var policy=requests.Single().LiveDashSelection;if(mode=="disabled")Assert(policy is null,"Disabled preferences changed a default native request.");else Assert(policy is {NormalizeTransitions:true,AdaptiveQualityLadder:true,TransitionHeight:480}&&policy.AudioLanguages.SequenceEqual(["fr","en"])&&policy.AudioRoles.SequenceEqual(["main","commentary"])&&policy.CaptionRoles.SequenceEqual(["subtitle"])&&requests[0].MaximumHeight==720,"WPF preferences or quality constraints did not reach the native request.");
    }
    private sealed class WpfPolicyProvider:IMediaProvider
    {
        public MediaProviderInfo Info=>new("wpf-policy","Fixture",10000);public bool CanHandle(Uri uri)=>true;
        public Task<PageMetadata> ExtractAsync(NativeDownloadRequest request,MediaHttp http,CancellationToken ct)=>Task.FromResult(new PageMetadata{Id="fixture",Title="Fixture",IsLive=true,Media=[new(){Id="master",Url=new("https://ladder.example/live.mpd"),Kind=MediaKind.Dash,HasAudio=true,HasVideo=true,Extension="mpd"}]});
    }
    private sealed class WpfRouteEngine:IYtDlpEngine
    {
        internal int Analysis,Formats,Downloads,Installs;internal bool Refuse;internal AppSettings? Snapshot;internal Action<string>? Completion;
        public bool IsInstalled=>true;
        public Task<VideoInfo> GetVideoInfoAsync(string url,CancellationToken ct,AnalysisOptions? options=null){ct.ThrowIfCancellationRequested();Analysis++;return Task.FromResult(new VideoInfo{Title="native"});}
        public Task<IReadOnlyList<FormatInfo>> ListFormatsAsync(string url,CancellationToken ct,AnalysisOptions? options=null,bool isPlaylist=false){ct.ThrowIfCancellationRequested();Formats++;return Task.FromResult<IReadOnlyList<FormatInfo>>([]);}
        public Task EnsureInstalledAsync(IProgress<(double Percent,string Message)>? progress,CancellationToken ct){ct.ThrowIfCancellationRequested();Installs++;return Task.CompletedTask;}
        public Task<DownloadRunResult> DownloadAsync(string url,DownloadOptions options,AppSettings settingsSnapshot,string? ffmpegLocation,IProgress<ProgressUpdate> progress,Action<string> log,CancellationToken ct,Action<string>? onFileCompleted=null){ct.ThrowIfCancellationRequested();Downloads++;Snapshot=settingsSnapshot;Completion=onFileCompleted;if(Refuse)throw new NativeEngineUnsupportedException("Fixture refusal.");return Task.FromResult(new DownloadRunResult{ExitCode=0});}
    }
    private static async Task WpfLadderRouting(string mode)
    {
        var service=new SettingsService(Path.Combine(TestRoot,"wpf-route-"+mode));var current=new AppSettings();current.Formats.NativeLiveDash=mode is "analysis" or "default-snapshot";service.Save(current);var native=new WpfRouteEngine{Refuse=mode=="refusal"};var engine=new YtDlpEngine(service,native);var snapshot=service.CreateSnapshot();snapshot.Formats=WpfPolicy();Action<string> completion=_=>{};
        if(mode=="analysis"){await engine.GetVideoInfoAsync("https://ladder.example/watch",default);await engine.ListFormatsAsync("https://ladder.example/watch",default);await engine.EnsureInstalledAsync(null,default);Assert(engine.IsInstalled&&native.Analysis==1&&native.Formats==1&&native.Installs==1&&(await engine.GetVersionAsync())!.StartsWith("native live DASH"),"Enabled WPF analysis/installation status still required the external engine.");return;}
        if(mode=="default-snapshot")snapshot.Formats.NativeLiveDash=false;using var canceled=new CancellationTokenSource();if(mode=="cancel")canceled.Cancel();
        try{await engine.DownloadAsync("https://ladder.example/watch",new(){ExtraArgs="\""},snapshot,null,new Progress<ProgressUpdate>(),_=>{},canceled.Token,completion);Assert(mode=="snapshot","A refused/canceled/default job unexpectedly succeeded.");}
        catch(NativeEngineUnsupportedException)when(mode=="refusal"){}
        catch(OperationCanceledException)when(mode=="cancel"){}
        catch(Exception error)when(mode=="default-snapshot"&&error is InvalidOperationException or FormatException or FileNotFoundException){}
        Assert(native.Downloads==(mode is "snapshot" or "refusal"?1:0),"Backend routing did not honor the immutable job snapshot or cancellation.");if(mode=="snapshot")Assert(ReferenceEquals(snapshot,native.Snapshot)&&ReferenceEquals(completion,native.Completion)&&!service.Data.Formats.NativeLiveDash,"Native routing changed the job snapshot/callback or saved backend setting.");
    }
    private sealed class WpfNullFfmpeg:IFfmpegLocator{public Task<string?> LocateAsync(AppSettings settingsSnapshot,CancellationToken ct=default)=>Task.FromResult<string?>(null);}
    private static async Task WpfLadderControls()
    {
        if(Application.Current is null){var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};foreach(var name in new[]{"Colors","Styles"})app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/YoutubeDlModern;component/Themes/"+name+".xaml")});}
        var service=new SettingsService(Path.Combine(TestRoot,"wpf-controls"));using var history=new HistoryService(Path.Combine(TestRoot,"wpf-controls"));var queue=new DownloadQueueService(new WpfRouteEngine(),new WpfNullFfmpeg(),service,history);
        try
        {
            var model=new SettingsViewModel(service,queue);var view=new YoutubeDlModern.Views.SettingsView{DataContext=model,Width=1000,Height=900,Background=new SolidColorBrush(Color.FromRgb(24,24,28))};var tabs=((Grid)view.Content).Children.OfType<TabControl>().Single();tabs.SelectedItem=tabs.Items.OfType<TabItem>().Single(item=>item.Header.ToString()!.StartsWith("Formats",StringComparison.Ordinal));view.Measure(new Size(1000,900));view.Arrange(new Rect(0,0,1000,900));view.UpdateLayout();await Dispatcher.Yield(DispatcherPriority.DataBind);
            var toggle=(CheckBox)view.FindName("NativeLiveDashToggle");toggle.SetCurrentValue(CheckBox.IsCheckedProperty,true);toggle.GetBindingExpression(CheckBox.IsCheckedProperty)!.UpdateSource();var field=(TextBox)view.FindName("LiveAudioLanguagesField");field.SetCurrentValue(TextBox.TextProperty,"fr,en");field.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();var normalize=(CheckBox)view.FindName("LiveNormalizeTransitionsToggle");normalize.SetCurrentValue(CheckBox.IsCheckedProperty,true);normalize.GetBindingExpression(CheckBox.IsCheckedProperty)!.UpdateSource();model.S.Formats.LiveAdaptiveLadder=true;model.SaveCommand.Execute(null);await Dispatcher.Yield(DispatcherPriority.DataBind);
            var height=(ComboBox)view.FindName("LiveTransitionHeightChoice");height.SetCurrentValue(ComboBox.SelectedValueProperty,480);height.GetBindingExpression(ComboBox.SelectedValueProperty)!.UpdateSource();model.SaveCommand.Execute(null);await Dispatcher.Yield(DispatcherPriority.DataBind);
            Assert(service.Data.Formats.NativeLiveDash&&service.Data.Formats.LiveAudioLanguages=="fr,en"&&service.Data.Formats.LiveNormalizeTransitions&&service.Data.Formats.LiveAdaptiveLadder&&service.Data.Formats.LiveTransitionHeight==480&&height.SelectedItem is not null,"Compiled WPF controls did not update, validate and save their policy bindings.");
            var scroll=(ScrollViewer)((TabItem)tabs.SelectedItem).Content;scroll.ScrollToEnd();view.UpdateLayout();await Dispatcher.Yield(DispatcherPriority.Render);
            if(WpfLadderPreview is not null){var bitmap=new RenderTargetBitmap(1000,900,96,96,PixelFormats.Pbgra32);bitmap.Render(view);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(WpfLadderPreview);encoder.Save(output);}
        }
        finally{await queue.ShutdownAsync();}
    }
    private static async Task WpfLadderStatus()
    {
        var service=new SettingsService(Path.Combine(TestRoot,"wpf-status"));var settings=new AppSettings();settings.General.CheckEngineUpdatesOnStart=false;service.Save(settings);using var history=new HistoryService(Path.Combine(TestRoot,"wpf-status"));var engine=new YtDlpEngine(service,new WpfRouteEngine());var queue=new DownloadQueueService(engine,new WpfNullFfmpeg(),service,history);using var model=new MainViewModel(queue,service,history,engine,new FfmpegManager(service));
        try{var draft=service.CreateDraft();draft.Formats.NativeLiveDash=true;service.Save(draft);await Dispatcher.Yield(DispatcherPriority.Background);await model.RefreshEngineStatusAsync();Assert(model.EngineStatusText.StartsWith("native live DASH"),"The WPF engine status did not follow the saved native policy.");draft.Formats.NativeLiveDash=false;service.Save(draft);await Dispatcher.Yield(DispatcherPriority.Background);await model.RefreshEngineStatusAsync();Assert(!model.EngineStatusText.StartsWith("native"),"The WPF status retained a disabled native selection.");}
        finally{await queue.ShutdownAsync();}
    }
}
