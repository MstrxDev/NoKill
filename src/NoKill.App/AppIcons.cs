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

    /// <summary>Green guardian circle (calm) or red (alert) with the N mark.</summary>
    internal static (Drawing.Icon Icon, nint Handle) Draw(bool alert)
    {
        using var bitmap = new Drawing.Bitmap(32, 32);
        using (var graphics = Drawing.Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var fill = new Drawing.SolidBrush(
                alert ? Drawing.Color.FromArgb(200, 40, 40) : Drawing.Color.FromArgb(40, 140, 80));
            graphics.FillEllipse(fill, 1, 1, 30, 30);

            using var font = new Drawing.Font("Segoe UI", 15, Drawing.FontStyle.Bold, Drawing.GraphicsUnit.Pixel);
            var format = new Drawing.StringFormat
            {
                Alignment = Drawing.StringAlignment.Center,
                LineAlignment = Drawing.StringAlignment.Center,
            };
            graphics.DrawString("N", font, Drawing.Brushes.White, new Drawing.RectangleF(0, 0, 32, 32), format);
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
