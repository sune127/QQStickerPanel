using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace QQStickerPanel.Services;

public static class ShellThumbnailService
{
    public static BitmapSource? TryCreateThumbnail(string filePath, int size)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            var itemId = typeof(IShellItem).GUID;
            SHCreateItemFromParsingName(filePath, IntPtr.Zero, ref itemId, out var shellItem);
            var imageFactory = (IShellItemImageFactory)shellItem;
            imageFactory.GetImage(new NativeSize(size, size), ThumbnailOptions.ThumbnailOnly | ThumbnailOptions.BiggerSizeOk, out var bitmapHandle);
            if (bitmapHandle == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                    bitmapHandle,
                    IntPtr.Zero,
                    System.Windows.Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                DeleteObject(bitmapHandle);
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or FileNotFoundException or ArgumentException)
        {
            return null;
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        string pszPath,
        IntPtr pbc,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    [ComImport]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        void GetImage(NativeSize size, ThumbnailOptions flags, out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeSize
    {
        public NativeSize(int width, int height)
        {
            Width = width;
            Height = height;
        }

        public readonly int Width;
        public readonly int Height;
    }

    [Flags]
    private enum ThumbnailOptions
    {
        BiggerSizeOk = 0x00000001,
        ThumbnailOnly = 0x00000008
    }
}
