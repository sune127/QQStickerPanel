using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace QQStickerPanel.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly Icon? _icon;
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _startupItem;
    private readonly Func<bool> _isStartupEnabled;

    public TrayIconService(
        Action showPanel,
        Action togglePanelVisibility,
        Action openStickerRoot,
        Action openSettings,
        Action toggleStartup,
        Action exit,
        Func<bool> isStartupEnabled)
    {
        _isStartupEnabled = isStartupEnabled;
        _icon = LoadAppIcon();
        _startupItem = new ToolStripMenuItem("开机启动", null, (_, _) => toggleStartup())
        {
            CheckOnClick = false
        };
        _notifyIcon = new NotifyIcon
        {
            Text = "QQ 表情包面板",
            Icon = _icon ?? SystemIcons.Application,
            ContextMenuStrip = new ContextMenuStrip()
        };
        _notifyIcon.ContextMenuStrip.Items.Add("显示面板", null, (_, _) => showPanel());
        _notifyIcon.ContextMenuStrip.Items.Add("隐藏/恢复面板", null, (_, _) => togglePanelVisibility());
        _notifyIcon.ContextMenuStrip.Items.Add("打开表情目录", null, (_, _) => openStickerRoot());
        _notifyIcon.ContextMenuStrip.Items.Add("设置", null, (_, _) => openSettings());
        _notifyIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator());
        _notifyIcon.ContextMenuStrip.Items.Add(_startupItem);
        _notifyIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator());
        _notifyIcon.ContextMenuStrip.Items.Add("退出", null, (_, _) => exit());
        _notifyIcon.ContextMenuStrip.Opening += (_, _) => _startupItem.Checked = _isStartupEnabled();
        _notifyIcon.DoubleClick += (_, _) => showPanel();
    }

    public void Show()
    {
        _notifyIcon.Visible = true;
    }

    public bool IsVisible => _notifyIcon.Visible;

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _icon?.Dispose();
    }

    private static Icon? LoadAppIcon()
    {
        var processPath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(processPath) || !File.Exists(processPath))
        {
            return null;
        }

        try
        {
            return Icon.ExtractAssociatedIcon(processPath);
        }
        catch (Exception ex) when (ex is ArgumentException or FileNotFoundException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
