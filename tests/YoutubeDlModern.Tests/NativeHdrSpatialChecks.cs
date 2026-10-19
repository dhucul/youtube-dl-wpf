using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Http;
using YoutubeDlModern.Models;
using YoutubeDlModern.Services;
using YoutubeDlModern.ViewModels;

internal static partial class Program
{
    private static IEnumerable<(string Name,Func<Task> Test)> HdrSpatialAppChecks=>new[]{"mapping","roundtrip","snapshot","disabled","codec","missing-codec"}.Select(mode=>("HDR spatial WPF "+mode,(Func<Task>)(()=>HdrSpatialSettings(mode))))
        .Concat(new[]{("HDR spatial WPF compiled choice",(Func<Task>)HdrSpatialControls)});
    private static async Task HdrSpatialSettings(string mode)
    {
        var service=new SettingsService(Path.Combine(TestRoot,"hdr-spatial-"+mode));var saved=ColorAudioSettingsValue();saved.Formats.LiveTransitionColorPolicy="hdr-spatial";if(mode=="codec")saved.Formats.LiveTransitionVideoCodec="vp9";if(mode=="missing-codec")saved.Formats.LiveTransitionVideoCodec="auto";
        if(mode is "codec" or "missing-codec"){try{service.Save(saved);throw new Exception("Invalid metadata-aware HDR codec persisted.");}catch(ArgumentException){}Assert(!service.Data.Formats.NativeLiveDash,"Rejected metadata settings changed saved state.");return;}
        service.Save(saved);service.Load();var snapshot=service.CreateSnapshot();Assert(snapshot.Formats.LiveTransitionColorPolicy=="hdr-spatial","The metadata-aware choice did not persist.");if(mode=="roundtrip")return;
        if(mode=="snapshot"){var draft=service.CreateDraft();draft.Formats.LiveTransitionColorPolicy="hdr-preserve";Assert(snapshot.Formats.LiveTransitionColorPolicy=="hdr-spatial","A draft changed queued metadata intent.");return;}if(mode=="disabled")snapshot.Formats.LiveNormalizeTransitions=false;
        var requests=new List<NativeDownloadRequest>();var engine=new NativeEngine([new WpfPolicyProvider()],httpFactory:request=>{requests.Add(request);return new MediaHttp();});await new NativeEngineAdapter(engine).DownloadAsync("https://ladder.example/watch",new(){OutputDirectory=TestRoot,AddMetadata=false,EmbedChapters=false,ExtraArgs="--simulate"},snapshot,null,new Progress<ProgressUpdate>(),_=>{},default);var policy=requests.Single().LiveDashSelection!;Assert(policy.TransitionColorPolicy==(mode=="disabled"?null:"hdr-spatial")&&policy.HdrPeakNits==0,"Native mapping changed metadata intent or retained an unused tone-map peak.");
    }
    private static async Task HdrSpatialControls()
    {
        if(Application.Current is null){var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};foreach(var name in new[]{"Colors","Styles"})app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/YoutubeDlModern;component/Themes/"+name+".xaml")});}
        var service=new SettingsService(Path.Combine(TestRoot,"hdr-spatial-controls"));service.Save(ColorAudioSettingsValue());using var history=new HistoryService(Path.Combine(TestRoot,"hdr-spatial-controls"));var queue=new DownloadQueueService(new WpfRouteEngine(),new WpfNullFfmpeg(),service,history);
        try
        {
            var model=new SettingsViewModel(service,queue);var view=new YoutubeDlModern.Views.SettingsView{DataContext=model,Width=1000,Height=1000,Background=new SolidColorBrush(Color.FromRgb(24,24,28))};var tabs=((Grid)view.Content).Children.OfType<TabControl>().Single();tabs.SelectedItem=tabs.Items.OfType<TabItem>().Single(item=>item.Header.ToString()!.StartsWith("Formats",StringComparison.Ordinal));view.Measure(new Size(1000,1000));view.Arrange(new Rect(0,0,1000,1000));view.UpdateLayout();await Dispatcher.Yield(DispatcherPriority.DataBind);var choice=(ComboBox)view.FindName("LiveColorPolicyChoice");choice.SetCurrentValue(ComboBox.SelectedValueProperty,"hdr-spatial");choice.GetBindingExpression(ComboBox.SelectedValueProperty)!.UpdateSource();Assert(choice.SelectedItem is not null,"The compiled metadata policy choice is missing.");model.SaveCommand.Execute(null);await Dispatcher.Yield(DispatcherPriority.DataBind);Assert(service.Data.Formats.LiveTransitionColorPolicy=="hdr-spatial","The compiled metadata choice did not save.");((ScrollViewer)((TabItem)tabs.SelectedItem).Content).ScrollToEnd();view.UpdateLayout();await Dispatcher.Yield(DispatcherPriority.Render);
            if(WpfLadderPreview is not null){var bitmap=new RenderTargetBitmap(1000,1000,96,96,PixelFormats.Pbgra32);bitmap.Render(view);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(WpfLadderPreview);encoder.Save(output);}
        }
        finally{await queue.ShutdownAsync();}
    }
}

