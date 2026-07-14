using System.Security.Cryptography;
using System.Text;

namespace KeyHub.Core.Storage;

public sealed class SecretProtector
{
    private static byte[] Entropy(string secretId) =>
        SHA256.HashData(Encoding.UTF8.GetBytes($"KeyHubDesktop:v1:{secretId}"));

    public byte[] Protect(string secretId, string plaintext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secretId);
        var bytes = Encoding.UTF8.GetBytes(plaintext ?? string.Empty);
        try
        {
            return ProtectedData.Protect(bytes, Entropy(secretId), DataProtectionScope.CurrentUser);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public string Unprotect(string secretId, byte[] ciphertext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secretId);
        var bytes = ProtectedData.Unprotect(ciphertext, Entropy(secretId), DataProtectionScope.CurrentUser);
        try
        {
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
