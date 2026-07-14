using System.Windows;
using KeyHub.Core.Models;
using KeyHub.Core.Services;
using KeyHub.Desktop.ViewModels;

namespace KeyHub.Desktop.Views;

public partial class DeploymentEditorWindow : Window
{
    private readonly DeploymentProfile? _existing;
    private readonly IReadOnlyDictionary<string, ServerProfile> _servers;

    public DeploymentEditorWindow(IReadOnlyList<ServerProfile> servers, IReadOnlyList<ProjectProfile> projects, DeploymentProfile? existing = null)
    {
        InitializeComponent();
        _existing = existing;
        _servers = servers.ToDictionary(x => x.Id);
        ServerBox.ItemsSource = servers.Select(x => new ServerChoice(x.Id, x.Name)).ToList();
        ProjectBox.ItemsSource = projects.Select(x => new ProjectChoice(x.Id, x.DisplayName)).ToList();
        FormatBox.ItemsSource = Enum.GetValues<DeploymentFormat>();
        FormatBox.SelectedItem = existing?.Format ?? DeploymentFormat.DotEnv;
        if (existing is null)
        {
            ServerBox.SelectedIndex = ServerBox.Items.Count > 0 ? 0 : -1;
            ProjectBox.SelectedIndex = ProjectBox.Items.Count > 0 ? 0 : -1;
            return;
        }
        HeadingText.Text = "编辑部署配置";
        NameBox.Text = existing.Name;
        TargetPathBox.Text = existing.TargetPath;
        RestartBox.Text = existing.RestartCommand;
        ServerBox.SelectedItem = ServerBox.Items.Cast<ServerChoice>().FirstOrDefault(x => x.Id == existing.ServerId);
        ProjectBox.SelectedItem = ProjectBox.Items.Cast<ProjectChoice>().FirstOrDefault(x => x.Id == existing.ProjectId);
    }

    public DeploymentProfile BuildProfile()
    {
        var server = (ServerChoice)ServerBox.SelectedItem;
        var project = (ProjectChoice)ProjectBox.SelectedItem;
        return new DeploymentProfile(_existing?.Id ?? Guid.NewGuid().ToString("N"), NameBox.Text.Trim(), server.Id, server.Name,
            project.Id, project.Name, TargetPathBox.Text.Trim(), (DeploymentFormat)(FormatBox.SelectedItem ?? DeploymentFormat.DotEnv),
            RestartBox.Text.Trim(), DateTimeOffset.UtcNow);
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text) || string.IsNullOrWhiteSpace(TargetPathBox.Text) || ServerBox.SelectedItem is not ServerChoice server || ProjectBox.SelectedItem is null)
        {
            MessageBox.Show(this, "请填写名称、服务器、项目和目标文件。", "无法保存", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!SshDeploymentService.IsAllowedRestartCommand(_servers[server.Id].OperatingSystem, RestartBox.Text.Trim()))
        {
            MessageBox.Show(this, "重启命令必须符合界面中的预设格式，或保持为空。", "命令不允许", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
