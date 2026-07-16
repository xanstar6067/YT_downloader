using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;
using YT_downloader.Models;

namespace YT_downloader.Services;

public sealed class JsonSettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _settingsPath;

    public JsonSettingsService()
    {
        var settingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "YtDlpDownloader");
        _settingsPath = Path.Combine(settingsDirectory, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions) ?? new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
        catch (IOException)
        {
            return new AppSettings();
        }
        catch (UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(_settingsPath)
            ?? throw new InvalidOperationException("Не удалось определить папку настроек.");
        Directory.CreateDirectory(directory);

        var temporaryPath = _settingsPath + ".tmp";
        var json = JsonSerializer.Serialize(settings, SerializerOptions);
        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, _settingsPath, true);
    }
}
