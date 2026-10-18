using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace MusicHub.Api.Identification;

/// <summary>A Chromaprint fingerprint and the duration of the audio it was made from.</summary>
public sealed record AudioFingerprintData(int DurationSeconds, string Fingerprint);

/// <summary>Makes a Chromaprint fingerprint of audio. Throws <see cref="FingerprintUnavailableException"/> when it cannot.</summary>
public interface IChromaprintFingerprinter
{
    Task<AudioFingerprintData> FingerprintAsync(Stream audio, string? fileNameHint, CancellationToken ct);
}

/// <summary>
/// Runs <c>fpcalc</c> on a private temporary copy of the audio. No shell is involved and the arguments are fixed
/// apart from the temp file path; the process has a timeout, a bounded output and a concurrency limit.
/// Register it as a singleton: the concurrency limit is per instance.
/// </summary>
public sealed class FpcalcFingerprinter : IChromaprintFingerprinter
{
    private const int MaxOutputChars = 1 << 20;

    private readonly AcoustIdOptions _options;
    private readonly SemaphoreSlim _slots;

    public FpcalcFingerprinter(IOptions<AcoustIdOptions> options)
    {
        _options = options.Value;
        _slots = new SemaphoreSlim(Math.Max(1, _options.MaxConcurrentFingerprints));
    }

    public async Task<AudioFingerprintData> FingerprintAsync(Stream audio, string? fileNameHint, CancellationToken ct)
    {
        await _slots.WaitAsync(ct);
        var dir = Path.Combine(Path.GetTempPath(), "musichub-fp-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "audio" + SafeExtension(fileNameHint));
            await CopyAsync(audio, file, ct);
            return Parse(await RunAsync(file, ct));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            _slots.Release();
        }
    }

    /// <summary>Only a short alphanumeric extension is kept, so a hint can never shape the path.</summary>
    private static string SafeExtension(string? fileNameHint)
    {
        var ext = Path.GetExtension(fileNameHint ?? "");
        return ext.Length is > 1 and <= 6 && ext.Skip(1).All(char.IsAsciiLetterOrDigit) ? ext.ToLowerInvariant() : "";
    }

    private async Task CopyAsync(Stream audio, string file, CancellationToken ct)
    {
        var max = (long)_options.MaxFileMegabytes * 1024 * 1024;
        await using var target = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await audio.ReadAsync(buffer, ct)) > 0)
        {
            total += read;
            if (total > max)
            {
                throw new FingerprintUnavailableException(
                    $"The audio is larger than {_options.MaxFileMegabytes} MB; it is not fingerprinted.");
            }
            await target.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        if (total == 0)
        {
            throw new FingerprintUnavailableException("The audio is empty.");
        }
    }

    private async Task<string> RunAsync(string file, CancellationToken ct)
    {
        var info = new ProcessStartInfo(_options.FpcalcPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        info.ArgumentList.Add("-json");
        info.ArgumentList.Add("-length");
        info.ArgumentList.Add(Math.Max(1, _options.FingerprintSeconds).ToString(CultureInfo.InvariantCulture));
        info.ArgumentList.Add(file);

        Process process;
        try
        {
            process = Process.Start(info) ?? throw new FingerprintUnavailableException("fpcalc could not be started.");
        }
        catch (Win32Exception)
        {
            throw new FingerprintUnavailableException(
                $"fpcalc (Chromaprint) is not installed or not executable at \"{_options.FpcalcPath}\"; set AcoustId__FpcalcPath.");
        }

        using (process)
        {
            process.StandardInput.Close();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds)));
            try
            {
                var output = ReadLimitedAsync(process.StandardOutput, timeout.Token);
                var error = ReadLimitedAsync(process.StandardError, timeout.Token);
                await process.WaitForExitAsync(timeout.Token);
                var stdout = await output;
                await error;
                if (process.ExitCode != 0)
                {
                    throw new FingerprintUnavailableException("The audio could not be read or decoded by fpcalc.");
                }
                return stdout;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                Kill(process);
                throw new FingerprintUnavailableException($"fpcalc did not finish within {_options.TimeoutSeconds} seconds.");
            }
            catch
            {
                Kill(process);
                throw;
            }
        }
    }

    private static void Kill(Process process)
    {
        try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
    }

    private static async Task<string> ReadLimitedAsync(StreamReader reader, CancellationToken ct)
    {
        var sb = new StringBuilder();
        var buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), ct)) > 0)
        {
            if (sb.Length + read > MaxOutputChars)
            {
                throw new FingerprintUnavailableException("fpcalc produced unexpectedly large output.");
            }
            sb.Append(buffer, 0, read);
        }
        return sb.ToString();
    }

    private static AudioFingerprintData Parse(string json)
    {
        int duration;
        string? fingerprint;
        try
        {
            using var doc = JsonDocument.Parse(json);
            duration = (int)Math.Round(doc.RootElement.GetProperty("duration").GetDouble());
            fingerprint = doc.RootElement.GetProperty("fingerprint").GetString();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new FingerprintUnavailableException("fpcalc produced output that could not be understood.");
        }
        if (duration < 1 || string.IsNullOrWhiteSpace(fingerprint))
        {
            throw new FingerprintUnavailableException("fpcalc produced no fingerprint for the audio.");
        }
        return new AudioFingerprintData(duration, fingerprint);
    }
}
