using System.Windows.Controls;
using System.ComponentModel;
using YoutubeDlModern.ViewModels;

namespace YoutubeDlModern.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private SettingsViewModel? Vm => DataContext as SettingsViewModel;

    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        // PasswordBox does not support binding; sync manually.
        if (Vm is null) return;
        Vm.PropertyChanged -= OnViewModelPropertyChanged;
        Vm.PropertyChanged += OnViewModelPropertyChanged;
        SyncPasswords();
    }

    private void OnUnloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (Vm is not null)
            Vm.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.S))
            SyncPasswords();
    }

    private void SyncPasswords()
    {
        if (Vm is null) return;
        AuthPasswordBox.Password = Vm.S.Auth.Password;
        VideoPasswordBox.Password = Vm.S.Auth.VideoPassword;
        ApPasswordBox.Password = Vm.S.Auth.ApPassword;
    }

    private void AuthPasswordBox_PasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (Vm is not null) Vm.S.Auth.Password = AuthPasswordBox.Password;
    }

    private void VideoPasswordBox_PasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (Vm is not null) Vm.S.Auth.VideoPassword = VideoPasswordBox.Password;
    }

    private void ApPasswordBox_PasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (Vm is not null) Vm.S.Auth.ApPassword = ApPasswordBox.Password;
    }
}
