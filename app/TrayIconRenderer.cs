using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace PcWatch;

/// <summary>
/// Draws the load percentage as a notification-area icon, and owns its handle lifetime.
/// </summary>
/// <remarks>
/// ⛔ 2026-08-31, THE HANDLE LEAK. Bitmap.GetHicon() creates an unmanaged HICON. Icon.FromHandle()
///    wraps it but does NOT own it, so Icon.Dispose() releases the wrapper and leaves the handle
///    allocated. At one icon per second that is 3600 orphaned GDI handles an hour against a
///    10000-handle process quota: the app would run for under three hours and then die, which is
///    comfortably long enough to look correct while being tested.
///
///    This class exists so that lifetime lives in exactly one place. Callers never see the handle.
/// </remarks>
public sealed class TrayIconRenderer : IDisposable
{
    private const int Size = 32;   // rendered at 32 so Windows has something to downscale on high DPI

    private Icon? _current;
    private IntPtr _currentHandle;

    /// <summary>
    /// Render the percentage and hand back an Icon owned by this renderer.
    /// </summary>
    /// <remarks>
    /// The previous icon is freed only AFTER the new one exists, so the icon a caller has just
    /// assigned is never one that has already been destroyed.
    /// </remarks>
    public Icon Render(double? percent)
    {
        using var bitmap = new Bitmap(Size, Size);
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            using (var back = new SolidBrush(Theme.ForLoad(percent)))
            {
                g.FillRectangle(back, 0, 0, Size, Size);
            }

            // 100 will not fit legibly in 32 px, and the difference between 99 and 100 does not
            // matter to anyone reading a tray icon.
            string text = percent is null ? "?" : Math.Min(99, Math.Round(percent.Value)).ToString("N0");
            float emSize = text.Length >= 2 ? 17f : 22f;

            using var font = new Font("Segoe UI", emSize, FontStyle.Bold, GraphicsUnit.Pixel);
            using var ink = new SolidBrush(Color.White);
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };
            g.DrawString(text, font, ink, new RectangleF(0, 0, Size, Size), format);
        }

        IntPtr handle = bitmap.GetHicon();
        Icon fresh = Icon.FromHandle(handle);

        Icon? previous = _current;
        IntPtr previousHandle = _currentHandle;
        _current = fresh;
        _currentHandle = handle;

        previous?.Dispose();
        Native.ReleaseIconHandle(previousHandle);

        return fresh;
    }

    /// <summary>Hover text. Capped at 127 characters, above which NotifyIcon.Text throws.</summary>
    public static string Tooltip(Snapshot snapshot)
    {
        string cpu = snapshot.TotalCpuPercent is { } v ? $"{v:N0}% CPU" : "measuring...";
        string top = snapshot.TopProcesses.Count > 0
            ? $" - {snapshot.TopProcesses[0].Name} {snapshot.TopProcesses[0].Percent:N1}%"
            : string.Empty;

        string text = $"PC Watch - {cpu}{top}";
        return text.Length > 127 ? text[..127] : text;
    }

    public void Dispose()
    {
        _current?.Dispose();
        Native.ReleaseIconHandle(_currentHandle);
        _current = null;
        _currentHandle = IntPtr.Zero;
    }
}
