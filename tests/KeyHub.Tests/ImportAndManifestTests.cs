using KeyHub.Core.Models;
using KeyHub.Core.Services;

namespace KeyHub.Tests;

public sealed class ImportAndManifestTests
{
    [Fact]
    public void ScannerReadsDotEnvAndPrivateKeyWithoutScanningBuildDirectories()
    {
        var directory = Path.Combine(Path.GetTempPath(), "keyhub-import-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "bin"));
        Directory.CreateDirectory(Path.Combine(directory, ".ssh"));
        try
        {
            File.WriteAllText(Path.Combine(directory, ".env"), "API_KEY=hello\nPASSWORD='world'\nEMPTY=\n");
            var privateKeyFixture = "-----BEGIN OPENSSH " + "PRIVATE KEY-----\nabc\n-----END OPENSSH " + "PRIVATE KEY-----";
            File.WriteAllText(Path.Combine(directory, ".ssh", "id_test"), privateKeyFixture);
            File.WriteAllText(Path.Combine(directory, "bin", ".env"), "SHOULD_NOT_EXIST=x");

            var results = new ImportScanner().Scan(directory);

            Assert.Equal(3, results.Count);
            Assert.Contains(results, x => x.Name == "API_KEY" && x.Kind == SecretKind.ApiKey && x.Value == "hello");
            Assert.Contains(results, x => x.Name == "PASSWORD" && x.Kind == SecretKind.UsernamePassword && x.Value == "world");
            Assert.Contains(results, x => x.Kind == SecretKind.SshPrivateKey);
            Assert.DoesNotContain(results, x => x.Name == "SHOULD_NOT_EXIST");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void ManifestLoaderNormalizesAndValidatesVariables()
    {
        var path = Path.Combine(Path.GetTempPath(), $"keyhub-manifest-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """
                {"schema_version":1,"project_id":"demo","display_name":"Demo","required_environment":["B_KEY","A_KEY","B_KEY","bad-name"],"default_command":"demo.exe"}
                """);
            var manifest = new ProjectManifestService().Load(path);
            Assert.Equal(["A_KEY", "B_KEY"], manifest.RequiredEnvironment);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
