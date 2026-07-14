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
        var startInfo = new ProcessStartInfo
        {
            FileName = command[0],
            UseShellExecute = false,
            WorkingDirectory = string.IsNullOrWhiteSpace(project.WorkingDirectory) ? Environment.CurrentDirectory : project.WorkingDirectory
        };
        foreach (var argument in command.Skip(1)) startInfo.ArgumentList.Add(argument);
        foreach (var pair in environment) startInfo.Environment[pair.Key] = pair.Value;

        try
        {
            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动目标进程。");
            process.WaitForExit();
            store.AddAudit("project.run", "project", project.DisplayName, true, $"进程退出码 {process.ExitCode}");
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            store.AddAudit("project.run", "project", project.DisplayName, false, Sanitize(ex.Message));
            throw;
        }
    }

    private static string Sanitize(string message) => message.Length > 300 ? message[..300] : message;
}
