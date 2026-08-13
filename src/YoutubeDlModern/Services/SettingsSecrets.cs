using System.Security.Cryptography;
using System.Text;
using YoutubeDlModern.Models;

namespace YoutubeDlModern.Services;

internal static class SettingsSecrets
{
    private const string Prefix = "dpapi:v1:";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("YoutubeDlModern.Settings.v1");

    public static void Protect(AppSettings settings)
    {
        settings.Auth.Password = ProtectValue(settings.Auth.Password);
        settings.Auth.TwoFactor = ProtectValue(settings.Auth.TwoFactor);
        settings.Auth.VideoPassword = ProtectValue(settings.Auth.VideoPassword);
        settings.Auth.ApPassword = ProtectValue(settings.Auth.ApPassword);
        settings.Network.Proxy = ProtectValue(settings.Network.Proxy);
        settings.Network.GeoVerificationProxy = ProtectValue(settings.Network.GeoVerificationProxy);
        settings.Network.CustomHeaders = ProtectValue(settings.Network.CustomHeaders);
    }

    public static void Unprotect(AppSettings settings)
    {
        settings.Auth.Password = UnprotectValue(settings.Auth.Password);
        settings.Auth.TwoFactor = UnprotectValue(settings.Auth.TwoFactor);
        settings.Auth.VideoPassword = UnprotectValue(settings.Auth.VideoPassword);
        settings.Auth.ApPassword = UnprotectValue(settings.Auth.ApPassword);
        settings.Network.Proxy = UnprotectValue(settings.Network.Proxy);
        settings.Network.GeoVerificationProxy = UnprotectValue(settings.Network.GeoVerificationProxy);
        settings.Network.CustomHeaders = UnprotectValue(settings.Network.CustomHeaders);
    }

    internal static string ProtectValue(string value)
    {
        if (string.IsNullOrEmpty(value) || value.StartsWith(Prefix, StringComparison.Ordinal))
            return value;
        var plaintext = Encoding.UTF8.GetBytes(value);
        try
        {
            var protectedBytes = ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);
            return Prefix + Convert.ToBase64String(protectedBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    internal static string UnprotectValue(string value)
    {
        if (string.IsNullOrEmpty(value) || !value.StartsWith(Prefix, StringComparison.Ordinal))
            return value; // Legacy plaintext is migrated on the next successful save.

        var protectedBytes = Convert.FromBase64String(value[Prefix.Length..]);
        var plaintext = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
        try
        {
            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }
}
