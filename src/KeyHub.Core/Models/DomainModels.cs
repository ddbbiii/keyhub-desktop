namespace KeyHub.Core.Models;

public enum SecretKind
{
    ApiKey,
    Token,
    UsernamePassword,
    SshPrivateKey,
    Certificate,
    GenericText
}

public enum ServerOperatingSystem
{
    Linux,
    Windows
}

public enum DeploymentFormat
{
    DotEnv,
    Json
}

public sealed record SecretRecord(
    string Id,
    string Name,
    SecretKind Kind,
    string Notes,
    string Tags,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record SecretValue(SecretRecord Metadata, string Value);

public sealed record EnvironmentBinding(
    long Id,
    string ProjectId,
    string EnvironmentName,
    string SecretId,
    string SecretName);

public sealed record ProjectProfile(
    string Id,
    string DisplayName,
    string WorkingDirectory,
    string DefaultCommand,
    string? ManifestPath,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<EnvironmentBinding> Bindings);

public sealed record ServerProfile(
    string Id,
    string Name,
    string Host,
    int Port,
    string Username,
    ServerOperatingSystem OperatingSystem,
    string? AuthenticationSecretId,
    string? AuthenticationSecretName,
    string? HostFingerprint,
    string Notes,
    DateTimeOffset UpdatedAt);

public sealed record DeploymentProfile(
    string Id,
    string Name,
    string ServerId,
    string ServerName,
    string ProjectId,
    string ProjectName,
    string TargetPath,
    DeploymentFormat Format,
    string RestartCommand,
    DateTimeOffset UpdatedAt);

public sealed record AuditEvent(
    long Id,
    DateTimeOffset Timestamp,
    string Action,
    string TargetType,
    string TargetName,
    bool Success,
    string Detail);

public sealed record ImportCandidate(
    string SourcePath,
    string Name,
    SecretKind Kind,
    string Value,
    bool Selected,
    string Detail);

public sealed record ProjectManifest(
    int SchemaVersion,
    string ProjectId,
    string DisplayName,
    IReadOnlyList<string> RequiredEnvironment,
    string? DefaultCommand);

public sealed record DeploymentResult(bool Success, string Message);
