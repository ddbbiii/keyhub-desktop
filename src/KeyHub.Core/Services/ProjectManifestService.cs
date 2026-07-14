using System.Text.Json;
using System.Text.Json.Serialization;
using KeyHub.Core.Models;

namespace KeyHub.Core.Services;

public sealed class ProjectManifestService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string? Find(string startDirectory)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(startDirectory));
        while (directory is not null)
        {
            var path = Path.Combine(directory.FullName, ".keyhub.json");
            if (File.Exists(path)) return path;
            directory = directory.Parent;
        }
        return null;
    }

    public ProjectManifest Load(string path)
    {
        var manifest = JsonSerializer.Deserialize<ProjectManifest>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("项目清单为空。");
        if (manifest.SchemaVersion != 1) throw new InvalidDataException($"不支持清单版本 {manifest.SchemaVersion}。");
        if (string.IsNullOrWhiteSpace(manifest.ProjectId)) throw new InvalidDataException("project_id 不能为空。");
        return manifest with
        {
            DisplayName = string.IsNullOrWhiteSpace(manifest.DisplayName) ? manifest.ProjectId : manifest.DisplayName,
            RequiredEnvironment = manifest.RequiredEnvironment
                .Where(IsValidEnvironmentName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    public void SaveTemplate(string path, ProjectManifest manifest) =>
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, JsonOptions));

    public static bool IsValidEnvironmentName(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        (char.IsLetter(value[0]) || value[0] == '_') &&
        value.All(c => char.IsLetterOrDigit(c) || c == '_');
}
