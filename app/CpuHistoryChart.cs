using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace PcWatch;

/// <summary>
/// A rolling CPU history graph. This is the part that makes the window feel live.
/// </summary>
/// <remarks>
/// 2026-08-31. A single number cannot tell a spike from a plateau, and that distinction is the whole
/// question when a machine "feels slow": 100% for two seconds during a build is normal, 60% flat for
/// an hour is not. The graph answers it at a glance; the number alone never can.
///
/// Double buffered because it repaints every second - without it the fill flickers visibly.
/// </remarks>
public sealed class CpuHistoryChart : Control
{
    private readonly Queue<double> _history = new();

    // Hidden from designer serialization: this control is built in code and never dropped on a
    // form, so there is no .Designer.cs for the WinForms analyzer to worry about.
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Capacity { get; init; } = 120;   // 2 minutes at a one-second tick

    public CpuHistoryChart()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                 | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Panel;
    }

    /// <summary>Append a reading. Null (not yet measured) is recorded as a gap, not as zero.</summary>
    public void Push(double? percent)
    {
        _history.Enqueue(percent ?? -1);
        while (_history.Count > Capacity) _history.Dequeue();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        int w = Width, h = Height;
        if (w <= 2 || h <= 2) return;

        using (var grid = new Pen(Theme.Grid))
        {
            // 25 / 50 / 75%. Labelled by position rather than text: at this size a legend costs
            // more room than it explains.
            for (int i = 1; i < 4; i++)
            {
                int y = h - (int)(h * i / 4.0);
                g.DrawLine(grid, 0, y, w, y);
            }
        }

        if (_history.Count < 2) return;

        double[] values = _history.ToArray();
        float step = (float)w / Math.Max(1, Capacity - 1);
        float left = w - (values.Length - 1) * step;   // newest pinned to the right edge

        var points = new List<PointF>();
        foreach (double v in values)
        {
            float x = left;
            left += step;
            if (v < 0) { points.Clear(); continue; }   // gap: start a new segment
            points.Add(new PointF(x, (float)(h - h * v / 100.0)));
        }
        if (points.Count < 2) return;

        // Filled area under the line, so the eye reads magnitude rather than just shape.
        var area = new List<PointF>(points) { new(points[^1].X, h), new(points[0].X, h) };
        double latest = values[^1];
        Color line = Theme.ForLoad(latest < 0 ? null : latest);

        using (var fill = new SolidBrush(Color.FromArgb(52, line)))
        {
            g.FillPolygon(fill, area.ToArray());
        }
        using (var pen = new Pen(line, 1.6f))
        {
            g.DrawLines(pen, points.ToArray());
        }
    }
}
