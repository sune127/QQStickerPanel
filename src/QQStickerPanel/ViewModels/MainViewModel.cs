using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows.Input;
using System.Windows.Threading;
using QQStickerPanel.Models;
using QQStickerPanel.Services;

namespace QQStickerPanel.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private const int ClipboardBusyHResult = unchecked((int)0x800401D0);
    private const string CategoryDragFormat = "QQStickerPanel.CategoryDrag";
    private const int StickerLoadBatchSize = 80;
    private const int MaxImmediateStickerSwitchCount = 240;
    private const int DefaultStickerColumnCount = 3;
    private static readonly StickerLibrarySnapshot EmptySnapshot = new()
    {
        Stickers = [],
        DirectoryCategories = [],
        TagCategories = []
    };

    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;
    private readonly StickerLibraryService _libraryService;
    private readonly ClipboardService _clipboardService;
    private readonly RecentService _recentService;
    private readonly FavoriteService _favoriteService;
    private readonly DragDropService _dragDropService;
    private readonly StickerManagementService _managementService;
    private readonly StickerMetadataService _metadataService;
    private readonly HashSet<string> _selectedStickerPaths = new(StringComparer.OrdinalIgnoreCase);
    private string? _selectionAnchorPath;
    private PendingImport? _pendingImport;
    private bool _handledInternalCategoryDrop;
    private DateTimeOffset _suppressBackgroundRefreshUntil;
    private CancellationTokenSource? _filterLoadCancellation;
    private CancellationTokenSource? _refreshCancellation;
    private StickerLibrarySnapshot _snapshot;
    private StickerCategory? _selectedCategory;
    private string _statusText = string.Empty;
    private int _stickerColumnCount = DefaultStickerColumnCount;

    public MainViewModel(
        AppSettings settings,
        SettingsService settingsService,
        StickerLibraryService libraryService,
        ClipboardService clipboardService,
        RecentService recentService,
        FavoriteService favoriteService,
        DragDropService dragDropService,
        StickerManagementService managementService,
        StickerMetadataService metadataService)
    {
        _settings = settings;
        _settingsService = settingsService;
        _libraryService = libraryService;
        _clipboardService = clipboardService;
        _recentService = recentService;
        _favoriteService = favoriteService;
        _dragDropService = dragDropService;
        _managementService = managementService;
        _metadataService = metadataService;
        _snapshot = EmptySnapshot;
        CopyStickerCommand = new RelayCommand(parameter => CopySticker(parameter as StickerItem), parameter => parameter is StickerItem);
        SelectCategoryCommand = new RelayCommand(parameter => SelectCategory(parameter as StickerCategory), parameter => parameter is StickerCategory);
        OpenStickerRootCommand = new RelayCommand(_ => OpenStickerRoot());
        OpenStickerFolderCommand = new RelayCommand(parameter => OpenStickerFolder(parameter as StickerItem), parameter => parameter is StickerItem);
        DeleteStickerCommand = new RelayCommand(parameter => DeleteSticker(parameter as StickerItem), parameter => parameter is StickerItem);
        CreateCategoryCommand = new RelayCommand(parameter => CreateCategory(parameter as string));
        ImportSelectedFilesCommand = new RelayCommand(parameter => ImportSelectedFiles(parameter as IEnumerable<string>));
        ToggleFavoriteCommand = new RelayCommand(parameter => ToggleFavorite(parameter as StickerItem), parameter => parameter is StickerItem);
        SelectAllVisibleStickersCommand = new RelayCommand(_ => SelectAllVisibleStickers());
        ClearStickerSelectionCommand = new RelayCommand(_ => ClearStickerSelection());
        InvertVisibleStickerSelectionCommand = new RelayCommand(_ => InvertVisibleStickerSelection());
        CopySelectedStickersCommand = new RelayCommand(_ => CopySelectedStickers());
        DeleteSelectedStickersCommand = new RelayCommand(_ => DeleteSelectedStickers());
        FavoriteSelectedStickersCommand = new RelayCommand(_ => FavoriteSelectedStickers());
        UnfavoriteSelectedStickersCommand = new RelayCommand(_ => UnfavoriteSelectedStickers());
        PruneMissingStateCommand = new RelayCommand(_ => PruneMissingState());
        DeleteDuplicateStickersCommand = new RelayCommand(_ => DeleteDuplicateStickers());
        ConfirmPendingImportCommand = new RelayCommand(_ => ConfirmPendingImport(), _ => HasPendingImport);
        CancelPendingImportCommand = new RelayCommand(_ => CancelPendingImport(), _ => HasPendingImport);
        ToggleDockSideCommand = new RelayCommand(_ => ToggleDockSide());
        NarrowPanelCommand = new RelayCommand(_ => ChangePanelWidth(-40));
        WidenPanelCommand = new RelayCommand(_ => ChangePanelWidth(40));
        ResetPanelWidthCommand = new RelayCommand(_ => ResetPanelWidth());
        OpenSettingsFileCommand = new RelayCommand(_ => OpenSettingsFile());
        RenameStickerCommand = new RelayCommand(parameter => RenameSticker(parameter as RenameStickerRequest), parameter => parameter is RenameStickerRequest);
        SetStickerTagsCommand = new RelayCommand(parameter => SetStickerTags(parameter as SetStickerTagsRequest), parameter => parameter is SetStickerTagsRequest);
        AddTagsToSelectedCommand = new RelayCommand(parameter => AddTagsToSelected(parameter as string));
        ClearStickerTagsCommand = new RelayCommand(parameter => ClearStickerTags(parameter as StickerItem), parameter => parameter is StickerItem);
        BatchRenameSelectedStickersCommand = new RelayCommand(parameter => BatchRenameSelectedStickers(parameter as string));
        MoveStickerCommand = new RelayCommand(parameter => MoveSticker(parameter as MoveStickerRequest), parameter => parameter is MoveStickerRequest);
        MergeCategoryCommand = new RelayCommand(parameter => MergeCategory(parameter as MergeCategoryRequest), parameter => parameter is MergeCategoryRequest);
        OpenCategoryFolderCommand = new RelayCommand(parameter => OpenCategoryFolder(parameter as StickerCategory), parameter => parameter is StickerCategory);
        RenameCategoryCommand = new RelayCommand(parameter => RenameCategory(parameter as RenameCategoryRequest), parameter => parameter is RenameCategoryRequest);
        DeleteEmptyCategoryCommand = new RelayCommand(parameter => DeleteEmptyCategory(parameter as StickerCategory), parameter => parameter is StickerCategory { Kind: StickerCategoryKind.Directory, DirectoryPath: not null });
        LoadCachedLibrary();
        RefreshLibraryInBackground();
    }

    public ObservableCollection<StickerCategory> Categories { get; } = [];

    public ObservableCollection<StickerCategory> MoveTargetCategories { get; } = [];

    public List<StickerItem> Stickers { get; } = [];

    public ObservableCollection<IReadOnlyList<StickerItem>> StickerRows { get; } = [];

    public int StickerColumnCount
    {
        get => _stickerColumnCount;
        private set
        {
            if (_stickerColumnCount == value)
            {
                return;
            }

            _stickerColumnCount = value;
            OnPropertyChanged();
        }
    }

    public ICommand CopyStickerCommand { get; }

    public ICommand SelectCategoryCommand { get; }

    public ICommand OpenStickerRootCommand { get; }

    public ICommand OpenStickerFolderCommand { get; }

    public ICommand DeleteStickerCommand { get; }

    public ICommand CreateCategoryCommand { get; }

    public ICommand ImportSelectedFilesCommand { get; }

    public ICommand ToggleFavoriteCommand { get; }

    public ICommand SelectAllVisibleStickersCommand { get; }

    public ICommand ClearStickerSelectionCommand { get; }

    public ICommand InvertVisibleStickerSelectionCommand { get; }

    public ICommand CopySelectedStickersCommand { get; }

    public ICommand DeleteSelectedStickersCommand { get; }

    public ICommand FavoriteSelectedStickersCommand { get; }

    public ICommand UnfavoriteSelectedStickersCommand { get; }

    public ICommand PruneMissingStateCommand { get; }

    public ICommand DeleteDuplicateStickersCommand { get; }

    public ICommand ConfirmPendingImportCommand { get; }

    public ICommand CancelPendingImportCommand { get; }

    public ICommand ToggleDockSideCommand { get; }

    public ICommand NarrowPanelCommand { get; }

    public ICommand WidenPanelCommand { get; }

    public ICommand ResetPanelWidthCommand { get; }

    public ICommand OpenSettingsFileCommand { get; }

    public ICommand RenameStickerCommand { get; }

    public ICommand SetStickerTagsCommand { get; }

    public ICommand AddTagsToSelectedCommand { get; }

    public ICommand ClearStickerTagsCommand { get; }

    public ICommand BatchRenameSelectedStickersCommand { get; }

    public ICommand MoveStickerCommand { get; }

    public ICommand MergeCategoryCommand { get; }

    public ICommand OpenCategoryFolderCommand { get; }

    public ICommand RenameCategoryCommand { get; }

    public ICommand DeleteEmptyCategoryCommand { get; }

    public string ImportPreviewText
    {
        get => _pendingImport?.PreviewText ?? string.Empty;
        private set
        {
            if (ImportPreviewText == value)
            {
                return;
            }

            if (_pendingImport is not null)
            {
                _pendingImport = _pendingImport with { PreviewText = value };
            }

            OnPropertyChanged();
        }
    }

    public bool HasPendingImport => _pendingImport is not null;

    public StickerCategory? SelectedCategory
    {
        get => _selectedCategory;
        private set
        {
            if (_selectedCategory == value)
            {
                return;
            }

            _selectedCategory = value;
            OnPropertyChanged();
            ApplyFilter();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (_statusText == value)
            {
                return;
            }

            _statusText = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Func<ImportConfirmationRequest, ImportConfirmationResult?>? RequestImportConfirmation { get; set; }

    public void ReloadRuntimeSettings()
    {
        _recentService.UpdateLimit(_settings.RecentLimit);
        RefreshLibraryInBackground();
    }

    public void CancelBackgroundWork()
    {
        CancelRefresh();
        CancelFilterLoad();
    }

    public void ShowBindingStatus(string status)
    {
        StatusText = status;
    }

    public void SetStickerColumnCount(int columnCount)
    {
        var normalizedCount = Math.Max(1, columnCount);
        if (StickerColumnCount == normalizedCount)
        {
            return;
        }

        StickerColumnCount = normalizedCount;
        RebuildStickerRows();
    }

    public void SelectNextCategory()
    {
        SelectCategoryByOffset(1);
    }

    public void SelectPreviousCategory()
    {
        SelectCategoryByOffset(-1);
    }

    public void OpenSelectedCategoryDirectory()
    {
        var category = SelectedCategory;
        if (category is { Kind: StickerCategoryKind.Directory, DirectoryPath: not null })
        {
            OpenDirectory(category.DirectoryPath, $"已打开分类目录：{category.Name}", "打开分类目录失败");
            return;
        }

        OpenStickerRoot();
    }

    public void ToggleSelectedFavoriteState()
    {
        var stickers = GetSelectedStickers();
        if (stickers.Count == 0)
        {
            StatusText = "未选择表情包";
            return;
        }

        SetFavoriteState(stickers, stickers.Any(sticker => !sticker.IsFavorite), stickers[0].FileName);
    }

    public void NarrowPanel()
    {
        ChangePanelWidth(-40);
    }

    public void WidenPanel()
    {
        ChangePanelWidth(40);
    }

    public void ResetPanelWidthToDefault()
    {
        ResetPanelWidth();
    }

    public void RefreshLibrary()
    {
        CancelRefresh();
        var snapshot = _libraryService.RefreshSnapshotIncrementally();
        ApplyFavoriteState(snapshot);
        ApplyMetadataState(snapshot);
        LoadSnapshot(snapshot);
    }

    private void LoadCachedLibrary()
    {
        var snapshot = _libraryService.LoadCachedSnapshot();
        if (snapshot.Stickers.Count == 0 && snapshot.DirectoryCategories.Count == 0)
        {
            return;
        }

        ApplyFavoriteState(snapshot);
        ApplyMetadataState(snapshot);
        LoadSnapshot(snapshot);
        StatusText = $"已从缓存加载 {snapshot.Stickers.Count} 个表情包，正在后台校验...";
    }

    public void RefreshLibraryInBackground()
    {
        if (DateTimeOffset.Now < _suppressBackgroundRefreshUntil)
        {
            return;
        }

        var cancellation = ResetRefreshCancellation();
        var dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        StatusText = "正在加载表情包...";
        _ = Task.Run(() =>
            {
                var snapshot = _libraryService.RefreshSnapshotIncrementally();
                ApplyFavoriteState(snapshot);
                ApplyMetadataState(snapshot);
                return snapshot;
            }, cancellation.Token)
            .ContinueWith(task => dispatcher.BeginInvoke(new Action(() =>
            {
                if (cancellation.IsCancellationRequested)
                {
                    return;
                }

                if (task.IsCompletedSuccessfully)
                {
                    LoadSnapshot(task.Result);
                    return;
                }

                if (task.Exception is not null)
                {
                    StatusText = $"加载表情包失败：{task.Exception.GetBaseException().Message}";
                }
            }), DispatcherPriority.Background), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    private void LoadSnapshot(StickerLibrarySnapshot snapshot)
    {
        _snapshot = snapshot;
        RebuildCategories();
        StatusText = _snapshot.Stickers.Count == 0 ? "未找到表情包，请把图片放入配置目录" : $"已加载 {_snapshot.Stickers.Count} 个表情包";
    }

    public bool CanImportFiles(System.Windows.IDataObject dataObject)
    {
        return _dragDropService.HasSupportedFiles(dataObject)
            || _dragDropService.HasSupportedTextFiles(dataObject)
            || _dragDropService.HasSupportedVirtualFiles(dataObject)
            || _dragDropService.HasSupportedImageData(dataObject);
    }

    public bool CanDropFilesToCategory(System.Windows.IDataObject dataObject, StickerCategory category)
    {
        return GetCategoryDropEffect(dataObject, category) != System.Windows.DragDropEffects.None;
    }

    public System.Windows.DragDropEffects GetCategoryDropEffect(System.Windows.IDataObject dataObject, StickerCategory category)
    {
        if (dataObject.GetDataPresent(CategoryDragFormat))
        {
            return CanDropCategoryOn(dataObject, category)
                ? System.Windows.DragDropEffects.Move
                : System.Windows.DragDropEffects.None;
        }

        if (category.Kind is not (StickerCategoryKind.Directory or StickerCategoryKind.Uncategorized))
        {
            return System.Windows.DragDropEffects.None;
        }

        if (_dragDropService.HasInternalStickerFiles(dataObject))
        {
            return System.Windows.DragDropEffects.Copy;
        }

        return _dragDropService.HasSupportedFiles(dataObject)
            || _dragDropService.HasSupportedTextFiles(dataObject)
            || _dragDropService.HasSupportedVirtualFiles(dataObject)
            || _dragDropService.HasSupportedImageData(dataObject)
            ? System.Windows.DragDropEffects.Copy
            : System.Windows.DragDropEffects.None;
    }

    public void ImportFiles(System.Windows.IDataObject dataObject)
    {
        var files = _dragDropService.GetSupportedFiles(dataObject);
        if (files.Count > 0)
        {
            ImportSelectedFiles(files);
            return;
        }

        var textFiles = _dragDropService.GetSupportedTextFiles(dataObject);
        if (textFiles.Count > 0)
        {
            ImportSelectedFiles(textFiles);
            return;
        }

        if (_dragDropService.HasSupportedVirtualFiles(dataObject))
        {
            ImportVirtualFilesToDirectory(dataObject, GetImportDirectory(), SelectedCategory?.Kind == StickerCategoryKind.Directory ? SelectedCategory.Name : null);
            return;
        }

        if (_dragDropService.TryGetBitmapSource(dataObject, out var image))
        {
            ImportClipboardImage(image);
            return;
        }

        StatusText = "未找到可导入的图片文件";
    }

    public void DropFilesToCategory(System.Windows.IDataObject dataObject, StickerCategory category)
    {
        if (GetCategoryDropEffect(dataObject, category) == System.Windows.DragDropEffects.None)
        {
            return;
        }

        if (TryGetDraggedCategoryKey(dataObject, out var draggedCategoryKey))
        {
            ReorderCategory(draggedCategoryKey, category);
            return;
        }

        if (_dragDropService.HasInternalStickerFiles(dataObject))
        {
            _handledInternalCategoryDrop = true;
            MoveFilesToCategory(_dragDropService.GetInternalStickerFiles(dataObject), category);
            return;
        }

        var files = _dragDropService.GetSupportedFiles(dataObject);
        if (files.Count > 0)
        {
            ImportFilesToCategory(files, category);
            return;
        }

        if (_dragDropService.HasSupportedVirtualFiles(dataObject))
        {
            ImportVirtualFilesToDirectory(dataObject, GetCategoryDirectory(category), category.Kind == StickerCategoryKind.Directory ? category.Name : null);
            return;
        }

        var textFiles = _dragDropService.GetSupportedTextFiles(dataObject);
        if (textFiles.Count > 0)
        {
            ImportFilesToCategory(textFiles, category);
            return;
        }

        if (_dragDropService.TryGetBitmapSource(dataObject, out var image))
        {
            ImportClipboardImage(image, category);
        }
    }

    public bool ConsumeHandledInternalCategoryDrop()
    {
        if (!_handledInternalCategoryDrop)
        {
            return false;
        }

        _handledInternalCategoryDrop = false;
        return true;
    }

    public System.Windows.DataObject CreateStickerDragData(StickerItem sticker)
    {
        var stickers = GetDragStickers(sticker);
        return _dragDropService.CreateStickerDataObject(stickers);
    }

    public bool CanDragCategory(StickerCategory category)
    {
        return category.Kind == StickerCategoryKind.Directory;
    }

    public System.Windows.DataObject CreateCategoryDragData(StickerCategory category)
    {
        var dataObject = new System.Windows.DataObject();
        if (CanDragCategory(category))
        {
            dataObject.SetData(CategoryDragFormat, category.Key);
        }

        return dataObject;
    }

    private bool CanDropCategoryOn(System.Windows.IDataObject dataObject, StickerCategory targetCategory)
    {
        return targetCategory.Kind == StickerCategoryKind.Directory
            && TryGetDraggedCategoryKey(dataObject, out var sourceKey)
            && !string.Equals(sourceKey, targetCategory.Key, StringComparison.Ordinal)
            && _snapshot.DirectoryCategories.Any(category => string.Equals(category.Key, sourceKey, StringComparison.Ordinal));
    }

    private static bool TryGetDraggedCategoryKey(System.Windows.IDataObject dataObject, out string categoryKey)
    {
        categoryKey = string.Empty;
        if (!dataObject.GetDataPresent(CategoryDragFormat) || dataObject.GetData(CategoryDragFormat) is not string value || string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        categoryKey = value;
        return true;
    }

    private void ReorderCategory(string sourceCategoryKey, StickerCategory targetCategory)
    {
        if (targetCategory.Kind != StickerCategoryKind.Directory || string.Equals(sourceCategoryKey, targetCategory.Key, StringComparison.Ordinal))
        {
            return;
        }

        var orderedKeys = GetOrderedDirectoryCategories()
            .Select(GetCategoryOrderKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var sourceOrderKey = GetCategoryOrderKey(sourceCategoryKey);
        var targetOrderKey = GetCategoryOrderKey(targetCategory);
        if (string.IsNullOrWhiteSpace(sourceOrderKey) || string.IsNullOrWhiteSpace(targetOrderKey))
        {
            return;
        }

        orderedKeys.RemoveAll(key => string.Equals(key, sourceOrderKey, StringComparison.OrdinalIgnoreCase));
        var targetIndex = orderedKeys.FindIndex(key => string.Equals(key, targetOrderKey, StringComparison.OrdinalIgnoreCase));
        if (targetIndex < 0)
        {
            orderedKeys.Add(sourceOrderKey);
        }
        else
        {
            orderedKeys.Insert(targetIndex, sourceOrderKey);
        }

        _settings.CategoryOrder = orderedKeys;
        _settingsService.Save(_settings);
        RebuildCategories();
        StatusText = "分类顺序已保存";
    }

    public void RecordStickerUse(StickerItem sticker)
    {
        var stickers = GetDragStickers(sticker);
        _suppressBackgroundRefreshUntil = DateTimeOffset.Now.AddSeconds(1);
        foreach (var dragSticker in stickers)
        {
            _recentService.RecordUse(dragSticker);
        }

        RefreshCategoryCounters();
        StatusText = stickers.Count == 1 ? $"已拖出：{sticker.FileName}" : $"已拖出 {stickers.Count} 个表情包";
    }

    public void ShowPasteResult(StickerItem sticker, bool pastedToQQ)
    {
        var stickers = GetDragStickers(sticker);
        if (stickers.Count == 1)
        {
            StatusText = pastedToQQ ? $"已粘贴到 QQ：{stickers[0].FileName}" : $"已复制，请在 QQ 中粘贴：{stickers[0].FileName}";
            return;
        }

        StatusText = pastedToQQ ? $"已粘贴 {stickers.Count} 个表情包到 QQ" : $"已复制 {stickers.Count} 个表情包，请在 QQ 中粘贴";
    }

    public void ToggleStickerSelection(StickerItem sticker)
    {
        _selectionAnchorPath = sticker.FilePath;
        sticker.IsSelected = !sticker.IsSelected;
        if (sticker.IsSelected)
        {
            _selectedStickerPaths.Add(sticker.FilePath);
        }
        else
        {
            _selectedStickerPaths.Remove(sticker.FilePath);
        }

        StatusText = _selectedStickerPaths.Count == 0 ? "已清除选择" : $"已选择 {_selectedStickerPaths.Count} 个表情包";
    }

    public void SelectStickerRange(StickerItem sticker)
    {
        var targetIndex = Stickers.IndexOf(sticker);
        if (targetIndex < 0)
        {
            return;
        }

        var anchorIndex = _selectionAnchorPath is null
            ? -1
            : Stickers.Select((visibleSticker, index) => new { visibleSticker, index })
                .FirstOrDefault(item => string.Equals(item.visibleSticker.FilePath, _selectionAnchorPath, StringComparison.OrdinalIgnoreCase))
                ?.index ?? -1;
        if (anchorIndex < 0)
        {
            anchorIndex = targetIndex;
        }

        var startIndex = Math.Min(anchorIndex, targetIndex);
        var endIndex = Math.Max(anchorIndex, targetIndex);
        for (var index = startIndex; index <= endIndex; index++)
        {
            var rangeSticker = Stickers[index];
            rangeSticker.IsSelected = true;
            _selectedStickerPaths.Add(rangeSticker.FilePath);
        }

        _selectionAnchorPath = sticker.FilePath;
        StatusText = $"已选择 {_selectedStickerPaths.Count} 个表情包";
    }

    public void DeleteStickers(StickerItem sticker)
    {
        var stickers = GetSelectedStickers().Count > 0 && sticker.IsSelected
            ? GetSelectedStickers()
            : [sticker];
        DeleteStickers(stickers, sticker.FileName);
    }

    public void DeleteSelectedStickers()
    {
        var stickers = GetSelectedStickers();
        if (stickers.Count == 0)
        {
            return;
        }

        DeleteStickers(stickers, stickers[0].FileName);
    }

    private void DeleteStickers(IReadOnlyList<StickerItem> stickers, string singleFileName)
    {
        var deletedCount = 0;

        foreach (var selectedSticker in stickers)
        {
            try
            {
                _managementService.DeleteSticker(selectedSticker);
                _favoriteService.Remove(selectedSticker);
                _recentService.Remove(selectedSticker);
                _metadataService.Remove(selectedSticker);
                _selectedStickerPaths.Remove(selectedSticker.FilePath);
                deletedCount++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                StatusText = $"删除失败：{ex.Message}";
                RefreshLibrary();
                return;
            }
        }

        RefreshLibrary();
        StatusText = deletedCount == 1 ? $"已移入回收站：{singleFileName}" : $"已移入回收站 {deletedCount} 个表情包";
    }

    public void CopySelectedStickers()
    {
        var stickers = GetSelectedStickers();
        if (stickers.Count == 0)
        {
            StatusText = "未选择表情包";
            return;
        }

        CopyStickers(stickers);
    }

    public void SelectAllVisibleStickers()
    {
        foreach (var sticker in Stickers)
        {
            sticker.IsSelected = true;
            _selectedStickerPaths.Add(sticker.FilePath);
        }

        StatusText = Stickers.Count == 0 ? "当前列表没有表情包" : $"已选择 {Stickers.Count} 个表情包";
    }

    public void ClearStickerSelection()
    {
        _selectionAnchorPath = null;
        _selectedStickerPaths.Clear();
        foreach (var sticker in _snapshot.Stickers)
        {
            sticker.IsSelected = false;
        }

        StatusText = "已清除选择";
    }

    public void InvertVisibleStickerSelection()
    {
        if (Stickers.Count == 0)
        {
            StatusText = "当前列表没有表情包";
            return;
        }

        foreach (var sticker in Stickers)
        {
            sticker.IsSelected = !sticker.IsSelected;
            if (sticker.IsSelected)
            {
                _selectedStickerPaths.Add(sticker.FilePath);
            }
            else
            {
                _selectedStickerPaths.Remove(sticker.FilePath);
            }
        }

        StatusText = _selectedStickerPaths.Count == 0 ? "已清除选择" : $"已选择 {_selectedStickerPaths.Count} 个表情包";
    }

    public void FavoriteSelectedStickers()
    {
        SetSelectedFavoriteState(true);
    }

    public void UnfavoriteSelectedStickers()
    {
        SetSelectedFavoriteState(false);
    }

    private void PruneMissingState()
    {
        var recentCount = _recentService.PruneMissingFiles();
        var favoriteCount = _favoriteService.PruneMissingFiles();
        var metadataCount = _metadataService.PruneMissingFiles();
        RefreshLibrary();
        StatusText = recentCount + favoriteCount + metadataCount == 0
            ? "没有需要清理的记录"
            : $"已清理 {recentCount} 条最近记录、{favoriteCount} 条收藏记录、{metadataCount} 条元数据";
    }

    public int CountDuplicateStickers()
    {
        return FindDuplicateStickers().Count;
    }

    private void DeleteDuplicateStickers()
    {
        var duplicates = FindDuplicateStickers();
        if (duplicates.Count == 0)
        {
            StatusText = "没有重复表情包";
            return;
        }

        DeleteStickers(duplicates, duplicates[0].FileName);
        StatusText = $"已移入回收站 {duplicates.Count} 个重复表情包";
    }

    private void ToggleDockSide()
    {
        _settings.Dock.Side = _settings.Dock.Side switch
        {
            DockSide.Right => DockSide.InnerRight,
            DockSide.InnerRight => DockSide.Left,
            DockSide.Left => DockSide.InnerLeft,
            DockSide.InnerLeft => DockSide.Top,
            DockSide.Top => DockSide.Bottom,
            _ => DockSide.Right
        };
        _settingsService.Save(_settings);
        StatusText = $"已切换为吸附 QQ {GetDockSideText(_settings.Dock.Side)}";
    }

    private static string GetDockSideText(DockSide side)
    {
        return side switch
        {
            DockSide.Left => "左侧",
            DockSide.InnerLeft => "QQ 内左侧",
            DockSide.InnerRight => "QQ 内右侧",
            DockSide.Top => "上方",
            DockSide.Bottom => "下方",
            _ => "右侧"
        };
    }

    private void ChangePanelWidth(int delta)
    {
        _settings.Dock.Width = Math.Clamp(_settings.Dock.Width + delta, 240, 640);
        _settingsService.Save(_settings);
        StatusText = $"面板宽度：{_settings.Dock.Width}";
    }

    private void ResetPanelWidth()
    {
        _settings.Dock.Width = 320;
        _settingsService.Save(_settings);
        StatusText = "面板宽度已恢复默认：320";
    }

    private void OpenSettingsFile()
    {
        try
        {
            _settingsService.Save(_settings);
            Process.Start(new ProcessStartInfo
            {
                FileName = _settingsService.SettingsPath,
                UseShellExecute = true
            });
            StatusText = "已打开配置文件";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            StatusText = $"打开配置失败：{ex.Message}";
        }
    }

    public bool ChangeStickerRoot(string stickerRoot)
    {
        if (string.IsNullOrWhiteSpace(stickerRoot))
        {
            StatusText = "表情目录不能为空";
            return false;
        }

        try
        {
            _settings.StickerRoot = Path.GetFullPath(stickerRoot);
            _settings.CategoryOrder.Clear();
            _settingsService.Save(_settings);
            RefreshLibraryInBackground();
            StatusText = $"正在切换表情目录：{_settings.StickerRoot}";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            StatusText = $"切换表情目录失败：{ex.Message}";
            return false;
        }
    }

    private void ApplyFavoriteState(StickerLibrarySnapshot snapshot)
    {
        foreach (var sticker in snapshot.Stickers)
        {
            sticker.IsFavorite = _favoriteService.IsFavorite(sticker);
            sticker.IsSelected = _selectedStickerPaths.Contains(sticker.FilePath);
        }
    }

    private void ApplyMetadataState(StickerLibrarySnapshot snapshot)
    {
        var tagsByPath = _metadataService.GetTagsByPath(snapshot.Stickers);
        foreach (var sticker in snapshot.Stickers)
        {
            sticker.Tags = tagsByPath.TryGetValue(sticker.FilePath, out var tags) ? tags : [];
        }
    }

    private void RebuildCategories()
    {
        var previousKey = SelectedCategory?.Key;
        var favoriteCount = _favoriteService.GetFavoriteStickers(_snapshot.Stickers).Count;
        var recentCount = _recentService.GetRecentStickers(_snapshot.Stickers).Count;
        var allCount = _snapshot.Stickers.Count;
        var uncategorizedCount = _snapshot.Stickers.Count(sticker => sticker.IsUncategorized);
        var tagCategories = _snapshot.Stickers
            .SelectMany(sticker => sticker.Tags)
            .GroupBy(tag => tag, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => new StickerCategory { Key = $"tag:{group.Key}", Name = $"#{group.Key}", Kind = StickerCategoryKind.Tag, Count = group.Count() })
            .OrderBy(category => category.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var directoryCategories = GetOrderedDirectoryCategories().ToList();

        Categories.Clear();
        Categories.Add(new StickerCategory { Key = "recent", Name = "最近", Kind = StickerCategoryKind.Recent, Count = recentCount });
        Categories.Add(new StickerCategory { Key = "favorites", Name = "收藏", Kind = StickerCategoryKind.Favorites, Count = favoriteCount });
        Categories.Add(new StickerCategory { Key = "uncategorized", Name = "未分类", Kind = StickerCategoryKind.Uncategorized, Count = uncategorizedCount });

        foreach (var category in tagCategories)
        {
            Categories.Add(category);
        }

        foreach (var category in directoryCategories)
        {
            Categories.Add(category);
        }

        Categories.Add(new StickerCategory { Key = "all", Name = "全部", Kind = StickerCategoryKind.All, Count = allCount });

        RebuildMoveTargetCategories();

        SelectedCategory = Categories.FirstOrDefault(category => string.Equals(category.Key, previousKey, StringComparison.Ordinal))
            ?? Categories.FirstOrDefault(category => category.Kind == StickerCategoryKind.Recent)
            ?? Categories.FirstOrDefault(category => category.Kind == StickerCategoryKind.All)
            ?? Categories.FirstOrDefault();
    }

    private void SelectCategory(StickerCategory? category)
    {
        if (category is null)
        {
            return;
        }

        SelectedCategory = category;
    }

    private void SelectCategoryByOffset(int offset)
    {
        if (Categories.Count == 0)
        {
            return;
        }

        var currentIndex = SelectedCategory is null ? -1 : Categories.IndexOf(SelectedCategory);
        var nextIndex = currentIndex < 0
            ? 0
            : (currentIndex + offset + Categories.Count) % Categories.Count;
        SelectedCategory = Categories[nextIndex];
        StatusText = $"当前分类：{SelectedCategory.Name}";
    }

    private void RebuildMoveTargetCategories()
    {
        MoveTargetCategories.Clear();
        MoveTargetCategories.Add(new StickerCategory { Key = "uncategorized", Name = "未分类", Kind = StickerCategoryKind.Uncategorized, Count = 0 });
        foreach (var category in GetOrderedDirectoryCategories())
        {
            MoveTargetCategories.Add(category);
        }
    }

    private IEnumerable<StickerCategory> GetOrderedDirectoryCategories()
    {
        var order = (_settings.CategoryOrder ?? [])
            .Select((key, index) => new { Key = NormalizeCategoryOrderKey(key), Index = index })
            .Where(item => item.Key.Length > 0)
            .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Index, StringComparer.OrdinalIgnoreCase);

        return _snapshot.DirectoryCategories
            .OrderBy(category => order.TryGetValue(GetCategoryOrderKey(category), out var index) ? index : int.MaxValue)
            .ThenBy(category => category.Name, StringComparer.CurrentCultureIgnoreCase);
    }

    private string GetCategoryOrderKey(StickerCategory category)
    {
        return category.DirectoryPath is not null ? GetCategoryOrderKey(category.DirectoryPath) : GetCategoryOrderKey(category.Key);
    }

    private string GetCategoryOrderKey(string categoryKey)
    {
        try
        {
            if (Path.IsPathFullyQualified(categoryKey))
            {
                return NormalizeCategoryOrderKey(Path.GetRelativePath(_settings.StickerRoot, categoryKey));
            }
        }
        catch (ArgumentException)
        {
        }

        return NormalizeCategoryOrderKey(categoryKey);
    }

    private static string NormalizeCategoryOrderKey(string key)
    {
        return key.Trim().Replace('\\', '/');
    }

    private void RefreshCategoryCounters()
    {
        var selectedKey = SelectedCategory?.Key;
        var categories = Categories.ToList();
        foreach (var category in categories)
        {
            category.Count = CountStickersInCategory(category);
        }

        if (selectedKey is not null)
        {
            _selectedCategory = categories.FirstOrDefault(category => string.Equals(category.Key, selectedKey, StringComparison.Ordinal));
            OnPropertyChanged(nameof(SelectedCategory));
        }
    }

    private int CountStickersInCategory(StickerCategory category)
    {
        return category.Kind switch
        {
            StickerCategoryKind.Recent => _recentService.GetRecentStickers(_snapshot.Stickers).Count,
            StickerCategoryKind.Favorites => _favoriteService.GetFavoriteStickers(_snapshot.Stickers).Count,
            StickerCategoryKind.All => _snapshot.Stickers.Count,
            StickerCategoryKind.Uncategorized => _snapshot.Stickers.Count(sticker => sticker.IsUncategorized),
            StickerCategoryKind.Tag => _snapshot.Stickers.Count(sticker => sticker.Tags.Any(tag => string.Equals($"tag:{tag}", category.Key, StringComparison.CurrentCultureIgnoreCase))),
            StickerCategoryKind.Directory => _snapshot.Stickers.Count(sticker => string.Equals(sticker.CategoryKey, category.Key, StringComparison.Ordinal)),
            _ => 0
        };
    }

    private void ApplyFilter()
    {
        var selectedCategory = SelectedCategory;
        var source = GetFilteredStickers(selectedCategory).ToList();
        CancelFilterLoad();
        Stickers.Clear();
        Stickers.AddRange(source);
        RebuildStickerRows();
    }

    private void RebuildStickerRows()
    {
        StickerRows.Clear();
        for (var index = 0; index < Stickers.Count; index += StickerColumnCount)
        {
            StickerRows.Add(Stickers.Skip(index).Take(StickerColumnCount).ToList());
        }
    }

    private IEnumerable<StickerItem> GetFilteredStickers(StickerCategory? selectedCategory)
    {
        return selectedCategory?.Kind switch
        {
            StickerCategoryKind.Recent => _recentService.GetRecentStickers(_snapshot.Stickers),
            StickerCategoryKind.Favorites => _favoriteService.GetFavoriteStickers(_snapshot.Stickers),
            StickerCategoryKind.Uncategorized => _snapshot.Stickers.Where(sticker => sticker.IsUncategorized),
            StickerCategoryKind.Tag => _snapshot.Stickers.Where(sticker => sticker.Tags.Any(tag => string.Equals($"tag:{tag}", selectedCategory.Key, StringComparison.CurrentCultureIgnoreCase))),
            StickerCategoryKind.Directory => _snapshot.Stickers.Where(sticker => string.Equals(sticker.CategoryKey, selectedCategory.Key, StringComparison.Ordinal)),
            _ => _snapshot.Stickers
        };
    }

    private void AddStickerBatch(IReadOnlyList<StickerItem> source, int startIndex, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        var endIndex = Math.Min(startIndex + StickerLoadBatchSize, source.Count);
        for (var index = startIndex; index < endIndex; index++)
        {
            Stickers.Add(source[index]);
        }

        if (endIndex >= source.Count)
        {
            RebuildStickerRows();
            return;
        }

        Dispatcher.CurrentDispatcher.BeginInvoke(
            new Action(() => AddStickerBatch(source, endIndex, cancellationToken)),
            DispatcherPriority.Background);
    }

    private CancellationTokenSource ResetFilterCancellation()
    {
        _filterLoadCancellation?.Cancel();
        _filterLoadCancellation?.Dispose();
        _filterLoadCancellation = new CancellationTokenSource();
        return _filterLoadCancellation;
    }

    private CancellationTokenSource ResetRefreshCancellation()
    {
        _refreshCancellation?.Cancel();
        _refreshCancellation?.Dispose();
        _refreshCancellation = new CancellationTokenSource();
        return _refreshCancellation;
    }

    private void CancelRefresh()
    {
        _refreshCancellation?.Cancel();
        _refreshCancellation?.Dispose();
        _refreshCancellation = null;
    }

    private void CancelFilterLoad()
    {
        _filterLoadCancellation?.Cancel();
        _filterLoadCancellation?.Dispose();
        _filterLoadCancellation = null;
    }

    private void CopySticker(StickerItem? sticker)
    {
        if (sticker is null)
        {
            return;
        }

        CopyStickers(sticker.IsSelected ? GetSelectedStickers() : [sticker]);
    }

    private void CopyStickers(IReadOnlyList<StickerItem> stickers)
    {
        if (stickers.Count == 0)
        {
            StatusText = "未选择表情包";
            return;
        }

        try
        {
            _clipboardService.CopyStickers(stickers);
            _suppressBackgroundRefreshUntil = DateTimeOffset.Now.AddSeconds(1);
            foreach (var sticker in stickers)
            {
                _recentService.RecordUse(sticker);
            }

            RefreshCategoryCounters();
            StatusText = stickers.Count == 1 ? $"已复制到剪贴板：{stickers[0].FileName}" : $"已复制 {stickers.Count} 个表情包到剪贴板";
        }
        catch (COMException ex) when (ex.HResult == ClipboardBusyHResult)
        {
            _suppressBackgroundRefreshUntil = DateTimeOffset.Now.AddSeconds(1);
            foreach (var sticker in stickers)
            {
                _recentService.RecordUse(sticker);
            }

            RefreshCategoryCounters();
            StatusText = stickers.Count == 1 ? $"已尝试复制，可直接粘贴：{stickers[0].FileName}" : $"已尝试复制 {stickers.Count} 个表情包，可直接粘贴";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException or NotSupportedException)
        {
            StatusText = $"复制失败：{ex.Message}";
        }
    }

    private string GetImportDirectory()
    {
        return GetCategoryDirectory(SelectedCategory);
    }

    private string GetCategoryDirectory(StickerCategory? category)
    {
        return category is { Kind: StickerCategoryKind.Directory, DirectoryPath: not null }
            ? category.DirectoryPath
            : _settings.StickerRoot;
    }

    private void ImportSelectedFiles(IEnumerable<string>? sourceFiles)
    {
        ImportFilesToDirectory(sourceFiles, GetImportDirectory(), null);
    }

    private void ImportFilesToCategory(IEnumerable<string>? sourceFiles, StickerCategory category)
    {
        ImportFilesToDirectory(sourceFiles, GetCategoryDirectory(category), category.Name);
    }

    private void ImportFilesToDirectory(IEnumerable<string>? sourceFiles, string targetDirectory, string? categoryName)
    {
        if (sourceFiles is null)
        {
            StatusText = "未找到可导入的图片文件";
            return;
        }

        var fileList = sourceFiles.ToList();
        if (fileList.Count == 0)
        {
            StatusText = "未找到可导入的图片文件";
            return;
        }

        var duplicateCount = _dragDropService.CountDuplicateFiles(fileList, _snapshot.Stickers, _metadataService.GetHash);
        if (fileList.Count == 1 && duplicateCount == 0)
        {
            ExecuteImportFiles(fileList, targetDirectory, categoryName);
            return;
        }

        if (RequestImportConfirmation is not null)
        {
            var result = RequestImportConfirmation(new ImportConfirmationRequest(
                fileList,
                GetImportTargetCategories(),
                GetInitialImportCategory(targetDirectory),
                duplicateCount));
            if (result is null)
            {
                StatusText = "已取消导入";
                return;
            }

            var targetCategory = ResolveImportCategory(result);
            ExecuteImportFiles(
                fileList,
                GetCategoryDirectory(targetCategory),
                targetCategory.Kind == StickerCategoryKind.Directory ? targetCategory.Name : null,
                result.Deduplicate,
                result.TagText,
                result.IsFavorite);
            return;
        }

        if (fileList.Count > 1 || duplicateCount > 0)
        {
            SetPendingImport(new PendingImport(fileList, targetDirectory, categoryName, $"待导入 {fileList.Count} 个表情包，预计跳过 {duplicateCount} 个重复。右键状态栏确认或取消。"));
            return;
        }

        ExecuteImportFiles(fileList, targetDirectory, categoryName);
    }

    private void ExecuteImportFiles(IEnumerable<string> sourceFiles, string targetDirectory, string? categoryName)
    {
        ExecuteImportFiles(sourceFiles, targetDirectory, categoryName, true, string.Empty, false);
    }

    private void ExecuteImportFiles(IEnumerable<string> sourceFiles, string targetDirectory, string? categoryName, bool deduplicate, string tagText, bool isFavorite)
    {
        try
        {
            var importResult = _dragDropService.ImportFiles(sourceFiles, targetDirectory, _snapshot.Stickers, _metadataService.GetHash, deduplicate);
            ClearPendingImport();
            RefreshLibrary();
            ApplyImportMetadata(importResult.ImportedFiles, tagText, isFavorite);
            StatusText = CreateImportStatus(importResult, categoryName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            ClearPendingImport();
            StatusText = $"导入失败：{ex.Message}";
        }
    }

    private StickerCategory ResolveImportCategory(ImportConfirmationResult result)
    {
        if (string.IsNullOrWhiteSpace(result.NewCategoryName))
        {
            return result.Category;
        }

        var directoryPath = _managementService.CreateCategory(result.NewCategoryName);
        AddCategoryToOrder(directoryPath);
        RefreshLibrary();
        var normalizedPath = Path.GetFullPath(directoryPath);
        return MoveTargetCategories.FirstOrDefault(category =>
                category.DirectoryPath is not null && string.Equals(Path.GetFullPath(category.DirectoryPath), normalizedPath, StringComparison.OrdinalIgnoreCase))
            ?? new StickerCategory
            {
                Key = Path.GetRelativePath(_settings.StickerRoot, directoryPath).Replace(Path.DirectorySeparatorChar, '/'),
                Name = Path.GetRelativePath(_settings.StickerRoot, directoryPath).Replace(Path.DirectorySeparatorChar, '/'),
                Kind = StickerCategoryKind.Directory,
                DirectoryPath = directoryPath,
                Count = 0
            };
    }

    private StickerCategory GetClipboardImportCategory()
    {
        return new StickerCategory { Key = "clipboard", Name = "剪贴板图片", Kind = StickerCategoryKind.Uncategorized, Count = 1 };
    }

    private IReadOnlyList<StickerCategory> GetImportTargetCategories()
    {
        return MoveTargetCategories.ToList();
    }

    private StickerCategory? GetInitialImportCategory(string targetDirectory)
    {
        var normalizedTarget = Path.GetFullPath(targetDirectory);
        return MoveTargetCategories.FirstOrDefault(category =>
                string.Equals(Path.GetFullPath(GetCategoryDirectory(category)), normalizedTarget, StringComparison.OrdinalIgnoreCase))
            ?? MoveTargetCategories.FirstOrDefault();
    }

    private void ApplyImportMetadata(IReadOnlyList<string> importedFiles, string tagText, bool isFavorite)
    {
        if (importedFiles.Count == 0)
        {
            return;
        }

        var importedPathSet = importedFiles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var importedStickers = _snapshot.Stickers
            .Where(sticker => importedPathSet.Contains(sticker.FilePath))
            .ToList();
        if (importedStickers.Count == 0)
        {
            return;
        }

        var tags = StickerMetadataService.NormalizeTags([tagText]);
        if (tags.Count > 0)
        {
            _metadataService.SetTags(importedStickers, tags);
        }

        if (isFavorite)
        {
            _favoriteService.AddRange(importedStickers);
        }

        if (tags.Count > 0 || isFavorite)
        {
            RefreshLibrary();
        }
    }

    private void ImportVirtualFilesToDirectory(System.Windows.IDataObject dataObject, string targetDirectory, string? categoryName)
    {
        try
        {
            if (RequestImportConfirmation is not null)
            {
                var sourceFiles = _dragDropService.GetSupportedVirtualFileNames(dataObject);
                var duplicateCount = _dragDropService.CountDuplicateVirtualFiles(dataObject, _snapshot.Stickers, _metadataService.GetHash);
                var result = RequestImportConfirmation(new ImportConfirmationRequest(
                    sourceFiles,
                    GetImportTargetCategories(),
                    GetInitialImportCategory(targetDirectory),
                    duplicateCount));
                if (result is null)
                {
                    StatusText = "已取消导入";
                    return;
                }

                var targetCategory = ResolveImportCategory(result);
                ExecuteImportVirtualFiles(
                    dataObject,
                    GetCategoryDirectory(targetCategory),
                    targetCategory.Kind == StickerCategoryKind.Directory ? targetCategory.Name : null,
                    result.Deduplicate,
                    result.TagText,
                    result.IsFavorite);
                return;
            }

            ExecuteImportVirtualFiles(dataObject, targetDirectory, categoryName, true, string.Empty, false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException)
        {
            StatusText = $"导入虚拟文件失败：{ex.Message}";
        }
    }

    private void ExecuteImportVirtualFiles(System.Windows.IDataObject dataObject, string targetDirectory, string? categoryName, bool deduplicate, string tagText, bool isFavorite)
    {
        var importResult = _dragDropService.ImportVirtualFiles(dataObject, targetDirectory, _snapshot.Stickers, _metadataService.GetHash, deduplicate);
        RefreshLibrary();
        ApplyImportMetadata(importResult.ImportedFiles, tagText, isFavorite);
        StatusText = CreateImportStatus(importResult, categoryName);
    }

    private void ImportClipboardImage(System.Windows.Media.Imaging.BitmapSource image)
    {
        ImportClipboardImage(image, GetImportDirectory(), SelectedCategory?.Kind == StickerCategoryKind.Directory ? SelectedCategory.Name : null);
    }

    private void ImportClipboardImage(System.Windows.Media.Imaging.BitmapSource image, StickerCategory category)
    {
        ImportClipboardImage(image, GetCategoryDirectory(category), category.Kind == StickerCategoryKind.Directory ? category.Name : null);
    }

    private void ImportClipboardImage(System.Windows.Media.Imaging.BitmapSource image, string targetDirectory, string? categoryName)
    {
        try
        {
            if (RequestImportConfirmation is not null)
            {
                var duplicateCount = _dragDropService.CountDuplicateClipboardImage(image, _snapshot.Stickers, _metadataService.GetHash);
                var result = RequestImportConfirmation(new ImportConfirmationRequest(
                    ["clipboard.png"],
                    GetImportTargetCategories(),
                    GetInitialImportCategory(targetDirectory),
                    duplicateCount));
                if (result is null)
                {
                    StatusText = "已取消导入";
                    return;
                }

                var targetCategory = ResolveImportCategory(result);
                ExecuteImportClipboardImage(
                    image,
                    GetCategoryDirectory(targetCategory),
                    targetCategory.Kind == StickerCategoryKind.Directory ? targetCategory.Name : null,
                    result.Deduplicate,
                    result.TagText,
                    result.IsFavorite);
                return;
            }

            ExecuteImportClipboardImage(image, targetDirectory, categoryName, true, string.Empty, false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException)
        {
            StatusText = $"导入剪贴板图片失败：{ex.Message}";
        }
    }

    private void ExecuteImportClipboardImage(System.Windows.Media.Imaging.BitmapSource image, string targetDirectory, string? categoryName, bool deduplicate, string tagText, bool isFavorite)
    {
        var importResult = _dragDropService.ImportClipboardImage(image, targetDirectory, _snapshot.Stickers, _metadataService.GetHash, deduplicate);
        RefreshLibrary();
        ApplyImportMetadata(importResult.ImportedFiles, tagText, isFavorite);
        StatusText = CreateImportStatus(importResult, categoryName);
    }

    private void ConfirmPendingImport()
    {
        if (_pendingImport is null)
        {
            StatusText = "没有待确认的导入";
            return;
        }

        var pendingImport = _pendingImport;
        ExecuteImportFiles(pendingImport.SourceFiles, pendingImport.TargetDirectory, pendingImport.CategoryName);
    }

    private void CancelPendingImport()
    {
        if (_pendingImport is null)
        {
            return;
        }

        ClearPendingImport();
        StatusText = "已取消导入";
    }

    private void SetPendingImport(PendingImport pendingImport)
    {
        _pendingImport = pendingImport;
        OnPropertyChanged(nameof(ImportPreviewText));
        OnPropertyChanged(nameof(HasPendingImport));
        (ConfirmPendingImportCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (CancelPendingImportCommand as RelayCommand)?.RaiseCanExecuteChanged();
        StatusText = pendingImport.PreviewText;
    }

    private void ClearPendingImport()
    {
        if (_pendingImport is null)
        {
            return;
        }

        _pendingImport = null;
        OnPropertyChanged(nameof(ImportPreviewText));
        OnPropertyChanged(nameof(HasPendingImport));
        (ConfirmPendingImportCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (CancelPendingImportCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private static string CreateImportStatus(ImportResult importResult, string? categoryName)
    {
        var importedCount = importResult.ImportedFiles.Count;
        var skippedCount = importResult.SkippedDuplicateCount;
        if (importedCount == 0)
        {
            return skippedCount == 0 ? "未找到可导入的图片文件" : $"已跳过 {skippedCount} 个重复表情包";
        }

        var targetText = categoryName is null ? string.Empty : $"到：{categoryName}";
        return skippedCount == 0
            ? $"已导入 {importedCount} 个表情包{targetText}"
            : $"已导入 {importedCount} 个表情包{targetText}，跳过 {skippedCount} 个重复";
    }

    private void OpenStickerRoot()
    {
        OpenDirectory(_settings.StickerRoot, $"已打开目录：{_settings.StickerRoot}", "打开目录失败");
    }

    private void OpenDirectory(string directoryPath, string successStatus, string failurePrefix)
    {
        try
        {
            Directory.CreateDirectory(directoryPath);
            Process.Start(new ProcessStartInfo
            {
                FileName = directoryPath,
                UseShellExecute = true
            });
            StatusText = successStatus;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            StatusText = $"{failurePrefix}：{ex.Message}";
        }
    }

    private void OpenStickerFolder(StickerItem? sticker)
    {
        if (sticker is null)
        {
            return;
        }

        try
        {
            _managementService.OpenStickerFolder(sticker);
            StatusText = $"已打开所在位置：{sticker.FileName}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            StatusText = $"打开所在位置失败：{ex.Message}";
        }
    }

    private void DeleteSticker(StickerItem? sticker)
    {
        if (sticker is null)
        {
            return;
        }

        DeleteStickers(sticker);
    }

    private void CreateCategory(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            StatusText = "分类名不能为空";
            return;
        }

        try
        {
            var directoryPath = _managementService.CreateCategory(name);
            AddCategoryToOrder(directoryPath);
            RefreshLibrary();
            StatusText = $"已创建分类：{Path.GetRelativePath(_settings.StickerRoot, directoryPath).Replace(Path.DirectorySeparatorChar, '/')}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            StatusText = $"创建分类失败：{ex.Message}";
        }
    }

    private void AddCategoryToOrder(string directoryPath)
    {
        var orderKey = GetCategoryOrderKey(directoryPath);
        if (string.IsNullOrWhiteSpace(orderKey)
            || _settings.CategoryOrder.Any(key => string.Equals(NormalizeCategoryOrderKey(key), orderKey, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        _settings.CategoryOrder.Add(orderKey);
        _settingsService.Save(_settings);
    }

    private void RenameSticker(RenameStickerRequest? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.NewName))
        {
            StatusText = "文件名不能为空";
            return;
        }

        try
        {
            var oldPath = request.Sticker.FilePath;
            var targetPath = _managementService.RenameSticker(request.Sticker, request.NewName);
            _favoriteService.UpdatePath(oldPath, targetPath);
            _recentService.UpdatePath(oldPath, targetPath);
            _metadataService.UpdatePath(oldPath, targetPath);
            UpdateSelectedStickerPath(oldPath, targetPath);
            RefreshLibrary();
            StatusText = $"已重命名：{Path.GetFileName(targetPath)}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            StatusText = $"重命名失败：{ex.Message}";
        }
    }

    private void SetStickerTags(SetStickerTagsRequest? request)
    {
        if (request is null)
        {
            return;
        }

        var stickers = GetDragStickers(request.Sticker);
        var tags = StickerMetadataService.NormalizeTags([request.TagText]);
        _metadataService.SetTags(stickers, tags);
        RefreshLibrary();
        StatusText = tags.Count == 0
            ? $"已清空 {stickers.Count} 个表情包的标签"
            : stickers.Count == 1 ? $"已设置标签：{stickers[0].FileName}" : $"已设置 {stickers.Count} 个表情包的标签";
    }

    private void AddTagsToSelected(string? tagText)
    {
        if (string.IsNullOrWhiteSpace(tagText))
        {
            StatusText = "标签不能为空";
            return;
        }

        var stickers = GetSelectedStickers();
        if (stickers.Count == 0)
        {
            StatusText = "未选择表情包";
            return;
        }

        var tags = StickerMetadataService.NormalizeTags([tagText]);
        _metadataService.AddTags(stickers, tags);
        RefreshLibrary();
        StatusText = $"已给 {stickers.Count} 个表情包添加标签：{string.Join("、", tags)}";
    }

    private void ClearStickerTags(StickerItem? sticker)
    {
        if (sticker is null)
        {
            return;
        }

        var stickers = GetDragStickers(sticker);
        _metadataService.ClearTags(stickers);
        RefreshLibrary();
        StatusText = stickers.Count == 1 ? $"已清空标签：{sticker.FileName}" : $"已清空 {stickers.Count} 个表情包的标签";
    }

    private void BatchRenameSelectedStickers(string? baseName)
    {
        if (string.IsNullOrWhiteSpace(baseName))
        {
            StatusText = "文件名不能为空";
            return;
        }

        var stickers = GetSelectedStickers();
        if (stickers.Count == 0)
        {
            StatusText = "未选择表情包";
            return;
        }

        try
        {
            var renamedCount = 0;
            foreach (var sticker in stickers.OrderBy(sticker => sticker.DisplayName, StringComparer.CurrentCultureIgnoreCase))
            {
                var oldPath = sticker.FilePath;
                var targetPath = _managementService.RenameSticker(sticker, $"{baseName}_{renamedCount + 1}{sticker.Extension}");
                _favoriteService.UpdatePath(oldPath, targetPath);
                _recentService.UpdatePath(oldPath, targetPath);
                _metadataService.UpdatePath(oldPath, targetPath);
                UpdateSelectedStickerPath(oldPath, targetPath);
                renamedCount++;
            }

            RefreshLibrary();
            StatusText = $"已批量重命名 {renamedCount} 个表情包";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            RefreshLibrary();
            StatusText = $"批量重命名失败：{ex.Message}";
        }
    }

    private void ToggleFavorite(StickerItem? sticker)
    {
        if (sticker is null)
        {
            return;
        }

        var stickers = GetDragStickers(sticker);
        var shouldFavorite = stickers.Any(selectedSticker => !selectedSticker.IsFavorite);
        SetFavoriteState(stickers, shouldFavorite, sticker.FileName);
    }

    private void SetSelectedFavoriteState(bool isFavorite)
    {
        var stickers = GetSelectedStickers();
        if (stickers.Count == 0)
        {
            StatusText = "未选择表情包";
            return;
        }

        SetFavoriteState(stickers, isFavorite, stickers[0].FileName);
    }

    private void SetFavoriteState(IReadOnlyList<StickerItem> stickers, bool isFavorite, string singleFileName)
    {
        if (isFavorite)
        {
            _favoriteService.AddRange(stickers);
        }
        else
        {
            _favoriteService.RemoveRange(stickers);
        }

        foreach (var selectedSticker in stickers)
        {
            selectedSticker.IsFavorite = isFavorite;
        }

        RefreshCategoryCounters();
        if (SelectedCategory?.Kind == StickerCategoryKind.Favorites)
        {
            ApplyFilter();
        }

        StatusText = stickers.Count == 1
            ? isFavorite ? $"已收藏：{singleFileName}" : $"已取消收藏：{singleFileName}"
            : isFavorite ? $"已收藏 {stickers.Count} 个表情包" : $"已取消收藏 {stickers.Count} 个表情包";
    }

    private void MoveSticker(MoveStickerRequest? request)
    {
        if (request is null)
        {
            return;
        }

        var stickers = GetDragStickers(request.Sticker);
        var movedCount = MoveStickersToCategory(stickers, request.Category, "移动");
        if (movedCount > 0)
        {
            StatusText = movedCount == 1 ? $"已移动到：{request.Category.Name}" : $"已移动 {movedCount} 个表情包到：{request.Category.Name}";
        }
    }

    private int MoveStickersToCategory(IReadOnlyList<StickerItem> stickers, StickerCategory category, string actionName)
    {
        var movedCount = 0;
        try
        {
            foreach (var sticker in stickers)
            {
                var oldPath = sticker.FilePath;
                var targetPath = _managementService.MoveStickerToCategory(sticker, category);
                _favoriteService.UpdatePath(oldPath, targetPath);
                _recentService.UpdatePath(oldPath, targetPath);
                _metadataService.UpdatePath(oldPath, targetPath);
                UpdateSelectedStickerPath(oldPath, targetPath);
                movedCount++;
            }

            RefreshLibrary();
            return movedCount;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            RefreshLibrary();
            StatusText = $"{actionName}失败：{ex.Message}";
            return 0;
        }
    }

    private void MoveFilesToCategory(IEnumerable<string> sourceFiles, StickerCategory category)
    {
        var fileList = sourceFiles.ToList();
        if (fileList.Count == 0)
        {
            StatusText = "未找到可移动的表情包";
            return;
        }

        var movedCount = 0;
        try
        {
            foreach (var sourcePath in fileList)
            {
                var targetPath = _managementService.MoveFileToCategory(sourcePath, category);
                _favoriteService.UpdatePath(sourcePath, targetPath);
                _recentService.UpdatePath(sourcePath, targetPath);
                _metadataService.UpdatePath(sourcePath, targetPath);
                UpdateSelectedStickerPath(sourcePath, targetPath);
                movedCount++;
            }

            RefreshLibrary();
            StatusText = movedCount == 1 ? $"已移动到：{category.Name}" : $"已移动 {movedCount} 个表情包到：{category.Name}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            RefreshLibrary();
            StatusText = $"移动失败：{ex.Message}";
        }
    }

    private void MergeCategory(MergeCategoryRequest? request)
    {
        if (request is null)
        {
            return;
        }

        if (request.Source.Kind != StickerCategoryKind.Directory || request.Source.DirectoryPath is null)
        {
            StatusText = "只能合并目录分类";
            return;
        }

        if (request.Target.Kind == StickerCategoryKind.Directory
            && request.Target.DirectoryPath is not null
            && string.Equals(Path.GetFullPath(request.Source.DirectoryPath), Path.GetFullPath(request.Target.DirectoryPath), StringComparison.OrdinalIgnoreCase))
        {
            StatusText = "不能合并到自身";
            return;
        }

        var sourceStickers = _snapshot.Stickers
            .Where(sticker => string.Equals(sticker.CategoryKey, request.Source.Key, StringComparison.Ordinal))
            .ToList();
        if (sourceStickers.Count == 0)
        {
            DeleteEmptyCategory(request.Source);
            return;
        }

        var movedCount = MoveStickersToCategory(sourceStickers, request.Target, "合并");
        if (movedCount == 0)
        {
            return;
        }

        try
        {
            _managementService.DeleteEmptyCategory(request.Source);
            RefreshLibrary();
            StatusText = $"已合并 {movedCount} 个表情包到：{request.Target.Name}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            RefreshLibrary();
            StatusText = $"表情已移动，删除空分类失败：{ex.Message}";
        }
    }

    private void OpenCategoryFolder(StickerCategory? category)
    {
        if (category is null)
        {
            return;
        }

        try
        {
            _managementService.OpenCategoryFolder(category);
            StatusText = $"已打开分类目录：{category.Name}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            StatusText = $"打开分类目录失败：{ex.Message}";
        }
    }

    private void RenameCategory(RenameCategoryRequest? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.NewName))
        {
            StatusText = "分类名不能为空";
            return;
        }

        try
        {
            var oldDirectory = request.Category.DirectoryPath;
            var targetPath = _managementService.RenameCategory(request.Category, request.NewName);
            if (oldDirectory is not null)
            {
                _favoriteService.UpdateDirectory(oldDirectory, targetPath);
                _recentService.UpdateDirectory(oldDirectory, targetPath);
                _metadataService.UpdateDirectory(oldDirectory, targetPath);
                UpdateSelectedStickerDirectory(oldDirectory, targetPath);
                UpdateCategoryOrderPath(oldDirectory, targetPath);
            }
            RefreshLibrary();
            StatusText = $"已重命名分类：{Path.GetRelativePath(_settings.StickerRoot, targetPath).Replace(Path.DirectorySeparatorChar, '/')}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            StatusText = $"重命名分类失败：{ex.Message}";
        }
    }

    private void DeleteEmptyCategory(StickerCategory? category)
    {
        if (category is null)
        {
            return;
        }

        try
        {
            _managementService.DeleteEmptyCategory(category);
            if (category.DirectoryPath is not null)
            {
                RemoveCategoryFromOrder(category.DirectoryPath);
            }

            RefreshLibrary();
            StatusText = $"已删除空分类：{category.Name}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            StatusText = $"删除分类失败：{ex.Message}";
        }
    }

    private IReadOnlyList<StickerItem> GetSelectedStickers()
    {
        return _snapshot.Stickers
            .Where(sticker => _selectedStickerPaths.Contains(sticker.FilePath))
            .ToList();
    }

    private IReadOnlyList<StickerItem> GetDragStickers(StickerItem sticker)
    {
        var selectedStickers = GetSelectedStickers();
        return sticker.IsSelected && selectedStickers.Count > 0 ? selectedStickers : [sticker];
    }

    private void UpdateSelectedStickerPath(string oldPath, string newPath)
    {
        if (_selectedStickerPaths.Remove(oldPath))
        {
            _selectedStickerPaths.Add(newPath);
        }

        if (string.Equals(_selectionAnchorPath, oldPath, StringComparison.OrdinalIgnoreCase))
        {
            _selectionAnchorPath = newPath;
        }
    }

    private void UpdateSelectedStickerDirectory(string oldDirectory, string newDirectory)
    {
        var changedPaths = _selectedStickerPaths
            .Where(path => IsInDirectory(path, oldDirectory))
            .ToList();
        foreach (var oldPath in changedPaths)
        {
            _selectedStickerPaths.Remove(oldPath);
            var relativePath = Path.GetRelativePath(oldDirectory, oldPath);
            _selectedStickerPaths.Add(Path.Combine(newDirectory, relativePath));
        }

        if (_selectionAnchorPath is not null && IsInDirectory(_selectionAnchorPath, oldDirectory))
        {
            var relativePath = Path.GetRelativePath(oldDirectory, _selectionAnchorPath);
            _selectionAnchorPath = Path.Combine(newDirectory, relativePath);
        }
    }

    private void UpdateCategoryOrderPath(string oldDirectory, string newDirectory)
    {
        var oldKey = GetCategoryOrderKey(oldDirectory);
        var newKey = GetCategoryOrderKey(newDirectory);
        var index = _settings.CategoryOrder.FindIndex(key => string.Equals(NormalizeCategoryOrderKey(key), oldKey, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return;
        }

        _settings.CategoryOrder[index] = newKey;
        _settingsService.Save(_settings);
    }

    private void RemoveCategoryFromOrder(string directoryPath)
    {
        var orderKey = GetCategoryOrderKey(directoryPath);
        if (_settings.CategoryOrder.RemoveAll(key => string.Equals(NormalizeCategoryOrderKey(key), orderKey, StringComparison.OrdinalIgnoreCase)) > 0)
        {
            _settingsService.Save(_settings);
        }
    }

    private IReadOnlyList<StickerItem> FindDuplicateStickers()
    {
        var seenHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicates = new List<StickerItem>();
        foreach (var sticker in _snapshot.Stickers.OrderByDescending(sticker => sticker.LastWriteTime))
        {
            if (!File.Exists(sticker.FilePath))
            {
                continue;
            }

            var hash = _metadataService.GetHash(sticker);
            if (!seenHashes.Add(hash))
            {
                duplicates.Add(sticker);
            }
        }

        return duplicates;
    }

    private static bool IsInDirectory(string filePath, string directoryPath)
    {
        var relativePath = Path.GetRelativePath(directoryPath, filePath);
        return relativePath != "." && !relativePath.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relativePath);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed record RenameStickerRequest(StickerItem Sticker, string NewName);

public sealed record SetStickerTagsRequest(StickerItem Sticker, string TagText);

public sealed record MoveStickerRequest(StickerItem Sticker, StickerCategory Category);

public sealed record MergeCategoryRequest(StickerCategory Source, StickerCategory Target);

public sealed record RenameCategoryRequest(StickerCategory Category, string NewName);

public sealed record PendingImport(IReadOnlyList<string> SourceFiles, string TargetDirectory, string? CategoryName, string PreviewText);

public sealed record ImportConfirmationRequest(
    IReadOnlyList<string> SourceFiles,
    IReadOnlyList<StickerCategory> Categories,
    StickerCategory? InitialCategory,
    int DuplicateCount);

public sealed record ImportConfirmationResult(
    StickerCategory Category,
    string? NewCategoryName,
    string TagText,
    bool IsFavorite,
    bool Deduplicate);
