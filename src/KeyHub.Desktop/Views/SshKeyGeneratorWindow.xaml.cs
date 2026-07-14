using System.Windows;

namespace KeyHub.Desktop.Views;

public partial class SshKeyGeneratorWindow : Window
{
    public SshKeyGeneratorWindow()
    {
        InitializeComponent();
        NameBox.Text = $"SSH {Environment.MachineName}";
        CommentBox.Text = $"keyhub@{Environment.MachineName}";
    }

    public string KeyName => NameBox.Text.Trim();
    public string Comment => CommentBox.Text.Trim();

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(KeyName))
        {
            MessageBox.Show(this, "密钥名称不能为空。", "无法生成", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
