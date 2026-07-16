# YT Downloader

Настольное WPF-приложение для Windows, которое скачивает доступные пользователю видео через `yt-dlp`. Для объединения потоков и конвертации аудио используются `ffmpeg` и `ffprobe`.

> Используйте приложение только для материалов, на скачивание которых у вас есть разрешение правообладателя или иное законное право.

## Возможности

- анализ ссылки с показом названия, длительности и миниатюры;
- выбор доступной аудиодорожки/языка после анализа ссылки;
- загрузка MP4-видео или извлечение MP3-аудио;
- ограничение качества: лучшее, 2160p, 1440p, 1080p, 720p или 480p;
- прогресс, скорость, размер, оставшееся время и журнал yt-dlp;
- отмена с завершением всего дерева дочернего процесса;
- обновление автономного `yt-dlp.exe` кнопкой в интерфейсе;
- сохранение последней папки, режима и выбранного качества в `%LOCALAPPDATA%\YtDlpDownloader\settings.json`;
- защита от одновременного запуска нескольких скачиваний.

## Требования для сборки

- Windows 10/11 x64;
- .NET 10 SDK;
- Visual Studio с поддержкой WPF либо командная строка `dotnet`;
- доступ к NuGet при первом восстановлении пакетов тестового проекта.

Исполняемые файлы `yt-dlp.exe`, `ffmpeg.exe` и `ffprobe.exe` уже находятся в `YT_downloader/Tools` и копируются в выходной каталог обычной сборки. `ffplay.exe` приложению не требуется.

## Сборка и запуск

Из папки с файлом `YT_downloader.slnx`:

```powershell
dotnet restore YT_downloader.slnx
dotnet build YT_downloader.slnx -c Release
dotnet run --project .\YT_downloader\YT_downloader.csproj
```

Запуск тестов:

```powershell
dotnet test YT_downloader.slnx -c Release
```

## Публикация self-contained для Windows x64

Профиль `Properties/PublishProfiles/win-x64.pubxml` создаёт self-contained single-file приложение. В него включены .NET 10 Runtime и компоненты yt-dlp/ffmpeg/ffprobe, поэтому на другом компьютере с Windows x64 не требуется устанавливать .NET SDK или Runtime. Служебные компоненты извлекаются механизмом .NET при запуске.

```powershell
dotnet publish .\YT_downloader\YT_downloader.csproj -c Release -p:PublishProfile=win-x64
```

Результат появится в `YT_downloader/bin/Release/net10.0-windows/win-x64/publish/`.

Эквивалентная команда без профиля:

```powershell
dotnet publish .\YT_downloader\YT_downloader.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -p:EnableCompressionInSingleFile=true
```

## Структура

- `Views` — WPF-окна;
- `ViewModels` — состояние интерфейса и сценарии пользователя;
- `Models` — модели видео, загрузки, прогресса и настроек;
- `Services` — запуск yt-dlp, разбор вывода, настройки и системные диалоги;
- `Commands` — синхронные и асинхронные MVVM-команды;
- `Tools` — автономные yt-dlp, ffmpeg и ffprobe;
- `YT_downloader.Tests` — unit-тесты парсера прогресса.

## Примечания

- Приложение не запускает `cmd.exe`: `yt-dlp.exe` создаётся напрямую через `System.Diagnostics.Process`.
- Все аргументы добавляются через `ProcessStartInfo.ArgumentList`, поэтому ссылки и пути не объединяются в командную строку вручную.
- При отмене может остаться файл `.part`; yt-dlp обычно использует его для продолжения последующей загрузки.
- Для закрытых видео, требующих авторизации, в текущей версии не предусмотрен импорт cookies.
