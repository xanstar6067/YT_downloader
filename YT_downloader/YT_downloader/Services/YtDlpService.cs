using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using YT_downloader.Models;

namespace YT_downloader.Services;

public sealed class YtDlpService : IYtDlpService
{
    private const string ProgressTemplate =
        "download:download:%(progress._percent_str)s|%(progress._speed_str)s|%(progress._downloaded_bytes_str)s/%(progress._total_bytes_str)s|%(progress._eta_str)s|%(playlist_index)s|%(playlist_count)s";
    private const int MaximumDownloadAttempts = 2;

    private readonly string _toolsDirectory;
    private readonly string _ytDlpPath;
    private readonly string _ffmpegPath;
    private readonly string _ffprobePath;
    private readonly JavaScriptRuntime? _javaScriptRuntime;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _processSync = new();
    private Process? _activeProcess;
    private int _downloadRunning;

    public YtDlpService(string toolsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolsDirectory);
        _toolsDirectory = Path.GetFullPath(toolsDirectory);
        _ytDlpPath = Path.Combine(_toolsDirectory, "yt-dlp.exe");
        _ffmpegPath = Path.Combine(_toolsDirectory, "ffmpeg.exe");
        _ffprobePath = Path.Combine(_toolsDirectory, "ffprobe.exe");
        _javaScriptRuntime = FindJavaScriptRuntime(_toolsDirectory);
    }

    public ToolAvailability GetToolAvailability() => new(
        File.Exists(_ytDlpPath),
        File.Exists(_ffmpegPath),
        File.Exists(_ffprobePath),
        _javaScriptRuntime is not null && File.Exists(_javaScriptRuntime.ExecutablePath));

    public async Task<VideoInfo> AnalyzeAsync(
        string url,
        bool includePlaylist,
        IProgress<string>? log,
        CancellationToken cancellationToken)
    {
        EnsureTools(requireMediaTools: false);
        await _operationGate.WaitAsync(cancellationToken);

        try
        {
            var result = await RunBufferedProcessAsync(
                _ytDlpPath,
                BuildAnalyzeArguments(url, includePlaylist),
                cancellationToken);

            ReportLines(result.StandardError, log);
            if (result.ExitCode != 0)
            {
                throw CreateProcessException(result.StandardError);
            }

            try
            {
                return YtDlpMetadataParser.Parse(result.StandardOutput);
            }
            catch (JsonException exception)
            {
                throw new YtDlpException(
                    YtDlpErrorKind.Unknown,
                    "yt-dlp вернул данные в неожиданном формате. Попробуйте обновить yt-dlp.",
                    result.StandardOutput,
                    exception);
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task DownloadAsync(
        DownloadRequest request,
        IProgress<DownloadProgress>? progress,
        IProgress<string>? log,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureTools(requireMediaTools: true);

        if (Interlocked.CompareExchange(ref _downloadRunning, 1, 0) != 0)
        {
            throw new YtDlpException(
                YtDlpErrorKind.AlreadyRunning,
                "Другая загрузка уже выполняется. Дождитесь её завершения или отмените её.");
        }

        try
        {
            await _operationGate.WaitAsync(cancellationToken);
            try
            {
                Directory.CreateDirectory(request.OutputDirectory);
                var arguments = BuildDownloadArguments(request);
                for (var attempt = 1; attempt <= MaximumDownloadAttempts; attempt++)
                {
                    var result = await RunDownloadProcessAsync(
                        arguments,
                        progress,
                        log,
                        cancellationToken);

                    if (result.ExitCode == 0)
                    {
                        return;
                    }

                    if (attempt < MaximumDownloadAttempts && IsForbiddenDownloadError(result.StandardError))
                    {
                        log?.Report(
                            "Сервер вернул HTTP 403. Получаю новые ссылки на медиапотоки и повторяю загрузку…");
                        continue;
                    }

                    throw CreateProcessException(result.StandardError);
                }
            }
            finally
            {
                _operationGate.Release();
            }
        }
        finally
        {
            Interlocked.Exchange(ref _downloadRunning, 0);
        }
    }

    public async Task<string> UpdateAsync(IProgress<string>? log, CancellationToken cancellationToken)
    {
        EnsureTools(requireMediaTools: false, requireJavaScriptRuntime: false);
        await _operationGate.WaitAsync(cancellationToken);

        try
        {
            var result = await RunBufferedProcessAsync(
                _ytDlpPath,
                ["--encoding", "utf-8", "-U"],
                cancellationToken);
            ReportLines(result.StandardOutput, log);
            ReportLines(result.StandardError, log);

            if (result.ExitCode != 0)
            {
                throw CreateProcessException(result.StandardError);
            }

            return result.StandardOutput
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .LastOrDefault()
                ?? "Обновление yt-dlp завершено.";
        }
        finally
        {
            _operationGate.Release();
        }
    }

    internal IReadOnlyList<string> BuildAnalyzeArguments(string url, bool includePlaylist = false)
    {
        var arguments = new List<string> { "--encoding", "utf-8" };
        AddJavaScriptRuntimeArguments(arguments);
        arguments.AddRange(["--dump-single-json", "--skip-download", "--no-warnings"]);
        arguments.Add(includePlaylist ? "--yes-playlist" : "--no-playlist");
        if (includePlaylist)
        {
            arguments.Add("--flat-playlist");
        }

        arguments.Add(url);
        return arguments;
    }

    internal IReadOnlyList<string> BuildDownloadArguments(DownloadRequest request)
    {
        var arguments = new List<string>
        {
            "--encoding",
            "utf-8",
            "--newline",
            request.DownloadPlaylist ? "--yes-playlist" : "--no-playlist",
            "--windows-filenames",
            "--progress",
            "--progress-template",
            ProgressTemplate,
            "--ffmpeg-location",
            _toolsDirectory,
            "--output",
            BuildOutputTemplate(request)
        };
        AddJavaScriptRuntimeArguments(arguments);

        if (request.Mode == DownloadMode.Mp3Audio)
        {
            var audioFormat = string.IsNullOrWhiteSpace(request.AudioFormatId)
                ? "ba/b"
                : request.AudioFormatId;
            arguments.AddRange([
                "--format", audioFormat,
                "--extract-audio",
                "--audio-format", "mp3",
                "--audio-quality", "0"
            ]);
        }
        else
        {
            var hasSelectedAudio = !string.IsNullOrWhiteSpace(request.AudioFormatId);
            var audioFormat = hasSelectedAudio ? request.AudioFormatId : "ba";
            var format = request.MaximumResolution.Equals("best", StringComparison.OrdinalIgnoreCase)
                ? hasSelectedAudio ? $"bv*+{audioFormat}" : "bv*+ba/b"
                : hasSelectedAudio
                    ? $"bv*[height<={request.MaximumResolution}]+{audioFormat}"
                    : $"bv*[height<={request.MaximumResolution}]+ba/b[height<={request.MaximumResolution}]";
            arguments.AddRange(["--format", format, "--merge-output-format", "mp4"]);
        }

        arguments.Add(request.Url);
        return arguments;
    }

    private static string BuildOutputTemplate(DownloadRequest request) =>
        request.DownloadPlaylist
            ? Path.Combine(
                request.OutputDirectory,
                "%(playlist_title).120B [%(playlist_id)s]",
                "%(playlist_index)03d - %(title).160B [%(id)s].%(ext)s")
            : Path.Combine(request.OutputDirectory, "%(title).180B [%(id)s].%(ext)s");

    private void AddJavaScriptRuntimeArguments(List<string> arguments)
    {
        if (_javaScriptRuntime is null)
        {
            return;
        }

        arguments.AddRange([
            "--js-runtimes", $"{_javaScriptRuntime.Name}:{_javaScriptRuntime.ExecutablePath}"
        ]);
    }

    private async Task<ProcessResult> RunDownloadProcessAsync(
        IReadOnlyList<string> arguments,
        IProgress<DownloadProgress>? progress,
        IProgress<string>? log,
        CancellationToken cancellationToken)
    {
        using var process = CreateProcess(_ytDlpPath, arguments);
        StartAndTrack(process);

        using var cancellationRegistration = cancellationToken.Register(
            static state => TryTerminate((Process)state!),
            process);

        var errors = new StringBuilder();
        var standardOutputTask = PumpReaderAsync(process.StandardOutput, line =>
            HandleDownloadLine(line, progress, log));
        var standardErrorTask = PumpReaderAsync(process.StandardError, line =>
        {
            if (errors.Length < 32_000)
            {
                errors.AppendLine(line);
            }

            HandleDownloadLine(line, progress, log);
        });

        try
        {
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(standardOutputTask, standardErrorTask);
            cancellationToken.ThrowIfCancellationRequested();
            return new ProcessResult(process.ExitCode, string.Empty, errors.ToString());
        }
        finally
        {
            ClearActiveProcess(process);
        }
    }

    private async Task<ProcessResult> RunBufferedProcessAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        using var process = CreateProcess(executablePath, arguments);
        StartAndTrack(process);

        using var cancellationRegistration = cancellationToken.Register(
            static state => TryTerminate((Process)state!),
            process);

        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();

        try
        {
            await process.WaitForExitAsync(CancellationToken.None);
            var standardOutput = await standardOutputTask;
            var standardError = await standardErrorTask;
            cancellationToken.ThrowIfCancellationRequested();
            return new ProcessResult(process.ExitCode, standardOutput, standardError);
        }
        finally
        {
            ClearActiveProcess(process);
        }
    }

    private static Process CreateProcess(string executablePath, IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
        startInfo.Environment["PYTHONUTF8"] = "1";

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return new Process { StartInfo = startInfo };
    }

    private void StartAndTrack(Process process)
    {
        try
        {
            if (!process.Start())
            {
                throw new YtDlpException(YtDlpErrorKind.ToolMissing, "Не удалось запустить yt-dlp.");
            }

            lock (_processSync)
            {
                _activeProcess = process;
            }
        }
        catch (Win32Exception exception)
        {
            throw new YtDlpException(
                YtDlpErrorKind.ToolMissing,
                "Не удалось запустить yt-dlp. Проверьте наличие и целостность исполняемого файла.",
                exception.Message,
                exception);
        }
    }

    private void ClearActiveProcess(Process process)
    {
        lock (_processSync)
        {
            if (ReferenceEquals(_activeProcess, process))
            {
                _activeProcess = null;
            }
        }
    }

    private static async Task PumpReaderAsync(StreamReader reader, Action<string> onLine)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                onLine(line);
            }
        }
    }

    private static void HandleDownloadLine(
        string line,
        IProgress<DownloadProgress>? progress,
        IProgress<string>? log)
    {
        if (YtDlpProgressParser.TryParse(line, out var parsedProgress))
        {
            progress?.Report(parsedProgress);
        }
        else
        {
            log?.Report(line);
        }
    }

    private static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (Win32Exception)
        {
        }
    }

    private void EnsureTools(bool requireMediaTools, bool requireJavaScriptRuntime = true)
    {
        var availability = GetToolAvailability();
        if (!availability.YtDlpExists)
        {
            throw new YtDlpException(
                YtDlpErrorKind.ToolMissing,
                $"Не найден yt-dlp.exe. Ожидаемый путь: {_ytDlpPath}");
        }

        if (requireJavaScriptRuntime && !availability.JavaScriptRuntimeExists)
        {
            throw new YtDlpException(
                YtDlpErrorKind.ToolMissing,
                "Не найдена JavaScript-среда для yt-dlp. Поместите node.exe или deno.exe в папку Tools рядом с приложением.");
        }

        if (requireMediaTools && (!availability.FfmpegExists || !availability.FfprobeExists))
        {
            var missing = new List<string>();
            if (!availability.FfmpegExists)
            {
                missing.Add("ffmpeg.exe");
            }

            if (!availability.FfprobeExists)
            {
                missing.Add("ffprobe.exe");
            }

            throw new YtDlpException(
                YtDlpErrorKind.FfmpegMissing,
                $"Не найдены компоненты обработки медиа: {string.Join(", ", missing)}. Поместите их в папку Tools рядом с приложением.");
        }
    }

    private static void ReportLines(string text, IProgress<string>? log)
    {
        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            log?.Report(line);
        }
    }

    private static YtDlpException CreateProcessException(string details)
    {
        if (ContainsAny(details, "Unsupported URL", "not a valid URL", "Invalid URL"))
        {
            return new YtDlpException(
                YtDlpErrorKind.InvalidUrl,
                "Ссылка неверна или не поддерживается yt-dlp.",
                details);
        }

        if (ContainsAny(
                details,
                "Video unavailable",
                "This video is unavailable",
                "Private video",
                "members-only",
                "Sign in to confirm",
                "login required",
                "Requested content is not available"))
        {
            return new YtDlpException(
                YtDlpErrorKind.VideoUnavailable,
                "Видео недоступно. Возможно, оно удалено, приватное, ограничено по региону или требует входа.",
                details);
        }

        if (IsForbiddenDownloadError(details))
        {
            return new YtDlpException(
                YtDlpErrorKind.Network,
                "Сервер отклонил загрузку (HTTP 403). Обновите yt-dlp и повторите попытку; если ошибка сохраняется, отключите VPN или прокси.",
                details);
        }

        if (ContainsAny(
                details,
                "Unable to download",
                "HTTP Error",
                "timed out",
                "Temporary failure",
                "Connection reset",
                "certificate verify failed",
                "Name or service not known"))
        {
            return new YtDlpException(
                YtDlpErrorKind.Network,
                "Сетевая ошибка. Проверьте подключение к интернету и повторите попытку.",
                details);
        }

        if (ContainsAny(details, "ffmpeg not found", "ffprobe not found", "Postprocessing: ffmpeg"))
        {
            return new YtDlpException(
                YtDlpErrorKind.FfmpegMissing,
                "yt-dlp не смог использовать ffmpeg/ffprobe. Проверьте файлы в папке Tools.",
                details);
        }

        return new YtDlpException(
            YtDlpErrorKind.Unknown,
            "yt-dlp завершился с ошибкой. Подробности доступны в журнале.",
            details);
    }

    internal static bool IsForbiddenDownloadError(string details) =>
        ContainsAny(details, "HTTP Error 403", "HTTP 403: Forbidden");

    private static bool ContainsAny(string source, params string[] values) =>
        values.Any(value => source.Contains(value, StringComparison.OrdinalIgnoreCase));

    private static JavaScriptRuntime? FindJavaScriptRuntime(string toolsDirectory)
    {
        foreach (var runtime in new[]
                 {
                     new JavaScriptRuntime("deno", Path.Combine(toolsDirectory, "deno.exe")),
                     new JavaScriptRuntime("node", Path.Combine(toolsDirectory, "node.exe"))
                 })
        {
            if (File.Exists(runtime.ExecutablePath))
            {
                return runtime;
            }
        }

        var pathDirectories = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var (name, executableName) in new[] { ("deno", "deno.exe"), ("node", "node.exe") })
        {
            foreach (var directory in pathDirectories)
            {
                var executablePath = Path.Combine(directory.Trim('"'), executableName);
                if (File.Exists(executablePath))
                {
                    return new JavaScriptRuntime(name, Path.GetFullPath(executablePath));
                }
            }
        }

        return null;
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
    private sealed record JavaScriptRuntime(string Name, string ExecutablePath);
}
