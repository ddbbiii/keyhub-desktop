using System.Text;
using KeyHub.Core.Models;

namespace KeyHub.Core.Services;

public sealed class ImportScanner
{
    private static readonly HashSet<string> IgnoredDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", "node_modules", "bin", "obj", "artifacts", ".next"
    };

    public IReadOnlyList<ImportCandidate> Scan(string path)
    {
        if (File.Exists(path)) return ScanFile(path).ToList();
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException(path);

        var candidates = new List<ImportCandidate>();
        foreach (var file in EnumerateFiles(path)) candidates.AddRange(ScanFile(file));
        return candidates
            .GroupBy(x => $"{x.Name}\0{x.Value}", StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<string> EnumerateFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            IEnumerable<string> directories;
            IEnumerable<string> files;
            try
            {
                directories = Directory.EnumerateDirectories(current);
                files = Directory.EnumerateFiles(current);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var directory in directories)
                if (!IgnoredDirectories.Contains(Path.GetFileName(directory)) &&
                    !File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint)) pending.Push(directory);

            foreach (var file in files)
            {
                var name = Path.GetFileName(file);
                var extension = Path.GetExtension(file);
                if (name.Equals(".env", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith(".env.", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".pem", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".key", StringComparison.OrdinalIgnoreCase) ||
                    (string.IsNullOrEmpty(extension) && file.Contains($"{Path.DirectorySeparatorChar}.ssh{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)))
                    yield return file;
            }
        }
    }

    private static IEnumerable<ImportCandidate> ScanFile(string path)
    {
        string text;
        try { text = File.ReadAllText(path, Encoding.UTF8); }
        catch { yield break; }

        if (text.Contains("PRIVATE KEY-----", StringComparison.Ordinal))
        {
            yield return new ImportCandidate(path, Path.GetFileName(path), SecretKind.SshPrivateKey, text.Trim(), true, "SSH 私钥");
            yield break;
        }

        var fileName = Path.GetFileName(path);
        if (!fileName.Equals(".env", StringComparison.OrdinalIgnoreCase) && !fileName.StartsWith(".env.", StringComparison.OrdinalIgnoreCase)) yield break;
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.StartsWith("export ", StringComparison.OrdinalIgnoreCase)) line = line[7..].TrimStart();
            var equals = line.IndexOf('=');
            if (equals <= 0) continue;
            var name = line[..equals].Trim();
            if (!ProjectManifestService.IsValidEnvironmentName(name)) continue;
            var value = Unquote(line[(equals + 1)..].Trim());
            if (string.IsNullOrEmpty(value)) continue;
            yield return new ImportCandidate(path, name, InferKind(name), value, true, Path.GetFileName(path));
        }
    }

    private static SecretKind InferKind(string name)
    {
        if (name.Contains("PASSWORD", StringComparison.OrdinalIgnoreCase)) return SecretKind.UsernamePassword;
        if (name.Contains("TOKEN", StringComparison.OrdinalIgnoreCase)) return SecretKind.Token;
        if (name.Contains("KEY", StringComparison.OrdinalIgnoreCase)) return SecretKind.ApiKey;
        return SecretKind.GenericText;
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 && value[0] == value[^1] && value[0] is '\'' or '"') value = value[1..^1];
        return value
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\\r", "\r", StringComparison.Ordinal)
            .Replace("\\\"", "\"", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal);
    }
}
