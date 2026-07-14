using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using KeyHub.Core.Models;
using KeyHub.Core.Storage;
using Renci.SshNet;

namespace KeyHub.Core.Services;

public sealed record SshConnectionTest(bool Success, string Fingerprint, string Message);

public sealed class SshDeploymentService(KeyHubStore store, EnvironmentFileService environmentFiles)
{
    public SshConnectionTest TestConnection(ServerProfile server)
    {
        try
        {
            using var material = CreateConnection(server, trustUnknownHost: true);
            material.Client.Connect();
            material.Client.Disconnect();
            var fingerprint = material.ObservedFingerprint ?? string.Empty;
            return new SshConnectionTest(true, fingerprint, $"已连接 {server.Host}:{server.Port}");
        }
        catch (Exception ex)
        {
            return new SshConnectionTest(false, string.Empty, ex.Message);
        }
    }

    public DeploymentResult Deploy(string deploymentId)
    {
        var deployment = store.GetDeployment(deploymentId);
        var server = store.GetServer(deployment.ServerId);
        if (string.IsNullOrWhiteSpace(server.HostFingerprint))
            return Fail(deployment, "请先测试连接并确认服务器主机指纹。");

        var values = store.ResolveEnvironment(deployment.ProjectId);
        if (values.Count == 0) return Fail(deployment, "项目没有已映射的环境变量。");
        if (!IsAllowedRestartCommand(server.OperatingSystem, deployment.RestartCommand))
            return Fail(deployment, "重启命令不在允许的预设范围内。");

        var content = environmentFiles.Serialize(values, deployment.Format);
        var temporaryPath = $"{deployment.TargetPath}.keyhub-{Guid.NewGuid():N}.tmp";
        try
        {
            using var sshMaterial = CreateConnection(server, trustUnknownHost: false);
            using var sftpMaterial = CreateSftpConnection(server);
            sshMaterial.Client.Connect();
            sftpMaterial.Client.Connect();
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(content)))
                sftpMaterial.Client.UploadFile(stream, NormalizeSftpPath(temporaryPath), true);

