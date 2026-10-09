using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using YT_downloader.Models;

namespace YT_downloader.Services;

public sealed class WindowPlacementService
{
    private readonly Window _window;
    private readonly Action<WindowPlacement> _save;
    private readonly DispatcherTimer _saveTimer;
    private WindowPlacement? _placement;
    private nint _handle;
    private bool _restoring = true;

    public WindowPlacementService(Window window, WindowPlacement? placement, Action<WindowPlacement> save)
    {
        _window = window;
        _placement = placement is { Width: > 0, Height: > 0 } ? placement : null;
        _save = save;
        _saveTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background,
            (_, _) => Save(), window.Dispatcher);
        _saveTimer.Stop();
        window.SourceInitialized += OnSourceInitialized;
        window.Loaded += OnLoaded;
        window.LocationChanged += OnWindowChanged;
        window.SizeChanged += OnWindowChanged;
        window.StateChanged += OnWindowChanged;
        window.DpiChanged += OnWindowChanged;
        window.Closed += OnClosed;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _handle = new WindowInteropHelper(_window).Handle;
        var workArea = GetWorkArea(_placement);
        var scale = GetScale();
        _placement = FitToWorkArea(_placement, workArea, scale);
        // Move to the saved monitor first so WPF receives its DPI before applying constraints.
        _window.MinWidth = 1;
        _window.MinHeight = 1;
        ApplyBounds(_placement);
        UpdateLimits();
        _placement = FitToWorkArea(_placement, GetWorkArea(_placement), GetScale());
        ApplyBounds(_placement);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Show() can apply WPF's initial placement after SourceInitialized.
        ApplyBounds(_placement!);
        UpdateLimits();
        if (_placement!.IsMaximized)
        {
            _window.MaxWidth = double.PositiveInfinity;
            _window.MaxHeight = double.PositiveInfinity;
            _window.WindowState = WindowState.Maximized;
        }

        _restoring = false;
        Capture();
    }

    private void OnWindowChanged(object? sender, EventArgs e)
    {
        if (_restoring || _handle == 0)
        {
            return;
        }

        UpdateLimits();
        Capture();
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void UpdateLimits()
    {
        // Windows includes invisible frame borders in maximized bounds. Let it calculate those
        // itself; normal-window limits must not shrink the maximized client area.
        if (_window.WindowState != WindowState.Normal || IsZoomed(_handle) || IsIconic(_handle))
        {
            _window.MaxWidth = double.PositiveInfinity;
            _window.MaxHeight = double.PositiveInfinity;
            return;
        }

        var workArea = GetWorkArea(null);
        var scale = GetScale();
        _window.MinWidth = Math.Min(760, workArea.Width / scale);
        _window.MinHeight = Math.Min(480, workArea.Height / scale);
        _window.MaxWidth = workArea.Width / scale;
        _window.MaxHeight = workArea.Height / scale;
    }

    private void Capture()
    {
        // Native state guards against transitional SizeChanged events during maximize/minimize.
        if (_window.WindowState == WindowState.Normal && !IsZoomed(_handle) && !IsIconic(_handle)
            && GetWindowRect(_handle, out var bounds))
        {
            _placement = new WindowPlacement(bounds.Left, bounds.Top,
                bounds.Right - bounds.Left, bounds.Bottom - bounds.Top, false);
        }
        else if (_placement is not null && _window.WindowState != WindowState.Minimized)
        {
            _placement = _placement with { IsMaximized = _window.WindowState == WindowState.Maximized };
        }
    }

    private void Save()
    {
        _saveTimer.Stop();
        if (_placement is not null)
        {
            _save(_placement);
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        Save();
        _window.SourceInitialized -= OnSourceInitialized;
        _window.Loaded -= OnLoaded;
        _window.LocationChanged -= OnWindowChanged;
        _window.SizeChanged -= OnWindowChanged;
        _window.StateChanged -= OnWindowChanged;
        _window.DpiChanged -= OnWindowChanged;
        _window.Closed -= OnClosed;
    }

    private double GetScale() => Math.Max(96, GetDpiForWindow(_handle)) / 96.0;

    private Rect GetWorkArea(WindowPlacement? placement)
    {
        nint monitor;
        if (placement is not null)
        {
            var bounds = new NativeRect
            {
                Left = placement.Left,
                Top = placement.Top,
                Right = (int)Math.Clamp((long)placement.Left + placement.Width, int.MinValue, int.MaxValue),
                Bottom = (int)Math.Clamp((long)placement.Top + placement.Height, int.MinValue, int.MaxValue)
            };
            monitor = MonitorFromRect(ref bounds, 2); // MONITOR_DEFAULTTONEAREST
        }
        else
        {
            monitor = MonitorFromWindow(_handle, 2);
        }

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
        {
            var fallback = SystemParameters.WorkArea;
            var scale = GetScale();
            return new Rect(fallback.X * scale, fallback.Y * scale, fallback.Width * scale, fallback.Height * scale);
        }

        return new Rect(info.Work.Left, info.Work.Top,
            info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top);
    }

    internal static WindowPlacement FitToWorkArea(WindowPlacement? saved, Rect workArea, double scale)
    {
        var width = Math.Clamp(saved?.Width ?? 1100 * scale, Math.Min(760 * scale, workArea.Width), workArea.Width);
        var height = Math.Clamp(saved?.Height ?? 740 * scale, Math.Min(480 * scale, workArea.Height), workArea.Height);
        var left = Math.Clamp(saved?.Left ?? workArea.Left + (workArea.Width - width) / 2, workArea.Left, workArea.Right - width);
        var top = Math.Clamp(saved?.Top ?? workArea.Top + (workArea.Height - height) / 2, workArea.Top, workArea.Bottom - height);
        return new WindowPlacement((int)Math.Round(left), (int)Math.Round(top),
            (int)Math.Round(width), (int)Math.Round(height), saved?.IsMaximized ?? false);
    }

    private void ApplyBounds(WindowPlacement placement) =>
        SetWindowPos(_handle, 0, placement.Left, placement.Top, placement.Width, placement.Height, 0x0014); // NOZORDER | NOACTIVATE

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }

    [DllImport("user32.dll")]
    private static extern nint MonitorFromRect(ref NativeRect rect, uint flags);
    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint handle, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint handle);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint handle, out NativeRect rect);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint handle, nint insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(nint handle);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint handle);
}
