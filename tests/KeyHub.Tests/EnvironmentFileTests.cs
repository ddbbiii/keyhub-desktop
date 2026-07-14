using KeyHub.Core.Models;
using KeyHub.Core.Services;

namespace KeyHub.Tests;

public sealed class EnvironmentFileTests
{
    [Fact]
    public void DotEnvSerializationEscapesSpecialCharacters()
    {
        var service = new EnvironmentFileService();
        var result = service.SerializeDotEnv(new Dictionary<string, string>
        {
            ["B_TOKEN"] = "line1\nline2\"quoted\"\\tail",
            ["A_KEY"] = "simple"
        });

        Assert.Equal("A_KEY=\"simple\"\r\nB_TOKEN=\"line1\\nline2\\\"quoted\\\"\\\\tail\"\r\n", result.Replace("\n", "\r\n").Replace("\r\r\n", "\r\n"));
    }

    [Fact]
    public void AtomicWriteReplacesExistingFileWithoutLeavingTemporaryFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "keyhub-env-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var target = Path.Combine(directory, ".env");
            File.WriteAllText(target, "OLD=1");
            new EnvironmentFileService().WriteAtomic(target, "NEW=2");
            Assert.Equal("NEW=2", File.ReadAllText(target));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void JsonSerializationContainsOnlyMappedVariables()
    {
        var json = new EnvironmentFileService().Serialize(new Dictionary<string, string> { ["TOKEN"] = "abc" }, DeploymentFormat.Json);
        Assert.Contains("\"TOKEN\"", json);
        Assert.Contains("\"abc\"", json);
    }
}
