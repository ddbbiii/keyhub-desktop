using KeyHub.Core.Models;
using KeyHub.Core.Services;
using KeyHub.Core.Storage;

namespace KeyHub.Cli;

internal static class Program
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        try
        {
            if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
            {
                PrintHelp();
                return 0;
            }

            var store = new KeyHubStore();
            return args[0].ToLowerInvariant() switch
            {
                "doctor" => Doctor(store),
                "run" => Run(store, args[1..]),
                "export" => Export(store, args[1..]),
                "deploy" => Deploy(store, args[1..]),
                _ => Unknown(args[0])
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"KeyHub 错误：{ex.Message}");
            return 1;
        }
    }

    private static int Doctor(KeyHubStore store)
    {
        Console.WriteLine("KeyHub Desktop 诊断");
        Console.WriteLine($"数据库：{store.DatabasePath}");
        Console.WriteLine($"密钥：{store.ListSecrets().Count}");
        Console.WriteLine($"项目：{store.ListProjects().Count}");
        Console.WriteLine($"服务器：{store.ListServers().Count}");
        Console.WriteLine($"部署配置：{store.ListDeployments().Count}");
        Console.WriteLine("DPAPI：当前 Windows 用户");
        return 0;
    }

    private static int Run(KeyHubStore store, string[] args)
    {
        var projectId = ReadOption(args, "--project");
        if (string.IsNullOrWhiteSpace(projectId)) throw new ArgumentException("请指定 --project <project-id>。");
        var separator = Array.IndexOf(args, "--");
        if (separator < 0 || separator == args.Length - 1) throw new ArgumentException("请在 -- 后提供要运行的命令。");
        return new ProjectRuntimeService(store).Run(projectId, args[(separator + 1)..]);
    }

    private static int Export(KeyHubStore store, string[] args)
    {
        var projectId = ReadOption(args, "--project");
        var output = ReadOption(args, "--output");
        var formatText = ReadOption(args, "--format") ?? "dotenv";
        if (string.IsNullOrWhiteSpace(projectId)) throw new ArgumentException("请指定 --project <project-id>。");
        if (string.IsNullOrWhiteSpace(output)) throw new ArgumentException("请指定 --output <path>。");
        var format = formatText.Equals("json", StringComparison.OrdinalIgnoreCase) ? DeploymentFormat.Json : DeploymentFormat.DotEnv;
        var values = store.ResolveEnvironment(projectId);
        if (values.Count == 0) throw new InvalidOperationException("项目没有已映射的环境变量。");
        Console.WriteLine($"即将把 {values.Count} 个变量以明文写入 {Path.GetFullPath(output)}：");
        foreach (var name in values.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)) Console.WriteLine($"  {name}");
        if (!args.Contains("--yes", StringComparer.OrdinalIgnoreCase) && !Confirm()) return 3;
        var service = new EnvironmentFileService();
        service.WriteAtomic(output, service.Serialize(values, format));
        store.AddAudit("project.export", "project", store.GetProject(projectId).DisplayName, true, $"已导出 {values.Count} 个变量到 {Path.GetFullPath(output)}");
        Console.WriteLine($"已导出 {values.Count} 个变量到 {Path.GetFullPath(output)}");
        return 0;
    }

    private static int Deploy(KeyHubStore store, string[] args)
    {
        if (args.Length is < 1 or > 2) throw new ArgumentException("用法：keyhub deploy <deployment-id> [--yes]");
        var profile = store.GetDeployment(args[0]);
        var names = store.ListBindings(profile.ProjectId).Select(x => x.EnvironmentName).ToList();
        Console.WriteLine($"即将通过 SSH 把 {names.Count} 个变量写入 {profile.ServerName}:{profile.TargetPath}：");
        foreach (var name in names) Console.WriteLine($"  {name}");
        if (!args.Contains("--yes", StringComparer.OrdinalIgnoreCase) && !Confirm()) return 3;
        var result = new SshDeploymentService(store, new EnvironmentFileService()).Deploy(profile.Id);
        if (result.Success)
        {
            Console.WriteLine(result.Message);
            return 0;
        }
        Console.Error.WriteLine(result.Message);
        return 2;
    }

    private static string? ReadOption(string[] args, string name)
    {
        for (var index = 0; index < args.Length - 1; index++)
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase)) return args[index + 1];
        return null;
    }

    private static bool Confirm()
    {
        Console.Write("输入 yes 继续：");
        return string.Equals(Console.ReadLine()?.Trim(), "yes", StringComparison.OrdinalIgnoreCase);
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"未知命令：{command}");
        PrintHelp();
        return 1;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            KeyHub Desktop CLI

            keyhub doctor
            keyhub run --project <project-id> -- <command> [args...]
            keyhub export --project <project-id> --format dotenv|json --output <path> [--yes]
            keyhub deploy <deployment-id> [--yes]
            """);
    }
}
