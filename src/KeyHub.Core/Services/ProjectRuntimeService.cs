using System.Diagnostics;
using KeyHub.Core.Storage;

namespace KeyHub.Core.Services;

public sealed class ProjectRuntimeService(KeyHubStore store)
{
    public int Run(string projectId, IReadOnlyList<string> command)
    {
        if (command.Count == 0) throw new ArgumentException("缺少要运行的命令。", nameof(command));
        var project = store.GetProject(projectId);
        var environment = store.ResolveEnvironment(projectId);
        // MCP stdio clients start KeyHub with redirected streams. Forward those streams to the
        // child so `keyhub run` can safely wrap an MCP server without corrupting JSON-RPC.
        var proxyStandardStreams = Console.IsInputRedirected || Console.IsOutputRedirected || Console.IsErrorRedirected;
        var startInfo = new ProcessStartInfo
        {
            FileName = command[0],
            UseShellExecute = false,
            WorkingDirectory = string.IsNullOrWhiteSpace(project.WorkingDirectory) ? Environment.CurrentDirectory : project.WorkingDirectory,
            RedirectStandardInput = proxyStandardStreams,
            RedirectStandardOutput = proxyStandardStreams,
            RedirectStandardError = proxyStandardStreams
        };
        foreach (var argument in command.Skip(1)) startInfo.ArgumentList.Add(argument);
        foreach (var pair in environment) startInfo.Environment[pair.Key] = pair.Value;

        try
        {
            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动目标进程。");
            var outputPump = proxyStandardStreams
                ? process.StandardOutput.BaseStream.CopyToAsync(Console.OpenStandardOutput())
                : Task.CompletedTask;
            var errorPump = proxyStandardStreams
                ? process.StandardError.BaseStream.CopyToAsync(Console.OpenStandardError())
                : Task.CompletedTask;
            _ = proxyStandardStreams ? PumpInputAsync(process) : Task.CompletedTask;
            process.WaitForExit();
            Task.WaitAll(outputPump, errorPump);
            store.AddAudit("project.run", "project", project.DisplayName, true, $"进程退出码 {process.ExitCode}");
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            store.AddAudit("project.run", "project", project.DisplayName, false, Sanitize(ex.Message));
            throw;
        }
    }

    private static async Task PumpInputAsync(Process process)
    {
        try
        {
            await Console.OpenStandardInput().CopyToAsync(process.StandardInput.BaseStream);
        }
        catch (IOException)
        {
            // The child can exit before the MCP client closes its input stream.
        }
        catch (ObjectDisposedException)
        {
            // The child can exit before the MCP client closes its input stream.
        }
        finally
        {
            try { process.StandardInput.Close(); }
            catch (InvalidOperationException) { }
        }
    }

    private static string Sanitize(string message) => message.Length > 300 ? message[..300] : message;
}
