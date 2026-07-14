using System.Windows;
using KeyHub.Core.Models;

namespace KeyHub.Desktop.Views;

public partial class SecretEditorWindow : Window
{
    public SecretEditorWindow(SecretValue? existing = null)
    {
        InitializeComponent();
        KindBox.ItemsSource = Enum.GetValues<SecretKind>();
        KindBox.SelectedItem = existing?.Metadata.Kind ?? SecretKind.ApiKey;
        ExistingId = existing?.Metadata.Id;
        if (existing is null) return;
        HeadingText.Text = "查看 / 编辑密钥";
        NameBox.Text = existing.Metadata.Name;
        ValueBox.Text = existing.Value;
        TagsBox.Text = existing.Metadata.Tags;
        NotesBox.Text = existing.Metadata.Notes;
        ExpiryPicker.SelectedDate = existing.Metadata.ExpiresAt?.LocalDateTime;
    }

    public string? ExistingId { get; }
    public string SecretName => NameBox.Text.Trim();
    public SecretKind Kind => (SecretKind)(KindBox.SelectedItem ?? SecretKind.GenericText);
    public string SecretValue => ValueBox.Text;
    public string Tags => TagsBox.Text.Trim();
    public string Notes => NotesBox.Text.Trim();
    public DateTimeOffset? ExpiresAt => ExpiryPicker.SelectedDate is { } date ? new DateTimeOffset(date) : null;

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SecretName) || string.IsNullOrEmpty(SecretValue))
        {
            MessageBox.Show(this, "名称和密钥值不能为空。", "无法保存", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
