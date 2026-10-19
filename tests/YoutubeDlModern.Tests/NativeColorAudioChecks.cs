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
    private static IEnumerable<(string Name,Func<Task> Test)> ColorAudioAppChecks=>new[]{"mapping","roundtrip","snapshot","disabled","peak","opus-rate","codec"}.Select(mode=>("Color audio WPF settings "+mode,(Func<Task>)(()=>ColorAudioSettings(mode))))
        .Concat(new[]{("Color audio WPF compiled profile controls",(Func<Task>)ColorAudioControls)});
    private static AppSettings ColorAudioSettingsValue()
    {
        var settings=new AppSettings{Formats=WpfPolicy()};var format=settings.Formats;format.LiveTransitionVideoCodec="hevc";format.LiveTransitionAudioCodec="opus";format.LiveTransitionAudioLayout="stereo";format.LiveTransitionAudioSampleRate=48000;format.LiveNormalizeGeometry=true;format.LiveTransitionColorPolicy="hdr-to-bt709";format.LiveHdrPeakNits=2000;return settings;
    }
    private static async Task ColorAudioSettings(string mode)
    {
        var service=new SettingsService(Path.Combine(TestRoot,"color-audio-"+mode));var saved=ColorAudioSettingsValue();if(mode=="peak")saved.Formats.LiveHdrPeakNits=0;if(mode=="opus-rate")saved.Formats.LiveTransitionAudioSampleRate=44100;if(mode=="codec")saved.Formats.LiveTransitionVideoCodec="unknown";
        if(mode is "peak" or "opus-rate" or "codec"){try{service.Save(saved);throw new InvalidOperationException("Invalid color/audio policy persisted.");}catch(ArgumentException){}Assert(!service.Data.Formats.NativeLiveDash,"A rejected conversion policy changed saved settings.");return;}
        service.Save(saved);var snapshot=service.CreateSnapshot();service.Load();Assert(service.Data.Formats.LiveTransitionAudioCodec=="opus"&&service.Data.Formats.LiveTransitionColorPolicy=="hdr-to-bt709"&&service.Data.Formats.LiveHdrPeakNits==2000,"Conversion choices did not persist through settings reload.");
        if(mode=="roundtrip")return;if(mode=="snapshot"){var draft=service.CreateDraft();draft.Formats.LiveHdrPeakNits=4000;draft.Formats.LiveTransitionAudioLayout="mono";Assert(snapshot.Formats.LiveHdrPeakNits==2000&&snapshot.Formats.LiveTransitionAudioLayout=="stereo","A changed draft altered a queued conversion policy.");return;}
        if(mode=="disabled")snapshot.Formats.LiveNormalizeTransitions=false;
        var requests=new List<NativeDownloadRequest>();var engine=new NativeEngine([new WpfPolicyProvider()],httpFactory:request=>{requests.Add(request);return new MediaHttp();});await new NativeEngineAdapter(engine).DownloadAsync("https://ladder.example/watch",new(){OutputDirectory=TestRoot,AddMetadata=false,EmbedChapters=false,ExtraArgs="--simulate"},snapshot,null,new Progress<ProgressUpdate>(),_=>{},default);
        var policy=requests.Single().LiveDashSelection!;if(mode=="disabled")Assert(policy.TransitionVideoCodec is null&&policy.TransitionAudioCodec is null&&policy.TransitionAudioLayout is null&&policy.TransitionAudioSampleRate==0&&!policy.NormalizeVideoGeometry&&policy.TransitionColorPolicy is null&&policy.HdrPeakNits==0,"Disabled conversion choices changed a default request.");else Assert(policy is{TransitionVideoCodec:"hevc",TransitionAudioCodec:"opus",TransitionAudioLayout:"stereo",TransitionAudioSampleRate:48000,NormalizeVideoGeometry:true,TransitionColorPolicy:"hdr-to-bt709",HdrPeakNits:2000},"Saved WPF choices did not map into the native conversion policy.");
    }
    private static async Task ColorAudioControls()
    {
        if(Application.Current is null){var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};foreach(var name in new[]{"Colors","Styles"})app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/YoutubeDlModern;component/Themes/"+name+".xaml")});}
        var service=new SettingsService(Path.Combine(TestRoot,"color-controls"));service.Save(ColorAudioSettingsValue());using var history=new HistoryService(Path.Combine(TestRoot,"color-controls"));var queue=new DownloadQueueService(new WpfRouteEngine(),new WpfNullFfmpeg(),service,history);
        try
        {
            var model=new SettingsViewModel(service,queue);var view=new YoutubeDlModern.Views.SettingsView{DataContext=model,Width=1000,Height=1000,Background=new SolidColorBrush(Color.FromRgb(24,24,28))};var tabs=((Grid)view.Content).Children.OfType<TabControl>().Single();tabs.SelectedItem=tabs.Items.OfType<TabItem>().Single(item=>item.Header.ToString()!.StartsWith("Formats",StringComparison.Ordinal));view.Measure(new Size(1000,1000));view.Arrange(new Rect(0,0,1000,1000));view.UpdateLayout();await Dispatcher.Yield(DispatcherPriority.DataBind);
            foreach(var (name,value) in new[]{("LiveVideoCodecChoice","av1"),("LiveAudioCodecChoice","flac"),("LiveAudioLayoutChoice","5.1"),("LiveColorPolicyChoice","hdr-to-bt709")}){var control=(ComboBox)view.FindName(name);control.SetCurrentValue(ComboBox.SelectedValueProperty,value);control.GetBindingExpression(ComboBox.SelectedValueProperty)!.UpdateSource();Assert(control.SelectedItem is not null,"A compiled conversion choice was unavailable: "+name);}
            var peak=(TextBox)view.FindName("LiveHdrPeakField");peak.SetCurrentValue(TextBox.TextProperty,"1000");peak.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();model.SaveCommand.Execute(null);await Dispatcher.Yield(DispatcherPriority.DataBind);Assert(service.Data.Formats.LiveTransitionVideoCodec=="av1"&&service.Data.Formats.LiveTransitionAudioCodec=="flac"&&service.Data.Formats.LiveTransitionAudioLayout=="5.1"&&service.Data.Formats.LiveHdrPeakNits==1000,"Compiled color/audio controls did not save their actual binding values.");
            var scroll=(ScrollViewer)((TabItem)tabs.SelectedItem).Content;scroll.ScrollToEnd();view.UpdateLayout();await Dispatcher.Yield(DispatcherPriority.Render);if(WpfLadderPreview is not null){var bitmap=new RenderTargetBitmap(1000,1000,96,96,PixelFormats.Pbgra32);bitmap.Render(view);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(WpfLadderPreview);encoder.Save(file);}
        }
        finally{await queue.ShutdownAsync();}
    }
}
