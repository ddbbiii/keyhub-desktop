using System.Globalization;
using KeyHub.Core.Models;
using Microsoft.Data.Sqlite;

namespace KeyHub.Core.Storage;

public sealed class KeyHubStore
{
    private readonly AppPaths _paths;
    private readonly SecretProtector _protector;

    public KeyHubStore(AppPaths? paths = null, SecretProtector? protector = null)
    {
        _paths = paths ?? new AppPaths();
        _protector = protector ?? new SecretProtector();
        _paths.EnsureCreated();
        using var connection = Open();
        DatabaseInitializer.Initialize(connection);
    }

    public string DatabasePath => _paths.DatabasePath;

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _paths.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        }.ToString());
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    private static string Stamp(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset ParseStamp(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    public IReadOnlyList<SecretRecord> ListSecrets(string? search = null)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, name, kind, notes, tags, expires_at, created_at, updated_at
            FROM secrets
            WHERE $search = '' OR name LIKE $pattern OR tags LIKE $pattern OR notes LIKE $pattern
            ORDER BY name COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$search", search?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("$pattern", $"%{search?.Trim() ?? string.Empty}%");
        using var reader = command.ExecuteReader();
        var result = new List<SecretRecord>();
        while (reader.Read()) result.Add(ReadSecret(reader));
        return result;
    }

    public SecretValue RevealSecret(string id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, name, kind, encrypted_value, notes, tags, expires_at, created_at, updated_at FROM secrets WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) throw new KeyNotFoundException("密钥不存在。");
        var metadata = new SecretRecord(
            reader.GetString(0), reader.GetString(1), Enum.Parse<SecretKind>(reader.GetString(2)),
            reader.GetString(4), reader.GetString(5), reader.IsDBNull(6) ? null : ParseStamp(reader.GetString(6)),
            ParseStamp(reader.GetString(7)), ParseStamp(reader.GetString(8)));
        return new SecretValue(metadata, _protector.Unprotect(id, (byte[])reader[3]));
    }

    public SecretRecord SaveSecret(string? id, string name, SecretKind kind, string value, string notes = "", string tags = "", DateTimeOffset? expiresAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var secretId = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id;
        var now = DateTimeOffset.UtcNow;
        var existing = ListSecrets().FirstOrDefault(x => x.Id == secretId);
        var created = existing?.CreatedAt ?? now;
        var encrypted = _protector.Protect(secretId, value);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO secrets (id, name, kind, encrypted_value, notes, tags, expires_at, created_at, updated_at)
            VALUES ($id, $name, $kind, $value, $notes, $tags, $expires, $created, $updated)
            ON CONFLICT(id) DO UPDATE SET name=$name, kind=$kind, encrypted_value=$value,
                notes=$notes, tags=$tags, expires_at=$expires, updated_at=$updated;
            """;
        command.Parameters.AddWithValue("$id", secretId);
        command.Parameters.AddWithValue("$name", name.Trim());
        command.Parameters.AddWithValue("$kind", kind.ToString());
        command.Parameters.AddWithValue("$value", encrypted);
        command.Parameters.AddWithValue("$notes", notes.Trim());
        command.Parameters.AddWithValue("$tags", tags.Trim());
        command.Parameters.AddWithValue("$expires", expiresAt is null ? DBNull.Value : Stamp(expiresAt.Value));
        command.Parameters.AddWithValue("$created", Stamp(created));
        command.Parameters.AddWithValue("$updated", Stamp(now));
        command.ExecuteNonQuery();
        AddAudit("secret.save", "secret", name.Trim(), true, "密钥已保存");
        return new SecretRecord(secretId, name.Trim(), kind, notes.Trim(), tags.Trim(), expiresAt, created, now);
    }

    public void DeleteSecret(string id)
    {
        var secret = ListSecrets().FirstOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException("密钥不存在。");
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM secrets WHERE id=$id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
        AddAudit("secret.delete", "secret", secret.Name, true, "密钥已删除");
    }

    public IReadOnlyList<ProjectProfile> ListProjects()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, display_name, working_directory, default_command, manifest_path, updated_at FROM projects ORDER BY display_name COLLATE NOCASE;";
        using var reader = command.ExecuteReader();
        var rows = new List<(string Id, string Name, string Directory, string Command, string? Manifest, DateTimeOffset Updated)>();
        while (reader.Read()) rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), ParseStamp(reader.GetString(5))));
        return rows.Select(x => new ProjectProfile(x.Id, x.Name, x.Directory, x.Command, x.Manifest, x.Updated, ListBindings(x.Id))).ToList();
    }

    public ProjectProfile GetProject(string id) => ListProjects().FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? throw new KeyNotFoundException($"项目 {id} 不存在。");

    public void SaveProject(string id, string displayName, string workingDirectory, string defaultCommand, string? manifestPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO projects (id, display_name, working_directory, default_command, manifest_path, updated_at)
            VALUES ($id,$name,$dir,$command,$manifest,$updated)
            ON CONFLICT(id) DO UPDATE SET display_name=$name, working_directory=$dir,
                default_command=$command, manifest_path=$manifest, updated_at=$updated;
            """;
        command.Parameters.AddWithValue("$id", id.Trim());
        command.Parameters.AddWithValue("$name", displayName.Trim());
        command.Parameters.AddWithValue("$dir", workingDirectory.Trim());
        command.Parameters.AddWithValue("$command", defaultCommand.Trim());
        command.Parameters.AddWithValue("$manifest", string.IsNullOrWhiteSpace(manifestPath) ? DBNull.Value : manifestPath);
        command.Parameters.AddWithValue("$updated", Stamp(DateTimeOffset.UtcNow));
        command.ExecuteNonQuery();
        AddAudit("project.save", "project", displayName.Trim(), true, "项目配置已保存");
    }

    public void DeleteProject(string id)
    {
        var project = GetProject(id);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM projects WHERE id=$id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
        AddAudit("project.delete", "project", project.DisplayName, true, "项目配置已删除");
    }

    public IReadOnlyList<EnvironmentBinding> ListBindings(string projectId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT b.id,b.project_id,b.environment_name,b.secret_id,s.name
            FROM environment_bindings b JOIN secrets s ON s.id=b.secret_id
            WHERE b.project_id=$project ORDER BY b.environment_name COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$project", projectId);
        using var reader = command.ExecuteReader();
        var result = new List<EnvironmentBinding>();
        while (reader.Read()) result.Add(new EnvironmentBinding(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4)));
        return result;
    }

    public void SetBinding(string projectId, string environmentName, string secretId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO environment_bindings(project_id,environment_name,secret_id)
            VALUES($project,$name,$secret)
            ON CONFLICT(project_id,environment_name) DO UPDATE SET secret_id=$secret;
            """;
        command.Parameters.AddWithValue("$project", projectId);
        command.Parameters.AddWithValue("$name", environmentName.Trim());
        command.Parameters.AddWithValue("$secret", secretId);
        command.ExecuteNonQuery();
        AddAudit("binding.save", "project", projectId, true, $"已映射变量 {environmentName.Trim()}");
    }

    public void RemoveBinding(string projectId, string environmentName)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM environment_bindings WHERE project_id=$project AND environment_name=$name;";
        command.Parameters.AddWithValue("$project", projectId);
        command.Parameters.AddWithValue("$name", environmentName);
        command.ExecuteNonQuery();
    }

    public IReadOnlyDictionary<string, string> ResolveEnvironment(string projectId)
    {
        var bindings = ListBindings(projectId);
        return bindings.ToDictionary(x => x.EnvironmentName, x => RevealSecret(x.SecretId).Value, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ServerProfile> ListServers()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT r.id,r.name,r.host,r.port,r.username,r.operating_system,r.authentication_secret_id,
                   s.name,r.host_fingerprint,r.notes,r.updated_at
            FROM servers r LEFT JOIN secrets s ON s.id=r.authentication_secret_id
            ORDER BY r.name COLLATE NOCASE;
            """;
        using var reader = command.ExecuteReader();
        var result = new List<ServerProfile>();
        while (reader.Read()) result.Add(new ServerProfile(reader.GetString(0),reader.GetString(1),reader.GetString(2),reader.GetInt32(3),reader.GetString(4),Enum.Parse<ServerOperatingSystem>(reader.GetString(5)),reader.IsDBNull(6)?null:reader.GetString(6),reader.IsDBNull(7)?null:reader.GetString(7),reader.IsDBNull(8)?null:reader.GetString(8),reader.GetString(9),ParseStamp(reader.GetString(10))));
        return result;
    }

    public ServerProfile GetServer(string id) => ListServers().FirstOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException("服务器不存在。");

    public void SaveServer(ServerProfile profile)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO servers(id,name,host,port,username,operating_system,authentication_secret_id,host_fingerprint,notes,updated_at)
            VALUES($id,$name,$host,$port,$user,$os,$secret,$fingerprint,$notes,$updated)
            ON CONFLICT(id) DO UPDATE SET name=$name,host=$host,port=$port,username=$user,operating_system=$os,
              authentication_secret_id=$secret,host_fingerprint=$fingerprint,notes=$notes,updated_at=$updated;
            """;
        command.Parameters.AddWithValue("$id", profile.Id);
        command.Parameters.AddWithValue("$name", profile.Name.Trim());
        command.Parameters.AddWithValue("$host", profile.Host.Trim());
        command.Parameters.AddWithValue("$port", profile.Port);
        command.Parameters.AddWithValue("$user", profile.Username.Trim());
        command.Parameters.AddWithValue("$os", profile.OperatingSystem.ToString());
        command.Parameters.AddWithValue("$secret", string.IsNullOrWhiteSpace(profile.AuthenticationSecretId) ? DBNull.Value : profile.AuthenticationSecretId);
        command.Parameters.AddWithValue("$fingerprint", string.IsNullOrWhiteSpace(profile.HostFingerprint) ? DBNull.Value : profile.HostFingerprint);
        command.Parameters.AddWithValue("$notes", profile.Notes.Trim());
        command.Parameters.AddWithValue("$updated", Stamp(DateTimeOffset.UtcNow));
        command.ExecuteNonQuery();
        AddAudit("server.save", "server", profile.Name, true, "服务器配置已保存");
    }

    public void DeleteServer(string id)
    {
        var server = GetServer(id);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM servers WHERE id=$id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
        AddAudit("server.delete", "server", server.Name, true, "服务器配置已删除");
    }

    public IReadOnlyList<DeploymentProfile> ListDeployments()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT d.id,d.name,d.server_id,s.name,d.project_id,p.display_name,d.target_path,d.format,d.restart_command,d.updated_at
            FROM deployments d JOIN servers s ON s.id=d.server_id JOIN projects p ON p.id=d.project_id
            ORDER BY d.name COLLATE NOCASE;
            """;
        using var reader = command.ExecuteReader();
        var result = new List<DeploymentProfile>();
        while (reader.Read()) result.Add(new DeploymentProfile(reader.GetString(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),reader.GetString(4),reader.GetString(5),reader.GetString(6),Enum.Parse<DeploymentFormat>(reader.GetString(7)),reader.GetString(8),ParseStamp(reader.GetString(9))));
        return result;
    }

    public DeploymentProfile GetDeployment(string id) => ListDeployments().FirstOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException("部署配置不存在。");

    public void SaveDeployment(DeploymentProfile profile)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO deployments(id,name,server_id,project_id,target_path,format,restart_command,updated_at)
            VALUES($id,$name,$server,$project,$path,$format,$restart,$updated)
            ON CONFLICT(id) DO UPDATE SET name=$name,server_id=$server,project_id=$project,target_path=$path,
              format=$format,restart_command=$restart,updated_at=$updated;
            """;
        command.Parameters.AddWithValue("$id", profile.Id);
        command.Parameters.AddWithValue("$name", profile.Name.Trim());
        command.Parameters.AddWithValue("$server", profile.ServerId);
        command.Parameters.AddWithValue("$project", profile.ProjectId);
        command.Parameters.AddWithValue("$path", profile.TargetPath.Trim());
        command.Parameters.AddWithValue("$format", profile.Format.ToString());
        command.Parameters.AddWithValue("$restart", profile.RestartCommand.Trim());
        command.Parameters.AddWithValue("$updated", Stamp(DateTimeOffset.UtcNow));
        command.ExecuteNonQuery();
        AddAudit("deployment.save", "deployment", profile.Name, true, "部署配置已保存");
    }

    public void DeleteDeployment(string id)
    {
        var profile = GetDeployment(id);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM deployments WHERE id=$id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
        AddAudit("deployment.delete", "deployment", profile.Name, true, "部署配置已删除");
    }

    public IReadOnlyList<AuditEvent> ListAuditEvents(int limit = 200)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,timestamp,action,target_type,target_name,success,detail FROM audit_events ORDER BY id DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 2000));
        using var reader = command.ExecuteReader();
        var result = new List<AuditEvent>();
        while (reader.Read()) result.Add(new AuditEvent(reader.GetInt64(0),ParseStamp(reader.GetString(1)),reader.GetString(2),reader.GetString(3),reader.GetString(4),reader.GetInt32(5)==1,reader.GetString(6)));
        return result;
    }

    public void AddAudit(string action, string targetType, string targetName, bool success, string detail)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO audit_events(timestamp,action,target_type,target_name,success,detail) VALUES($timestamp,$action,$type,$name,$success,$detail);";
        command.Parameters.AddWithValue("$timestamp", Stamp(DateTimeOffset.UtcNow));
        command.Parameters.AddWithValue("$action", action);
        command.Parameters.AddWithValue("$type", targetType);
        command.Parameters.AddWithValue("$name", targetName);
        command.Parameters.AddWithValue("$success", success ? 1 : 0);
        command.Parameters.AddWithValue("$detail", detail);
        command.ExecuteNonQuery();
    }

    public string GetSetting(string key, string defaultValue = "")
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM settings WHERE key=$key;";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as string ?? defaultValue;
    }

    public void SetSetting(string key, string value)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO settings(key,value) VALUES($key,$value) ON CONFLICT(key) DO UPDATE SET value=$value;";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    private static SecretRecord ReadSecret(SqliteDataReader reader) => new(
        reader.GetString(0), reader.GetString(1), Enum.Parse<SecretKind>(reader.GetString(2)), reader.GetString(3), reader.GetString(4),
        reader.IsDBNull(5) ? null : ParseStamp(reader.GetString(5)), ParseStamp(reader.GetString(6)), ParseStamp(reader.GetString(7)));
}
