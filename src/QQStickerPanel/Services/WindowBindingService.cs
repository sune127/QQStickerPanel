using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using QQStickerPanel.Models;
using QQStickerPanel.Native;

namespace QQStickerPanel.Services;

public sealed class WindowBindingService : IDisposable
{
    private readonly Window _panelWindow;
    private readonly QQWindowScanner _scanner;
    private readonly DockController _dockController;
    private readonly QQSettings _qqSettings;
    private readonly DockSettings _dockSettings;
    private readonly Action<string> _statusChanged;
    private readonly DispatcherTimer _timer;
    private readonly LinkedList<IntPtr> _recentActiveHandles = [];
    private IntPtr _boundHandle;
    private DateTimeOffset _manualShowUntil;
    private string _bindingStatus = string.Empty;
    private bool _hasEverBound;
    private bool _isManuallyHidden;
    private bool _isDockPaused;
    private bool _isPinned;
    private bool _isDisposed;

    public WindowBindingService(
        Window panelWindow,
        QQWindowScanner scanner,
        DockController dockController,
        QQSettings qqSettings,
        DockSettings dockSettings,
        Action<string> statusChanged)
    {
        _panelWindow = panelWindow;
        _scanner = scanner;
        _dockController = dockController;
        _qqSettings = qqSettings;
        _dockSettings = dockSettings;
        _statusChanged = statusChanged;
        _timer = new DispatcherTimer(DispatcherPriority.Background, panelWindow.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(dockSettings.FollowTimerIntervalMs)
        };
        _timer.Tick += OnTick;
    }

    public void Start()
    {
        if (_isDisposed || IsWindowClosed)
        {
            return;
        }

        _timer.Start();
        Tick();
    }

    public void HandleWindowEvent(IntPtr eventHandle)
    {
        if (_isDisposed || IsWindowClosed)
        {
            return;
        }

        if (_panelWindow.Dispatcher.CheckAccess())
        {
            HandleWindowEventOnDispatcher(eventHandle);
            return;
        }

        _panelWindow.Dispatcher.BeginInvoke(() => HandleWindowEventOnDispatcher(eventHandle), DispatcherPriority.Background);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _timer.Stop();
        _timer.Tick -= OnTick;
    }

    public bool PasteToBoundWindow(QQSettings qqSettings)
    {
        if (_isDisposed || IsWindowClosed || _boundHandle == IntPtr.Zero || !_scanner.IsUsableWindow(_boundHandle))
        {
            return false;
        }

        if (!Win32.SetForegroundWindow(_boundHandle))
        {
            return false;
        }

        SendCtrlV();
        if (qqSettings.SendAfterPaste)
        {
            SendShortcutAfterDelay(qqSettings.SendShortcut);
        }

        return true;
    }

    public void TogglePanelVisibility()
    {
        _isManuallyHidden = !_isManuallyHidden;
        if (_isManuallyHidden)
        {
            HidePanel();
            SetBindingStatus("面板已手动隐藏，按 Ctrl+Alt+Q 恢复");
            return;
        }

        ShowPanel();
        _manualShowUntil = DateTimeOffset.Now.AddSeconds(30);
        _dockController.Reset();
        Tick();
        SetBindingStatus("面板已恢复");
    }

    public void TogglePinCurrentWindow()
    {
        if (_isPinned)
        {
            _isPinned = false;
            SetBindingStatus("已取消固定 QQ 窗口");
            return;
        }

        var foregroundQQ = _scanner.FindBestWindow();
        if (foregroundQQ is null)
        {
            SetBindingStatus("未找到可固定的 QQ 窗口");
            return;
        }

        _boundHandle = foregroundQQ.Handle;
        RememberActiveWindow(_boundHandle);
        _isPinned = true;
        _dockController.Reset();
        Tick();
        SetBindingStatus("已固定当前 QQ 窗口");
    }

