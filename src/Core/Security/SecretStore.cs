using System.Security.Cryptography;
using System.Text;
using System.Runtime.Versioning;

namespace Core.Security;

/// <summary>
/// User-scoped encryption for secrets at rest (e.g. the TLS cert's PFX password),
/// using Windows DPAPI. Data is bound to the current Windows user account.
/// </summary>
[SupportedOSPlatform("windows")]
public static class SecretStore
{
    public static void ProtectToFile(string path, string secret)
    {
        byte[] encrypted = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(secret), optionalEntropy: null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(path, encrypted);
    }

    public static string UnprotectFromFile(string path)
    {
        byte[] decrypted = ProtectedData.Unprotect(
            File.ReadAllBytes(path), optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(decrypted);
    }
}
