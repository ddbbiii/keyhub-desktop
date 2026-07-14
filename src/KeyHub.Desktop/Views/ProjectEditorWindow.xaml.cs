using System.IO;
using System.Windows;
using KeyHub.Core.Models;
using KeyHub.Core.Services;
using Microsoft.Win32;

namespace KeyHub.Desktop.Views;

public partial class ProjectEditorWindow : Window
{
    private readonly ProjectManifestService _manifests = new();

    public ProjectEditorWindow(ProjectProfile? existing = null)
    {
        InitializeComponent();
        if (existing is null) return;
        HeadingText.Text = "编辑项目";
        IdBox.Text = existing.Id;
        IdBox.IsEnabled = false;
        NameBox.Text = existing.DisplayName;
        DirectoryBox.Text = existing.WorkingDirectory;
        CommandBox.Text = existing.DefaultCommand;
        ManifestBox.Text = existing.ManifestPath;
    }

    public string ProjectId => IdBox.Text.Trim();
    public string DisplayName => NameBox.Text.Trim();
    public string WorkingDirectory => DirectoryBox.Text.Trim();
    public string DefaultCommand => CommandBox.Text.Trim();
    public string? ManifestPath => string.IsNullOrWhiteSpace(ManifestBox.Text) ? null : ManifestBox.Text.Trim();
    public IReadOnlyList<string> ManifestVariables { get; private set; } = [];

    private void OnBrowseDirectory(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择项目工作目录" };
        if (dialog.ShowDialog(this) == true) DirectoryBox.Text = dialog.FolderName;
    }

    private void OnBrowseManifest(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "KeyHub 项目清单|.keyhub.json|JSON 文件|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var manifest = _manifests.Load(dialog.FileName);
            ManifestBox.Text = dialog.FileName;
            IdBox.Text = manifest.ProjectId;
            NameBox.Text = manifest.DisplayName;
            CommandBox.Text = manifest.DefaultCommand ?? string.Empty;
            DirectoryBox.Text = Path.GetDirectoryName(dialog.FileName) ?? string.Empty;
            ManifestVariables = manifest.RequiredEnvironment;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "清单无效", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ProjectId) || string.IsNullOrWhiteSpace(DisplayName))
        {
            MessageBox.Show(this, "项目 ID 和显示名称不能为空。", "无法保存", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
