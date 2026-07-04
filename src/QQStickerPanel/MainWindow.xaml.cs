using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Win32;
using QQStickerPanel.Models;
using QQStickerPanel.Native;
using QQStickerPanel.Services;
using QQStickerPanel.ViewModels;

namespace QQStickerPanel;

public partial class MainWindow : Window
{
    private const int TogglePanelHotKeyId = 1;
    private const double StickerCardOuterWidth = 84;
    private readonly StartupService _startupService = new();
    private FileWatcherService? _fileWatcherService;
    private WindowBindingService? _windowBindingService;
    private WindowEventHookService? _windowEventHookService;
    private TrayIconService? _trayIconService;
    private QQWindowScanner? _qqWindowScanner;
    private SettingsService? _settingsService;
    private HwndSource? _hwndSource;
    private AppSettings? _settings;
    private Point? _dragStartPoint;
    private Point? _categoryDragStartPoint;
    private StickerCategory? _dragCategory;
    private readonly bool _startHidden;

    public MainWindow(bool startHidden = false)
    {
        _startHidden = startHidden;
        InitializeComponent();
        if (_startHidden)
        {
            Opacity = 0;
            ShowInTaskbar = false;
        }

        var settingsService = new SettingsService();
        _settingsService = settingsService;
        var settings = settingsService.Load();
        settings.Startup.Enabled = _startupService.IsEnabled();
        _settings = settings;
        var indexService = new StickerIndexService(settings, settingsService.DataDirectory);
        var libraryService = new StickerLibraryService(indexService);
        var clipboardService = new ClipboardService();
        var recentService = new RecentService(settingsService.DataDirectory, settings.RecentLimit);
        var favoriteService = new FavoriteService(settingsService.DataDirectory);
        var dragDropService = new DragDropService(settings);
        var managementService = new StickerManagementService(settings);
        var metadataService = new StickerMetadataService(settingsService.DataDirectory);
        var viewModel = new MainViewModel(settings, settingsService, libraryService, clipboardService, recentService, favoriteService, dragDropService, managementService, metadataService)
        {
            RequestImportConfirmation = request =>
            {
                var dialog = new ImportConfirmWindow(request) { Owner = this };
                return dialog.ShowDialog() == true ? dialog.Result : null;
            }
        };

        DataContext = viewModel;

        _fileWatcherService = new FileWatcherService(Dispatcher, viewModel.RefreshLibraryInBackground);
        _fileWatcherService.Start(settings.StickerRoot);

        _qqWindowScanner = new QQWindowScanner(settings.QQ, settingsService.DataDirectory);
        var dockController = new DockController(this, settings.Dock);
        _windowBindingService = new WindowBindingService(this, _qqWindowScanner, dockController, settings.QQ, settings.Dock, viewModel.ShowBindingStatus);
        _windowEventHookService = new WindowEventHookService(handle => _windowBindingService?.HandleWindowEvent(handle));
        _trayIconService = new TrayIconService(
            () => Dispatcher.Invoke(ShowPanelFromTray),
            () => Dispatcher.Invoke(() => _windowBindingService?.TogglePanelVisibility()),
            () => Dispatcher.Invoke(() => viewModel.OpenStickerRootCommand.Execute(null)),
            () => Dispatcher.Invoke(OpenSettingsDialog),
            () => Dispatcher.Invoke(ToggleStartup),
            () => Dispatcher.Invoke(ExitFromTray),
            () => _settings?.Startup.Enabled == true);

        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += OnClosed;
        LocationChanged += OnWindowBoundsChanged;
        SizeChanged += OnWindowBoundsChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _windowBindingService?.Start();
        _windowEventHookService?.Start();
        _trayIconService?.Show();
        RegisterTogglePanelHotKey();
        if (_startHidden)
        {
            Hide();
            Opacity = 1;
        }
    }

    private void OnWindowBoundsChanged(object? sender, EventArgs e)
    {
        _windowBindingService?.SaveFreeDockPosition();
    }

