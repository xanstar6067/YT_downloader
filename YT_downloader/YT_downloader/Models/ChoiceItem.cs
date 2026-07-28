namespace YT_downloader.Models;

public sealed record ChoiceItem<T>(string DisplayName, T Value)
{
    public override string ToString() => DisplayName;
}
