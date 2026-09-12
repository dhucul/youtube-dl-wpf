using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using YoutubeDlModern.Models;

namespace YoutubeDlModern.Services;

public sealed class SettingsService
{
    private const int MaximumSettingsBytes = 4 * 1024 * 1024;
    public static string AppDataDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "YoutubeDlModern");

    public static string LocalDataDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YoutubeDlModern");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _storageDirectory;
    private string FilePath => Path.Combine(_storageDirectory, "settings.json");
    public SettingsService(string? storageDirectory = null) => _storageDirectory = storageDirectory ?? AppDataDir;
    public event Action? Changed;
    public string? BackupWarning { get; private set; }

    public AppSettings Data { get; private set; } = new();

    public void Load()
    {
        if (TryLoad(FilePath, out var loaded))
        {
            Data = Normalize(loaded!);
            TryMigrateProtectedStorage();
            return;
        }

        if (TryLoad(AtomicFile.BackupPath(FilePath), out loaded))
        {
            Data = Normalize(loaded!);
            TryMigrateProtectedStorage();
            return;
        }

        Data = Normalize(new AppSettings());
    }

    public void Save()
    {
        Save(Data);
    }

    public void Save(AppSettings candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var copy = Clone(candidate);
        EnsureSections(copy);
        SettingsValidation.ThrowIfInvalid(copy);
        var normalized = Normalize(copy);
        var persisted = Clone(normalized);
        persisted.Auth.TwoFactor = string.Empty; // One-time codes must never be written to disk.
        SettingsSecrets.Protect(persisted);
        var serialized = JsonSerializer.Serialize(persisted, JsonOptions);
        AtomicFile.WriteAllText(FilePath, serialized, createBackup: false);
        // Store only protected data in the recovery file. Copying a legacy primary
        // before migration would preserve its plaintext credentials indefinitely.
        // The successful primary-file replacement is the commit point.
        Data = normalized;
        BackupWarning = null;
        try { AtomicFile.WriteAllText(AtomicFile.BackupPath(FilePath), serialized, createBackup: false); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            BackupWarning = "Settings saved, but the recovery copy could not be updated: " + ex.Message;
        }
        Changed?.Invoke();
    }

    public AppSettings CreateSnapshot() => Normalize(Clone(Data));

    public AppSettings CreateDraft() => CreateSnapshot();

    public static AppSettings CreateDefault() => Normalize(new AppSettings());

    private static bool TryLoad(string path, out AppSettings? settings)
    {
        settings = null;
        if (!File.Exists(path)) return false;

        try
        {
            settings = JsonSerializer.Deserialize<AppSettings>(AtomicFile.ReadAllText(path, MaximumSettingsBytes));
            if (settings is not null)
            {
                EnsureSections(settings);
                SettingsSecrets.Unprotect(settings);
                settings.Auth.TwoFactor = string.Empty;
            }
            return settings is not null;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        EnsureSections(settings);

        var sections = new object[]
        {
            settings.General,
            settings.Formats,
            settings.Subtitles,
            settings.Network,
            settings.Selection,
            settings.Auth,
            settings.PostProcess
        };
        foreach (var section in sections)
        {
            foreach (var property in section.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (property.PropertyType == typeof(string) && property.CanWrite && property.GetValue(section) is null)
                    property.SetValue(section, string.Empty);
            }
        }

        var defaults = new GeneralSettings();
        if (string.IsNullOrWhiteSpace(settings.General.DownloadDirectory))
            settings.General.DownloadDirectory = defaults.DownloadDirectory;
        if (string.IsNullOrWhiteSpace(settings.General.OutputTemplate))
            settings.General.OutputTemplate = defaults.OutputTemplate;
        settings.General.MaxConcurrentDownloads = Math.Clamp(settings.General.MaxConcurrentDownloads, 1, 8);

        settings.Network.SocketTimeout = Math.Clamp(settings.Network.SocketTimeout, 0, 3600);
        settings.Network.Retries = Math.Clamp(settings.Network.Retries, 0, 1000);
        settings.Network.FragmentRetries = Math.Clamp(settings.Network.FragmentRetries, 0, 1000);
        settings.Network.ConcurrentFragments = Math.Clamp(settings.Network.ConcurrentFragments, 1, 32);
        settings.Network.SleepInterval = Math.Max(0, settings.Network.SleepInterval);
        settings.Network.MaxSleepInterval = Math.Max(0, settings.Network.MaxSleepInterval);
        if (settings.Network.MaxSleepInterval > 0 && settings.Network.SleepInterval == 0)
            settings.Network.MaxSleepInterval = 0;
        else if (settings.Network.MaxSleepInterval > 0 &&
                 settings.Network.MaxSleepInterval < settings.Network.SleepInterval)
            settings.Network.MaxSleepInterval = settings.Network.SleepInterval;
        if (!SettingsValidation.IsValidRateLimit(settings.Network.RateLimit))
            settings.Network.RateLimit = string.Empty;

        settings.Selection.PlaylistStart = Math.Max(0, settings.Selection.PlaylistStart);
        settings.Selection.PlaylistEnd = Math.Max(0, settings.Selection.PlaylistEnd);
        if (settings.Selection.PlaylistStart > 0 && settings.Selection.PlaylistEnd > 0 &&
            settings.Selection.PlaylistEnd < settings.Selection.PlaylistStart)
            settings.Selection.PlaylistEnd = 0;
        settings.Selection.MaxDownloads = Math.Max(0, settings.Selection.MaxDownloads);
        settings.Selection.MinViews = Math.Max(0, settings.Selection.MinViews);
        settings.Selection.MaxViews = Math.Max(0, settings.Selection.MaxViews);
        if (settings.Selection.MinViews > 0 && settings.Selection.MaxViews > 0 &&
            settings.Selection.MaxViews < settings.Selection.MinViews)
            settings.Selection.MaxViews = 0;
        settings.Selection.AgeLimit = Math.Clamp(settings.Selection.AgeLimit, 0, 99);

        try { SettingsValidation.ValidateExtraArguments(settings.General.ExtraArgs, "global extra arguments"); }
        catch (ArgumentException) { settings.General.ExtraArgs = string.Empty; }

        // These switches are mutually exclusive at the command line. Abort takes precedence
        // when an older or hand-edited settings file contains both.
        if (settings.General.AbortOnError && settings.General.IgnoreErrors)
            settings.General.IgnoreErrors = false;

        return settings;
    }

    private static void EnsureSections(AppSettings settings)
    {
        settings.General ??= new GeneralSettings();
        settings.Formats ??= new FormatSettings();
        settings.Subtitles ??= new SubtitleSettings();
        settings.Network ??= new NetworkSettings();
        settings.Selection ??= new SelectionSettings();
        settings.Auth ??= new AuthSettings();
        settings.PostProcess ??= new PostProcessSettings();
    }

    private static AppSettings Clone(AppSettings settings) =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings, JsonOptions), JsonOptions)
        ?? new AppSettings();

    private void TryMigrateProtectedStorage()
    {
        try { Save(Data); }
        catch
        {
            // Loading usable settings is more important than failing startup because
            // the storage location is temporarily read-only. The next save retries.
        }
    }
}
