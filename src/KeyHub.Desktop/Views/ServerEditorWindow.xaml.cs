using System.Windows;
using KeyHub.Core.Models;
using KeyHub.Desktop.ViewModels;

namespace KeyHub.Desktop.Views;

public partial class ServerEditorWindow : Window
{
    private readonly ServerProfile? _existing;

    public ServerEditorWindow(IEnumerable<SecretChoice> authenticationSecrets, ServerProfile? existing = null)
    {
        InitializeComponent();
        _existing = existing;
        OsBox.ItemsSource = Enum.GetValues<ServerOperatingSystem>();
        AuthenticationBox.ItemsSource = authenticationSecrets.ToList();
        OsBox.SelectedItem = existing?.OperatingSystem ?? ServerOperatingSystem.Linux;
        if (existing is null)
        {
            AuthenticationBox.SelectedIndex = AuthenticationBox.Items.Count > 0 ? 0 : -1;
            return;
        }
        HeadingText.Text = "编辑服务器";
        NameBox.Text = existing.Name;
        HostBox.Text = existing.Host;
        PortBox.Text = existing.Port.ToString();
        UsernameBox.Text = existing.Username;
        NotesBox.Text = existing.Notes;
        AuthenticationBox.SelectedItem = AuthenticationBox.Items.Cast<SecretChoice>().FirstOrDefault(x => x.Id == existing.AuthenticationSecretId);
    }

    public ServerProfile BuildProfile()
    {
        var secret = AuthenticationBox.SelectedItem as SecretChoice;
        var host = HostBox.Text.Trim();
        var port = int.Parse(PortBox.Text);
        var fingerprint = _existing is not null &&
                          string.Equals(_existing.Host, host, StringComparison.OrdinalIgnoreCase) &&
                          _existing.Port == port
            ? _existing.HostFingerprint
            : null;
        return new ServerProfile(
            _existing?.Id ?? Guid.NewGuid().ToString("N"), NameBox.Text.Trim(), host, port,
            UsernameBox.Text.Trim(), (ServerOperatingSystem)(OsBox.SelectedItem ?? ServerOperatingSystem.Linux),
            secret?.Id, secret?.Name, fingerprint, NotesBox.Text.Trim(), DateTimeOffset.UtcNow);
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text) || string.IsNullOrWhiteSpace(HostBox.Text) || string.IsNullOrWhiteSpace(UsernameBox.Text) ||
            !int.TryParse(PortBox.Text, out var port) || port is < 1 or > 65535 || AuthenticationBox.SelectedItem is null)
        {
            MessageBox.Show(this, "请填写名称、主机、有效端口、用户名和认证密钥。", "无法保存", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
