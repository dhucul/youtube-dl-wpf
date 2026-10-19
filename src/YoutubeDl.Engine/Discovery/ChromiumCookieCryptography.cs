using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace YoutubeDl.Engine.Discovery;

internal sealed class ChromiumCookieCryptography(string localState) : IDisposable
{
    private AesGcm? _cipher;
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
    internal string Decrypt(byte[] encrypted, string host, int schemaVersion)
    {
        byte[]? clear = null;
        try
        {
            if (encrypted.AsSpan().StartsWith("v20"u8))
                throw new NotSupportedException("The selected browser uses application-bound cookie encryption. Export cookies from that browser for native downloading.");
            if (encrypted.AsSpan().StartsWith("v10"u8))
            {
                if (encrypted.Length < 31) throw new CryptographicException("The browser cookie encryption record is incomplete.");
                _cipher ??= LoadCipher();
                clear = new byte[encrypted.Length - 31];
                try { _cipher.Decrypt(encrypted.AsSpan(3, 12), encrypted.AsSpan(15, clear.Length), encrypted.AsSpan(encrypted.Length - 16), clear); }
                catch (CryptographicException) { throw new CryptographicException("Browser cookie authentication failed. Select the original profile under its Windows account, or export cookies."); }
            }
            else
            {
                if (encrypted.Length >= 3 && encrypted[0] == (byte)'v' && char.IsAsciiDigit((char)encrypted[1]) && char.IsAsciiDigit((char)encrypted[2]))
                    throw new NotSupportedException("This browser cookie encryption version is not supported.");
                clear = WindowsCookieProtection.Unprotect(encrypted);
            }
            var value = clear.AsSpan();
            if (schemaVersion >= 24)
            {
                var hash = SHA256.HashData(Encoding.UTF8.GetBytes(host));
                if (value.Length < hash.Length || !CryptographicOperations.FixedTimeEquals(hash, value[..hash.Length]))
                    throw new CryptographicException("The decrypted browser cookie does not match its recorded host.");
                value = value[hash.Length..];
            }
            try { return Utf8.GetString(value); }
            catch (DecoderFallbackException) { throw new CryptographicException("The decrypted browser cookie is not valid UTF-8."); }
        }
        finally { if (clear is not null) CryptographicOperations.ZeroMemory(clear); }
    }
    private AesGcm LoadCipher()
    {
        using var state = ChromiumProfiles.ReadState(localState);
        var encoded = Providers.ProviderJson.Text(Providers.ProviderJson.Object(state.RootElement, "os_crypt"), "encrypted_key");
        if (string.IsNullOrWhiteSpace(encoded) || encoded.Length > 65536)
            throw new CryptographicException("The browser profile does not expose a supported Windows-protected cookie key.");
        byte[] bytes;
        try { bytes = Convert.FromBase64String(encoded); }
        catch (FormatException) { throw new CryptographicException("The browser encryption key encoding is invalid."); }
        byte[]? key = null;
        try
        {
            if (!bytes.AsSpan().StartsWith("DPAPI"u8)) throw new NotSupportedException("The browser key uses an unsupported protection method.");
            key = WindowsCookieProtection.Unprotect(bytes.AsSpan(5));
            if (key.Length != 32) throw new CryptographicException("The browser encryption key has an invalid size.");
            return new AesGcm(key, 16);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); if (key is not null) CryptographicOperations.ZeroMemory(key); }
    }
    public void Dispose() => _cipher?.Dispose();
}

internal static class WindowsCookieProtection
{
    [StructLayout(LayoutKind.Sequential)] internal struct Blob { internal uint Length; internal IntPtr Data; }
    internal static byte[] Unprotect(ReadOnlySpan<byte> encrypted)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows cookie decryption requires Windows.");
        if (encrypted.Length is 0 or > 32768) throw new CryptographicException("The Windows-protected browser data has an invalid size.");
        var input = encrypted.ToArray(); var pinned = GCHandle.Alloc(input, GCHandleType.Pinned); Blob output = default;
        try
        {
            var data = new Blob { Length = (uint)input.Length, Data = pinned.AddrOfPinnedObject() };
            if (!UnprotectData(ref data, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output))
                throw new CryptographicException("Windows could not decrypt browser data for the current account (error " + Marshal.GetLastWin32Error() + ").");
            if (output.Length > 32768 || output.Length > 0 && output.Data == IntPtr.Zero) throw new CryptographicException("Windows returned an invalid browser-data size.");
            var result = new byte[output.Length]; if (result.Length > 0) Marshal.Copy(output.Data, result, 0, result.Length); return result;
        }
        finally
        {
            pinned.Free(); CryptographicOperations.ZeroMemory(input);
            if (output.Data != IntPtr.Zero)
            {
                var zeros = new byte[8192]; var remaining = Math.Min((long)output.Length, 1024 * 1024); var offset = 0;
                while (remaining > 0) { var count = (int)Math.Min(remaining, zeros.Length); Marshal.Copy(zeros, 0, IntPtr.Add(output.Data, offset), count); offset += count; remaining -= count; }
                LocalFree(output.Data);
            }
        }
    }
    [DllImport("crypt32.dll", EntryPoint = "CryptUnprotectData", SetLastError = true, ExactSpelling = true), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("kernel32.dll", ExactSpelling = true), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr LocalFree(IntPtr pointer);
}
