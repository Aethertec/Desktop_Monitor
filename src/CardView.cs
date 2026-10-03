using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace DesktopMonitor
{
    // Draws the whole gauge card from the latest Snapshot (v1 spec section 3, v2 spec section 3).
    internal sealed class CardView : FrameworkElement
    {
        public const double CardWidth = 250;
        private const double Pad = 14, RingSize = 64, RingRadius = 25, RingStroke = 6;
        private const double ColGap = 14, RowHeight = 16, RowGap = 6, GraphHeight = 30, GraphMin = 40, GraphMax = 100;

        private static readonly Pen TrackPen = Palette.Frozen(Palette.Solid(33, 255, 255, 255), RingStroke); // white at 13 %
        private static readonly Pen OutlinePen = DashedPen(Brushes.White, 1);
        private static readonly Pen ThresholdPen = DashedPen(Palette.Solid(128, 0xFA, 0xC7, 0x75), 1); // amber at 50 %

        private readonly TextPainter _text = new TextPainter();
        private Snapshot _snap = new Snapshot();
        private AppSettings _settings = new AppSettings();
        private bool _unlocked;
        private double?[] _history = new double?[0];
        private double? _peak;
        private int _historyCapacity = 60;

        public CardView()
        {
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.Grayscale); // ClearType cannot render on a transparent window
            Loaded += delegate { _text.PixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip; InvalidateVisual(); };
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

        public void SetHistory(double?[] points, double? peak, int capacity)
        {
            _history = points ?? new double?[0];
            _peak = peak;
            _historyCapacity = Math.Max(2, capacity);
            InvalidateVisual();
        }

        public void ApplySettings(AppSettings s)
        {
            _settings = s;
            InvalidateVisual();
        }

        public static double CardHeight(bool unlocked)
        {
            return unlocked ? 304 : 282;
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
            _text.Left(dc, "System", 12, Palette.Label, Pad, y);
            FormattedText clock = _text.Make(s.Time.ToString("HH:mm", CultureInfo.InvariantCulture), 12, Palette.Label);
            dc.DrawText(clock, new Point(CardWidth - Pad - clock.Width, y));
            if (s.Throttled) DrawPill(dc, "Throttled " + Rules.FormatPercent(s.CpuLimitPercent), CardWidth - Pad - clock.Width - 6, y);
            y += RowHeight + 8;

            double cpuFraction = s.CpuPercent.HasValue ? s.CpuPercent.Value / 100 : 0;
            double ramFraction = s.RamPercent.HasValue ? s.RamPercent.Value / 100 : 0;
            DrawRing(dc, Pad + RingSize / 2, y, cpuFraction, Rules.FormatPercent(s.CpuPercent), "CPU", Rules.Classify(s.CpuPercent, st.UsageAmber, st.UsageRed));
            DrawRing(dc, CardWidth / 2, y, ramFraction, Rules.FormatPercent(s.RamPercent), "RAM", Rules.Classify(s.RamPercent, st.UsageAmber, st.UsageRed));
            DrawRing(dc, CardWidth - Pad - RingSize / 2, y, Rules.TempRingFraction(s.CpuTempC, st.TempRingMin, st.TempRingMax),
                Rules.FormatTemp(s.CpuTempC, false), "CPU temp", Rules.Classify(s.CpuTempC, st.TempAmber, st.TempRed));
            y += RingSize + 2 + RowHeight + 8;

            _text.Left(dc, "CPU temp \u00B7 10 min", 12, Palette.Label, Pad, y);
            FormattedText peak = _text.Make(Rules.FormatTemp(_peak, false), 12, Palette.Colour(Rules.Classify(_peak, st.TempAmber, st.TempRed), Palette.Ok));
            dc.DrawText(peak, new Point(CardWidth - Pad - peak.Width, y));
            _text.Right(dc, "peak", 12, Palette.Label, CardWidth - Pad - peak.Width - 4, y); // explicit gap: FormattedText.Width ignores a trailing space
            y += RowHeight + 3;
            DrawHistory(dc, Pad, y, CardWidth - 2 * Pad, st);
            y += GraphHeight + 6;

            AppUsage top = s.TopApps.Count > 0 ? s.TopApps[0] : null;
            double labelW = _text.Width("Top app", 12);
            _text.Left(dc, "Top app", 12, Palette.Label, Pad, y);
            _text.Right(dc, _text.Fit(Rules.FormatTopApp(top), CardWidth - 2 * Pad - labelW - 8, 12), 12, top == null ? Palette.Label : Palette.Text, CardWidth - Pad, y);
            y += RowHeight + 9;

            double colW = (CardWidth - 2 * Pad - ColGap) / 2;
            double x1 = Pad, x2 = Pad + colW + ColGap;
            Cell(dc, x1, y, colW, "GPU", Rules.FormatPercent(s.GpuPercent), Palette.Colour(Rules.Classify(s.GpuPercent, st.UsageAmber, st.UsageRed), Palette.Text));
            Cell(dc, x2, y, colW, "C: free", Rules.FormatDiskFree(s.DiskFreeBytes), Palette.Colour(Rules.Classify(s.DiskPercent, st.DiskAmber, st.DiskRed), Palette.Text));
            y += RowHeight + RowGap;
            Cell(dc, x1, y, colW, "Net \u2193", Rules.FormatSpeed(s.DownBytesPerSec), s.DownBytesPerSec.HasValue ? Palette.Text : Palette.Label);
            Cell(dc, x2, y, colW, "Net \u2191", Rules.FormatSpeed(s.UpBytesPerSec), s.UpBytesPerSec.HasValue ? Palette.Text : Palette.Label);
            y += RowHeight + RowGap;
            BatteryCell(dc, x1, y, colW, s, st);
            Cell(dc, x2, y, colW, "Skin", Rules.FormatTemp(s.SkinTempC, true), Palette.Colour(Rules.Classify(s.SkinTempC, st.TempAmber, st.TempRed), Palette.Ok));

            if (_unlocked) _text.Centered(dc, "Drag to move \u00B7 locks when you let go", 11, Palette.Text, CardWidth / 2, 280);
        }

        // Rounded red pill whose right edge is at `right`.
        private void DrawPill(DrawingContext dc, string text, double right, double y)
        {
            FormattedText t = _text.Make(text, 11, Palette.Red);
            double w = t.Width + 12;
            dc.DrawRoundedRectangle(Palette.RedTint, null, new Rect(right - w, y + 1, w, 15), 6, 6);
            dc.DrawText(t, new Point(right - w + 6, y + 1));
        }

        // Last 10 minutes of CPU temperature, newest at the right edge; gaps where a reading was missing.
        private void DrawHistory(DrawingContext dc, double x0, double y0, double w, AppSettings st)
        {
            double ty = GraphY(st.TempAmber, y0);
            dc.DrawLine(ThresholdPen, new Point(x0, ty), new Point(x0 + w, ty));
            int n = _history.Length;
            if (n == 0) return;
            Level level = Rules.Classify(_peak, st.TempAmber, st.TempRed);
            Brush line = Palette.Colour(level, Palette.Ok);
            Brush fill = FillFor(level);
            var pen = new Pen(line, 1.5);
            double step = w / (_historyCapacity - 1);
            int i = 0;
            while (i < n)
            {
                if (!_history[i].HasValue) { i++; continue; }
                int start = i;
                while (i < n && _history[i].HasValue) i++;
                DrawSegment(dc, start, i - 1, n, x0 + w, step, y0, pen, fill);
            }
        }

        private void DrawSegment(DrawingContext dc, int first, int last, int n, double right, double step, double y0, Pen pen, Brush fill)
        {
            Func<int, Point> at = k => new Point(right - (n - 1 - k) * step, GraphY(_history[k].Value, y0));
            if (first == last)
            {
                dc.DrawEllipse(pen.Brush, null, at(first), 1.5, 1.5);
                return;
            }
            var area = new StreamGeometry();
            using (StreamGeometryContext ctx = area.Open())
            {
                ctx.BeginFigure(new Point(at(first).X, y0 + GraphHeight), true, true);
                for (int k = first; k <= last; k++) ctx.LineTo(at(k), false, false);
                ctx.LineTo(new Point(at(last).X, y0 + GraphHeight), false, false);
            }
            area.Freeze();
            dc.DrawGeometry(fill, null, area);
            var curve = new StreamGeometry();
            using (StreamGeometryContext ctx = curve.Open())
            {
                ctx.BeginFigure(at(first), false, false);
                for (int k = first + 1; k <= last; k++) ctx.LineTo(at(k), true, false);
            }
            curve.Freeze();
            dc.DrawGeometry(null, pen, curve);
        }

        private static double GraphY(double celsius, double y0)
        {
            double c = Math.Max(GraphMin, Math.Min(GraphMax, celsius));
            return y0 + GraphHeight - (c - GraphMin) / (GraphMax - GraphMin) * GraphHeight;
        }

        private static Brush FillFor(Level level)
        {
            switch (level)
            {
                case Level.Red: return Palette.Solid(38, 0xF0, 0x95, 0x95);
                case Level.Amber: return Palette.Solid(38, 0xFA, 0xC7, 0x75);
                default: return Palette.Solid(38, 0x5D, 0xCA, 0xA5); // 15 %
            }
        }

        private void DrawRing(DrawingContext dc, double cx, double top, double fraction, string value, string label, Level level)
        {
            var center = new Point(cx, top + RingSize / 2);
            dc.DrawEllipse(null, TrackPen, center, RingRadius, RingRadius);
            if (fraction > 0)
            {
                var pen = new Pen(Palette.Colour(level, Palette.Ok), RingStroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                if (fraction >= 0.999) dc.DrawEllipse(null, pen, center, RingRadius, RingRadius);
                else dc.DrawGeometry(null, pen, Arc(center, RingRadius, fraction));
            }
            FormattedText v = _text.Make(value, 13, Palette.Text);
            dc.DrawText(v, new Point(cx - v.Width / 2, center.Y - v.Height / 2));
            _text.Centered(dc, label, 12, Palette.Label, cx, top + RingSize + 2);
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
            _text.Left(dc, "Battery", 12, Palette.Label, x, y);
            double right = x + w;
            FormattedText temp = _text.Make(Rules.FormatTemp(s.BatteryTempC, false), 12, Palette.Colour(Rules.Classify(s.BatteryTempC, st.TempAmber, st.TempRed), Palette.Ok));
            right -= temp.Width;
            dc.DrawText(temp, new Point(right, y));
            if (s.OnAc)
            {
                right -= 4 + 7;
                dc.DrawGeometry(Palette.Text, null, Bolt(right, y + 2.5));
            }
            FormattedText pct = _text.Make(Rules.FormatPercent(s.BatteryPercent), 12, s.BatteryPercent.HasValue ? Palette.Text : Palette.Label);
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
            _text.Left(dc, label, 12, Palette.Label, x, y);
            _text.Right(dc, value, 12, valueBrush, x + w, y);
        }

        private static Pen DashedPen(Brush brush, double thickness)
        {
            var pen = new Pen(brush, thickness) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) };
            pen.Freeze();
            return pen;
        }
    }
}
