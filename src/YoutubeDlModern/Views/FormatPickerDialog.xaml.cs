using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using YoutubeDlModern.Models;

namespace YoutubeDlModern.Views;

public sealed class FormatChoice
{
    public FormatChoice(FormatInfo format)
    {
        FormatId = format.FormatId!.Trim();
        HasVideo = !string.IsNullOrWhiteSpace(format.Vcodec) && format.Vcodec != "none";
        HasAudio = !string.IsNullOrWhiteSpace(format.Acodec) && format.Acodec != "none";
        MediaType = HasVideo && HasAudio ? "Video + audio"
            : HasVideo ? "Video only"
            : HasAudio ? "Audio only"
            : "Other";
        Resolution = BuildResolution(format);
        Extension = string.IsNullOrWhiteSpace(format.Ext) ? "-" : format.Ext.ToUpperInvariant();
        Details = BuildDetails(format);
        SizeAndRate = BuildSizeAndRate(format);
        SortHeight = ParseHeight(Resolution);
    }

    public string FormatId { get; }
    public bool HasVideo { get; }
    public bool HasAudio { get; }
    public bool IsVideoOnly => HasVideo && !HasAudio;
    public bool IsAudioOnly => HasAudio && !HasVideo;
    public string MediaType { get; }
    public string Resolution { get; }
    public string Extension { get; }
    public string Details { get; }
    public string SizeAndRate { get; }
    private int SortHeight { get; }

    public static IReadOnlyList<FormatChoice> CreateChoices(
        IEnumerable<FormatInfo> formats,
        CancellationToken ct)
    {
        var choices = new List<FormatChoice>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var format in formats)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(format.FormatId))
                continue;
            var formatId = format.FormatId.Trim();
            if (seen.Add(formatId))
                choices.Add(new FormatChoice(format));
        }

        choices.Sort((left, right) =>
        {
            var comparison = right.HasVideo.CompareTo(left.HasVideo);
            if (comparison != 0) return comparison;
            comparison = right.SortHeight.CompareTo(left.SortHeight);
            if (comparison != 0) return comparison;
            comparison = right.HasAudio.CompareTo(left.HasAudio);
            return comparison != 0
                ? comparison
                : StringComparer.Ordinal.Compare(left.FormatId, right.FormatId);
        });
        ct.ThrowIfCancellationRequested();
        return choices;
    }

    private static string BuildResolution(FormatInfo format)
    {
        var resolution = format.Width is > 0 && format.Height is > 0
            ? $"{format.Width}x{format.Height}"
            : format.Height is > 0 ? $"{format.Height}p" : format.Resolution;
        if (string.IsNullOrWhiteSpace(resolution) || resolution == "audio only")
            resolution = "-";
        return format.Fps is > 0 ? $"{resolution} {format.Fps:0.#}fps" : resolution;
    }

    private static string BuildDetails(FormatInfo format)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(format.Vcodec) && format.Vcodec != "none")
            parts.Add(format.Vcodec);
        if (!string.IsNullOrWhiteSpace(format.Acodec) && format.Acodec != "none")
            parts.Add(format.Acodec);
        if (!string.IsNullOrWhiteSpace(format.FormatNote))
            parts.Add(format.FormatNote);
        return parts.Count == 0 ? "-" : string.Join(" / ", parts);
    }

    private static string BuildSizeAndRate(FormatInfo format)
    {
        var parts = new List<string>();
        var size = Formatting.FormatBytes(format.Filesize ?? format.FilesizeApprox);
        if (size.Length > 0)
            parts.Add(size);
        var rate = format.Tbr ?? format.Abr;
        if (rate is > 0)
            parts.Add($"{rate:0}k");
        return parts.Count == 0 ? "-" : string.Join(" / ", parts);
    }

    private static int ParseHeight(string resolution)
    {
        var value = resolution.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        var marker = value.LastIndexOf('x');
        if (marker >= 0)
            value = value[(marker + 1)..];
        value = value.TrimEnd('p');
        return int.TryParse(value, out var height) ? height : 0;
    }
}

public partial class FormatPickerDialog : Window
{
    private bool _includeBestAudio = true;
    private bool _updatingAudioOption;

    public FormatPickerDialog(string title, IReadOnlyList<FormatChoice> choices)
    {
        InitializeComponent();
        Title = title;
        TitleBlock.Text = title;

        FormatList.ItemsSource = choices;
        if (choices.Count > 0)
            FormatList.SelectedIndex = 0;
    }

    public string? SelectedSelector { get; private set; }
    public string SelectedDownloadMode { get; private set; } = "video-audio";

    private FormatChoice? CurrentChoice => FormatList.SelectedItem as FormatChoice;

    private void FormatList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var choice = CurrentChoice;
        UseButton.IsEnabled = choice is not null;
        CopyButton.IsEnabled = choice is not null;

        _updatingAudioOption = true;
        AddAudioCheckBox.Visibility = choice?.IsVideoOnly == true ? Visibility.Visible : Visibility.Collapsed;
        AddAudioCheckBox.IsChecked = choice?.IsVideoOnly == true && _includeBestAudio;
        _updatingAudioOption = false;
        UpdateSelectorPreview();
    }

    private void AddAudioCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingAudioOption)
            return;
        _includeBestAudio = AddAudioCheckBox.IsChecked == true;
        UpdateSelectorPreview();
    }

    private void UpdateSelectorPreview()
    {
        var selector = BuildSelector(CurrentChoice);
        SelectorPreview.Text = selector is null
            ? "Select a format to preview its download selector."
            : $"Download selector: {selector}";
    }

    private string? BuildSelector(FormatChoice? choice)
    {
        if (choice is null)
            return null;
        return choice.IsVideoOnly && _includeBestAudio
            ? $"{choice.FormatId}+ba/b"
            : choice.FormatId;
    }

    private void Use_Click(object sender, RoutedEventArgs e)
    {
        var choice = CurrentChoice;
        var selector = BuildSelector(choice);
        if (choice is null || selector is null)
            return;

        SelectedSelector = selector;
        SelectedDownloadMode = choice.IsAudioOnly ? "audio-only"
            : choice.IsVideoOnly && !_includeBestAudio ? "video-only"
            : "video-audio";
        DialogResult = true;
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        var selector = BuildSelector(CurrentChoice);
        if (selector is null)
            return;
        try { Clipboard.SetText(selector); } catch { /* clipboard busy */ }
    }

    private void FormatList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (CurrentChoice is not null)
            Use_Click(sender, e);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

}
