using System.Windows;

namespace YT_downloader.Services;

public sealed class WpfThemeService : IThemeService
{
    private const string DarkThemePath = "/Themes/DarkTheme.xaml";
    private const string LightThemePath = "/Themes/LightTheme.xaml";

    public void ApplyTheme(bool isLightTheme)
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var themeDictionary = new ResourceDictionary
        {
            Source = new Uri(isLightTheme ? LightThemePath : DarkThemePath, UriKind.Relative)
        };

        for (var index = 0; index < dictionaries.Count; index++)
        {
            var source = dictionaries[index].Source?.OriginalString;
            if (source?.Contains("/Themes/", StringComparison.OrdinalIgnoreCase) == true)
            {
                dictionaries[index] = themeDictionary;
                return;
            }
        }

        dictionaries.Insert(0, themeDictionary);
    }
}
