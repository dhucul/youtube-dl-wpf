using System.Windows;

namespace YoutubeDlModern.Views;

/// <summary>Simple viewer window for raw engine output (format lists, JSON).</summary>
public partial class JsonDialog : Window
{
    public JsonDialog(string title, string content)
    {
        InitializeComponent();
        Title = title;
        TitleBlock.Text = title;
        ContentBox.Text = content;
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(ContentBox.Text); } catch { /* clipboard busy */ }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
