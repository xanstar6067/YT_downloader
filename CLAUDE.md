# CLAUDE.md — карта проекта YT Downloader

Этот файл — быстрый ориентир по кодовой базе для ускорения поиска и анализа. Общее описание
возможностей, требований и инструкций по сборке/публикации — в [README.md](README.md), там же
таблица сторонних инструментов (`Tools/yt-dlp.exe`, `ffmpeg.exe`, `ffprobe.exe`, `node.exe`).

## Что это

WPF-приложение (.NET 10, Windows) — GUI-обёртка над `yt-dlp.exe`. Анализирует ссылку (видео или
плейлист), показывает метаданные, скачивает через `yt-dlp` + `ffmpeg`/`ffprobe`, парсит прогресс
из `--progress-template` в реальном времени. Без DI-контейнера — сервисы создаются и связываются
вручную в `App.xaml.cs`.

## Структура решения

```
YT_downloader/                         ← корень .sln (YT_downloader.slnx)
├── YT_downloader/                     ← WPF-приложение (проект YtDlpDownloader.dll)
│   ├── App.xaml.cs                    ← composition root: создаёт все сервисы и MainViewModel
│   ├── Views/MainWindow.xaml(.cs)     ← единственное окно, MVVM-биндинг и логика компоновки
│   ├── ViewModels/
│   │   ├── ViewModelBase.cs           ← INotifyPropertyChanged + SetProperty helper
│   │   └── MainViewModel.cs           ← всё состояние и сценарии интерфейса (самый большой файл)
│   ├── Models/                        ← record-модели (см. ниже)
│   ├── Services/                      ← вся логика работы с yt-dlp, настройками, ОС-диалогами
│   ├── Commands/                      ← RelayCommand, AsyncRelayCommand (ICommand для MVVM)
│   └── Tools/                         ← *.exe бинарники (не в Git, см. README «Подготовка Tools»)
└── YT_downloader.Tests/               ← MSTest-юниты для парсеров и построения аргументов
```

## Модели (`Models/`)

- `DownloadRequest` — входные параметры скачивания (url, папка, режим, разрешение, аудиоформат,
  playlist-флаг, cookies-браузер, **`ConcurrentFragments`**). Передаётся в `YtDlpService.DownloadAsync`.
- `DownloadProgress` — один «тик» прогресса, разобранный из строки yt-dlp (percent, speed, size,
  eta, playlist index/count, format/media id, кодеки, номер и общее число фрагментов).
- `VideoInfo` — результат анализа: заголовок, миниатюра, длительность, список аудиодорожек,
  признак плейлиста и число элементов.
- `AudioTrackInfo`, `BrowserCookieSource`, `ChoiceItem<T>` (пара displayName/value для комбобоксов),
  `DownloadMode` (Mp4Video/Mp3Audio), `AppSettings` (персистентные настройки пользователя),
  `ToolAvailability` (какие из 4 инструментов найдены на диске).
- `WindowPlacement` — размер и координаты обычного окна в физических пикселях экрана,
  плюс признак развёрнутого состояния; хранится в `AppSettings.WindowPlacement`.

## Сервисы (`Services/`)

- **`YtDlpService`** (реализует `IYtDlpService`) — ядро приложения:
  - `BuildAnalyzeArguments` / `BuildDownloadArguments` — сборка `ArgumentList` для yt-dlp
    (важно: аргументы передаются списком, `cmd.exe` не используется, инъекция исключена).
  - `NormalizeYouTubeUrl` — конвертирует `youtube.com/show/VL<id>` в `playlist?list=<id>`.
  - `AnalyzeAsync` — буферизованный запуск с `--dump-single-json`; для плейлиста добавляется
    `--flat-playlist` (быстро, без разбора форматов каждого видео).
  - `DownloadAsync` — стриминговый запуск с построчным парсингом stdout/stderr; при HTTP 403
    (истёкшая подписанная ссылка) один раз перезапускает весь процесс целиком
    (`MaximumDownloadAttempts = 2`), см. `IsForbiddenDownloadError`.
  - `--concurrent-fragments N` добавляется только если `request.ConcurrentFragments > 1`
    (иначе аргумент не передаётся вовсе — поведение yt-dlp по умолчанию).
  - `EnsureTools` — проверка наличия yt-dlp/ffmpeg/ffprobe/JS-рантайма перед операцией.
  - `_operationGate` (SemaphoreSlim) не даёт двум операциям использовать процесс одновременно;
    `_downloadRunning` (Interlocked) — отдельная защита от параллельных загрузок с понятной
    ошибкой `YtDlpErrorKind.AlreadyRunning`.
- **`YtDlpProgressParser`** — чистый парсер строк вывода. Два формата: «человеческий»
  (`[download] NN% of ...`) и машинный (`download:` + `ProgressTemplate` из `YtDlpService`,
  12/14 полей в зависимости от того, есть ли данные о фрагментах).
- **`YtDlpMetadataParser`** — парсит `--dump-single-json`: определяет плейлист по `_type` или
  наличию `entries`, собирает список аудиодорожек (группировка по языку, выбор лучшего битрейта,
  отдельно — лучший MP4-совместимый вариант для режима видео).
- **`DownloadProgressAggregator`** — сглаживает прогресс: держит процент монотонным (не даёт
  шкале прыгать назад), использует позицию фрагмента вместо «сырого» процента, где возможно.
  Для `Mp4Video` с раздельными потоками (video+audio) делит шкалу на 2 стадии по 50%
  (`_fallbackFormatOrder`) — отсюда в интерфейсе виден переход «видео» → «аудио».
- **`JsonSettingsService`** — читает/пишет `%LOCALAPPDATA%\YtDlpDownloader\settings.json`
  (atomic write через `.tmp` + `File.Move`). Любое новое поле `AppSettings` не ломает старые
  файлы (JSON-десериализация даёт значения по умолчанию).
