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

    /// <summary>Recordings are always written here, as Snipping Tool does: a video is too costly to lose.</summary>
    public static readonly string RecordingsFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Screen Recordings");

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

    /// <summary><paramref name="scaleTo"/> resizes while encoding (Fant filter).</summary>
    public static Task<byte[]> EncodePngAsync(CapturedImage image, (int Width, int Height)? scaleTo = null) =>
        EncodeAsync(image, BitmapEncoder.PngEncoderId, scaleTo);

    /// <summary>JPEG has no alpha: transparent pixels are put onto white.</summary>
    public static Task<byte[]> EncodeJpegAsync(CapturedImage image, (int Width, int Height)? scaleTo = null)
    {
        if (image.HasTransparency)
        {
            var bgra = (byte[])image.Bgra.Clone();
            Pixels.OverWhite(bgra);
            image = image with { Bgra = bgra, HasTransparency = false };
        }
        return EncodeAsync(image, BitmapEncoder.JpegEncoderId, scaleTo);
    }

    private static async Task<byte[]> EncodeAsync(CapturedImage image, Guid encoderId, (int Width, int Height)? scaleTo)
    {
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(encoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, image.HasTransparency ? BitmapAlphaMode.Straight : BitmapAlphaMode.Ignore,
            (uint)image.Width, (uint)image.Height, 96, 96, image.Bgra);
        if (scaleTo is { } size && (size.Width != image.Width || size.Height != image.Height))
        {
            encoder.BitmapTransform.ScaledWidth = (uint)size.Width;
            encoder.BitmapTransform.ScaledHeight = (uint)size.Height;
            encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Fant;
        }
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
        if (!Open(owner))
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

    /// <summary>Puts Unicode text on the clipboard.</summary>
    public static bool CopyTextToClipboard(nint owner, string text)
    {
        nint mem = ToGlobal(System.Text.Encoding.Unicode.GetBytes(text + "\0"));
        if (!Open(owner))
        {
            GlobalFree(mem);
            return false;
        }
        try
        {
            EmptyClipboard();
            if (SetClipboardData(CF_UNICODETEXT, mem) != 0)
                return true;
            GlobalFree(mem);
            return false;
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>Puts the file on the clipboard as CF_HDROP (paste in Explorer, chat and mail apps).</summary>
    public static unsafe bool CopyFileToClipboard(nint owner, string path)
    {
        // DROPFILES header (20 bytes: pFiles, pt, fNC, fWide) + UTF-16 path list ending in two NULs.
        var names = System.Text.Encoding.Unicode.GetBytes(path + "\0\0");
        var data = new byte[20 + names.Length];
        BitConverter.GetBytes(20).CopyTo(data, 0);
        BitConverter.GetBytes(1).CopyTo(data, 16);
        names.CopyTo(data, 20);
        nint mem = ToGlobal(data);
        if (!Open(owner))
        {
            GlobalFree(mem);
            return false;
        }
        try
        {
            EmptyClipboard();
            if (SetClipboardData(CF_HDROP, mem) != 0)
                return true;
            GlobalFree(mem);
            return false;
        }
        finally
        {
            CloseClipboard();
        }
    }

    private static bool Open(nint owner)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            if (OpenClipboard(owner))
                return true;
            Thread.Sleep(20); // another app holds the clipboard
        }
        return false;
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

    /// <summary>AUMID of the unpackaged app; the Start menu shortcut carries it so toasts show as banners.</summary>
    public const string AppId = "ITBAER.Clipmark";

    private enum ToastRoute { None, Classic, AppSdk }

    private static ToastRoute s_route;
    // Classic toasts raise Activated only while their object is alive.
    private static readonly Queue<Windows.UI.Notifications.ToastNotification> s_recent = new();

    /// <summary>Raised (on a background thread) with the "open" path of a clicked classic toast.</summary>
    public static event Action<string>? ToastClicked;

    /// <summary>
    /// Unpackaged: classic toasts under <see cref="AppId"/> plus the Start menu shortcut (Windows App SDK notifications of an
    /// unpackaged app reach only the notification center, and self-contained builds cannot register them at all).
    /// Packaged: AppNotificationManager.
    /// </summary>
    public static void RegisterNotifications(bool writeShortcut)
    {
        if (!IsPackaged())
        {
            SetCurrentProcessExplicitAppUserModelID(AppId);
            try
            {
                if (writeShortcut)
                    Shell.StartMenuShortcut.Write(AppId);
            }
            catch (Exception) { } // toasts still land in the notification center
            s_route = ToastRoute.Classic;
            return;
        }
        try
        {
            AppNotificationManager.Default.Register();
            s_route = ToastRoute.AppSdk;
        }
        catch (Exception) { }
    }

    internal static unsafe bool IsPackaged()
    {
        uint length = 0;
        return GetCurrentPackageFullName(ref length, null) != 15700; // APPMODEL_ERROR_NO_PACKAGE
    }

    /// <summary>Text-only toast (text extractor); always shown, since the extractor has no other feedback.</summary>
    public static void ShowTextToast(string title) => Show(title, [], null, null);

    public static void ShowRecordingToast(string videoPath) => Show(Loc.Get("ToastRecordingSaved"), [videoPath], videoPath, null);

    public static void ShowToast(string imagePath, bool copied, string? savedPath) =>
        Show(Loc.Get(copied ? "ToastCaptureCopied" : "ToastCaptureTaken"), savedPath is null ? [] : [Loc.Format("ToastSavedTo", savedPath)],
            imagePath, imagePath);

    private static void Show(string title, string[] lines, string? open, string? image)
    {
        try
        {
            if (s_route == ToastRoute.AppSdk)
            {
                var builder = new AppNotificationBuilder().AddText(title);
                foreach (var line in lines)
                    builder.AddText(line);
                if (open is not null)
                    builder.AddArgument("open", open);
                if (image is not null)
                    builder.SetInlineImage(new Uri(image));
                AppNotificationManager.Default.Show(builder.BuildNotification());
            }
            else if (s_route == ToastRoute.Classic)
            {
                static string X(string v) => System.Security.SecurityElement.Escape(v);
                var xml = new System.Text.StringBuilder("<toast");
                if (open is not null)
                    xml.Append(" launch=\"").Append(X(open)).Append('"');
                xml.Append("><visual><binding template=\"ToastGeneric\"><text>").Append(X(title)).Append("</text>");
                foreach (var line in lines)
                    xml.Append("<text>").Append(X(line)).Append("</text>");
                if (image is not null)
                    xml.Append("<image src=\"").Append(X(new Uri(image).AbsoluteUri)).Append("\"/>");
                xml.Append("</binding></visual></toast>");
                var doc = new Windows.Data.Xml.Dom.XmlDocument();
                doc.LoadXml(xml.ToString());
                var toast = new Windows.UI.Notifications.ToastNotification(doc);
                toast.Activated += (_, e) =>
                {
                    if (e is Windows.UI.Notifications.ToastActivatedEventArgs { Arguments: { Length: > 0 } path })
                        ToastClicked?.Invoke(path);
                };
                lock (s_recent)
                {
                    s_recent.Enqueue(toast);
                    if (s_recent.Count > 20)
                        s_recent.Dequeue();
                }
                Windows.UI.Notifications.ToastNotificationManager.CreateToastNotifier(AppId).Show(toast);
            }
        }
        catch (Exception) { } // notifications turned off for the app or by policy
    }
}