    public void ToggleDockPause()
    {
        _isDockPaused = !_isDockPaused;
        if (_isDockPaused)
        {
            _dockController.Reset();
            SetBindingStatus("已暂停吸附，可手动移动面板");
            return;
        }

        _dockController.Reset();
        Tick();
        SetBindingStatus("已恢复吸附");
    }

    public void ToggleFreeDock()
    {
        _dockSettings.FreeDockEnabled = !_dockSettings.FreeDockEnabled;
        if (_dockSettings.FreeDockEnabled)
        {
            _isDockPaused = false;
            _dockController.Reset();
            RestoreFreeDockPosition();
            SetBindingStatus("已进入自由停靠，拖动面板后会记住位置");
            return;
        }

        SaveFreeDockPosition();
        _dockController.Reset();
        Tick();
        SetBindingStatus("已恢复跟随吸附");
    }

    public void ReloadSettings()
    {
        _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(16, _dockSettings.FollowTimerIntervalMs));
        _dockController.Reset();
        if (_dockSettings.FreeDockEnabled)
        {
            RestoreFreeDockPosition();
        }

        Tick();
    }

    public void ShowPanelFromTray()
    {
        _isManuallyHidden = false;
        _manualShowUntil = DateTimeOffset.Now.AddSeconds(30);
        ReloadSettings();
    }

    public bool TryAutoDockAfterManualMove()
    {
        if (_isDisposed || IsWindowClosed)
        {
            return false;
        }

        var targetHandle = _boundHandle != IntPtr.Zero && _scanner.IsUsableWindow(_boundHandle)
            ? _boundHandle
            : FindPreferredWindowHandle();
        if (targetHandle == IntPtr.Zero || !_dockController.TryInferDockSide(targetHandle, 48, out var side))
        {
            return false;
        }

        _boundHandle = targetHandle;
        _dockSettings.Side = side;
        _dockSettings.FreeDockEnabled = false;
        _isDockPaused = false;
        _manualShowUntil = DateTimeOffset.Now.AddSeconds(3);
        _dockController.Reset();
        Tick();
        SetBindingStatus($"已自动吸附到 QQ {GetDockSideText(side)}");
        return true;
    }

    public void SaveFreeDockPosition()
    {
        if (!_dockSettings.FreeDockEnabled)
        {
            return;
        }

        _dockSettings.FreeLeft = double.IsFinite(_panelWindow.Left) ? _panelWindow.Left : null;
        _dockSettings.FreeTop = double.IsFinite(_panelWindow.Top) ? _panelWindow.Top : null;
        if (double.IsFinite(_panelWindow.Width) && _panelWindow.Width > 0)
        {
            _dockSettings.FreeWidth = _panelWindow.Width;
        }

        if (double.IsFinite(_panelWindow.Height) && _panelWindow.Height > 0)
        {
            _dockSettings.FreeHeight = _panelWindow.Height;
        }
    }

    private void RestoreFreeDockPosition()
    {
        if (_dockSettings.FreeLeft.HasValue)
        {
            _panelWindow.Left = _dockSettings.FreeLeft.Value;
        }

        if (_dockSettings.FreeTop.HasValue)
        {
            _panelWindow.Top = _dockSettings.FreeTop.Value;
        }

        if (double.IsFinite(_dockSettings.FreeWidth) && _dockSettings.FreeWidth > 0)
        {
            _panelWindow.Width = _dockSettings.FreeWidth;
        }

        if (double.IsFinite(_dockSettings.FreeHeight) && _dockSettings.FreeHeight > 0)
        {
            _panelWindow.Height = _dockSettings.FreeHeight;
        }
    }

    private void OnTick(object? sender, EventArgs e) => Tick();

    private bool IsWindowClosed => _panelWindow.Dispatcher.HasShutdownStarted || _panelWindow.Dispatcher.HasShutdownFinished;

    private void HandleWindowEventOnDispatcher(IntPtr eventHandle)
    {
        if (_isDisposed || IsWindowClosed)
        {
            return;
        }

        var windowInfo = _scanner.GetWindowInfo(eventHandle);
        if (windowInfo is not null)
        {
            RememberActiveWindow(windowInfo.Handle);
            if (!_isPinned && _scanner.IsUsableWindow(windowInfo.Handle))
            {
                if (_boundHandle != windowInfo.Handle)
                {
                    _boundHandle = windowInfo.Handle;
                    _dockController.Reset();
                }
            }
        }

        Tick();
    }

    private void RememberActiveWindow(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var existingNode = _recentActiveHandles.Find(handle);
        if (existingNode is not null)
        {
            _recentActiveHandles.Remove(existingNode);
        }

        _recentActiveHandles.AddFirst(handle);
        while (_recentActiveHandles.Count > 8)
        {
            _recentActiveHandles.RemoveLast();
        }
    }

    private IntPtr FindPreferredWindowHandle()
    {
        var bestWindow = _scanner.FindBestWindow();
        if (bestWindow is not null)
        {
            RememberActiveWindow(bestWindow.Handle);
            return bestWindow.Handle;
        }

        for (var node = _recentActiveHandles.First; node is not null;)
        {
            var nextNode = node.Next;
            if (_scanner.IsUsableWindow(node.Value))
            {
                return node.Value;
            }

            _recentActiveHandles.Remove(node);
            node = nextNode;
        }

        return IntPtr.Zero;
    }

    private void Tick()
    {
        if (_isDisposed || IsWindowClosed)
        {
            return;
        }

        if (_isManuallyHidden)
        {
            HidePanel();
            return;
        }

        if (_boundHandle == IntPtr.Zero || !_isPinned && !_scanner.IsUsableWindow(_boundHandle))
        {
            var nextHandle = FindPreferredWindowHandle();
            if (nextHandle != _boundHandle)
            {
                _dockController.Reset();
            }

            _boundHandle = nextHandle;
            _hasEverBound = _hasEverBound || _boundHandle != IntPtr.Zero;
        }

        if (_boundHandle == IntPtr.Zero)
        {
            if (_qqSettings.HideWhenQQNotRunning && !HasManualShowOverride)
            {
                HidePanel();
                SetBindingStatus(_hasEverBound ? "QQ 窗口已关闭，面板已回到托盘" : "未找到 QQ，面板已回到托盘");
            }
            else
            {
                ShowPanel();
                SetBindingStatus("未找到 QQ 窗口");
            }
            return;
        }

        if (!Win32.IsWindow(_boundHandle) || !Win32.IsWindowVisible(_boundHandle) || Win32.IsIconic(_boundHandle))
        {
            HidePanel();
            SetBindingStatus("QQ 已隐藏，面板已收回托盘");
            _isPinned = false;
            _boundHandle = IntPtr.Zero;
            return;
        }

        var foregroundWindow = Win32.GetForegroundWindow();
        var foregroundQQ = _scanner.GetWindowInfo(foregroundWindow);
        if (!_isPinned && foregroundQQ is not null)
        {
            RememberActiveWindow(foregroundQQ.Handle);
            if (foregroundQQ.Handle != _boundHandle)
            {
                _boundHandle = foregroundQQ.Handle;
                _dockController.Reset();
            }
        }

        var boundQQIsForeground = IsBoundQQForeground(foregroundQQ);
        if (_qqSettings.HideWhenQQNotForeground
            && !boundQQIsForeground
            && !IsPanelForeground(foregroundWindow)
            && !HasManualShowOverride)
        {
            HidePanel();
            SetBindingStatus("QQ 不在前台，面板已回到托盘");
            return;
        }

        if (!_qqSettings.ShowWhenQQForeground && !_panelWindow.IsVisible && !HasManualShowOverride)
        {
            return;
        }

        if (_dockSettings.FreeDockEnabled)
        {
            ShowPanel();
            SetBindingStatus("自由停靠中");
            return;
        }

        if (_isDockPaused)
        {
            ShowPanel();
            SetBindingStatus("吸附已暂停");
            return;
        }

        ShowPanel();
        SetBindingStatus("已吸附 QQ 窗口");
        KeepPanelWithQQ(boundQQIsForeground);
        _dockController.SyncToQQWindow(_boundHandle);
    }

    private void SetBindingStatus(string status)
    {
        if (_isDisposed || IsWindowClosed || _bindingStatus == status)
        {
            return;
        }

        _bindingStatus = status;
        _statusChanged(status);
    }

    private void ShowPanel()
    {
        if (_isDisposed || IsWindowClosed || _panelWindow.IsVisible)
        {
            return;
        }

        _panelWindow.Show();
    }

    private void HidePanel()
    {
        if (_isDisposed || IsWindowClosed)
        {
            return;
        }

        _dockController.Reset();
        if (_panelWindow.IsVisible)
        {
            _panelWindow.Hide();
        }
    }

    private bool IsBoundQQForeground(QQWindowInfo? foregroundQQ)
    {
        return foregroundQQ is not null && foregroundQQ.Handle == _boundHandle;
    }

    private bool HasManualShowOverride => DateTimeOffset.Now < _manualShowUntil;

    private bool IsPanelForeground(IntPtr foregroundWindow)
    {
        var panelHandle = new WindowInteropHelper(_panelWindow).Handle;
        return panelHandle != IntPtr.Zero && foregroundWindow == panelHandle;
    }

    private static string GetDockSideText(DockSide side)
    {
        return side switch
        {
            DockSide.Left => "左侧",
            DockSide.InnerLeft => "内左侧",
            DockSide.InnerRight => "内右侧",
            DockSide.Top => "上方",
            DockSide.Bottom => "下方",
            _ => "右侧"
        };
    }

    private void KeepPanelWithQQ(bool qqIsForeground)
    {
        if (!qqIsForeground)
        {
            return;
        }

        var panelHandle = new WindowInteropHelper(_panelWindow).Handle;
        if (panelHandle == IntPtr.Zero)
        {
            return;
        }

        Win32.SetWindowPos(
            panelHandle,
            Win32.HwndTopMost,
            0,
            0,
            0,
            0,
            Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW | Win32.SWP_NOSENDCHANGING);
        Win32.SetWindowPos(
            panelHandle,
            Win32.HwndNoTopMost,
            0,
            0,
            0,
            0,
            Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW | Win32.SWP_NOSENDCHANGING);
    }

    private static void SendCtrlV()
    {
        const byte virtualKeyControl = 0x11;
        const byte virtualKeyV = 0x56;

        SendKeyDown(virtualKeyControl);
        SendKeyPress(virtualKeyV);
        SendKeyUp(virtualKeyControl);
    }

    private async void SendShortcutAfterDelay(QQSendShortcut shortcut)
    {
        await Task.Delay(450);
        if (_isDisposed || IsWindowClosed || _boundHandle == IntPtr.Zero || !_scanner.IsUsableWindow(_boundHandle))
        {
            return;
        }

        Win32.SetForegroundWindow(_boundHandle);
        SendShortcut(shortcut);
    }

    private static void SendShortcut(QQSendShortcut shortcut)
    {
        const byte virtualKeyControl = 0x11;
        const byte virtualKeyEnter = 0x0D;

        if (shortcut == QQSendShortcut.CtrlEnter)
        {
            SendKeyDown(virtualKeyControl);
            SendKeyPress(virtualKeyEnter);
            SendKeyUp(virtualKeyControl);
            return;
        }

        SendKeyPress(virtualKeyEnter);
    }

    private static void SendKeyPress(byte virtualKey)
    {
        SendKeyDown(virtualKey);
        SendKeyUp(virtualKey);
    }

    private static void SendKeyDown(byte virtualKey)
    {
        Win32.keybd_event(virtualKey, 0, 0, UIntPtr.Zero);
    }

    private static void SendKeyUp(byte virtualKey)
    {
        const uint keyEventKeyUp = 0x0002;

        Win32.keybd_event(virtualKey, 0, keyEventKeyUp, UIntPtr.Zero);
    }

}
