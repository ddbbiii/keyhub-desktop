using KeyHub.Core.Models;
using KeyHub.Core.Services;

namespace KeyHub.Tests;

public sealed class RuntimeAndSshTests
{
    [Fact]
    public void RunInjectsEnvironmentIntoChildProcess()
    {
        using var test = new TestStore();
        var secret = test.Store.SaveSecret(null, "Runtime", SecretKind.Token, "expected-value");
        test.Store.SaveProject("runtime", "Runtime", Environment.CurrentDirectory, "");
        test.Store.SetBinding("runtime", "KEYHUB_RUNTIME_TEST", secret.Id);

        var exitCode = new ProjectRuntimeService(test.Store).Run("runtime",
            ["cmd.exe", "/d", "/c", "if \"%KEYHUB_RUNTIME_TEST%\"==\"expected-value\" (exit 0) else (exit 7)"]);

        Assert.Equal(0, exitCode);
    }

    [Theory]
    [InlineData(ServerOperatingSystem.Linux, "systemctl restart trading-assistant", true)]
    [InlineData(ServerOperatingSystem.Linux, "rm -rf /", false)]
    [InlineData(ServerOperatingSystem.Windows, "Restart-Service -Name KeyHubDemo", true)]
    [InlineData(ServerOperatingSystem.Windows, "Invoke-Expression anything", false)]
    public void RestartCommandsAreRestricted(ServerOperatingSystem os, string command, bool expected) =>
        Assert.Equal(expected, SshDeploymentService.IsAllowedRestartCommand(os, command));

    [Fact]
    public void HostFingerprintUsesOpenSshSha256Shape()
    {
        var fingerprint = SshDeploymentService.Fingerprint([1, 2, 3, 4]);
        Assert.StartsWith("SHA256:", fingerprint);
        Assert.DoesNotContain('=', fingerprint);
    }
}
