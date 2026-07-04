using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace QQStickerPanel.Models;

public sealed class StickerItem : INotifyPropertyChanged
{
    private bool _isFavorite;
    private bool _isSelected;
    private IReadOnlyList<string> _tags = [];

    public required string Id { get; init; }
    public required string FilePath { get; init; }
    public required string FileName { get; init; }
    public required string Extension { get; init; }
    public string? CategoryKey { get; init; }
    public string CategoryName { get; init; } = "未分类";
    public DateTime LastWriteTime { get; init; }

    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            if (_isFavorite == value)
            {
                return;
            }

            _isFavorite = value;
            OnPropertyChanged();
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public string TagText => Tags.Count == 0 ? string.Empty : "#" + string.Join(" #", Tags);

    public IReadOnlyList<string> Tags
    {
        get => _tags;
        set
        {
            if (_tags.SequenceEqual(value))
            {
                return;
            }

            _tags = value.ToList();
            OnPropertyChanged();
            OnPropertyChanged(nameof(TagText));
        }
    }

    public string DisplayName => Path.GetFileNameWithoutExtension(FileName);
    public bool IsUncategorized => string.IsNullOrEmpty(CategoryKey);
    public bool IsAnimated { get; init; }
    public string AnimationHint => IsAnimated ? "GIF 动图 · 双击复制或拖到 QQ" : string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
