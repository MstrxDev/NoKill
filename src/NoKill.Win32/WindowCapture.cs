using System.Drawing;
using System.Drawing.Imaging;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace NoKill.Win32;

/// <summary>
/// Captures what the user currently SEES of a window by copying the screen
/// region it occupies. Screen copy (not PrintWindow) is deliberate: a hung
/// window cannot render itself on request, but its last-drawn pixels are
/// still on screen — exactly the evidence a rescue report wants.
/// </summary>
public static class WindowCapture
{
    /// <summary>Returns the window's screen region as PNG bytes, or null if it can't be captured.</summary>
    public static byte[]? TryCapturePng(nint hwnd)
    {
        try
        {
            if (!PInvoke.GetWindowRect(new HWND(hwnd), out RECT rect))
            {
                return null;
            }

            int width = rect.right - rect.left;
            int height = rect.bottom - rect.top;
            if (width <= 0 || height <= 0)
            {
                return null;
            }

            using var bitmap = new Bitmap(width, height);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(rect.left, rect.top, 0, 0, new Size(width, height));
            }

            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }
        catch
        {
            return null; // secure desktop, session without a display, or GDI failure
        }
    }
}
