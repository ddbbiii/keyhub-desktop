using KeyHub.Core.Storage;

namespace KeyHub.Tests;

internal sealed class TestStore : IDisposable
{
    public TestStore()
    {
        DirectoryPath = Path.Combine(Path.GetTempPath(), "keyhub-tests", Guid.NewGuid().ToString("N"));
        Store = new KeyHubStore(new AppPaths(DirectoryPath));
    }

    public string DirectoryPath { get; }
    public KeyHubStore Store { get; }

    public void Dispose()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, true);
    }
}
