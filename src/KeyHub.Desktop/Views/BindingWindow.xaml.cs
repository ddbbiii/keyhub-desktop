using System.Windows;
using KeyHub.Core.Services;
using KeyHub.Desktop.ViewModels;

namespace KeyHub.Desktop.Views;

public partial class BindingWindow : Window
{
    public BindingWindow(IEnumerable<SecretChoice> secrets, IEnumerable<string> suggestedVariables)
    {
        InitializeComponent();
        SecretBox.ItemsSource = secrets.ToList();
        EnvironmentBox.ItemsSource = suggestedVariables.ToList();
        SecretBox.SelectedIndex = SecretBox.Items.Count > 0 ? 0 : -1;
    }

    public string EnvironmentName => EnvironmentBox.Text.Trim();
    public string? SecretId => (SecretBox.SelectedItem as SecretChoice)?.Id;

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (!ProjectManifestService.IsValidEnvironmentName(EnvironmentName) || SecretId is null)
        {
            MessageBox.Show(this, "请选择密钥并输入有效的环境变量名。", "无法保存", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