    private void OnStickerRowsListSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || !double.IsFinite(e.NewSize.Width) || e.NewSize.Width <= 0)
        {
            return;
        }

        var availableWidth = Math.Max(0, e.NewSize.Width - 18);
        viewModel.SetStickerColumnCount(Math.Max(1, (int)(availableWidth / StickerCardOuterWidth)));
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.CancelBackgroundWork();
        }

        _fileWatcherService?.Dispose();
        _windowEventHookService?.Dispose();
        _windowBindingService?.Dispose();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _windowBindingService?.SaveFreeDockPosition();
        if (_settings is not null)
        {
            _settingsService?.Save(_settings);
        }

        UnregisterTogglePanelHotKey();
        _trayIconService?.Dispose();
        _windowEventHookService?.Dispose();
        _fileWatcherService?.Dispose();
        _windowBindingService?.Dispose();
    }

    private void RegisterTogglePanelHotKey()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        _hwndSource = HwndSource.FromHwnd(handle);
        _hwndSource?.AddHook(OnWindowMessage);
        Win32.RegisterHotKey(handle, TogglePanelHotKeyId, Win32.MOD_CONTROL | Win32.MOD_ALT, Win32.VK_Q);
    }

    private void UnregisterTogglePanelHotKey()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
        {
            Win32.UnregisterHotKey(handle, TogglePanelHotKeyId);
        }

        _hwndSource?.RemoveHook(OnWindowMessage);
        _hwndSource = null;
    }

    private IntPtr OnWindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32.WM_HOTKEY && wParam.ToInt32() == TogglePanelHotKeyId)
        {
            _windowBindingService?.TogglePanelVisibility();
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void ShowPanelFromTray()
    {
        Opacity = 1;
        Show();
        Activate();
        _windowBindingService?.ShowPanelFromTray();
    }

    private void OpenSettingsDialog()
    {
        OnOpenSettingsClick(this, new RoutedEventArgs());
    }

    private void ShowShortcutHelpDialog()
    {
        var dialog = new ShortcutHelpWindow { Owner = this };
        dialog.ShowDialog();
    }

    private void ToggleStartup()
    {
        if (_settings is null)
        {
            return;
        }

        _settings.Startup.Enabled = !_startupService.IsEnabled();
        _startupService.SetEnabled(_settings.Startup.Enabled);
        _settingsService?.Save(_settings);
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.ShowBindingStatus(_settings.Startup.Enabled ? "已开启开机启动" : "已关闭开机启动");
        }
    }

    private void ExitFromTray()
    {
        Close();
    }

    private void OnCloseWindowClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (e.Key == Key.F1)
        {
            ShowShortcutHelpDialog();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Tab && (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            viewModel.SelectPreviousCategory();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Tab && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            viewModel.SelectNextCategory();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.O && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            viewModel.OpenSelectedCategoryDirectory();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            viewModel.ToggleSelectedFavoriteState();
            e.Handled = true;
            return;
        }

        if ((e.Key == Key.Add || e.Key == Key.OemPlus) && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            viewModel.WidenPanel();
            e.Handled = true;
            return;
        }

        if ((e.Key == Key.Subtract || e.Key == Key.OemMinus) && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            viewModel.NarrowPanel();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.D0 && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            viewModel.ResetPanelWidthToDefault();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            viewModel.ImportFiles(Clipboard.GetDataObject());
            e.Handled = true;
            return;
        }

        if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            viewModel.CopySelectedStickers();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.A && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            viewModel.SelectAllVisibleStickers();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.I && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            viewModel.InvertVisibleStickerSelection();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            viewModel.ClearStickerSelection();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete)
        {
            viewModel.DeleteSelectedStickers();
            e.Handled = true;
        }
    }

    private void OnPanelDragEnter(object sender, DragEventArgs e) => UpdateDropEffect(e);

    private void OnPanelDragOver(object sender, DragEventArgs e) => UpdateDropEffect(e);

    private void OnPanelDrop(object sender, DragEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.ImportFiles(e.Data);
        }

        e.Handled = true;
    }

    private void OnStickerMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(this);
        if (sender is not FrameworkElement { DataContext: StickerItem sticker } || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
        {
            _dragStartPoint = null;
            viewModel.SelectStickerRange(sticker);
            e.Handled = true;
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
        {
            return;
        }

        _dragStartPoint = null;
        viewModel.ToggleStickerSelection(sticker);
        e.Handled = true;
    }

    private void OnStickerMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStartPoint is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var currentPoint = e.GetPosition(this);
        if (Math.Abs(currentPoint.X - _dragStartPoint.Value.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(currentPoint.Y - _dragStartPoint.Value.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _dragStartPoint = null;
        if (sender is not FrameworkElement { DataContext: StickerItem sticker } || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var dataObject = viewModel.CreateStickerDragData(sticker);
        var result = DragDrop.DoDragDrop(this, dataObject, DragDropEffects.Copy);
        if (result != DragDropEffects.None && !viewModel.ConsumeHandledInternalCategoryDrop())
        {
            viewModel.RecordStickerUse(sticker);
        }
    }

    private void OnStickerMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: StickerItem sticker } || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        viewModel.CopyStickerCommand.Execute(sticker);
        viewModel.ShowPasteResult(sticker, _settings is not null && _windowBindingService?.PasteToBoundWindow(_settings.QQ) == true);

        e.Handled = true;
    }

    private void OnStickerPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: StickerItem sticker } || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (e.Key == Key.F2)
        {
            RenameSticker(sticker);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete)
        {
            viewModel.DeleteStickers(sticker);
            e.Handled = true;
        }
    }

    private void OnCreateCategoryClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var categoryName = ShowTextInputDialog("新建分类", "创建", string.Empty);
        if (categoryName is not null)
        {
            viewModel.CreateCategoryCommand.Execute(categoryName);
        }
    }

    private void OnImportFilesClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || _settings is null)
        {
            return;
        }

        var extensions = _settings.SupportedExtensions
            .Select(extension => $"*{extension}")
            .ToArray();
        var dialog = new OpenFileDialog
        {
            Title = "导入表情图片",
            Filter = $"图片文件 ({string.Join(';', extensions)})|{string.Join(';', extensions)}|所有文件 (*.*)|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog(this) == true)
        {
            viewModel.ImportSelectedFilesCommand.Execute(dialog.FileNames);
        }
    }

    private void OnChangeStickerRootClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || _settings is null)
        {
            return;
        }

        var dialog = new OpenFolderDialog
        {
            Title = "选择表情包根目录",
            InitialDirectory = _settings.StickerRoot
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (viewModel.ChangeStickerRoot(dialog.FolderName))
        {
            _fileWatcherService?.Start(_settings.StickerRoot);
        }
    }

    private void OnDeleteDuplicateStickersClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var duplicateCount = viewModel.CountDuplicateStickers();
        if (duplicateCount == 0)
        {
            viewModel.DeleteDuplicateStickersCommand.Execute(null);
            return;
        }

        var result = MessageBox.Show(
            this,
            $"将把 {duplicateCount} 个重复表情包移入回收站，保留较新的文件。",
            "清理重复表情",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);
        if (result == MessageBoxResult.OK)
        {
            viewModel.DeleteDuplicateStickersCommand.Execute(null);
        }
    }

    private void OnTogglePinQQWindowClick(object sender, RoutedEventArgs e)
    {
        _windowBindingService?.TogglePinCurrentWindow();
    }

    private void OnToggleDockPauseClick(object sender, RoutedEventArgs e)
    {
        _windowBindingService?.ToggleDockPause();
    }

    private void OnToggleFreeDockClick(object sender, RoutedEventArgs e)
    {
        _windowBindingService?.ToggleFreeDock();
        if (_settings is not null)
        {
            _settingsService?.Save(_settings);
        }
    }

    private void OnShortcutHelpClick(object sender, RoutedEventArgs e)
    {
        ShowShortcutHelpDialog();
    }

    private void OnOpenSettingsClick(object sender, RoutedEventArgs e)
    {
        var settingsService = _settingsService;
        if (_settings is null || settingsService is null)
        {
            return;
        }

        var dialog = new SettingsWindow(_settings) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _startupService.SetEnabled(_settings.Startup.Enabled);
            settingsService.Save(_settings);
            _fileWatcherService?.Start(_settings.StickerRoot);
            if (DataContext is MainViewModel viewModel)
            {
                _qqWindowScanner?.UpdateSettings(_settings.QQ, settingsService.DataDirectory);
                _windowBindingService?.ReloadSettings();
                viewModel.ReloadRuntimeSettings();
                viewModel.ShowBindingStatus("设置已保存");
            }
        }
    }

    private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            return;
        }

        if (_windowBindingService?.TryAutoDockAfterManualMove() == true && _settings is not null)
        {
            _settingsService?.Save(_settings);
        }
    }

    private void OnCategoryDragEnter(object sender, DragEventArgs e) => UpdateCategoryDropEffect(sender, e);

    private void OnCategoryScrollViewerPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer)
        {
            return;
        }

        scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset - e.Delta);
        e.Handled = true;
    }

    private void OnCategoryDragOver(object sender, DragEventArgs e) => UpdateCategoryDropEffect(sender, e);

    private void OnCategoryMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _categoryDragStartPoint = e.GetPosition(this);
        _dragCategory = sender is FrameworkElement { DataContext: StickerCategory category } ? category : null;
    }

    private void OnCategoryMouseMove(object sender, MouseEventArgs e)
    {
        if (_categoryDragStartPoint is null
            || _dragCategory is null
            || e.LeftButton != MouseButtonState.Pressed
            || DataContext is not MainViewModel viewModel
            || !viewModel.CanDragCategory(_dragCategory))
        {
            return;
        }

        var currentPoint = e.GetPosition(this);
        if (Math.Abs(currentPoint.X - _categoryDragStartPoint.Value.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(currentPoint.Y - _categoryDragStartPoint.Value.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var dragCategory = _dragCategory;
        _categoryDragStartPoint = null;
        _dragCategory = null;
        DragDrop.DoDragDrop(this, viewModel.CreateCategoryDragData(dragCategory), DragDropEffects.Move);
    }

    private void OnCategoryDrop(object sender, DragEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && sender is FrameworkElement { DataContext: StickerCategory category })
        {
            viewModel.DropFilesToCategory(e.Data, category);
        }

        e.Handled = true;
    }

    private void OnRenameStickerClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: StickerItem sticker })
        {
            RenameSticker(sticker);
        }
    }

    private void OnSetStickerTagsClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || sender is not FrameworkElement { DataContext: StickerItem sticker })
        {
            return;
        }

        var tags = ShowTextInputDialog("设置标签", "保存", sticker.TagText);
        if (tags is not null)
        {
            viewModel.SetStickerTagsCommand.Execute(new SetStickerTagsRequest(sticker, tags));
        }
    }

    private void OnClearStickerTagsClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && sender is FrameworkElement { DataContext: StickerItem sticker })
        {
            viewModel.ClearStickerTagsCommand.Execute(sticker);
        }
    }

    private void OnBatchAddTagsClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var tags = ShowTextInputDialog("给选中表情添加标签", "保存", string.Empty);
        if (tags is not null)
        {
            viewModel.AddTagsToSelectedCommand.Execute(tags);
        }
    }

    private void OnBatchRenameSelectedClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var baseName = ShowTextInputDialog("批量重命名", "保存", "表情");
        if (baseName is not null)
        {
            viewModel.BatchRenameSelectedStickersCommand.Execute(baseName);
        }
    }

    private void RenameSticker(StickerItem sticker)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var newName = ShowTextInputDialog("重命名表情", "保存", sticker.DisplayName);
        if (newName is not null)
        {
            viewModel.RenameStickerCommand.Execute(new RenameStickerRequest(sticker, newName));
        }
    }

    private void OnMoveStickerClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel
            || sender is not MenuItem { DataContext: StickerCategory category } menuItem
            || FindParentMenuItem(menuItem)?.Tag is not StickerItem sticker)
        {
            return;
        }

        viewModel.MoveStickerCommand.Execute(new MoveStickerRequest(sticker, category));
    }

    private void OnMergeCategoryClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel
            || sender is not FrameworkElement { DataContext: StickerCategory targetCategory }
            || FindContextMenu(sender as DependencyObject)?.DataContext is not StickerCategory sourceCategory)
        {
            return;
        }

        viewModel.MergeCategoryCommand.Execute(new MergeCategoryRequest(sourceCategory, targetCategory));
    }

    private void OnRenameCategoryClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || sender is not FrameworkElement { DataContext: StickerCategory category })
        {
            return;
        }

        var newName = ShowTextInputDialog("重命名分类", "保存", category.Name);
        if (newName is not null)
        {
            viewModel.RenameCategoryCommand.Execute(new RenameCategoryRequest(category, newName));
        }
    }

    private string? ShowTextInputDialog(string title, string okText, string initialText)
    {
        var dialog = new Window
        {
            Title = title,
            Owner = this,
            Width = 280,
            Height = 130,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = System.Windows.Media.Brushes.White
        };
        var textBox = new TextBox { Margin = new Thickness(12, 12, 12, 8), Text = initialText };
        var okButton = new Button { Content = okText, Width = 72, Margin = new Thickness(0, 0, 12, 12), IsDefault = true };
        var cancelButton = new Button { Content = "取消", Width = 72, Margin = new Thickness(0, 0, 12, 12), IsCancel = true };
        okButton.Click += (_, _) => dialog.DialogResult = true;

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(okButton);
        buttons.Children.Add(cancelButton);

        var panel = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        panel.Children.Add(buttons);
        panel.Children.Add(textBox);
        dialog.Content = panel;
        textBox.SelectAll();
        textBox.Focus();

        return dialog.ShowDialog() == true ? textBox.Text : null;
    }

    private static MenuItem? FindParentMenuItem(DependencyObject? current)
    {
        current = LogicalTreeHelper.GetParent(current);
        while (current is not null)
        {
            if (current is MenuItem menuItem)
            {
                return menuItem;
            }

            current = LogicalTreeHelper.GetParent(current);
        }

        return null;
    }

    private static ContextMenu? FindContextMenu(DependencyObject? current)
    {
        while (current is not null)
        {
            if (current is ContextMenu contextMenu)
            {
                return contextMenu;
            }

            if (current is FrameworkElement { Parent: DependencyObject parent })
            {
                current = parent;
                continue;
            }

            current = LogicalTreeHelper.GetParent(current);
        }

        return null;
    }

    private void UpdateDropEffect(DragEventArgs e)
    {
        e.Effects = DataContext is MainViewModel viewModel && viewModel.CanImportFiles(e.Data)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void UpdateCategoryDropEffect(object sender, DragEventArgs e)
    {
        e.Effects = DataContext is MainViewModel viewModel && sender is FrameworkElement { DataContext: StickerCategory category }
            ? viewModel.GetCategoryDropEffect(e.Data, category)
            : DragDropEffects.None;
        e.Handled = true;
    }
}
