using System.Diagnostics;

namespace KeyHub.Core.Services;

public sealed record GeneratedSshKey(string PrivateKey, string PublicKey);

public sealed class SshKeyGenerationService
{
    public GeneratedSshKey GenerateEd25519(string comment)
    {
        var directory = Path.Combine(Path.GetTempPath(), "keyhub-keygen", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var privateKeyPath = Path.Combine(directory, "id_ed25519");
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "ssh-keygen.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("-q");
            startInfo.ArgumentList.Add("-t");
            startInfo.ArgumentList.Add("ed25519");
            startInfo.ArgumentList.Add("-f");
            startInfo.ArgumentList.Add(privateKeyPath);
            startInfo.ArgumentList.Add("-N");
            startInfo.ArgumentList.Add(string.Empty);
            startInfo.ArgumentList.Add("-C");
            startInfo.ArgumentList.Add(string.IsNullOrWhiteSpace(comment) ? $"keyhub@{Environment.MachineName}" : comment.Trim());
            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动 ssh-keygen.exe。");
            process.WaitForExit();
            if (process.ExitCode != 0) throw new InvalidOperationException(process.StandardError.ReadToEnd().Trim());
            return new GeneratedSshKey(File.ReadAllText(privateKeyPath).Trim(), File.ReadAllText(privateKeyPath + ".pub").Trim());
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
