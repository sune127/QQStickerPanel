using System.Globalization;
using System.Windows.Data;
using QQStickerPanel.Models;

namespace QQStickerPanel.Converters;

public sealed class CategorySelectionConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2)
        {
            return false;
        }

        return values[0] is StickerCategory category
            && values[1] is StickerCategory selected
            && string.Equals(category.Key, selected.Key, StringComparison.Ordinal);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
