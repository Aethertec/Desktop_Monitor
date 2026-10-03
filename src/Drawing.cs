using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace DesktopMonitor
{
    // Colours shared by the card and the details panel (v1 spec section 3, v2 spec sections 3-4).
    internal static class Palette
    {
        public static readonly Brush Text = Solid(255, 0xEC, 0xEC, 0xEC);
        public static readonly Brush Label = Solid(255, 0xA3, 0xA3, 0xA3);
        public static readonly Brush Ok = Solid(255, 0x5D, 0xCA, 0xA5);
        public static readonly Brush Amber = Solid(255, 0xFA, 0xC7, 0x75);
        public static readonly Brush Red = Solid(255, 0xF0, 0x95, 0x95);
        public static readonly Brush RedTint = Solid(46, 0xF0, 0x95, 0x95);   // 18 %
        public static readonly Brush OkTint = Solid(64, 0x5D, 0xCA, 0xA5);    // 25 %
        public static readonly Brush NewTint = Solid(46, 0x5D, 0xCA, 0xA5);   // 18 %
        public static readonly Brush Segment = Solid(20, 255, 255, 255);      // 8 %
        public static readonly Pen Divider = Frozen(Solid(31, 255, 255, 255), 1); // 12 %

        public static Brush Colour(Level level, Brush okBrush)
        {
            switch (level)
            {
                case Level.Ok: return okBrush;
                case Level.Amber: return Amber;
                case Level.Red: return Red;
                default: return Label;
            }
        }

        public static Brush Solid(byte a, byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            brush.Freeze();
            return brush;
        }

        public static Pen Frozen(Brush brush, double thickness)
        {
            var pen = new Pen(brush, thickness);
            pen.Freeze();
            return pen;
        }
    }

    // Segoe UI text at the element's DPI, plus left/right/centred drawing and ellipsis fitting.
    internal sealed class TextPainter
    {
        private static readonly Typeface Face = new Typeface("Segoe UI");
        public double PixelsPerDip = 1.0;

        public FormattedText Make(string s, double size, Brush brush)
        {
            return new FormattedText(s ?? "", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, size, brush, PixelsPerDip);
        }

        public double Width(string s, double size)
        {
            return Make(s, size, Palette.Text).WidthIncludingTrailingWhitespace;
        }

        public void Left(DrawingContext dc, string s, double size, Brush brush, double x, double y)
        {
            dc.DrawText(Make(s, size, brush), new Point(x, y));
        }

        public void Right(DrawingContext dc, string s, double size, Brush brush, double right, double y)
        {
            FormattedText t = Make(s, size, brush);
            dc.DrawText(t, new Point(right - t.Width, y));
        }

        public void Centered(DrawingContext dc, string s, double size, Brush brush, double cx, double y)
        {
            FormattedText t = Make(s, size, brush);
            dc.DrawText(t, new Point(cx - t.Width / 2, y));
        }

        // Shortens the text with "…" until it fits maxWidth.
        public string Fit(string s, double maxWidth, double size)
        {
            if (s == null || Width(s, size) <= maxWidth) return s;
            for (int n = s.Length - 1; n > 0; n--)
            {
                string candidate = s.Substring(0, n).TrimEnd() + "\u2026";
                if (Width(candidate, size) <= maxWidth) return candidate;
            }
            return "\u2026";
        }
    }
}
