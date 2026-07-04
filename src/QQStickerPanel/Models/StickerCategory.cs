namespace QQStickerPanel.Models;

public enum StickerCategoryKind
{
    Recent,
    Favorites,
    All,
    Uncategorized,
    Tag,
    Directory
}

public sealed class StickerCategory
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required StickerCategoryKind Kind { get; init; }
    public string? DirectoryPath { get; init; }
    public int Count { get; set; }

    public string DisplayName => $"{Name} ({Count})";
}
