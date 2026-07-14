namespace KeyHub.Core.Storage;

public sealed class AppPaths
{
    public AppPaths(string? dataDirectory = null)
    {
        DataDirectory = dataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KeyHubDesktop");
        DatabasePath = Path.Combine(DataDirectory, "keyhub.db");
    }

    public string DataDirectory { get; }
    public string DatabasePath { get; }

    public void EnsureCreated() => Directory.CreateDirectory(DataDirectory);
}
