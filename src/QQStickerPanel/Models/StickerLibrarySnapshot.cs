namespace QQStickerPanel.Models;

public sealed class StickerLibrarySnapshot
{
    public required IReadOnlyList<StickerItem> Stickers { get; init; }
    public required IReadOnlyList<StickerCategory> DirectoryCategories { get; init; }
    public required IReadOnlyList<StickerCategory> TagCategories { get; init; }
}
