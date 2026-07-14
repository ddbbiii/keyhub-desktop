using System.IO;
using System.Windows;
using System.Windows.Controls;
using KeyHub.Core.Models;
using KeyHub.Core.Services;
using KeyHub.Core.Storage;
using KeyHub.Desktop.Services;
using KeyHub.Desktop.ViewModels;
using Microsoft.Win32;

namespace KeyHub.Desktop.Views;

public partial class MainWindow : Window
{
    private readonly KeyHubStore _store;
    private readonly EnvironmentFileService _environmentFiles = new();
    private readonly ProjectManifestService _manifests = new();
    private readonly ImportScanner _scanner = new();
    private readonly SafeClipboardService _clipboard;
    private readonly SshDeploymentService _ssh;
    private List<ImportCandidateRow> _importCandidates = [];

    public MainWindow(KeyHubStore store)
    {
        InitializeComponent();
        _store = store;
        _clipboard = new SafeClipboardService(store);
        _ssh = new SshDeploymentService(store, _environmentFiles);
        CurrentUserText.Text = $"{Environment.UserDomainName}\\{Environment.UserName}";
        ImportPathBox.Text = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        ShowPage("Dashboard");
        RefreshAll();
    }

    private IReadOnlyDictionary<string, Grid> Pages => new Dictionary<string, Grid>(StringComparer.OrdinalIgnoreCase)
    {
        ["Dashboard"] = DashboardPage,
        ["Secrets"] = SecretsPage,
        ["Projects"] = ProjectsPage,
        ["Servers"] = ServersPage,
        ["Import"] = ImportPage,
        ["Activity"] = ActivityPage,
        ["Settings"] = SettingsPage
    };

    private IReadOnlyDictionary<string, Button> Navigation => new Dictionary<string, Button>(StringComparer.OrdinalIgnoreCase)
    {
        ["Dashboard"] = DashboardNav,
        ["Secrets"] = SecretsNav,
        ["Projects"] = ProjectsNav,
        ["Servers"] = ServersNav,
        ["Import"] = ImportNav,
        ["Activity"] = ActivityNav,
        ["Settings"] = SettingsNav
    };

