using System.Windows.Controls;
using System.Windows.Input;
using YoutubeDlModern.ViewModels;

namespace YoutubeDlModern.Views;

public partial class QueueView : UserControl
{
    public QueueView()
    {
        InitializeComponent();
    }

    private void UrlBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is QueueViewModel vm)
        {
            if (vm.FetchCommand.CanExecute(null))
                vm.FetchCommand.Execute(null);
            e.Handled = true;
        }
    }
}
