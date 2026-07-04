using System.Diagnostics;
using System.Text;
using QQStickerPanel.Models;
using QQStickerPanel.Native;

namespace QQStickerPanel.Services;

public sealed class QQWindowScanner
{
    private QQSettings _settings;
    private QQWindowMatcher _matcher;
    private string _dataDirectory;

    public QQWindowScanner(QQSettings settings, string dataDirectory)
    {
        _settings = settings;
        _dataDirectory = dataDirectory;
        _matcher = new QQWindowMatcher(settings, dataDirectory);
    }

    public void UpdateSettings(QQSettings settings, string dataDirectory)
    {
        _settings = settings;
        _dataDirectory = dataDirectory;
        _matcher = new QQWindowMatcher(settings, dataDirectory);
    }

    public QQWindowInfo? FindBestWindow()
    {
        var windows = EnumerateQQWindows().ToList();
        if (windows.Count == 0)
        {
            return null;
        }

        var foreground = Win32.GetForegroundWindow();
        var foregroundWindow = windows.FirstOrDefault(window => window.Handle == foreground);
        return foregroundWindow ?? FindWindowUnderCursor(windows) ?? windows[0];
    }

    public QQWindowInfo? FindWindowUnderCursor()
    {
        return FindWindowUnderCursor(EnumerateQQWindows());
    }

    public QQWindowInfo? GetWindowInfo(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        var rootHandle = Win32.GetAncestor(handle, Win32.GA_ROOT);
        if (rootHandle == IntPtr.Zero)
        {
            rootHandle = handle;
        }

        return TryCreateWindowInfo(rootHandle, rootHandle == Win32.GetForegroundWindow(), out var windowInfo) ? windowInfo : null;
    }

    public bool IsForegroundQQWindow(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        return Win32.GetForegroundWindow() == handle;
    }

    public IEnumerable<QQWindowInfo> EnumerateQQWindows()
    {
        var windows = new List<QQWindowInfo>();

        var foreground = Win32.GetForegroundWindow();

        Win32.EnumWindows((hwnd, _) =>
        {
            if (!Win32.IsWindowVisible(hwnd))
            {
                return true;
            }

            if (!TryCreateWindowInfo(hwnd, hwnd == foreground, out var windowInfo))
            {
                return true;
            }

            windows.Add(windowInfo);
            return true;
        }, IntPtr.Zero);

        return windows;
    }

    public bool IsUsableWindow(IntPtr handle)
    {
        return handle != IntPtr.Zero
            && Win32.IsWindow(handle)
            && Win32.IsWindowVisible(handle)
            && !Win32.IsIconic(handle)
            && TryCreateWindowInfo(handle, handle == Win32.GetForegroundWindow(), out _);
    }

    private QQWindowInfo? FindWindowUnderCursor(IEnumerable<QQWindowInfo> windows)
    {
        if (!Win32.GetCursorPos(out var point))
        {
            return null;
        }

        var cursorHandle = Win32.GetAncestor(Win32.WindowFromPoint(point), Win32.GA_ROOT);
        if (cursorHandle == IntPtr.Zero)
        {
            return null;
        }

        return windows.FirstOrDefault(window => window.Handle == cursorHandle);
    }

    private bool TryCreateWindowInfo(IntPtr hwnd, bool isForeground, out QQWindowInfo windowInfo)
    {
        windowInfo = null!;
        Win32.GetWindowThreadProcessId(hwnd, out var processId);
        if (processId == 0)
        {
            return false;
        }

        string processName;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            processName = process.ProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? process.ProcessName
                : process.ProcessName + ".exe";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return false;
        }

        if (!_settings.ProcessNames.Contains(processName, StringComparer.OrdinalIgnoreCase) || !Win32.GetWindowRect(hwnd, out var rect))
        {
            return false;
        }

        var title = GetWindowTitle(hwnd);
        var className = GetWindowClassName(hwnd);

        var candidate = new QQWindowInfo
        {
            Handle = hwnd,
            ProcessId = (int)processId,
            ProcessName = processName,
            Title = title,
            ClassName = className,
            Left = rect.Left,
            Top = rect.Top,
            Width = rect.Width,
            Height = rect.Height
        };
        return _matcher.TryMatch(candidate, isForeground, out windowInfo);
    }

    private static string GetWindowTitle(IntPtr hwnd)
    {
        var length = Win32.GetWindowTextLength(hwnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(length + 1);
        Win32.GetWindowText(hwnd, builder, builder.Capacity);
        return builder.ToString();
    }

    private static string GetWindowClassName(IntPtr hwnd)
    {
        var builder = new StringBuilder(256);
        return Win32.GetClassName(hwnd, builder, builder.Capacity) > 0 ? builder.ToString() : string.Empty;
    }
}
