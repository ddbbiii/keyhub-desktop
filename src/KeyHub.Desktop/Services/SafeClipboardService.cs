using System.Windows;
using System.Windows.Threading;
using KeyHub.Core.Storage;

namespace KeyHub.Desktop.Services;

public sealed class SafeClipboardService(KeyHubStore store)
{
    private DispatcherTimer? _timer;
    private string? _lastValue;

    public void Copy(string value)
    {
        var data = new DataObject();
        data.SetData(DataFormats.UnicodeText, value);
        data.SetData("ExcludeClipboardContentFromMonitorProcessing", new byte[] { 1 });
        data.SetData("CanIncludeInClipboardHistory", BitConverter.GetBytes(0));
        data.SetData("CanUploadToCloudClipboard", BitConverter.GetBytes(0));
        Clipboard.SetDataObject(data, true);
        _lastValue = value;

        _timer?.Stop();
        if (!int.TryParse(store.GetSetting("clipboard_clear_seconds", "30"), out var seconds) || seconds <= 0) return;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Clamp(seconds, 1, 3600)) };
        _timer.Tick += ClearIfUnchanged;
        _timer.Start();
    }

    private void ClearIfUnchanged(object? sender, EventArgs e)
    {
        _timer?.Stop();
        try
        {
            if (_lastValue is not null && Clipboard.ContainsText() && Clipboard.GetText() == _lastValue) Clipboard.Clear();
        }
        catch
        {
            // Another application can temporarily own the clipboard; never clear unrelated content.
        }
        finally
        {
            _lastValue = null;
        }
    }
}
