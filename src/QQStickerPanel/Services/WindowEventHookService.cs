using QQStickerPanel.Native;

namespace QQStickerPanel.Services;

public sealed class WindowEventHookService : IDisposable
{
    private readonly Action<IntPtr> _windowEventReceived;
    private readonly Win32.WinEventProc _callback;
    private readonly List<IntPtr> _hooks = [];
    private bool _disposed;

    public WindowEventHookService(Action<IntPtr> windowEventReceived)
    {
        _windowEventReceived = windowEventReceived;
        _callback = OnWinEvent;
    }

    public void Start()
    {
        if (_hooks.Count > 0)
        {
            return;
        }

        AddHook(Win32.EVENT_SYSTEM_FOREGROUND, Win32.EVENT_SYSTEM_FOREGROUND);
        AddHook(Win32.EVENT_SYSTEM_MINIMIZESTART, Win32.EVENT_SYSTEM_MINIMIZEEND);
        AddHook(Win32.EVENT_OBJECT_DESTROY, Win32.EVENT_OBJECT_DESTROY);
        AddHook(Win32.EVENT_OBJECT_SHOW, Win32.EVENT_OBJECT_SHOW);
        AddHook(Win32.EVENT_OBJECT_HIDE, Win32.EVENT_OBJECT_HIDE);
        AddHook(Win32.EVENT_OBJECT_LOCATIONCHANGE, Win32.EVENT_OBJECT_LOCATIONCHANGE);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var hook in _hooks)
        {
            Win32.UnhookWinEvent(hook);
        }

        _hooks.Clear();
    }

    private void AddHook(uint eventMin, uint eventMax)
    {
        var hook = Win32.SetWinEventHook(
            eventMin,
            eventMax,
            IntPtr.Zero,
            _callback,
            0,
            0,
            Win32.WINEVENT_OUTOFCONTEXT);
        if (hook != IntPtr.Zero)
        {
            _hooks.Add(hook);
        }
    }

    private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (_disposed || hwnd == IntPtr.Zero || idObject != Win32.OBJID_WINDOW || idChild != 0)
        {
            return;
        }

        _windowEventReceived(hwnd);
    }
}
