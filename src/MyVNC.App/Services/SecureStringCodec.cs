using System.Security.Cryptography;
using System.Text;

namespace MyVNC.App.Services;

/// <summary>Encrypts saved VNC passwords with Windows DPAPI, scoped to the current user.</summary>
internal static class SecureStringCodec
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("MyVNC.ConnectionPassword.v1");

    public static string Protect(string plainText)
    {
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var protectedBytes = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    public static string? Unprotect(string? protectedBase64)
    {
        if (string.IsNullOrEmpty(protectedBase64)) return null;
        try
        {
            var protectedBytes = Convert.FromBase64String(protectedBase64);
            var plainBytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (CryptographicException)
        {
            // Protected by a different user/machine profile — treat as unavailable.
            return null;
        }
    }
}
