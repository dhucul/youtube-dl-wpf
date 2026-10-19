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
    private static IEnumerable<(string Name,Func<Task> Test)> HdrGamutAppChecks=>new[]{"hdr-preserve","wide-to-bt709","bt2020","roundtrip","snapshot","disabled","codec","missing-codec"}.Select(mode=>("HDR gamut WPF settings "+mode,(Func<Task>)(()=>HdrGamutSettings(mode))))
        .Concat(new[]{("HDR gamut WPF compiled color controls",(Func<Task>)HdrGamutControls)});
    private static async Task HdrGamutSettings(string mode)
    {
        var service=new SettingsService(Path.Combine(TestRoot,"hdr-gamut-"+mode));var saved=ColorAudioSettingsValue();saved.Formats.LiveTransitionAudioCodec="flac";saved.Formats.LiveTransitionColorPolicy=mode is "wide-to-bt709" or "bt2020"?mode:"hdr-preserve";if(mode=="codec")saved.Formats.LiveTransitionVideoCodec="av1";if(mode=="missing-codec")saved.Formats.LiveTransitionVideoCodec="auto";
        if(mode is "codec" or "missing-codec"){try{service.Save(saved);throw new Exception("Invalid ten-bit output codec persisted.");}catch(ArgumentException){}Assert(!service.Data.Formats.NativeLiveDash,"A rejected HDR policy changed saved settings.");return;}
        service.Save(saved);var snapshot=service.CreateSnapshot();service.Load();Assert(service.Data.Formats.LiveTransitionColorPolicy==saved.Formats.LiveTransitionColorPolicy,"HDR/wide-gamut setting did not persist.");
        if(mode=="roundtrip")return;if(mode=="snapshot"){var draft=service.CreateDraft();draft.Formats.LiveTransitionColorPolicy="bt2020";Assert(snapshot.Formats.LiveTransitionColorPolicy=="hdr-preserve","A changed color draft altered queued HDR intent.");return;}if(mode=="disabled")snapshot.Formats.LiveNormalizeTransitions=false;
        var requests=new List<NativeDownloadRequest>();var engine=new NativeEngine([new WpfPolicyProvider()],httpFactory:request=>{requests.Add(request);return new MediaHttp();});await new NativeEngineAdapter(engine).DownloadAsync("https://ladder.example/watch",new(){OutputDirectory=TestRoot,AddMetadata=false,EmbedChapters=false,ExtraArgs="--simulate"},snapshot,null,new Progress<ProgressUpdate>(),_=>{},default);
        var policy=requests.Single().LiveDashSelection!;Assert(policy.HdrPeakNits==0&&policy.TransitionColorPolicy==(mode=="disabled"?null:saved.Formats.LiveTransitionColorPolicy),"WPF HDR intent or unused tone-map peak changed native mapping.");
    }
    private static async Task HdrGamutControls()
    {
        if(Application.Current is null){var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};foreach(var name in new[]{"Colors","Styles"})app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/YoutubeDlModern;component/Themes/"+name+".xaml")});}
        var service=new SettingsService(Path.Combine(TestRoot,"hdr-gamut-controls"));var settings=ColorAudioSettingsValue();settings.Formats.LiveTransitionAudioCodec="flac";service.Save(settings);using var history=new HistoryService(Path.Combine(TestRoot,"hdr-gamut-controls"));var queue=new DownloadQueueService(new WpfRouteEngine(),new WpfNullFfmpeg(),service,history);
        try
        {
            var model=new SettingsViewModel(service,queue);var view=new YoutubeDlModern.Views.SettingsView{DataContext=model,Width=1000,Height=1000,Background=new SolidColorBrush(Color.FromRgb(24,24,28))};var tabs=((Grid)view.Content).Children.OfType<TabControl>().Single();tabs.SelectedItem=tabs.Items.OfType<TabItem>().Single(item=>item.Header.ToString()!.StartsWith("Formats",StringComparison.Ordinal));view.Measure(new Size(1000,1000));view.Arrange(new Rect(0,0,1000,1000));view.UpdateLayout();await Dispatcher.Yield(DispatcherPriority.DataBind);
            var color=(ComboBox)view.FindName("LiveColorPolicyChoice");foreach(var value in new[]{"wide-to-bt709","bt2020","hdr-preserve"}){color.SetCurrentValue(ComboBox.SelectedValueProperty,value);color.GetBindingExpression(ComboBox.SelectedValueProperty)!.UpdateSource();Assert(color.SelectedItem is not null,"A compiled HDR/gamut choice is unavailable.");model.SaveCommand.Execute(null);await Dispatcher.Yield(DispatcherPriority.DataBind);Assert(service.Data.Formats.LiveTransitionColorPolicy==value,"The compiled HDR/gamut choice did not save.");}
            ((ScrollViewer)((TabItem)tabs.SelectedItem).Content).ScrollToEnd();view.UpdateLayout();await Dispatcher.Yield(DispatcherPriority.Render);if(WpfLadderPreview is not null){var bitmap=new RenderTargetBitmap(1000,1000,96,96,PixelFormats.Pbgra32);bitmap.Render(view);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(WpfLadderPreview);encoder.Save(output);}
        }
        finally{await queue.ShutdownAsync();}
    }
}
