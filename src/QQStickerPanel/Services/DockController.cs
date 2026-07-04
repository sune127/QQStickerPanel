using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using QQStickerPanel.Models;
using QQStickerPanel.Native;

namespace QQStickerPanel.Services;

public sealed class DockController
{
    private readonly Window _panelWindow;
    private readonly DockSettings _settings;
    private TargetRect? _lastTargetRect;

    public DockController(Window panelWindow, DockSettings settings)
    {
        _panelWindow = panelWindow;
        _settings = settings;
    }

    public bool SyncToQQWindow(IntPtr qqHandle)
    {
        if (!Win32.GetWindowRect(qqHandle, out var qqRect))
        {
            return false;
        }

        var panelHandle = new WindowInteropHelper(_panelWindow).Handle;
        if (panelHandle == IntPtr.Zero)
        {
            return false;
        }

        var scale = VisualTreeHelper.GetDpi(_panelWindow);
        var gapX = DipsToPixels(_settings.Gap, scale.DpiScaleX);
        var gapY = DipsToPixels(_settings.Gap, scale.DpiScaleY);
        var width = DipsToPixels(_settings.Width, scale.DpiScaleX);
        var height = DipsToPixels(_settings.Width, scale.DpiScaleY);
        var minHeight = DipsToPixels(240, scale.DpiScaleY);
        var minWidth = DipsToPixels(240, scale.DpiScaleX);

        var targetRect = KeepVisible(qqHandle, _settings.Side switch
        {
            DockSide.Left => new TargetRect(
                qqRect.Left - width - gapX,
                qqRect.Top,
                width,
                Math.Max(minHeight, qqRect.Height)),
            DockSide.Top => new TargetRect(
                qqRect.Left,
                qqRect.Top - height - gapY,
                Math.Max(minWidth, qqRect.Width),
                height),
            DockSide.Bottom => new TargetRect(
                qqRect.Left,
                qqRect.Bottom + gapY,
                Math.Max(minWidth, qqRect.Width),
                height),
            DockSide.InnerLeft => new TargetRect(
                qqRect.Left + gapX,
                qqRect.Top + gapY,
                width,
                Math.Max(minHeight, qqRect.Height - gapY * 2)),
            DockSide.InnerRight => new TargetRect(
                qqRect.Right - width - gapX,
                qqRect.Top + gapY,
                width,
                Math.Max(minHeight, qqRect.Height - gapY * 2)),
            _ => new TargetRect(
                qqRect.Right + gapX,
                qqRect.Top,
                width,
                Math.Max(minHeight, qqRect.Height))
        });

        if (_lastTargetRect == targetRect)
        {
            return true;
        }

        _lastTargetRect = targetRect;
        return Win32.SetWindowPos(
            panelHandle,
            IntPtr.Zero,
            targetRect.X,
            targetRect.Y,
            targetRect.Width,
            targetRect.Height,
            Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW | Win32.SWP_NOSENDCHANGING);
    }

    public void Reset()
    {
        _lastTargetRect = null;
    }

    public bool TryInferDockSide(IntPtr qqHandle, double thresholdDip, out DockSide side)
    {
        side = _settings.Side;
        if (!Win32.GetWindowRect(qqHandle, out var qqRect))
        {
            return false;
        }

        var panelHandle = new WindowInteropHelper(_panelWindow).Handle;
        if (panelHandle == IntPtr.Zero || !Win32.GetWindowRect(panelHandle, out var panelRect))
        {
            return false;
        }

        var scale = VisualTreeHelper.GetDpi(_panelWindow);
        var thresholdX = DipsToPixels(thresholdDip, scale.DpiScaleX);
        var thresholdY = DipsToPixels(thresholdDip, scale.DpiScaleY);
        var candidates = new List<(DockSide Side, int Distance)>
        {
            (DockSide.Right, Math.Abs(panelRect.Left - qqRect.Right)),
            (DockSide.Left, Math.Abs(panelRect.Right - qqRect.Left)),
            (DockSide.Bottom, Math.Abs(panelRect.Top - qqRect.Bottom)),
            (DockSide.Top, Math.Abs(panelRect.Bottom - qqRect.Top))
        };

        if (RangesOverlap(panelRect.Top, panelRect.Bottom, qqRect.Top, qqRect.Bottom))
        {
            candidates.Add((DockSide.InnerLeft, Math.Abs(panelRect.Left - qqRect.Left)));
            candidates.Add((DockSide.InnerRight, Math.Abs(panelRect.Right - qqRect.Right)));
        }

        var best = candidates
            .Where(candidate => candidate.Side is DockSide.Left or DockSide.Right or DockSide.InnerLeft or DockSide.InnerRight
                ? candidate.Distance <= thresholdX
                : candidate.Distance <= thresholdY)
            .OrderBy(candidate => candidate.Distance)
            .Cast<(DockSide Side, int Distance)?>()
            .FirstOrDefault();

        if (best is null)
        {
            return false;
        }

        var requiresVerticalOverlap = best.Value.Side is DockSide.Left or DockSide.Right or DockSide.InnerLeft or DockSide.InnerRight;
        if (requiresVerticalOverlap && !RangesOverlap(panelRect.Top, panelRect.Bottom, qqRect.Top, qqRect.Bottom))
        {
            return false;
        }

        if (!requiresVerticalOverlap && !RangesOverlap(panelRect.Left, panelRect.Right, qqRect.Left, qqRect.Right))
        {
            return false;
        }

        side = best.Value.Side;
        return true;
    }

    private static int DipsToPixels(double value, double scale)
    {
        return (int)Math.Round(value * scale);
    }

    private static bool RangesOverlap(int firstStart, int firstEnd, int secondStart, int secondEnd)
    {
        return firstStart < secondEnd && secondStart < firstEnd;
    }

    private static TargetRect KeepVisible(IntPtr qqHandle, TargetRect targetRect)
    {
        var monitor = Win32.MonitorFromWindow(qqHandle, Win32.MONITOR_DEFAULTTONEAREST);
        var monitorInfo = new Win32.MonitorInfo
        {
            Size = System.Runtime.InteropServices.Marshal.SizeOf<Win32.MonitorInfo>()
        };
        if (monitor == IntPtr.Zero || !Win32.GetMonitorInfo(monitor, out monitorInfo))
        {
            return targetRect;
        }

        var screen = monitorInfo.WorkArea;
        var x = Math.Clamp(targetRect.X, screen.Left, Math.Max(screen.Left, screen.Right - targetRect.Width));
        var y = Math.Clamp(targetRect.Y, screen.Top, Math.Max(screen.Top, screen.Bottom - targetRect.Height));
        return targetRect with { X = x, Y = y };
    }

    private readonly record struct TargetRect(int X, int Y, int Width, int Height);
}
