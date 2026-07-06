using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;

namespace NoKill.App;

/// <summary>
/// Runtime-drawn application icons (no binary assets in the repo), shared by
/// the tray icon and the window icon so NoKill has one face everywhere.
/// </summary>
internal static class AppIcons
{
    [DllImport("user32.dll")]
    internal static extern bool DestroyIcon(nint hIcon);

    /// <summary>
    /// The NoKill life ring: green when calm, red when an app is Not
    /// Responding. Must match assets/nokill.ico (same generator geometry).
    /// </summary>
    internal static (Drawing.Icon Icon, nint Handle) Draw(bool alert)
    {
        var ringColor = alert ? Drawing.Color.FromArgb(200, 40, 40) : Drawing.Color.FromArgb(40, 140, 80);
        var notchColor = alert ? Drawing.Color.FromArgb(154, 31, 31) : Drawing.Color.FromArgb(31, 112, 64);

        const int size = 32;
        const float c = size / 2f;
        const float outer = size * 0.49f;
        const float thickness = size * 0.33f;
        const float mid = outer - thickness / 2f;

        using var bitmap = new Drawing.Bitmap(size, size);
        using (var graphics = Drawing.Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = Drawing.Drawing2D.PixelOffsetMode.HighQuality;

            using var ringPen = new Drawing.Pen(ringColor, thickness);
            graphics.DrawEllipse(ringPen, c - mid, c - mid, mid * 2, mid * 2);

            using var notchPen = new Drawing.Pen(notchColor, thickness + 0.5f);
            for (int i = 0; i < 4; i++)
            {
                graphics.DrawArc(notchPen, c - mid, c - mid, mid * 2, mid * 2, 45f + i * 90f - 15f, 30f);
            }
        }

        nint handle = bitmap.GetHicon();
        return (Drawing.Icon.FromHandle(handle), handle);
    }

    /// <summary>WPF window icon; the HICON is copied into the BitmapSource and released.</summary>
    internal static BitmapSource CreateWindowIcon()
    {
        var (icon, handle) = Draw(alert: false);
        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(
                handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            icon.Dispose();
            DestroyIcon(handle);
        }
    }
}
