using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace DesktopMonitor
{
    // Draws the whole gauge card from the latest Snapshot (spec section 3; approved preview style B).
    internal sealed class CardView : FrameworkElement
    {
        public const double CardWidth = 250;
        private const double Pad = 14, RingSize = 64, RingRadius = 25, RingStroke = 6;
        private const double ColGap = 14, RowHeight = 16, RowGap = 6;

        private static readonly Brush TextBrush = Solid(0xEC, 0xEC, 0xEC);
        private static readonly Brush LabelBrush = Solid(0xA3, 0xA3, 0xA3);
        private static readonly Brush OkBrush = Solid(0x5D, 0xCA, 0xA5);
        private static readonly Brush AmberBrush = Solid(0xFA, 0xC7, 0x75);
        private static readonly Brush RedBrush = Solid(0xF0, 0x95, 0x95);
        private static readonly Pen TrackPen = FrozenPen(Color.FromArgb(33, 255, 255, 255), RingStroke); // white at 13 %
        private static readonly Pen OutlinePen = FrozenDashedPen();
        private static readonly Typeface Face = new Typeface("Segoe UI");

        private Snapshot _snap = new Snapshot();
        private AppSettings _settings = new AppSettings();
        private bool _unlocked;
        private double _pixelsPerDip = 1.0;

        public CardView()
        {
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.Grayscale); // ClearType cannot render on a transparent window
            Loaded += delegate { _pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip; InvalidateVisual(); };
        }

        public bool Unlocked
        {
            get { return _unlocked; }
            set { _unlocked = value; InvalidateMeasure(); InvalidateVisual(); }
        }

        public void Update(Snapshot s)
        {
            _snap = s ?? new Snapshot();
            InvalidateVisual();
        }

        public void ApplySettings(AppSettings s)
        {
            _settings = s;
            InvalidateVisual();
        }

        public static double CardHeight(bool unlocked)
        {
            return unlocked ? 226 : 204;
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            return new Size(CardWidth, CardHeight(_unlocked));
        }

        protected override void OnRender(DrawingContext dc)
        {
            Snapshot s = _snap;
            AppSettings st = _settings;
            double height = CardHeight(_unlocked);
            var background = new SolidColorBrush(Color.FromArgb((byte)Math.Round(st.Opacity * 255), 0x14, 0x14, 0x16));
            dc.DrawRoundedRectangle(background, null, new Rect(0, 0, CardWidth, height), 10, 10);
            if (_unlocked) dc.DrawRoundedRectangle(null, OutlinePen, new Rect(1.5, 1.5, CardWidth - 3, height - 3), 9, 9);

            double y = Pad;
            DrawLeft(dc, "System", 12, LabelBrush, Pad, y);
            DrawRight(dc, s.Time.ToString("HH:mm", CultureInfo.InvariantCulture), 12, LabelBrush, CardWidth - Pad, y);
            y += RowHeight + 8;

            double cpuFraction = s.CpuPercent.HasValue ? s.CpuPercent.Value / 100 : 0;
            double ramFraction = s.RamPercent.HasValue ? s.RamPercent.Value / 100 : 0;
            DrawRing(dc, Pad + RingSize / 2, y, cpuFraction, Rules.FormatPercent(s.CpuPercent), "CPU", Rules.Classify(s.CpuPercent, st.UsageAmber, st.UsageRed));
            DrawRing(dc, CardWidth / 2, y, ramFraction, Rules.FormatPercent(s.RamPercent), "RAM", Rules.Classify(s.RamPercent, st.UsageAmber, st.UsageRed));
            DrawRing(dc, CardWidth - Pad - RingSize / 2, y, Rules.TempRingFraction(s.CpuTempC, st.TempRingMin, st.TempRingMax),
                Rules.FormatTemp(s.CpuTempC, false), "CPU temp", Rules.Classify(s.CpuTempC, st.TempAmber, st.TempRed));
            y += RingSize + 2 + RowHeight + 10;

            double colW = (CardWidth - 2 * Pad - ColGap) / 2;
            double x1 = Pad, x2 = Pad + colW + ColGap;
            Cell(dc, x1, y, colW, "GPU", Rules.FormatPercent(s.GpuPercent), Colour(Rules.Classify(s.GpuPercent, st.UsageAmber, st.UsageRed), TextBrush));
            Cell(dc, x2, y, colW, "Disk C:", Rules.FormatPercent(s.DiskPercent), Colour(Rules.Classify(s.DiskPercent, st.DiskAmber, st.DiskRed), TextBrush));
            y += RowHeight + RowGap;
            Cell(dc, x1, y, colW, "Net \u2193", Rules.FormatSpeed(s.DownBytesPerSec), s.DownBytesPerSec.HasValue ? TextBrush : LabelBrush);
            Cell(dc, x2, y, colW, "Net \u2191", Rules.FormatSpeed(s.UpBytesPerSec), s.UpBytesPerSec.HasValue ? TextBrush : LabelBrush);
            y += RowHeight + RowGap;
            BatteryCell(dc, x1, y, colW, s, st);
            Cell(dc, x2, y, colW, "Skin", Rules.FormatTemp(s.SkinTempC, true), Colour(Rules.Classify(s.SkinTempC, st.TempAmber, st.TempRed), OkBrush));

            if (_unlocked) DrawCentered(dc, "Drag to move \u00B7 locks when you let go", 11, TextBrush, CardWidth / 2, 202);
        }

        private void DrawRing(DrawingContext dc, double cx, double top, double fraction, string value, string label, Level level)
        {
            var center = new Point(cx, top + RingSize / 2);
            dc.DrawEllipse(null, TrackPen, center, RingRadius, RingRadius);
            if (fraction > 0)
            {
                var pen = new Pen(Colour(level, OkBrush), RingStroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                if (fraction >= 0.999) dc.DrawEllipse(null, pen, center, RingRadius, RingRadius);
                else dc.DrawGeometry(null, pen, Arc(center, RingRadius, fraction));
            }
            FormattedText v = Text(value, 13, TextBrush);
            dc.DrawText(v, new Point(cx - v.Width / 2, center.Y - v.Height / 2));
            DrawCentered(dc, label, 12, LabelBrush, cx, top + RingSize + 2);
        }

        // Clockwise arc starting at 12 o'clock.
        private static Geometry Arc(Point c, double r, double fraction)
        {
            double a0 = -Math.PI / 2, a1 = a0 + fraction * 2 * Math.PI;
            var g = new StreamGeometry();
            using (StreamGeometryContext ctx = g.Open())
            {
                ctx.BeginFigure(new Point(c.X + r * Math.Cos(a0), c.Y + r * Math.Sin(a0)), false, false);
                ctx.ArcTo(new Point(c.X + r * Math.Cos(a1), c.Y + r * Math.Sin(a1)), new Size(r, r), 0, fraction > 0.5, SweepDirection.Clockwise, true, false);
            }
            g.Freeze();
            return g;
        }

        // "Battery   99% [bolt] 39°": percentage, AC bolt when plugged in, battery temperature coloured by the temp rule.
        private void BatteryCell(DrawingContext dc, double x, double y, double w, Snapshot s, AppSettings st)
        {
            DrawLeft(dc, "Battery", 12, LabelBrush, x, y);
            double right = x + w;
            FormattedText temp = Text(Rules.FormatTemp(s.BatteryTempC, false), 12, Colour(Rules.Classify(s.BatteryTempC, st.TempAmber, st.TempRed), OkBrush));
            right -= temp.Width;
            dc.DrawText(temp, new Point(right, y));
            if (s.OnAc)
            {
                right -= 4 + 7;
                dc.DrawGeometry(TextBrush, null, Bolt(right, y + 2.5));
            }
            FormattedText pct = Text(Rules.FormatPercent(s.BatteryPercent), 12, s.BatteryPercent.HasValue ? TextBrush : LabelBrush);
            right -= 4 + pct.Width;
            dc.DrawText(pct, new Point(right, y));
        }

        // 7 x 11 lightning bolt with its top-left corner at (x, y).
        private static Geometry Bolt(double x, double y)
        {
            var g = new StreamGeometry();
            using (StreamGeometryContext ctx = g.Open())
            {
                ctx.BeginFigure(new Point(x + 4, y), true, true);
                ctx.PolyLineTo(new[]
                {
                    new Point(x, y + 6), new Point(x + 3, y + 6), new Point(x + 2, y + 11),
                    new Point(x + 7, y + 4.5), new Point(x + 4, y + 4.5), new Point(x + 5, y)
                }, true, true);
            }
            g.Freeze();
            return g;
        }

        private void Cell(DrawingContext dc, double x, double y, double w, string label, string value, Brush valueBrush)
        {
            DrawLeft(dc, label, 12, LabelBrush, x, y);
            DrawRight(dc, value, 12, valueBrush, x + w, y);
        }

        private static Brush Colour(Level level, Brush okBrush)
        {
            switch (level)
            {
                case Level.Ok: return okBrush;
                case Level.Amber: return AmberBrush;
                case Level.Red: return RedBrush;
                default: return LabelBrush;
            }
        }

        private FormattedText Text(string s, double size, Brush brush)
        {
            return new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, size, brush, _pixelsPerDip);
        }

        private void DrawLeft(DrawingContext dc, string s, double size, Brush brush, double x, double y)
        {
            dc.DrawText(Text(s, size, brush), new Point(x, y));
        }

        private void DrawRight(DrawingContext dc, string s, double size, Brush brush, double right, double y)
        {
            FormattedText t = Text(s, size, brush);
            dc.DrawText(t, new Point(right - t.Width, y));
        }

        private void DrawCentered(DrawingContext dc, string s, double size, Brush brush, double cx, double y)
        {
            FormattedText t = Text(s, size, brush);
            dc.DrawText(t, new Point(cx - t.Width / 2, y));
        }

        private static Brush Solid(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        private static Pen FrozenPen(Color c, double thickness)
        {
            var brush = new SolidColorBrush(c);
            brush.Freeze();
            var pen = new Pen(brush, thickness);
            pen.Freeze();
            return pen;
        }

        private static Pen FrozenDashedPen()
        {
            var pen = new Pen(Brushes.White, 1) { DashStyle = DashStyles.Dash };
            pen.Freeze();
            return pen;
        }
    }
}