            var commandText = server.OperatingSystem == ServerOperatingSystem.Linux
                ? BuildLinuxCommitCommand(temporaryPath, deployment.TargetPath, deployment.RestartCommand)
                : BuildWindowsCommitCommand(temporaryPath, deployment.TargetPath, deployment.RestartCommand);
            using var command = sshMaterial.Client.CreateCommand(commandText);
            command.CommandTimeout = TimeSpan.FromSeconds(45);
            command.Execute();
            if (command.ExitStatus != 0)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(command.Error) ? "远程命令执行失败。" : command.Error.Trim());

            sftpMaterial.Client.Disconnect();
            sshMaterial.Client.Disconnect();
            store.AddAudit("deployment.run", "deployment", deployment.Name, true, $"已写入 {deployment.TargetPath}");
            return new DeploymentResult(true, $"已部署 {values.Count} 个变量到 {server.Name}");
        }
        catch (Exception ex)
        {
            return Fail(deployment, ex.Message);
        }
    }

    private DeploymentResult Fail(DeploymentProfile deployment, string message)
    {
        store.AddAudit("deployment.run", "deployment", deployment.Name, false, message.Length > 400 ? message[..400] : message);
        return new DeploymentResult(false, message);
    }

    private SshClientMaterial CreateConnection(ServerProfile server, bool trustUnknownHost)
    {
        var auth = CreateAuthentication(server);
        var info = new ConnectionInfo(server.Host, server.Port, server.Username, auth.Method);
        var client = new SshClient(info);
        var material = new SshClientMaterial(client, auth.Stream);
        client.HostKeyReceived += (_, e) =>
        {
            material.ObservedFingerprint = Fingerprint(e.HostKey);
            e.CanTrust = trustUnknownHost || string.Equals(material.ObservedFingerprint, server.HostFingerprint, StringComparison.Ordinal);
        };
        return material;
    }

    private SftpClientMaterial CreateSftpConnection(ServerProfile server)
    {
        var auth = CreateAuthentication(server);
        var info = new ConnectionInfo(server.Host, server.Port, server.Username, auth.Method);
        var client = new SftpClient(info);
        var material = new SftpClientMaterial(client, auth.Stream);
        client.HostKeyReceived += (_, e) => e.CanTrust = string.Equals(Fingerprint(e.HostKey), server.HostFingerprint, StringComparison.Ordinal);
        return material;
    }

    private AuthenticationMaterial CreateAuthentication(ServerProfile server)
    {
        if (string.IsNullOrWhiteSpace(server.AuthenticationSecretId)) throw new InvalidOperationException("服务器没有配置认证密钥。");
        var secret = store.RevealSecret(server.AuthenticationSecretId);
        if (secret.Metadata.Kind == SecretKind.SshPrivateKey)
        {
            MemoryStream stream;
            if (secret.Value.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                var path = new Uri(secret.Value).LocalPath;
                stream = new MemoryStream(File.ReadAllBytes(path), writable: false);
            }
            else
            {
                stream = new MemoryStream(Encoding.UTF8.GetBytes(secret.Value), writable: false);
            }
            var key = new PrivateKeyFile(stream);
            return new AuthenticationMaterial(new PrivateKeyAuthenticationMethod(server.Username, key), stream);
        }
        return new AuthenticationMaterial(new PasswordAuthenticationMethod(server.Username, secret.Value), null);
    }

    public static string Fingerprint(byte[] hostKey) => "SHA256:" + Convert.ToBase64String(SHA256.HashData(hostKey)).TrimEnd('=');

    public static bool IsAllowedRestartCommand(ServerOperatingSystem os, string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return true;
        return os == ServerOperatingSystem.Linux
            ? Regex.IsMatch(command, "^systemctl restart [A-Za-z0-9_.@-]+$")
            : Regex.IsMatch(command, "^Restart-Service -Name [A-Za-z0-9_.-]+$");
    }

    private static string BuildLinuxCommitCommand(string temporary, string target, string restart)
    {
        var tmp = ShellQuote(temporary);
        var dst = ShellQuote(target);
        var backup = ShellQuote(target + ".keyhub-backup");
        var restartPart = string.IsNullOrWhiteSpace(restart) ? "true" : restart;
        return $"set -e; had=0; if [ -f {dst} ]; then cp -- {dst} {backup}; had=1; fi; chmod 600 -- {tmp}; mv -f -- {tmp} {dst}; if {restartPart}; then rm -f -- {backup}; else if [ $had -eq 1 ]; then mv -f -- {backup} {dst}; fi; exit 1; fi";
    }

    private static string BuildWindowsCommitCommand(string temporary, string target, string restart)
    {
        var escapedTmp = temporary.Replace("'", "''", StringComparison.Ordinal);
        var escapedTarget = target.Replace("'", "''", StringComparison.Ordinal);
        var escapedRestart = restart.Replace("'", "''", StringComparison.Ordinal);
        var restartStatement = string.IsNullOrWhiteSpace(escapedRestart) ? "$null=$true" : escapedRestart;
        var script = $"$ErrorActionPreference='Stop';$tmp='{escapedTmp}';$dst='{escapedTarget}';$bak=$dst+'.keyhub-backup';$had=Test-Path -LiteralPath $dst;if($had){{Copy-Item -LiteralPath $dst -Destination $bak -Force}};try{{Move-Item -LiteralPath $tmp -Destination $dst -Force;{restartStatement};if(Test-Path -LiteralPath $bak){{Remove-Item -LiteralPath $bak -Force}}}}catch{{if($had -and (Test-Path -LiteralPath $bak)){{Move-Item -LiteralPath $bak -Destination $dst -Force}};throw}}";
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        return $"powershell -NoProfile -NonInteractive -EncodedCommand {encoded}";
    }

    private static string NormalizeSftpPath(string path) => path.Replace('\\', '/');
    private static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private sealed record AuthenticationMaterial(AuthenticationMethod Method, MemoryStream? Stream);

    private sealed class SshClientMaterial(SshClient client, MemoryStream? stream) : IDisposable
    {
        public SshClient Client { get; } = client;
        public MemoryStream? Stream { get; } = stream;
        public string? ObservedFingerprint { get; set; }
        public void Dispose() { Client.Dispose(); Stream?.Dispose(); }
    }

    private sealed class SftpClientMaterial(SftpClient client, MemoryStream? stream) : IDisposable
    {
        public SftpClient Client { get; } = client;
        public MemoryStream? Stream { get; } = stream;
        public void Dispose() { Client.Dispose(); Stream?.Dispose(); }
    }
}
