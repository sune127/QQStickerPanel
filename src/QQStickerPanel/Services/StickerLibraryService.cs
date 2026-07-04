using QQStickerPanel.Models;

namespace QQStickerPanel.Services;

public sealed class StickerLibraryService
{
    private readonly StickerIndexService _indexService;

    public StickerLibraryService(StickerIndexService indexService)
    {
        _indexService = indexService;
    }

    public StickerLibrarySnapshot LoadCachedSnapshot()
    {
        return _indexService.LoadCachedSnapshot();
    }

    public StickerLibrarySnapshot RefreshSnapshotIncrementally()
    {
        return _indexService.RefreshSnapshotIncrementally();
    }

    public StickerLibrarySnapshot Scan()
    {
        return RefreshSnapshotIncrementally();
    }
}
