using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Media;

namespace YoutubeDl.Engine;

/// <summary>Direct post-move commands with durable, non-replaying publication intent.</summary>
public static class NativeExec
{
    private sealed record Command(string Program, string[] Arguments);
    private sealed record Receipt(string Recipe, string Invocation, string Media, int Count, int Completed, int? Started, string Digest);
    public static void Validate(IReadOnlyList<string>? values) { _ = Parse(values); }
    private static IReadOnlyList<Command> Parse(IReadOnlyList<string>? values)
    {
        if (values is null) return [];
        if (values.Count > 32) throw new ArgumentException("Too many native exec commands.");
        var result = new List<Command>();
        foreach (var raw in values)
        {
            if (string.IsNullOrWhiteSpace(raw) || raw.Length > 16384 || raw.Any(char.IsControl)) throw new ArgumentException("A native exec command is empty or exceeds its limits.");
            var value = raw.StartsWith("after_move:", StringComparison.Ordinal) ? raw[11..] : raw;
            if (Regex.IsMatch(value, @"\A[a-z_]{2,32}:")) throw new NotSupportedException("Native exec currently supports the after_move stage.");
            var args = Split(value); if (args.Count == 0 || args.Count > 256 || args[0].Contains('%') || args[0].Contains("{}", StringComparison.Ordinal)) throw new ArgumentException("Native exec needs a fixed program and bounded arguments.");
            var name = Path.GetFileNameWithoutExtension(args[0]).ToLowerInvariant();
            if (name is "cmd" or "powershell" or "pwsh" or "sh" or "bash" or "dash" or "zsh" or "wscript" or "cscript")
                throw new NotSupportedException("Native exec invokes programs directly; inline shell commands require additional support.");
            foreach (var arg in args.Skip(1))
            {
                if (arg is "|" or "||" or "&&" or ";" or ">" or ">>" or "<" or "&") throw new NotSupportedException("Native exec shell pipelines and redirection are not supported.");
                if (arg.Contains("%(", StringComparison.Ordinal) && !Regex.IsMatch(arg, @"\A%\((?:filepath|_filename|title|id|uploader|extractor|webpage_url)\)q\z|\A%\((?:duration|playlist_index)\)[dif]\z")) throw new NotSupportedException("Native exec fields must be separate quoted or numeric arguments.");
                if (arg.Contains("{}", StringComparison.Ordinal) && arg != "{}") throw new NotSupportedException("The native exec filepath placeholder must occupy a complete argument.");
            }
            if (name is "python" or "python3" or "node" or "ruby" or "perl")
                for (var i = 1; i + 1 < args.Count; i++) if (args[i] is "-c" or "-e" or "--eval" && (args[i + 1].Contains("%(", StringComparison.Ordinal) || args[i + 1].Contains("{}", StringComparison.Ordinal))) throw new NotSupportedException("Native exec metadata cannot become interpreter code.");
            if (!args.Skip(1).Any(arg => arg == "{}" || arg.Contains("%(", StringComparison.Ordinal))) args.Add("{}");
            result.Add(new(args[0], args.Skip(1).ToArray()));
        }
        return result;
    }

    internal static void CheckPending(string rawDirectory, NativeDownloadRequest request)
    {
        if (request.ExecCommands is not { Count: > 0 }) return;
        var receipt = Load(Path.Combine(rawDirectory, ".native-exec.json"));
        if (receipt?.Started is not null) throw new IOException("A previous exec command may already have run. Its output and recovery data were retained; automatic replay is refused.");
        if (receipt is not null && receipt.Recipe != DownloadWorkspace.Recipe(request)) throw new IOException("A pending exec receipt belongs to another job recipe.");
    }

    internal static async Task RunAsync(NativeDownloadRequest request, PageMetadata page, string primary, IReadOnlyList<string> media, string rawDirectory, CancellationToken ct)
    {
        var commands = Parse(request.ExecCommands); if (commands.Count == 0) return;
        var rendered = commands.Select(command => command with { Arguments = command.Arguments.Select(arg => Expand(arg, primary, request, page)).ToArray() }).ToArray();
        var recipe = DownloadWorkspace.Recipe(request); var invocation = Hash(JsonSerializer.Serialize(rendered));
        var fingerprints = new List<PublicationTransaction.Fingerprint>();
        foreach (var path in media) fingerprints.Add(await PublicationTransaction.DescribeAsync(path, ct).ConfigureAwait(false));
        var mediaHash = Hash(JsonSerializer.Serialize(fingerprints)); var file = Path.Combine(rawDirectory, ".native-exec.json");
        var receipt = Load(file) ?? new Receipt(recipe, invocation, mediaHash, commands.Count, 0, null, "");
        if (receipt.Recipe != recipe || receipt.Invocation != invocation || receipt.Media != mediaHash || receipt.Count != commands.Count || receipt.Started is not null)
            throw new IOException("Pending exec data changed or a previous command may already have run; automatic replay is refused.");
        for (var index = receipt.Completed; index < commands.Count; index++)
        {
            ct.ThrowIfCancellationRequested(); receipt = receipt with { Started = index }; await Save(file, receipt).ConfigureAwait(false);
            await PublicationFaults.AtAsync("exec-intent:" + index).ConfigureAwait(false);
            var command = rendered[index];
            await Execute(command, ct).ConfigureAwait(false);
            for (var item = 0; item < media.Count; item++)
                if (!await PublicationTransaction.MatchesAsync(media[item], fingerprints[item]).ConfigureAwait(false))
                    throw new IOException("Native exec commands must preserve the published media set. Previous files remain protected by recovery backups.");
            receipt = receipt with { Completed = index + 1, Started = null }; await Save(file, receipt).ConfigureAwait(false);
            await PublicationFaults.AtAsync("exec-completed:" + index).ConfigureAwait(false);
        }
    }