- **`WindowPlacementService`** — восстанавливает и сохраняет геометрию окна, ограничивает её
  рабочей областью ближайшего монитора с учётом DPI и панели задач. Запись после перемещения/
  изменения размера откладывается на 500 мс, при закрытии выполняется сразу. Свёрнутое состояние
  не заменяет сохранённые обычные размеры или предыдущее развёрнутое состояние.
- **`WpfThemeService`**, **`WpfUserInteractionService`** — тонкие обёртки над WPF/Win32
  (смена темы через `Application.Resources`, диалоги папки/сообщений, буфер обмена).
- **`YtDlpException`** + `YtDlpErrorKind` — типизированные ошибки для UI; `CreateProcessException`
  в `YtDlpService` классифицирует сырой stderr yt-dlp по ключевым фразам (403, CAPTCHA,
  video unavailable, сетевые ошибки, отсутствие ffmpeg и т.д.).

## ViewModels / Views

- `MainViewModel` — единственная VM. Основные сценарии: `AnalyzeAsync`, `DownloadAsync`,
  `UpdateYtDlpAsync`, всё через `ExecuteOperationAsync` (общий try/catch → понятные сообщения,
  `IsBusy`, отмена через `CancellationTokenSource`). Опции комбобоксов (`ResolutionOptions`,
  `BrowserCookieOptions`, `ConcurrentFragmentsOptions`, `ModeOptions`) — статические списки
  `ChoiceItem<T>`, объявленные прямо в VM.
- Настройки персистятся через `PersistSettings()` при каждом изменении соответствующего свойства
  (папка, режим, разрешение, cookies-браузер, `ConcurrentFragments`, playlist-флаг, тема).
  `SaveWindowPlacement` сохраняет геометрию через тот же путь; `PersistSettings` сохраняет
  её вместе с остальными настройками, чтобы изменение опций не сбрасывало положение окна.
- `MainWindow.xaml` — биндинг сценариев к VM; ссылка, карточки и плавающие кнопки скачивания/
  отмены поверх обложки помещены в `ScrollViewer`. Флажок плейлиста находится в строке ссылки.
  Прогресс расположен под карточками, журнал — внизу без разделителя. В code-behind карточки
  перестраиваются в один столбец при ширине области параметров менее 900 логических единиц;
  высота прокрутки ограничивается свободным местом, остальное получает журнал. Начальный
  размер — 1100×740. Карточка «Параметры сохранения» — основное место для новых опций.

## Тесты (`YT_downloader.Tests/`)

MSTest — парсеры, аргументы, геометрия окна и WPF-компоновка:
- `YtDlpServiceArgumentTests` — `BuildAnalyzeArguments`/`BuildDownloadArguments`/`NormalizeYouTubeUrl`/
  детекторы ошибок (403, bot-verification). При добавлении нового флага yt-dlp — добавляй сюда тест
  на его наличие/отсутствие по условию (см. `ConcurrentFragments_AreOnlyAddedWhenGreaterThanOne`).
- `YtDlpProgressParserTests`, `YtDlpMetadataParserTests`, `DownloadProgressAggregatorTests`,
  `DisplayModelTests`.
- `WindowPlacementTests` — ограничение геометрии рабочей областью, DPI, отключённый монитор,
  отрицательные координаты второго монитора и совместимость JSON-настроек.
- `WindowLayoutTests` — компоновка с реальной VM при масштабе 100–200%, доступность параметров
  через прокрутку, сохранение геометрии при изменении опций, восстановление скрытых нативных
  окон и закрытие в свёрнутом состоянии. PNG-превью генерируются в `bin/.../layout-previews`.

Запуск: `dotnet test .\YT_downloader\YT_downloader.slnx`.

## Частые точки изменений и на что обратить внимание

- **Новая опция скачивания, видимая пользователю** (как `ConcurrentFragments`) обычно требует
  правки в 5 местах: `DownloadRequest` → `YtDlpService.BuildDownloadArguments` →
  `AppSettings` → `MainViewModel` (список опций + свойство + передача в `DownloadRequest` +
  запись в `PersistSettings`) → `MainWindow.xaml` (ComboBox). Плюс тест в
  `YtDlpServiceArgumentTests`.
- **Прогресс-шаблон** (`ProgressTemplate` в `YtDlpService`) и его порядок полей жёстко завязаны
  на порядковый парсинг в `YtDlpProgressParser.TryParseTemplate`/`TryParseDetailedTemplate` —
  менять их можно только синхронно.
- **`Tools/*.exe` не в Git** (исключены правилом `*.exe`) — при локальной разработке их нужно
  положить вручную (см. README «Подготовка каталога Tools»), иначе `EnsureTools` бросит
  `YtDlpErrorKind.ToolMissing`/`FfmpegMissing`.
- Скачивание всего плейлиста — по явному флажку `DownloadPlaylist`; по умолчанию ссылка на
  плейлист внутри видео обрабатывается как одно видео (`--no-playlist`).
- Приложение никогда не строит команду как строку и не вызывает `cmd.exe` — аргументы всегда
  через `ProcessStartInfo.ArgumentList`. Сохраняй этот инвариант при любых изменениях в
  `YtDlpService`.

---

## Постоянное правило: синхронизация CLAUDE.md и AGENTS.md

`AGENTS.md` в корне репозитория — полный дубликат этого файла (тот же формат, тот же контент).
**Любое изменение этого файла должно сразу же зеркалироваться в `AGENTS.md`, и наоборот.**
Если найдено расхождение между файлами — считать это ошибкой, актуальным источником считать более
свежий по содержанию (не по дате модификации) и синхронизировать оба файла в тот же коммит/правку.
