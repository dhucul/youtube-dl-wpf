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

    /// <summary>Decrypts every protected setting, clearing any this account cannot read.</summary>
    /// <returns>The number of values that were cleared.</returns>
    public static int Unprotect(AppSettings settings)
    {
        var cleared = 0;
        settings.Auth.Password = RecoverValue(settings.Auth.Password, ref cleared);
        settings.Auth.TwoFactor = RecoverValue(settings.Auth.TwoFactor, ref cleared);
        settings.Auth.VideoPassword = RecoverValue(settings.Auth.VideoPassword, ref cleared);
        settings.Auth.ApPassword = RecoverValue(settings.Auth.ApPassword, ref cleared);
        settings.Network.Proxy = RecoverValue(settings.Network.Proxy, ref cleared);
        settings.Network.GeoVerificationProxy = RecoverValue(settings.Network.GeoVerificationProxy, ref cleared);
        settings.Network.CustomHeaders = RecoverValue(settings.Network.CustomHeaders, ref cleared);
        return cleared;
    }

    /// <summary>
    /// Decrypts one value. A value protected for another Windows account or machine can never
    /// be read here, so it is cleared rather than discarding every other stored setting or entry.
    /// </summary>
    internal static string RecoverValue(string value, ref int cleared)
    {
        try { return UnprotectValue(value); }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            cleared++;
            return string.Empty;
        }
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
