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
    private static IEnumerable<(string Name,Func<Task> Test)> HdrReconstructAppChecks=>new[]{"mapping","roundtrip","snapshot","disabled","codec","missing-codec","bad-crop","dormant-crop"}.Select(mode=>("HDR reconstruction WPF "+mode,(Func<Task>)(()=>HdrReconstructSettings(mode))))
        .Concat(new[]{("HDR reconstruction WPF compiled choice",(Func<Task>)HdrReconstructControls)});
    private static async Task HdrReconstructSettings(string mode)
    {
        var service=new SettingsService(Path.Combine(TestRoot,"hdr-reconstruct-"+mode));var saved=ColorAudioSettingsValue();saved.Formats.LiveTransitionColorPolicy=mode=="dormant-crop"?"hdr-transform":"hdr-reconstruct";saved.Formats.LiveTransitionCrop=mode is "bad-crop" or "dormant-crop"?"bad":"8,4,8,4";if(mode=="codec")saved.Formats.LiveTransitionVideoCodec="vp9";if(mode=="missing-codec")saved.Formats.LiveTransitionVideoCodec="auto";
        if(mode is "codec" or "missing-codec" or "bad-crop"){try{service.Save(saved);throw new Exception("Invalid metadata-aware HDR codec persisted.");}catch(ArgumentException){}Assert(!service.Data.Formats.NativeLiveDash,"Rejected metadata settings changed saved state.");return;}
        service.Save(saved);service.Load();var snapshot=service.CreateSnapshot();Assert(snapshot.Formats.LiveTransitionColorPolicy==(mode=="dormant-crop"?"hdr-transform":"hdr-reconstruct"),"The metadata-aware choice did not persist.");Assert(snapshot.Formats.LiveTransitionCrop==(mode=="dormant-crop"?"bad":"8,4,8,4"),"Crop margins did not persist.");if(mode=="roundtrip")return;
        if(mode=="snapshot"){var draft=service.CreateDraft();draft.Formats.LiveTransitionColorPolicy="hdr-preserve";draft.Formats.LiveTransitionCrop="0,0,0,0";Assert(snapshot.Formats.LiveTransitionCrop=="8,4,8,4","A draft changed queued crop intent.");Assert(snapshot.Formats.LiveTransitionColorPolicy==(mode=="dormant-crop"?"hdr-transform":"hdr-reconstruct"),"A draft changed queued metadata intent.");return;}if(mode=="disabled")snapshot.Formats.LiveNormalizeTransitions=false;
        var requests=new List<NativeDownloadRequest>();var engine=new NativeEngine([new WpfPolicyProvider()],httpFactory:request=>{requests.Add(request);return new MediaHttp();});await new NativeEngineAdapter(engine).DownloadAsync("https://ladder.example/watch",new(){OutputDirectory=TestRoot,AddMetadata=false,EmbedChapters=false,ExtraArgs="--simulate"},snapshot,null,new Progress<ProgressUpdate>(),_=>{},default);var policy=requests.Single().LiveDashSelection!;Assert(policy.TransitionColorPolicy==(mode=="disabled"?null:mode=="dormant-crop"?"hdr-transform":"hdr-reconstruct")&&policy.HdrPeakNits==0&&policy.TransitionCrop==(mode is "disabled" or "dormant-crop"?null:"8,4,8,4"),"Native mapping changed metadata intent or retained an unused tone-map peak.");
    }
    private static async Task HdrReconstructControls()
    {
        if(Application.Current is null){var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};foreach(var name in new[]{"Colors","Styles"})app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/YoutubeDlModern;component/Themes/"+name+".xaml")});}
        var service=new SettingsService(Path.Combine(TestRoot,"hdr-reconstruct-controls"));service.Save(ColorAudioSettingsValue());using var history=new HistoryService(Path.Combine(TestRoot,"hdr-reconstruct-controls"));var queue=new DownloadQueueService(new WpfRouteEngine(),new WpfNullFfmpeg(),service,history);
        try
        {
            var model=new SettingsViewModel(service,queue);var view=new YoutubeDlModern.Views.SettingsView{DataContext=model,Width=1000,Height=1000,Background=new SolidColorBrush(Color.FromRgb(24,24,28))};var tabs=((Grid)view.Content).Children.OfType<TabControl>().Single();tabs.SelectedItem=tabs.Items.OfType<TabItem>().Single(item=>item.Header.ToString()!.StartsWith("Formats",StringComparison.Ordinal));view.Measure(new Size(1000,1000));view.Arrange(new Rect(0,0,1000,1000));view.UpdateLayout();await Dispatcher.Yield(DispatcherPriority.DataBind);var choice=(ComboBox)view.FindName("LiveColorPolicyChoice");choice.SetCurrentValue(ComboBox.SelectedValueProperty,"hdr-reconstruct");choice.GetBindingExpression(ComboBox.SelectedValueProperty)!.UpdateSource();Assert(choice.SelectedItem is not null,"The compiled metadata policy choice is missing.");var crop=(TextBox)view.FindName("LiveHdrCropField");crop.SetCurrentValue(TextBox.TextProperty,"8,4,8,4");crop.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();model.SaveCommand.Execute(null);await Dispatcher.Yield(DispatcherPriority.DataBind);Assert(service.Data.Formats.LiveTransitionColorPolicy=="hdr-reconstruct"&&service.Data.Formats.LiveTransitionCrop=="8,4,8,4","The compiled metadata choice did not save.");((ScrollViewer)((TabItem)tabs.SelectedItem).Content).ScrollToEnd();view.UpdateLayout();await Dispatcher.Yield(DispatcherPriority.Render);
            if(WpfLadderPreview is not null){var bitmap=new RenderTargetBitmap(1000,1000,96,96,PixelFormats.Pbgra32);bitmap.Render(view);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(WpfLadderPreview);encoder.Save(output);}
        }
        finally{await queue.ShutdownAsync();}
    }
}

