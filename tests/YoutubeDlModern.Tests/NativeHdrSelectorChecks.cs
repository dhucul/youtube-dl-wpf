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
    private static IEnumerable<(string Name,Func<Task> Test)> HdrSelectorAppChecks=>new[]{"mapping","roundtrip","snapshot","disabled","dormant","invalid","default"}.Select(mode=>("HDR selectors WPF "+mode,(Func<Task>)(()=>HdrSelectorSettings(mode))))
        .Concat(new[]{("HDR selectors WPF compiled choice",(Func<Task>)HdrSelectorControls)});
    private static async Task HdrSelectorSettings(string mode)
    {
        var service=new SettingsService(Path.Combine(TestRoot,"hdr-selector-"+mode));var saved=ColorAudioSettingsValue();saved.Formats.LiveTransitionColorPolicy=mode=="dormant"?"hdr-transform":"hdr-reconstruct";saved.Formats.LiveHdrSelectorPolicy=mode is "invalid" or "dormant"?"bad":mode=="default"?"strict":"ellipse-envelope";
        if(mode=="invalid"){try{service.Save(saved);throw new Exception("Unknown selector authoring persisted.");}catch(ArgumentException){}Assert(!service.Data.Formats.NativeLiveDash,"Invalid authoring changed saved state.");return;}
        service.Save(saved);service.Load();var snapshot=service.CreateSnapshot();Assert(snapshot.Formats.LiveHdrSelectorPolicy==saved.Formats.LiveHdrSelectorPolicy,"Selector authoring did not round trip.");if(mode=="roundtrip")return;if(mode=="snapshot"){service.CreateDraft().Formats.LiveHdrSelectorPolicy="strict";Assert(snapshot.Formats.LiveHdrSelectorPolicy=="ellipse-envelope","A draft changed queued authoring intent.");return;}if(mode=="disabled")snapshot.Formats.LiveNormalizeTransitions=false;
        var requests=new List<NativeDownloadRequest>();var engine=new NativeEngine([new WpfPolicyProvider()],httpFactory:request=>{requests.Add(request);return new MediaHttp();});await new NativeEngineAdapter(engine).DownloadAsync("https://ladder.example/watch",new(){OutputDirectory=TestRoot,AddMetadata=false,EmbedChapters=false,ExtraArgs="--simulate"},snapshot,null,new Progress<ProgressUpdate>(),_=>{},default);Assert(requests.Single().LiveDashSelection!.TransitionSelectorPolicy==(mode is "disabled" or "dormant" or "default"?null:"ellipse-envelope"),"Queued selector authoring lost active/dormant semantics.");
    }
    private static async Task HdrSelectorControls()
    {
        if(Application.Current is null){var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};foreach(var name in new[]{"Colors","Styles"})app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/YoutubeDlModern;component/Themes/"+name+".xaml")});}
        var service=new SettingsService(Path.Combine(TestRoot,"hdr-selector-controls"));service.Save(ColorAudioSettingsValue());using var history=new HistoryService(Path.Combine(TestRoot,"hdr-selector-controls"));var queue=new DownloadQueueService(new WpfRouteEngine(),new WpfNullFfmpeg(),service,history);
        try
        {
            var model=new SettingsViewModel(service,queue);var view=new YoutubeDlModern.Views.SettingsView{DataContext=model,Width=1000,Height=1000,Background=new SolidColorBrush(Color.FromRgb(24,24,28))};var tabs=((Grid)view.Content).Children.OfType<TabControl>().Single();tabs.SelectedItem=tabs.Items.OfType<TabItem>().Single(item=>item.Header.ToString()!.StartsWith("Formats",StringComparison.Ordinal));view.Measure(new Size(1000,1000));view.Arrange(new Rect(0,0,1000,1000));view.UpdateLayout();await Dispatcher.Yield(DispatcherPriority.DataBind);
            foreach(var pair in new[]{("LiveColorPolicyChoice","hdr-reconstruct"),("LiveHdrSelectorChoice","ellipse-envelope")}){var choice=(ComboBox)view.FindName(pair.Item1);choice.SetCurrentValue(ComboBox.SelectedValueProperty,pair.Item2);choice.GetBindingExpression(ComboBox.SelectedValueProperty)!.UpdateSource();Assert(choice.SelectedItem is not null,"Compiled authoring choice missing.");}model.SaveCommand.Execute(null);await Dispatcher.Yield(DispatcherPriority.DataBind);Assert(service.Data.Formats.LiveHdrSelectorPolicy=="ellipse-envelope","Compiled authoring choice did not save.");((ScrollViewer)((TabItem)tabs.SelectedItem).Content).ScrollToEnd();view.UpdateLayout();await Dispatcher.Yield(DispatcherPriority.Render);
            if(WpfLadderPreview is not null){var bitmap=new RenderTargetBitmap(1000,1000,96,96,PixelFormats.Pbgra32);bitmap.Render(view);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(WpfLadderPreview);encoder.Save(output);}
        }
        finally{await queue.ShutdownAsync();}
    }
}
