using Clipmark.App.Capture;
using Clipmark.Core.Capture;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using static Clipmark.App.Interop.Native;

namespace Clipmark.App.Output;

internal static class CaptureOutput
{
    public static readonly string DefaultFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");

    /// <summary>Holds captures for the toast image and the "open" action when auto-save is off.</summary>
    public static readonly string TempFolder = Path.Combine(Path.GetTempPath(), "Clipmark");

    /// <summary>Removes temp captures older than a day; toasts in the notification center may still point at newer ones.</summary>
    public static void CleanTempFolder()
    {
        if (!Directory.Exists(TempFolder))
            return;
        foreach (var file in Directory.EnumerateFiles(TempFolder, "*.png"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-1))
                    File.Delete(file);
            }
            catch (IOException) { } // in use: try again next start
        }
    }

    public static Task<byte[]> EncodePngAsync(CapturedImage image) => EncodeAsync(image, BitmapEncoder.PngEncoderId);

    /// <summary>JPEG has no alpha: transparent pixels are put onto white.</summary>
    public static Task<byte[]> EncodeJpegAsync(CapturedImage image)
    {
        if (image.HasTransparency)
        {
            var bgra = (byte[])image.Bgra.Clone();
            Pixels.OverWhite(bgra);
            image = image with { Bgra = bgra, HasTransparency = false };
        }
        return EncodeAsync(image, BitmapEncoder.JpegEncoderId);
    }

    private static async Task<byte[]> EncodeAsync(CapturedImage image, Guid encoderId)
    {
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(encoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, image.HasTransparency ? BitmapAlphaMode.Straight : BitmapAlphaMode.Ignore,
            (uint)image.Width, (uint)image.Height, 96, 96, image.Bgra);
        await encoder.FlushAsync();
        var bytes = new byte[stream.Size];
        stream.Seek(0);
        using var reader = new DataReader(stream);
        await reader.LoadAsync((uint)stream.Size);
        reader.ReadBytes(bytes);
        return bytes;
    }

    /// <summary>Writes the PNG into the folder with a free "Clipmark yyyy-MM-dd HHmmss.png" name.</summary>
    public static async Task<string> SaveAsync(string folder, byte[] png)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, CaptureFileName.Next(DateTime.Now, name => File.Exists(Path.Combine(folder, name))));
        await File.WriteAllBytesAsync(path, png);
        return path;
    }

    /// <summary>Puts "PNG" (keeps transparency) and CF_DIB (for apps without PNG support) on the clipboard.</summary>
    public static unsafe bool CopyToClipboard(nint owner, CapturedImage image, byte[] png)
    {
        nint pngMem = ToGlobal(png);
        nint dibMem = ToGlobal(ToDib(image));
        bool opened = false;
        for (int attempt = 0; attempt < 10 && !(opened = OpenClipboard(owner)); attempt++)
            Thread.Sleep(20); // another app holds the clipboard
        if (!opened)
        {
            GlobalFree(pngMem);
            GlobalFree(dibMem);
            return false;
        }
        try
        {
            EmptyClipboard();
            // After a successful SetClipboardData the system owns the memory.
            if (SetClipboardData(RegisterClipboardFormat("PNG"), pngMem) == 0)
                GlobalFree(pngMem);
            if (SetClipboardData(CF_DIB, dibMem) == 0)
            {
                GlobalFree(dibMem);
                return false;
            }
            return true;
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>Bottom-up 32 bpp BI_RGB DIB; transparent pixels become white because most DIB readers ignore alpha.</summary>
    private static unsafe byte[] ToDib(CapturedImage image)
    {
        int header = sizeof(BITMAPINFOHEADER), stride = image.Width * 4;
        var dib = new byte[header + stride * image.Height];
        var h = new BITMAPINFOHEADER
        {
            biSize = header, biWidth = image.Width, biHeight = image.Height, biPlanes = 1, biBitCount = 32,
            biSizeImage = stride * image.Height,
        };
        fixed (byte* d = dib)
        fixed (byte* src = image.Bgra)
        {
            *(BITMAPINFOHEADER*)d = h;
            for (int row = 0; row < image.Height; row++)
            {
                byte* s = src + (long)row * stride;
                byte* t = d + header + (long)(image.Height - 1 - row) * stride;
                for (int x = 0; x < stride; x += 4)
                {
                    int a = s[x + 3];
                    t[x] = (byte)((s[x] * a + 255 * (255 - a)) / 255);
                    t[x + 1] = (byte)((s[x + 1] * a + 255 * (255 - a)) / 255);
                    t[x + 2] = (byte)((s[x + 2] * a + 255 * (255 - a)) / 255);
                    t[x + 3] = 0;
                }
            }
        }
        return dib;
    }

    private static unsafe nint ToGlobal(byte[] data)
    {
        nint mem = GlobalAlloc(GMEM_MOVEABLE, (nuint)data.Length);
        if (mem == 0)
            throw new OutOfMemoryException("GlobalAlloc failed for the clipboard data.");
        void* p = GlobalLock(mem);
        fixed (byte* src = data)
            System.Buffer.MemoryCopy(src, p, data.Length, data.Length);
        GlobalUnlock(mem);
        return mem;
    }

    private static bool s_notificationsRegistered;

    /// <summary>Self-contained builds lack Microsoft.WindowsAppRuntime.Insights.Resource.dll and Register throws
    /// 0x8007007E; the app then runs without toasts.</summary>
    public static void RegisterNotifications()
    {
        try
        {
            AppNotificationManager.Default.Register();
            s_notificationsRegistered = true;
        }
        catch (Exception) { }
    }

    public static void ShowToast(string imagePath, bool copied, string? savedPath)
    {
        if (!s_notificationsRegistered)
            return;
        string title = copied ? "Capture copied to clipboard" : "Capture taken";
        var builder = new AppNotificationBuilder()
            .AddArgument("open", imagePath)
            .AddText(title)
            .SetInlineImage(new Uri(imagePath));
        if (savedPath is not null)
            builder.AddText("Saved to " + savedPath);
        AppNotificationManager.Default.Show(builder.BuildNotification());
    }
}