    private void ShowPage(string name)
    {
        foreach (var page in Pages) page.Value.Visibility = page.Key.Equals(name, StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
        foreach (var item in Navigation)
        {
            item.Value.Background = item.Key.Equals(name, StringComparison.OrdinalIgnoreCase)
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(24, 73, 69))
                : System.Windows.Media.Brushes.Transparent;
            item.Value.Foreground = item.Key.Equals(name, StringComparison.OrdinalIgnoreCase)
                ? System.Windows.Media.Brushes.White
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(205, 229, 225));
        }
        if (name is "Dashboard" or "Activity") RefreshAudit();
    }

    private void RefreshAll()
    {
        RefreshSecrets();
        RefreshProjects();
        RefreshServers();
        RefreshAudit();
        RefreshSettings();
        var secrets = _store.ListSecrets();
        SecretCountText.Text = secrets.Count.ToString();
        ProjectCountText.Text = _store.ListProjects().Count.ToString();
        ServerCountText.Text = _store.ListServers().Count.ToString();
        ExpiringCountText.Text = secrets.Count(x => x.ExpiresAt is { } expires && expires <= DateTimeOffset.Now.AddDays(30)).ToString();
    }

    private void RefreshSecrets()
    {
        SecretsGrid.ItemsSource = _store.ListSecrets(SecretSearchBox.Text).Select(x => new SecretRow(x)).ToList();
    }

    private void RefreshProjects(string? selectId = null)
    {
        selectId ??= (ProjectsGrid.SelectedItem as ProjectRow)?.Source.Id;
        var rows = _store.ListProjects().Select(x => new ProjectRow(x)).ToList();
        ProjectsGrid.ItemsSource = rows;
        if (selectId is not null) ProjectsGrid.SelectedItem = rows.FirstOrDefault(x => x.Source.Id == selectId);
        if (ProjectsGrid.SelectedItem is null && rows.Count > 0) ProjectsGrid.SelectedIndex = 0;
        RefreshProjectDetail();
    }

    private void RefreshProjectDetail()
    {
        if (ProjectsGrid.SelectedItem is not ProjectRow row)
        {
            ProjectDetailTitle.Text = "选择一个项目";
            ProjectDetailPath.Text = string.Empty;
            BindingsGrid.ItemsSource = null;
            return;
        }
        var project = _store.GetProject(row.Source.Id);
        ProjectDetailTitle.Text = project.DisplayName;
        ProjectDetailPath.Text = string.IsNullOrWhiteSpace(project.WorkingDirectory) ? project.Id : project.WorkingDirectory;
        BindingsGrid.ItemsSource = project.Bindings;
    }

    private void RefreshServers()
    {
        ServersGrid.ItemsSource = _store.ListServers().Select(x => new ServerRow(x)).ToList();
        DeploymentsGrid.ItemsSource = _store.ListDeployments().Select(x => new DeploymentRow(x)).ToList();
    }

    private void RefreshAudit()
    {
        var rows = _store.ListAuditEvents().Select(x => new AuditRow(x)).ToList();
        ActivityGrid.ItemsSource = rows;
        DashboardAuditGrid.ItemsSource = rows.Take(8).ToList();
    }

    private void RefreshSettings()
    {
        DatabasePathText.Text = _store.DatabasePath;
        ClipboardSecondsBox.Text = _store.GetSetting("clipboard_clear_seconds", "30");
    }

    private void OnNavigate(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string page }) ShowPage(page);
    }

    private void OnGoImport(object sender, RoutedEventArgs e) => ShowPage("Import");
    private void OnSecretSearchChanged(object sender, TextChangedEventArgs e) { if (IsLoaded) RefreshSecrets(); }

    private void OnAddSecret(object sender, RoutedEventArgs e)
    {
        var dialog = new SecretEditorWindow { Owner = this };
        if (dialog.ShowDialog() != true) return;
        ExecuteUi(() => _store.SaveSecret(null, dialog.SecretName, dialog.Kind, dialog.SecretValue, dialog.Notes, dialog.Tags, dialog.ExpiresAt));
        RefreshAll();
    }

    private void OnGenerateSshKey(object sender, RoutedEventArgs e)
    {
        var dialog = new SshKeyGeneratorWindow { Owner = this };
        if (dialog.ShowDialog() != true) return;
        ExecuteUi(() =>
        {
            var key = new SshKeyGenerationService().GenerateEd25519(dialog.Comment);
            _store.SaveSecret(null, dialog.KeyName, SecretKind.SshPrivateKey, key.PrivateKey,
                $"Public key:\n{key.PublicKey}", "ssh,generated");
            _clipboard.Copy(key.PublicKey);
            MessageBox.Show(this, "SSH 密钥已生成，公钥已复制到剪贴板。", "生成成功", MessageBoxButton.OK, MessageBoxImage.Information);
        });
        RefreshAll();
    }

    private void OnEditSecret(object sender, RoutedEventArgs e)
    {
        if (SecretsGrid.SelectedItem is not SecretRow row) return;
        ExecuteUi(() =>
        {
            var dialog = new SecretEditorWindow(_store.RevealSecret(row.Source.Id)) { Owner = this };
            if (dialog.ShowDialog() == true)
                _store.SaveSecret(row.Source.Id, dialog.SecretName, dialog.Kind, dialog.SecretValue, dialog.Notes, dialog.Tags, dialog.ExpiresAt);
        });
        RefreshAll();
    }

    private void OnCopySecret(object sender, RoutedEventArgs e)
    {
        if (SecretsGrid.SelectedItem is not SecretRow row) return;
        ExecuteUi(() =>
        {
            _clipboard.Copy(_store.RevealSecret(row.Source.Id).Value);
            _store.AddAudit("secret.copy", "secret", row.Source.Name, true, "密钥已复制到受控剪贴板");
            MessageBox.Show(this, "密钥已复制，稍后会按设置自动清除。", "复制成功", MessageBoxButton.OK, MessageBoxImage.Information);
        });
        RefreshAudit();
    }

    private void OnDeleteSecret(object sender, RoutedEventArgs e)
    {
        if (SecretsGrid.SelectedItem is not SecretRow row || MessageBox.Show(this, $"确定删除“{row.Source.Name}”？\n如果项目或服务器仍在引用它，删除会被拒绝。", "删除密钥", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        ExecuteUi(() => _store.DeleteSecret(row.Source.Id));
        RefreshAll();
    }

    private void OnAddProject(object sender, RoutedEventArgs e)
    {
        var dialog = new ProjectEditorWindow { Owner = this };
        if (dialog.ShowDialog() != true) return;
        ExecuteUi(() => _store.SaveProject(dialog.ProjectId, dialog.DisplayName, dialog.WorkingDirectory, dialog.DefaultCommand, dialog.ManifestPath));
        RefreshAll();
        ProjectsGrid.SelectedItem = ProjectsGrid.Items.Cast<ProjectRow>().FirstOrDefault(x => x.Source.Id == dialog.ProjectId);
    }

    private void OnEditProject(object sender, RoutedEventArgs e)
    {
        if (ProjectsGrid.SelectedItem is not ProjectRow row) return;
        var dialog = new ProjectEditorWindow(_store.GetProject(row.Source.Id)) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        ExecuteUi(() => _store.SaveProject(dialog.ProjectId, dialog.DisplayName, dialog.WorkingDirectory, dialog.DefaultCommand, dialog.ManifestPath));
        RefreshAll();
    }

    private void OnDeleteProject(object sender, RoutedEventArgs e)
    {
        if (ProjectsGrid.SelectedItem is not ProjectRow row || MessageBox.Show(this, $"确定删除项目“{row.Source.DisplayName}”及其变量映射？", "删除项目", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        ExecuteUi(() => _store.DeleteProject(row.Source.Id));
        RefreshAll();
    }

    private void OnProjectSelected(object sender, SelectionChangedEventArgs e) => RefreshProjectDetail();

    private void OnAddBinding(object sender, RoutedEventArgs e)
    {
        if (ProjectsGrid.SelectedItem is not ProjectRow row) return;
        var secrets = _store.ListSecrets().Select(x => new SecretChoice(x.Id, x.Name));
        var suggested = GetSuggestedVariables(row.Source).Except(row.Source.Bindings.Select(x => x.EnvironmentName), StringComparer.OrdinalIgnoreCase);
        var dialog = new BindingWindow(secrets, suggested) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.SecretId is null) return;
        ExecuteUi(() => _store.SetBinding(row.Source.Id, dialog.EnvironmentName, dialog.SecretId));
        RefreshProjects(row.Source.Id);
    }

    private void OnRemoveBinding(object sender, RoutedEventArgs e)
    {
        if (ProjectsGrid.SelectedItem is not ProjectRow project || BindingsGrid.SelectedItem is not EnvironmentBinding binding) return;
        ExecuteUi(() => _store.RemoveBinding(project.Source.Id, binding.EnvironmentName));
        RefreshProjects(project.Source.Id);
    }

    private void OnCopyRunCommand(object sender, RoutedEventArgs e)
    {
        if (ProjectsGrid.SelectedItem is not ProjectRow row) return;
        var command = string.IsNullOrWhiteSpace(row.Source.DefaultCommand) ? "<command>" : row.Source.DefaultCommand;
        _clipboard.Copy($"keyhub run --project {row.Source.Id} -- {command}");
        MessageBox.Show(this, "启动命令已复制。", "KeyHub", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnExportProject(object sender, RoutedEventArgs e)
    {
        if (ProjectsGrid.SelectedItem is not ProjectRow row) return;
        var values = _store.ResolveEnvironment(row.Source.Id);
        if (values.Count == 0) { MessageBox.Show(this, "项目没有变量映射。", "无法导出", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        var dialog = new SaveFileDialog { FileName = ".env", Filter = "环境文件|.env|JSON 文件|*.json", InitialDirectory = Directory.Exists(row.Source.WorkingDirectory) ? row.Source.WorkingDirectory : null };
        if (dialog.ShowDialog(this) != true) return;
        var names = string.Join("\n", values.Keys.OrderBy(x => x));
        if (MessageBox.Show(this, $"即将把以下变量以明文写入：\n\n{names}\n\n目标：{dialog.FileName}", "确认明文导出", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        var format = Path.GetExtension(dialog.FileName).Equals(".json", StringComparison.OrdinalIgnoreCase) ? DeploymentFormat.Json : DeploymentFormat.DotEnv;
        ExecuteUi(() =>
        {
            _environmentFiles.WriteAtomic(dialog.FileName, _environmentFiles.Serialize(values, format));
            _store.AddAudit("project.export", "project", row.Source.DisplayName, true, $"已导出 {values.Count} 个变量到 {dialog.FileName}");
        });
        RefreshAudit();
    }

    private IEnumerable<string> GetSuggestedVariables(ProjectProfile project)
    {
        if (!string.IsNullOrWhiteSpace(project.ManifestPath) && File.Exists(project.ManifestPath))
        {
            try { return _manifests.Load(project.ManifestPath!).RequiredEnvironment; }
            catch { }
        }
        return [];
    }

    private IEnumerable<SecretChoice> AuthenticationSecrets() => _store.ListSecrets()
        .Where(x => x.Kind is SecretKind.SshPrivateKey or SecretKind.UsernamePassword)
        .Select(x => new SecretChoice(x.Id, x.Name));

    private void OnAddServer(object sender, RoutedEventArgs e)
    {
        var dialog = new ServerEditorWindow(AuthenticationSecrets()) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        ExecuteUi(() => _store.SaveServer(dialog.BuildProfile()));
        RefreshAll();
    }

    private void OnEditServer(object sender, RoutedEventArgs e)
    {
        if (ServersGrid.SelectedItem is not ServerRow row) return;
        var dialog = new ServerEditorWindow(AuthenticationSecrets(), row.Source) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        ExecuteUi(() => _store.SaveServer(dialog.BuildProfile()));
        RefreshAll();
    }

    private void OnDeleteServer(object sender, RoutedEventArgs e)
    {
        if (ServersGrid.SelectedItem is not ServerRow row || MessageBox.Show(this, $"确定删除服务器“{row.Source.Name}”及关联部署配置？", "删除服务器", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        ExecuteUi(() => _store.DeleteServer(row.Source.Id));
        RefreshAll();
    }

    private async void OnTestServer(object sender, RoutedEventArgs e)
    {
        if (ServersGrid.SelectedItem is not ServerRow row) return;
        var result = await Task.Run(() => _ssh.TestConnection(row.Source));
        if (!result.Success) { MessageBox.Show(this, result.Message, "连接失败", MessageBoxButton.OK, MessageBoxImage.Error); return; }
        if (string.Equals(row.Source.HostFingerprint, result.Fingerprint, StringComparison.Ordinal))
        {
            MessageBox.Show(this, $"连接成功，主机指纹与记录一致。\n{result.Fingerprint}", "服务器可信", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show(this, $"连接成功。是否信任并保存以下主机指纹？\n\n{result.Fingerprint}\n\n如果服务器并非首次连接，请先核对指纹。", "确认主机指纹", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _store.SaveServer(row.Source with { HostFingerprint = result.Fingerprint, UpdatedAt = DateTimeOffset.UtcNow });
        RefreshAll();
    }

    private void OnAddDeployment(object sender, RoutedEventArgs e)
    {
        var servers = _store.ListServers();
        var projects = _store.ListProjects();
        if (servers.Count == 0 || projects.Count == 0) { MessageBox.Show(this, "请先添加服务器和项目。", "无法创建部署", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        var dialog = new DeploymentEditorWindow(servers, projects) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        ExecuteUi(() => _store.SaveDeployment(dialog.BuildProfile()));
        RefreshAll();
    }

    private void OnEditDeployment(object sender, RoutedEventArgs e)
    {
        if (DeploymentsGrid.SelectedItem is not DeploymentRow row) return;
        var dialog = new DeploymentEditorWindow(_store.ListServers(), _store.ListProjects(), row.Source) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        ExecuteUi(() => _store.SaveDeployment(dialog.BuildProfile()));
        RefreshAll();
    }

    private void OnDeleteDeployment(object sender, RoutedEventArgs e)
    {
        if (DeploymentsGrid.SelectedItem is not DeploymentRow row || MessageBox.Show(this, $"确定删除部署配置“{row.Source.Name}”？", "删除部署", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        ExecuteUi(() => _store.DeleteDeployment(row.Source.Id));
        RefreshAll();
    }

    private void OnCopyDeployment(object sender, RoutedEventArgs e)
    {
        if (DeploymentsGrid.SelectedItem is not DeploymentRow row) return;
        ExecuteUi(() =>
        {
            var values = _store.ResolveEnvironment(row.Source.ProjectId);
            _clipboard.Copy(_environmentFiles.Serialize(values, row.Source.Format));
            _store.AddAudit("deployment.copy", "deployment", row.Source.Name, true, $"已复制 {values.Count} 个明文变量");
            MessageBox.Show(this, "部署内容已复制，稍后会按设置自动清除。", "复制成功", MessageBoxButton.OK, MessageBoxImage.Information);
        });
        RefreshAudit();
    }

    private async void OnDeploy(object sender, RoutedEventArgs e)
    {
        if (DeploymentsGrid.SelectedItem is not DeploymentRow row) return;
        var names = _store.ListBindings(row.Source.ProjectId).Select(x => x.EnvironmentName).ToList();
        if (MessageBox.Show(this, $"通过 SSH 把以下变量写入 {row.Source.ServerName}：\n\n{string.Join("\n", names)}\n\n目标：{row.Source.TargetPath}", "确认部署", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        IsEnabled = false;
        try
        {
            var result = await Task.Run(() => _ssh.Deploy(row.Source.Id));
            MessageBox.Show(this, result.Message, result.Success ? "部署完成" : "部署失败", MessageBoxButton.OK, result.Success ? MessageBoxImage.Information : MessageBoxImage.Error);
        }
        finally
        {
            IsEnabled = true;
            RefreshAll();
        }
    }

    private void OnBrowseImport(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择要扫描的项目目录或 .ssh 目录" };
        if (dialog.ShowDialog(this) == true) ImportPathBox.Text = dialog.FolderName;
    }

    private void OnBrowseImportFile(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "选择 .env、PEM 或 SSH 私钥", Filter = "支持的密钥文件|.env;*.env;*.pem;*.key|所有文件|*.*" };
        if (dialog.ShowDialog(this) == true) ImportPathBox.Text = dialog.FileName;
    }

    private async void OnScanImport(object sender, RoutedEventArgs e)
    {
        var path = ImportPathBox.Text.Trim();
        ImportStatusText.Text = "正在扫描…";
        try
        {
            var candidates = await Task.Run(() => _scanner.Scan(path));
            _importCandidates = candidates.Select(x => new ImportCandidateRow(x)).ToList();
            ImportGrid.ItemsSource = _importCandidates;
            ImportStatusText.Text = $"发现 {_importCandidates.Count} 个候选项；值仅显示脱敏预览。";
        }
        catch (Exception ex)
        {
            ImportStatusText.Text = "扫描失败";
            MessageBox.Show(this, ex.Message, "扫描失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnImportSelected(object sender, RoutedEventArgs e)
    {
        ImportGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        ImportGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var selected = _importCandidates.Where(x => x.Selected).ToList();
        if (selected.Count == 0) return;
        var overwrite = selected.Count(x => _store.ListSecrets().Any(s => s.Name.Equals(x.Name, StringComparison.OrdinalIgnoreCase)));
        if (MessageBox.Show(this, $"将导入 {selected.Count} 个密钥，其中 {overwrite} 个同名密钥会被更新。\n原始文件不会被删除。", "确认导入", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        var imported = 0;
        foreach (var row in selected)
        {
            try
            {
                var existing = _store.ListSecrets().FirstOrDefault(x => x.Name.Equals(row.Name, StringComparison.OrdinalIgnoreCase));
                _store.SaveSecret(existing?.Id, row.Name, row.Source.Kind, row.Source.Value, $"从 {row.Source.SourcePath} 导入", "imported");
                imported++;
            }
            catch (Exception ex)
            {
                _store.AddAudit("import", "secret", row.Name, false, ex.Message);
            }
        }
        _store.AddAudit("import", "batch", Path.GetFileName(ImportPathBox.Text.Trim()), true, $"已导入 {imported}/{selected.Count} 个密钥");
        _importCandidates = [];
        ImportGrid.ItemsSource = null;
        ImportStatusText.Text = $"已导入 {imported} 个密钥。原始文件未修改。";
        RefreshAll();
    }

    private void OnSaveSettings(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(ClipboardSecondsBox.Text, out var seconds) || seconds is < 0 or > 3600)
        {
            MessageBox.Show(this, "剪贴板清除时间必须是 0 到 3600 秒。", "设置无效", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _store.SetSetting("clipboard_clear_seconds", seconds.ToString());
        MessageBox.Show(this, "设置已保存。", "KeyHub", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ExecuteUi(Action action)
    {
        try { action(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "操作失败", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
}
