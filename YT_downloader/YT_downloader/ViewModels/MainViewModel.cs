using System.Windows.Input;
using System.IO;
using YT_downloader.Commands;
using YT_downloader.Models;
using YT_downloader.Services;

namespace YT_downloader.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private const int MaximumLogLength = 40_000;
    private const string EmptyVideoTitle = "Сначала проанализируйте ссылку";
    private static readonly AudioTrackInfo AutomaticAudioTrack = new(
        null,
        null,
        "Автоматически (лучшая)",
        null);

    private readonly IYtDlpService _ytDlpService;
    private readonly ISettingsService _settingsService;
    private readonly IUserInteractionService _userInteractionService;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly AsyncRelayCommand _analyzeCommand;
    private readonly AsyncRelayCommand _downloadCommand;
    private readonly AsyncRelayCommand _updateCommand;
    private readonly RelayCommand _pasteCommand;
    private readonly RelayCommand _selectFolderCommand;
    private readonly RelayCommand _cancelCommand;
    private CancellationTokenSource? _operationCancellation;
    private string _url = string.Empty;
    private string _videoTitle = EmptyVideoTitle;
    private string? _thumbnailUrl;
    private string? _durationText;
    private string? _analyzedUrl;
    private string _saveFolder;
    private DownloadMode _selectedMode;
    private string _selectedResolution;
    private IReadOnlyList<AudioTrackInfo> _audioTrackOptions = [AutomaticAudioTrack];
    private AudioTrackInfo _selectedAudioTrack = AutomaticAudioTrack;
    private double _progressValue;
    private string _speed = "—";
    private string _fileSize = "—";
    private string _remainingTime = "—";
    private string _statusText = "Готово к работе";
    private string _toolStatusText = string.Empty;
    private string _logText = string.Empty;
    private bool _isBusy;

    public MainViewModel(
        IYtDlpService ytDlpService,
        ISettingsService settingsService,
        IUserInteractionService userInteractionService)
    {
        _ytDlpService = ytDlpService;
        _settingsService = settingsService;
        _userInteractionService = userInteractionService;

        var settings = _settingsService.Load();
        _saveFolder = ResolveInitialFolder(settings.SaveFolder);
        _selectedMode = Enum.IsDefined(settings.Mode) ? settings.Mode : DownloadMode.Mp4Video;
        _selectedResolution = ResolutionOptions.Any(item => item.Value == settings.MaximumResolution)
            ? settings.MaximumResolution
            : "best";

        _pasteCommand = new RelayCommand(PasteUrl, () => !IsBusy);
        _selectFolderCommand = new RelayCommand(SelectFolder, () => !IsBusy);
        _cancelCommand = new RelayCommand(Cancel, () => IsBusy);
        _analyzeCommand = new AsyncRelayCommand(AnalyzeAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(Url));
        _downloadCommand = new AsyncRelayCommand(
            DownloadAsync,
            () => !IsBusy && _analyzedUrl is not null && string.Equals(_analyzedUrl, Url, StringComparison.Ordinal));
        _updateCommand = new AsyncRelayCommand(UpdateYtDlpAsync, () => !IsBusy);

        RefreshToolStatus();
        Log("Приложение запущено. Загружайте только материалы, на которые у вас есть права доступа.");
    }

    public IReadOnlyList<ChoiceItem<DownloadMode>> ModeOptions { get; } =
    [
        new("MP4 — видео", DownloadMode.Mp4Video),
        new("MP3 — аудио", DownloadMode.Mp3Audio)
    ];

    public IReadOnlyList<ChoiceItem<string>> ResolutionOptions { get; } =
    [
        new("Лучшее доступное", "best"),
        new("До 2160p", "2160"),
        new("До 1440p", "1440"),
        new("До 1080p", "1080"),
        new("До 720p", "720"),
        new("До 480p", "480")
    ];

    public string Url
    {
        get => _url;
        set
        {
            var normalized = value?.Trim() ?? string.Empty;
            if (!SetProperty(ref _url, normalized))
            {
                return;
            }

            if (!string.Equals(_analyzedUrl, normalized, StringComparison.Ordinal))
            {
                _analyzedUrl = null;
                VideoTitle = EmptyVideoTitle;
                ThumbnailUrl = null;
                DurationText = null;
                SetAudioTracks([]);
            }

            RaiseCommandStates();
        }
    }

    public string VideoTitle
    {
        get => _videoTitle;
        private set => SetProperty(ref _videoTitle, value);
    }

    public string? ThumbnailUrl
    {
        get => _thumbnailUrl;
        private set => SetProperty(ref _thumbnailUrl, value);
    }

    public string? DurationText
    {
        get => _durationText;
        private set => SetProperty(ref _durationText, value);
    }

    public string SaveFolder
    {
        get => _saveFolder;
        private set
        {
            if (SetProperty(ref _saveFolder, value))
            {
                PersistSettings();
            }
        }
    }

    public DownloadMode SelectedMode
    {
        get => _selectedMode;
        set
        {
            if (SetProperty(ref _selectedMode, value))
            {
                PersistSettings();
            }
        }
    }

    public string SelectedResolution
    {
        get => _selectedResolution;
        set
        {
            if (SetProperty(ref _selectedResolution, value))
            {
                PersistSettings();
            }
        }
    }

    public IReadOnlyList<AudioTrackInfo> AudioTrackOptions
    {
        get => _audioTrackOptions;
        private set => SetProperty(ref _audioTrackOptions, value);
    }

    public AudioTrackInfo SelectedAudioTrack
    {
        get => _selectedAudioTrack;
        set => SetProperty(ref _selectedAudioTrack, value ?? AutomaticAudioTrack);
    }

    public double ProgressValue
    {
        get => _progressValue;
        private set => SetProperty(ref _progressValue, Math.Clamp(value, 0, 100));
    }

    public string Speed
    {
        get => _speed;
        private set => SetProperty(ref _speed, value);
    }

    public string FileSize
    {
        get => _fileSize;
        private set => SetProperty(ref _fileSize, value);
    }

    public string RemainingTime
    {
        get => _remainingTime;
        private set => SetProperty(ref _remainingTime, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string ToolStatusText
    {
        get => _toolStatusText;
        private set => SetProperty(ref _toolStatusText, value);
    }

    public string LogText
    {
        get => _logText;
        private set => SetProperty(ref _logText, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public ICommand PasteCommand => _pasteCommand;

    public ICommand SelectFolderCommand => _selectFolderCommand;

    public ICommand AnalyzeCommand => _analyzeCommand;

    public ICommand DownloadCommand => _downloadCommand;

    public ICommand CancelCommand => _cancelCommand;

    public ICommand UpdateCommand => _updateCommand;

    public void Shutdown()
    {
        _operationCancellation?.Cancel();
        _lifetimeCancellation.Cancel();
    }

    private void PasteUrl()
    {
        try
        {
            var clipboardText = _userInteractionService.GetClipboardText();
            if (string.IsNullOrWhiteSpace(clipboardText))
            {
                StatusText = "В буфере обмена нет текста";
                return;
            }

            Url = clipboardText;
            StatusText = "Ссылка вставлена";
        }
        catch (Exception exception)
        {
            Log($"Не удалось прочитать буфер обмена: {exception.Message}");
            _userInteractionService.ShowError("Буфер обмена", "Не удалось прочитать текст из буфера обмена.");
        }
    }

    private void SelectFolder()
    {
        try
        {
            var folder = _userInteractionService.SelectFolder(SaveFolder);
            if (!string.IsNullOrWhiteSpace(folder))
            {
                SaveFolder = folder;
                StatusText = "Папка сохранения выбрана";
            }
        }
        catch (Exception exception)
        {
            Log($"Не удалось выбрать папку: {exception.Message}");
            _userInteractionService.ShowError("Выбор папки", "Не удалось открыть диалог выбора папки.");
        }
    }

    private async Task AnalyzeAsync()
    {
        if (!TryValidateUrl(Url, out var validationMessage))
        {
            StatusText = validationMessage;
            _userInteractionService.ShowError("Неверная ссылка", validationMessage);
            return;
        }

        await ExecuteOperationAsync(async cancellationToken =>
        {
            StatusText = "Анализ ссылки…";
            Log("Запущен анализ ссылки.");
            var logProgress = new Progress<string>(Log);
            var videoInfo = await _ytDlpService.AnalyzeAsync(Url, logProgress, cancellationToken);

            VideoTitle = videoInfo.Title;
            ThumbnailUrl = videoInfo.ThumbnailUrl;
            DurationText = videoInfo.DurationText;
            SetAudioTracks(videoInfo.AudioTracks);
            _analyzedUrl = Url;
            StatusText = "Ссылка успешно проанализирована";
            Log($"Найдено видео: {videoInfo.Title}");
            Log(videoInfo.AudioTracks.Count > 0
                ? $"Доступно аудиодорожек: {videoInfo.AudioTracks.Count}."
                : "Отдельные аудиодорожки не указаны; будет использован автоматический выбор.");
            RaiseCommandStates();
        });
    }

    private async Task DownloadAsync()
    {
        if (_analyzedUrl is null || !string.Equals(_analyzedUrl, Url, StringComparison.Ordinal))
        {
            _userInteractionService.ShowError("Требуется анализ", "Сначала проанализируйте текущую ссылку.");
            return;
        }

        await ExecuteOperationAsync(async cancellationToken =>
        {
            Directory.CreateDirectory(SaveFolder);
            ResetProgress();
            StatusText = "Подготовка загрузки…";
            Log($"Загрузка начата. Папка: {SaveFolder}");

            var selectedAudioFormatId = SelectedMode == DownloadMode.Mp4Video
                ? SelectedAudioTrack.Mp4FormatId ?? SelectedAudioTrack.BestFormatId
                : SelectedAudioTrack.BestFormatId;
            var request = new DownloadRequest(
                Url,
                SaveFolder,
                SelectedMode,
                SelectedResolution,
                selectedAudioFormatId);
            Log($"Аудиодорожка: {SelectedAudioTrack.DisplayName}.");
            var progress = new Progress<DownloadProgress>(ApplyProgress);
            var logProgress = new Progress<string>(Log);
            await _ytDlpService.DownloadAsync(request, progress, logProgress, cancellationToken);

            ProgressValue = 100;
            RemainingTime = "Готово";
            StatusText = "Загрузка завершена";
            Log("Загрузка и обработка файла успешно завершены.");
            _userInteractionService.ShowInformation(
                "Готово",
                $"Файл сохранён в папку:{Environment.NewLine}{SaveFolder}");
        });
    }

    private async Task UpdateYtDlpAsync()
    {
        await ExecuteOperationAsync(async cancellationToken =>
        {
            StatusText = "Обновление yt-dlp…";
            Log("Проверка обновлений yt-dlp.");
            var result = await _ytDlpService.UpdateAsync(new Progress<string>(Log), cancellationToken);
            RefreshToolStatus();
            StatusText = "yt-dlp обновлён";
            _userInteractionService.ShowInformation("Обновление yt-dlp", result);
        });
    }

    private async Task ExecuteOperationAsync(Func<CancellationToken, Task> operation)
    {
        if (IsBusy)
        {
            return;
        }

        _operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        IsBusy = true;

        try
        {
            await operation(_operationCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Операция отменена";
            Log("Операция отменена пользователем. Дочерний процесс завершён.");
        }
        catch (YtDlpException exception)
        {
            StatusText = exception.Message;
            Log(exception.Message);
            if (!string.IsNullOrWhiteSpace(exception.Details))
            {
                Log(exception.Details);
            }

            _userInteractionService.ShowError("Ошибка yt-dlp", exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            const string message = "Нет доступа к выбранной папке. Выберите другую папку сохранения.";
            StatusText = message;
            Log($"{message} {exception.Message}");
            _userInteractionService.ShowError("Нет доступа", message);
        }
        catch (IOException exception)
        {
            const string message = "Ошибка работы с файлами. Проверьте свободное место и выбранную папку.";
            StatusText = message;
            Log($"{message} {exception.Message}");
            _userInteractionService.ShowError("Ошибка файловой системы", message);
        }
        catch (Exception exception)
        {
            const string message = "Произошла непредвиденная ошибка. Подробности добавлены в журнал.";
            StatusText = message;
            Log($"{message} {exception}");
            _userInteractionService.ShowError("Ошибка", message);
        }
        finally
        {
            _operationCancellation.Dispose();
            _operationCancellation = null;
            IsBusy = false;
        }
    }

    private void Cancel()
    {
        if (_operationCancellation is null || _operationCancellation.IsCancellationRequested)
        {
            return;
        }

        StatusText = "Отмена операции…";
        Log("Запрошена отмена операции.");
        _operationCancellation.Cancel();
        _cancelCommand.NotifyCanExecuteChanged();
    }

    private void ApplyProgress(DownloadProgress progress)
    {
        if (progress.Percent.HasValue)
        {
            ProgressValue = progress.Percent.Value;
        }

        Speed = progress.Speed;
        FileSize = progress.FileSize;
        RemainingTime = progress.RemainingTime;
        StatusText = $"{progress.Status}: {ProgressValue:0.0}%";
    }

    private void ResetProgress()
    {
        ProgressValue = 0;
        Speed = "—";
        FileSize = "—";
        RemainingTime = "—";
    }

    private void SetAudioTracks(IReadOnlyList<AudioTrackInfo> tracks)
    {
        AudioTrackOptions = [AutomaticAudioTrack, .. tracks];
        SelectedAudioTrack = AutomaticAudioTrack;
    }

    private void RefreshToolStatus()
    {
        var tools = _ytDlpService.GetToolAvailability();
        ToolStatusText = tools.AllAvailable
            ? "yt-dlp, ffmpeg, ffprobe и JavaScript-среда готовы"
            : $"Не найдены: {string.Join(", ", GetMissingToolNames(tools))}";
    }

    private static IEnumerable<string> GetMissingToolNames(ToolAvailability tools)
    {
        if (!tools.YtDlpExists)
        {
            yield return "yt-dlp.exe";
        }

        if (!tools.FfmpegExists)
        {
            yield return "ffmpeg.exe";
        }

        if (!tools.FfprobeExists)
        {
            yield return "ffprobe.exe";
        }

        if (!tools.JavaScriptRuntimeExists)
        {
            yield return "node.exe/deno.exe";
        }
    }

    private void Log(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var entry = $"[{DateTime.Now:HH:mm:ss}] {message.Trim()}{Environment.NewLine}";
        var combined = LogText + entry;
        LogText = combined.Length <= MaximumLogLength
            ? combined
            : combined[^MaximumLogLength..];
    }

    private void PersistSettings()
    {
        try
        {
            _settingsService.Save(new AppSettings
            {
                SaveFolder = SaveFolder,
                Mode = SelectedMode,
                MaximumResolution = SelectedResolution
            });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log($"Не удалось сохранить настройки: {exception.Message}");
        }
    }

    private void RaiseCommandStates()
    {
        _pasteCommand?.NotifyCanExecuteChanged();
        _selectFolderCommand?.NotifyCanExecuteChanged();
        _cancelCommand?.NotifyCanExecuteChanged();
        _analyzeCommand?.NotifyCanExecuteChanged();
        _downloadCommand?.NotifyCanExecuteChanged();
        _updateCommand?.NotifyCanExecuteChanged();
    }

    private static bool TryValidateUrl(string url, out string message)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            message = "Введите ссылку на видео.";
            return false;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            message = "Введите корректную ссылку, начинающуюся с http:// или https://.";
            return false;
        }

        message = string.Empty;
        return true;
    }

    private static string ResolveInitialFolder(string configuredFolder)
    {
        if (!string.IsNullOrWhiteSpace(configuredFolder))
        {
            return configuredFolder;
        }

        var downloads = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");
        return downloads;
    }
}