    private static async Task Execute(Command command, CancellationToken ct)
    {
        var start = new ProcessStartInfo(command.Program) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in command.Arguments) start.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = start };
        try { if (!process.Start()) throw new InvalidOperationException("The configured exec program could not start."); }
        catch (System.ComponentModel.Win32Exception) { throw new InvalidOperationException("The configured exec program could not start; its intent was retained to prevent accidental replay."); }
        using var lifetime = ProcessLifetime.Attach(process); var output = Drain(process.StandardOutput); var error = Drain(process.StandardError);
        try { await PublicationFaults.AtAsync("exec-started").ConfigureAwait(false); await process.WaitForExitAsync(ct).ConfigureAwait(false); }
        catch
        {
            await ProcessLifetime.KillAndDrainAsync(process, lifetime, output, error).ConfigureAwait(false); throw;
        }
        lifetime.Dispose(); await Task.WhenAll(output, error).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        if (process.ExitCode != 0) throw new InvalidOperationException("The configured exec command failed with exit code " + process.ExitCode.ToString(CultureInfo.InvariantCulture) + ". Its intent was retained to prevent accidental replay.");
    }
    private static async Task Drain(StreamReader reader) { var buffer = new char[4096]; while (await reader.ReadAsync(buffer).ConfigureAwait(false) > 0) { } }
    private static string Expand(string arg, string path, NativeDownloadRequest request, PageMetadata page) => arg switch
    {
        "{}" or "%(filepath)q" or "%(_filename)q" => path, "%(title)q" => page.Title, "%(id)q" => page.Id ?? "", "%(uploader)q" => page.Uploader ?? "", "%(extractor)q" => page.Extractor, "%(webpage_url)q" => request.Url.AbsoluteUri,
        "%(duration)f" => (page.DurationSeconds ?? 0).ToString("R", CultureInfo.InvariantCulture), "%(duration)d" or "%(duration)i" => Math.Truncate(page.DurationSeconds ?? 0).ToString(CultureInfo.InvariantCulture),
        "%(playlist_index)d" or "%(playlist_index)i" => (request.PlaylistIndex ?? 0).ToString(CultureInfo.InvariantCulture), "%(playlist_index)f" => (request.PlaylistIndex ?? 0).ToString("F6", CultureInfo.InvariantCulture), _ => arg.Replace("%%", "%", StringComparison.Ordinal)
    };
    private static Receipt? Load(string file)
    {
        PublicationTransaction.Regular(file); if (!File.Exists(file)) return null;
        if (new FileInfo(file).Length > 65536) throw new InvalidDataException("The exec receipt exceeds its limit.");
        var receipt = JsonSerializer.Deserialize<Receipt>(File.ReadAllBytes(file)) ?? throw new InvalidDataException("The exec receipt is empty.");
        if (receipt.Count is < 1 or > 32 || receipt.Completed < 0 || receipt.Completed > receipt.Count || receipt.Started is { } started && (started != receipt.Completed || started >= receipt.Count) || receipt.Digest != Hash(JsonSerializer.Serialize(receipt with { Digest = "" }))) throw new InvalidDataException("The exec receipt changed.");
        return receipt;
    }
    private static async Task Save(string file, Receipt receipt)
    {
        PublicationTransaction.Regular(file); var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        receipt = receipt with { Digest = Hash(JsonSerializer.Serialize(receipt with { Digest = "" })) };
        try { await PublicationFaults.WriteAsync(temporary, JsonSerializer.SerializeToUtf8Bytes(receipt), "exec-receipt", CancellationToken.None).ConfigureAwait(false); File.Move(temporary, file, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static string Hash(string value) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static List<string> Split(string text)
    {
        var result = new List<string>(); var word = new StringBuilder(); char quote = '\0'; var started = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '\\' && i + 1 < text.Length && text[i + 1] is '\'' or '"') { word.Append(text[++i]); started = true; }
            else if (ch == quote) quote = '\0';
            else if (quote == '\0' && ch is '\'' or '"') { quote = ch; started = true; }
            else if (quote == '\0' && char.IsWhiteSpace(ch)) { if (started) { result.Add(word.ToString()); word.Clear(); started = false; } }
            else { word.Append(ch); started = true; }
        }
        if (quote != '\0') throw new ArgumentException("The exec command contains an unterminated quote.");
        if (started) result.Add(word.ToString()); return result;
    }
}
