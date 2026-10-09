namespace YT_downloader.Models;

// Screen coordinates in physical pixels, independent of WPF's device-independent units.
public sealed record WindowPlacement(int Left, int Top, int Width, int Height, bool IsMaximized);
