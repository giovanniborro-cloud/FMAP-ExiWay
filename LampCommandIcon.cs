using System.Drawing.Drawing2D;

namespace SchneiderLoader;

internal static class LampCommandIcon
{
    public static Bitmap Create(int operation)
    {
        var bitmap = new Bitmap(24, 24);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var outline = new Pen(Color.FromArgb(54, 65, 76), 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var fill = new SolidBrush(operation switch
        {
            5 => Color.FromArgb(255, 218, 106),
            240 => Color.FromArgb(255, 184, 77),
            _ => Color.FromArgb(216, 221, 227)
        });
        graphics.FillEllipse(fill, 6.5f, 5, 11, 12);
        graphics.DrawEllipse(outline, 6.5f, 5, 11, 12);
        graphics.DrawLine(outline, 9.5f, 19, 14.5f, 19);
        graphics.DrawLine(outline, 10.5f, 21, 13.5f, 21);
        if (operation == 0)
        {
            graphics.DrawLine(outline, 8.5f, 14, 15.5f, 8);
            return bitmap;
        }

        using var light = new Pen(Color.FromArgb(174, 100, 16), 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        graphics.DrawLine(light, 12, 1, 12, 3);
        graphics.DrawLine(light, 2, 10, 4, 10);
        graphics.DrawLine(light, 20, 10, 22, 10);
        if (operation == 240)
        {
            graphics.DrawLine(light, 3, 3, 5, 5);
            graphics.DrawLine(light, 19, 5, 21, 3);
            graphics.DrawLines(outline, [new PointF(13, 7), new PointF(10, 11), new PointF(13, 11), new PointF(11, 15)]);
        }
        return bitmap;
    }
}
