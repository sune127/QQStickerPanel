using System.Collections.Specialized;
using System.Windows;
using QQStickerPanel.Models;

namespace QQStickerPanel.Services;

public sealed class ClipboardService
{
    public void CopySticker(StickerItem sticker)
    {
        CopyStickers([sticker]);
    }

    public void CopyStickers(IEnumerable<StickerItem> stickers)
    {
        var dataObject = new DataObject();
        var fileList = new StringCollection();
        foreach (var sticker in stickers)
        {
            fileList.Add(sticker.FilePath);
        }

        dataObject.SetFileDropList(fileList);
        Clipboard.SetDataObject(dataObject, false);
    }
}
