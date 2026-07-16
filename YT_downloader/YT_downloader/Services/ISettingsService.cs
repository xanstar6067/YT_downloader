using YT_downloader.Models;

namespace YT_downloader.Services;

public interface ISettingsService
{
    AppSettings Load();

    void Save(AppSettings settings);
}
