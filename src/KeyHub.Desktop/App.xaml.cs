using System.Windows;
using KeyHub.Core.Storage;
using KeyHub.Desktop.Views;

namespace KeyHub.Desktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var store = new KeyHubStore();
            new MainWindow(store).Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"KeyHub 启动失败：{ex.Message}", "KeyHub Desktop", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
