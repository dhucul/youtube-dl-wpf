using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using YoutubeDl.Engine.Media;

namespace YoutubeDlModern.Services;

public sealed class ProcessResult
{
    public int ExitCode { get; set; } = -1;
    public string StdOut { get; set; } = "";
    public string StdErr { get; set; } = "";
}

/// <summary>
/// Runs external processes with UTF-8 output, line-by-line streaming callbacks
/// and cooperative cancellation (kills the process tree on cancel).
/// </summary>
public static class ProcessRunner
{
    private const int DefaultMaximumCapturedCharacters = 32 * 1024 * 1024;
    private const int DefaultMaximumLineCharacters = 1024 * 1024;
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(5);

    public static async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> args,
        Action<string>? onStdoutLine = null,
        Action<string>? onStderrLine = null,
        bool captureOutput = true,
        CancellationToken ct = default,
        TimeSpan? timeout = null,
        int maximumCapturedCharacters = DefaultMaximumCapturedCharacters,
        int maximumLineCharacters = DefaultMaximumLineCharacters)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCapturedCharacters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumLineCharacters);
        ct.ThrowIfCancellationRequested();
        using var timeoutCts = timeout is { } duration
            ? new CancellationTokenSource(duration)
            : null;
        using var linkedCts = timeoutCts is not null
            ? CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token)
            : null;
        var effectiveToken = linkedCts?.Token ?? ct;

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        // Make any Python-based engine emit UTF-8 on redirected pipes.
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        psi.Environment["PYTHONUTF8"] = "1";
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stdout = captureOutput ? new StringBuilder() : null;
        var stderr = captureOutput ? new StringBuilder() : null;

        if (!process.Start())
            throw new InvalidOperationException($"Could not start the process '{Path.GetFileName(fileName)}'.");

        using var lifetime = ProcessLifetime.Attach(process);
        void StopProcess()
        {
            KillTree(process);
            // The job also owns descendants whose parent has already exited.
            lifetime.Dispose();
        }

        var outTask = ReadLinesAsync(
            process.StandardOutput, stdout, onStdoutLine,
            maximumCapturedCharacters, maximumLineCharacters, StopProcess, effectiveToken);
        var errTask = ReadLinesAsync(
            process.StandardError, stderr, onStderrLine,
            maximumCapturedCharacters, maximumLineCharacters, StopProcess, effectiveToken);

        async Task StopAndDrainAsync()
        {
            StopProcess();
            try { await process.WaitForExitAsync().WaitAsync(CleanupTimeout).ConfigureAwait(false); } catch { /* best effort after termination */ }
            try { await Task.WhenAll(outTask, errTask).WaitAsync(CleanupTimeout).ConfigureAwait(false); } catch { /* preserve the original failure */ }
        }

        await using (effectiveToken.Register(StopProcess))
        {
            try
            {
                await process.WaitForExitAsync(effectiveToken).ConfigureAwait(false);
                // Descendants may hold redirected handles after the parent exits.
                // The deadline and cancellation registration cover the entire drain.
                await Task.WhenAll(outTask, errTask).WaitAsync(effectiveToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (effectiveToken.IsCancellationRequested)
            {
                await StopAndDrainAsync().ConfigureAwait(false);

                if (!ct.IsCancellationRequested && timeoutCts?.IsCancellationRequested == true)
                    throw new TimeoutException($"The process '{Path.GetFileName(fileName)}' exceeded its {durationText(timeout)} timeout.");
                throw;
            }
            catch
            {
                await StopAndDrainAsync().ConfigureAwait(false);
                throw;
            }
        }

        return new ProcessResult
        {
            ExitCode = process.ExitCode,
            StdOut = stdout?.ToString() ?? "",
            StdErr = stderr?.ToString() ?? ""
        };

        static string durationText(TimeSpan? value) => value switch
        {
            { TotalMinutes: >= 1 } duration => $"{duration.TotalMinutes:0.#}-minute",
            { } duration => $"{duration.TotalSeconds:0.#}-second",
            _ => "configured"
        };
    }

    private static void KillTree(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch { /* already gone */ }
    }

    private static async Task ReadLinesAsync(
        StreamReader reader,
        StringBuilder? capture,
        Action<string>? onLine,
        int maximumCapturedCharacters,
        int maximumLineCharacters,
        Action abortProcess,
        CancellationToken ct)
    {
        var buffer = GC.AllocateUninitializedArray<char>(4096);
        var currentLine = new StringBuilder();
        var capturedCharacters = 0;

        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
            if (read == 0)
                break;
            for (var index = 0; index < read; index++)
            {
                var character = buffer[index];
                if (character == '\n')
                {
                    EmitLine();
                    continue;
                }

                if (character != '\r')
                    currentLine.Append(character);
                if (currentLine.Length > maximumLineCharacters)
                {
                    abortProcess();
                    throw new InvalidDataException(
                        $"Process output contained a line longer than {maximumLineCharacters:N0} characters.");
                }
            }
        }

        if (currentLine.Length > 0)
            EmitLine();

        void EmitLine()
        {
            var line = currentLine.ToString();
            currentLine.Clear();
            if (capture is not null)
            {
                capturedCharacters = checked(capturedCharacters + line.Length + Environment.NewLine.Length);
                if (capturedCharacters > maximumCapturedCharacters)
                {
                    abortProcess();
                    throw new InvalidDataException(
                        $"Process output exceeded the {maximumCapturedCharacters:N0}-character capture limit.");
                }
                capture.AppendLine(line);
            }

            try { onLine?.Invoke(line); }
            catch { /* consumer errors must not break process pipe drainage */ }
        }
    }

    /// <summary>Splits a raw argument string respecting double quotes (for user-supplied extra args).</summary>
    public static List<string> SplitArguments(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];
        ValidateBalancedQuotes(raw);

        var argv = CommandLineToArgvW("YoutubeDlModern.exe " + raw, out var argumentCount);
        if (argv == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not parse the extra command-line arguments.");

        try
        {
            var result = new List<string>(Math.Max(0, argumentCount - 1));
            for (var index = 1; index < argumentCount; index++)
            {
                var pointer = Marshal.ReadIntPtr(argv, index * IntPtr.Size);
                result.Add(Marshal.PtrToStringUni(pointer) ?? string.Empty);
            }
            return result;
        }
        finally
        {
            _ = LocalFree(argv);
        }
    }

    private static void ValidateBalancedQuotes(string raw)
    {
        var inQuotes = false;
        var consecutiveBackslashes = 0;
        foreach (var character in raw)
        {
            if (character == '\\')
            {
                consecutiveBackslashes++;
                continue;
            }

            if (character == '"' && consecutiveBackslashes % 2 == 0)
                inQuotes = !inQuotes;
            consecutiveBackslashes = 0;
        }

        if (inQuotes)
            throw new FormatException("Double quotes are not balanced.");
    }

    [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CommandLineToArgvW(string commandLine, out int argumentCount);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr memory);
}
