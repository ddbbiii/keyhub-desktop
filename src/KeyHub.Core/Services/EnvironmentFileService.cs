using System.Text;
using System.Text.Json;
using KeyHub.Core.Models;

namespace KeyHub.Core.Services;

public sealed class EnvironmentFileService
{
    public string Serialize(IReadOnlyDictionary<string, string> values, DeploymentFormat format) => format switch
    {
        DeploymentFormat.DotEnv => SerializeDotEnv(values),
        DeploymentFormat.Json => JsonSerializer.Serialize(values, new JsonSerializerOptions { WriteIndented = true }),
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };

    public string SerializeDotEnv(IReadOnlyDictionary<string, string> values)
    {
        var builder = new StringBuilder();
        foreach (var pair in values.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!ProjectManifestService.IsValidEnvironmentName(pair.Key))
                throw new InvalidDataException($"环境变量名无效：{pair.Key}");
            builder.Append(pair.Key).Append('=').AppendLine(Quote(pair.Value));
        }
        return builder.ToString();
    }

    public void WriteAtomic(string targetPath, string content)
    {
        var fullPath = Path.GetFullPath(targetPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporary = $"{fullPath}.keyhub-{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporary, content, new UTF8Encoding(false));
            File.Move(temporary, fullPath, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static string Quote(string value) => "\"" + value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal)
        .Replace("\r", "\\r", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal) + "\"";
}
