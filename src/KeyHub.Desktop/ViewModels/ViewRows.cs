using KeyHub.Core.Models;

namespace KeyHub.Desktop.ViewModels;

public sealed class SecretRow(SecretRecord source)
{
    public SecretRecord Source { get; } = source;
    public string Name => Source.Name;
    public string KindText => Source.Kind switch
    {
        SecretKind.ApiKey => "API Key",
        SecretKind.Token => "Token",
        SecretKind.UsernamePassword => "密码",
        SecretKind.SshPrivateKey => "SSH 私钥",
        SecretKind.Certificate => "证书",
        _ => "通用文本"
    };
    public string Tags => Source.Tags;
    public string ExpiryText => Source.ExpiresAt?.ToLocalTime().ToString("yyyy-MM-dd") ?? "—";
    public string UpdatedText => Source.UpdatedAt.ToLocalTime().ToString("MM-dd HH:mm");
}

public sealed class ProjectRow(ProjectProfile source)
{
    public ProjectProfile Source { get; } = source;
    public string DisplayName => Source.DisplayName;
    public int BindingCount => Source.Bindings.Count;
}

public sealed class ServerRow(ServerProfile source)
{
    public ServerProfile Source { get; } = source;
    public string Name => Source.Name;
    public string Address => $"{Source.Username}@{Source.Host}:{Source.Port}";
    public string OsText => Source.OperatingSystem == ServerOperatingSystem.Linux ? "Linux" : "Windows";
    public string AuthenticationName => Source.AuthenticationSecretName ?? "未配置";
    public string FingerprintStatus => string.IsNullOrWhiteSpace(Source.HostFingerprint) ? "待确认" : "已确认";
}

public sealed class DeploymentRow(DeploymentProfile source)
{
    public DeploymentProfile Source { get; } = source;
    public string Name => Source.Name;
    public string ServerName => Source.ServerName;
    public string ProjectName => Source.ProjectName;
    public string TargetPath => Source.TargetPath;
    public string FormatText => Source.Format == DeploymentFormat.DotEnv ? ".env" : "JSON";
}

public sealed class AuditRow(AuditEvent source)
{
    public AuditEvent Source { get; } = source;
    public string LocalTime => Source.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    public string ActionText => Source.Action switch
    {
        "secret.save" => "保存密钥",
        "secret.copy" => "复制密钥",
        "secret.delete" => "删除密钥",
        "project.save" => "保存项目",
        "project.delete" => "删除项目",
        "project.run" => "运行项目",
        "project.export" => "导出配置",
        "binding.save" => "保存映射",
        "server.save" => "保存服务器",
        "server.delete" => "删除服务器",
        "deployment.save" => "保存部署",
        "deployment.delete" => "删除部署",
        "deployment.run" => "执行部署",
        "deployment.copy" => "复制部署内容",
        "import" => "导入密钥",
        _ => Source.Action
    };
    public string TargetType => Source.TargetType;
    public string TargetName => Source.TargetName;
    public string ResultText => Source.Success ? "成功" : "失败";
    public string Detail => Source.Detail;
}

public sealed class ImportCandidateRow(ImportCandidate source)
{
    public ImportCandidate Source { get; } = source;
    public bool Selected { get; set; } = source.Selected;
    public string Name => Source.Name;
    public string KindText => new SecretRow(new SecretRecord("", "", Source.Kind, "", "", null, default, default)).KindText;
    public string SourcePath => Source.SourcePath;
    public string Preview => Source.Value.Length <= 4 ? "••••" : $"{Source.Value[..2]}••••{Source.Value[^2..]}";
}

public sealed record SecretChoice(string Id, string Name)
{
    public override string ToString() => Name;
}

public sealed record ServerChoice(string Id, string Name)
{
    public override string ToString() => Name;
}

public sealed record ProjectChoice(string Id, string Name)
{
    public override string ToString() => Name;
}
