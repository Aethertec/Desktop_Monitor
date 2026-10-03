using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace DesktopMonitor
{
    // Draws the details panel (v2 spec section 4). One Layout pass both measures and draws, and records the
    // clickable areas, so what you click is always what you see.
    internal sealed class PanelView : FrameworkElement
    {
        public const double PanelWidth = 300;
        private const double PadX = 14, PadY = 12, RowH = 16, Inner = PanelWidth - 2 * PadX;
        private const int AlertsShown = 5; // keeps the panel inside a 720 px work area
        private static readonly Brush Background = Palette.Solid(250, 0x1C, 0x1C, 0x1F);
        private static readonly Pen Border = Palette.Frozen(Palette.Solid(36, 255, 255, 255), 1);
        private static readonly TimeSpan CopiedFor = TimeSpan.FromSeconds(1.5);

        private readonly TextPainter _text = new TextPainter();
        private readonly List<KeyValuePair<Rect, Action>> _hits = new List<KeyValuePair<Rect, Action>>();
        private Snapshot _snap = new Snapshot();
        private PanelData _data;
        private List<Alert> _alerts = new List<Alert>();
        private AppSettings _settings = new AppSettings();
        private bool _powerSwitch;
        private string _copied;
        private DateTime _copiedAt;

        public event Action<string> CopyRequested;
        public event Action<PowerModeKind> PowerModeRequested;
        public event Action LogToggleRequested;
        public event Action OpenLogFolderRequested;
        public event Action TaskManagerRequested;
        public event Action PowerSettingsRequested;

        public PanelView()
        {
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.Grayscale);
            Loaded += delegate { _text.PixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip; InvalidateVisual(); };
        }

        public void Update(Snapshot s, PanelData data, List<Alert> alerts, AppSettings settings, bool powerSwitch)
        {
            _snap = s ?? new Snapshot();
            _data = data;
            _alerts = alerts ?? new List<Alert>();
            _settings = settings;
            _powerSwitch = powerSwitch;
            InvalidateMeasure();
            InvalidateVisual();
        }

        // Shows "Copied" in place of the copied text for 1.5 s.
        public void ShowCopied(string text)
        {
            _copied = text;
            _copiedAt = DateTime.Now;
            InvalidateVisual();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            return new Size(PanelWidth, Layout(null));
        }

        protected override void OnRender(DrawingContext dc)
        {
            double h = Layout(null);
            dc.DrawRoundedRectangle(Background, Border, new Rect(0.5, 0.5, PanelWidth - 1, h - 1), 10, 10);
            Layout(dc);
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            Point p = e.GetPosition(this);
            foreach (KeyValuePair<Rect, Action> hit in _hits)
            {
                if (!hit.Key.Contains(p)) continue;
                hit.Value();
                e.Handled = true;
                return;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            Point p = e.GetPosition(this);
            bool over = _hits.Exists(h => h.Key.Contains(p));
            Cursor = over ? Cursors.Hand : Cursors.Arrow;
        }

        // Measures (dc == null) or draws the panel; returns its height.
        private double Layout(DrawingContext dc)
        {
            if (dc != null) _hits.Clear();
            Snapshot s = _snap;
            AppSettings st = _settings;
            double y = PadY;
            if (dc != null)
            {
                _text.Left(dc, "Details", 13, Palette.Text, PadX, y);
                _text.Right(dc, "Esc to close", 12, Palette.Label, PanelWidth - PadX, y + 1);
            }
            y += 20;

            y = Section(dc, y, "CPU speed");
            string speed = Rules.FormatCpuSpeed(s.CpuLimitPercent, Throttle.Reason(s.CpuTempC, st.TempRed, s.OnAc));
            Brush speedBrush = !s.CpuLimitPercent.HasValue ? Palette.Label : Rules.IsBelowFullSpeed(s.CpuLimitPercent.Value) ? Palette.Red : Palette.Ok;
            if (dc != null) _text.Left(dc, speed, 12, speedBrush, PadX, y);
            y += RowH;

            y = Section(dc, y, "Top apps");
            if (s.TopApps.Count == 0) y = Note(dc, y, "--");
            foreach (AppUsage app in s.TopApps)
                y = Row(dc, y, app.Name, Rules.FormatPercent(app.CpuPercent) + " \u00B7 " + Rules.FormatMemory(app.PrivateBytes), Palette.Text, null);

            y = Section(dc, y, "Network");
            if (_data == null) y = Note(dc, y, "Reading\u2026");
            else if (_data.Adapters.Count == 0) y = Note(dc, y, "Not connected");
            else
            {
                foreach (AdapterInfo a in _data.Adapters)
                {
                    string kindValue = a.Kind == "Wi-Fi"
                        ? (a.WifiName ?? "name hidden by Windows") + (a.SignalPercent.HasValue ? " \u00B7 " + a.SignalPercent.Value + "%" : "")
                        : "connected";
                    y = Row(dc, y, a.Kind, kindValue, Palette.Text, null);
                    string ip = a.Ipv4;
                    y = Row(dc, y, "IP", IsCopied(ip) ? "Copied" : ip, Palette.Ok, delegate { Raise(CopyRequested, ip); });
                    y = Row(dc, y, "Gateway", a.Gateway ?? "none", a.Gateway == null ? Palette.Label : Palette.Text, null);
                }
            }

            y = Section(dc, y, "COM ports");
            if (s.ComPorts.Count == 0) y = Note(dc, y, "None connected");
            foreach (ComPortInfo port in s.ComPorts)
            {
                if (dc != null && port.IsNew) dc.DrawRoundedRectangle(Palette.NewTint, null, new Rect(PadX - 4, y - 1, Inner + 8, RowH), 4, 4);
                string name = Rules.ComPortLabel(port.Name) + (port.IsNew ? " \u00B7 new" : "");
                string portName = port.Port;
                y = RowClickableLabel(dc, y, IsCopied(portName) ? "Copied" : portName, _text.Fit(name, Inner - 60, 12), delegate { Raise(CopyRequested, portName); });
            }

            y = Section(dc, y, "Alerts");
            if (_alerts.Count == 0) y = Note(dc, y, "No alerts today");
            for (int i = 0; i < Math.Min(AlertsShown, _alerts.Count); i++)
                y = Row(dc, y, _alerts[i].Time.ToString("HH:mm", CultureInfo.InvariantCulture), _alerts[i].Title, Palette.Colour(_alerts[i].Severity, Palette.Ok), null);
            if (_alerts.Count > AlertsShown) y = Note(dc, y, "+" + (_alerts.Count - AlertsShown) + " earlier today (all in alerts.csv)");

            y = Section(dc, y, "Power mode");
            PowerModeKind mode = _data == null ? PowerModeKind.Unknown : _data.PowerMode;
            if (_powerSwitch) y = Segments(dc, y, mode);
            else
            {
                y = Row(dc, y, "Current", PowerMode.Name(mode), Palette.Text, null);
                y = Link(dc, y, "Open power settings \u2197", delegate { Raise(PowerSettingsRequested); });
            }

            y = Section(dc, y, "Battery");
            if (dc != null) _text.Left(dc, Rules.FormatBattery(s.BatteryPercent, s.OnAc, s.Charging, s.BatteryMinutesLeft), 12, Palette.Text, PadX, y);
            y += RowH;

            // Footer on two rows (one row does not fit 272 px): log toggle, then the two links.
            y = Section(dc, y, "CSV log");
            string log = st.LogEnabled ? "On \u00B7 every " + Rules.FormatDuration(st.LogIntervalSeconds) + " \u00B7 click to turn off" : "Off \u00B7 click to turn on";
            y = Link(dc, y, log, delegate { Raise(LogToggleRequested); });
            if (dc != null)
            {
                FormattedText folder = _text.Make("Open log folder", 12, Palette.Ok);
                FormattedText task = _text.Make("Task Manager \u2197", 12, Palette.Ok);
                dc.DrawText(folder, new Point(PadX, y));
                Hit(new Rect(PadX, y, folder.Width, RowH), delegate { Raise(OpenLogFolderRequested); });
                double tx = PanelWidth - PadX - task.Width;
                dc.DrawText(task, new Point(tx, y));
                Hit(new Rect(tx, y, task.Width, RowH), delegate { Raise(TaskManagerRequested); });
            }
            y += RowH;
            return y + PadY;
        }

        private double Section(DrawingContext dc, double y, string title)
        {
            y += 6;
            if (dc != null)
            {
                dc.DrawLine(Palette.Divider, new Point(PadX, y + 0.5), new Point(PanelWidth - PadX, y + 0.5));
                _text.Left(dc, title, 12, Palette.Label, PadX, y + 5);
            }
            return y + 5 + RowH;
        }

        private double Row(DrawingContext dc, double y, string label, string value, Brush valueBrush, Action onClickValue)
        {
            if (dc != null)
            {
                double labelW = _text.Width(label, 12);
                _text.Left(dc, label, 12, Palette.Label, PadX, y);
                FormattedText v = _text.Make(_text.Fit(value, Inner - labelW - 10, 12), 12, valueBrush);
                double vx = PanelWidth - PadX - v.Width;
                dc.DrawText(v, new Point(vx, y));
                if (onClickValue != null) Hit(new Rect(vx, y, v.Width, RowH), onClickValue);
            }
            return y + RowH;
        }

        // A row whose label (the COM port) is the clickable part.
        private double RowClickableLabel(DrawingContext dc, double y, string label, string value, Action onClickLabel)
        {
            if (dc != null)
            {
                FormattedText l = _text.Make(label, 12, Palette.Ok);
                dc.DrawText(l, new Point(PadX, y));
                Hit(new Rect(PadX, y, l.Width, RowH), onClickLabel);
                _text.Right(dc, value, 12, Palette.Text, PanelWidth - PadX, y);
            }
            return y + RowH;
        }

        private double Note(DrawingContext dc, double y, string text)
        {
            if (dc != null) _text.Left(dc, text, 12, Palette.Label, PadX, y);
            return y + RowH;
        }

        private double Link(DrawingContext dc, double y, string text, Action onClick)
        {
            if (dc != null)
            {
                FormattedText t = _text.Make(text, 12, Palette.Ok);
                dc.DrawText(t, new Point(PadX, y));
                Hit(new Rect(PadX, y, t.Width, RowH), onClick);
            }
            return y + RowH;
        }

        // Efficiency / Balanced / Performance, the current one highlighted.
        private double Segments(DrawingContext dc, double y, PowerModeKind current)
        {
            if (dc != null)
            {
                var modes = new[] { PowerModeKind.Efficiency, PowerModeKind.Balanced, PowerModeKind.Performance };
                double gap = 4, w = (Inner - 2 * gap) / 3;
                for (int i = 0; i < modes.Length; i++)
                {
                    PowerModeKind m = modes[i];
                    var r = new Rect(PadX + i * (w + gap), y, w, 20);
                    bool on = m == current;
                    dc.DrawRoundedRectangle(on ? Palette.OkTint : Palette.Segment, null, r, 5, 5);
                    FormattedText t = _text.Make(m.ToString(), 11, on ? Palette.Ok : Palette.Text);
                    dc.DrawText(t, new Point(r.X + (w - t.Width) / 2, y + (20 - t.Height) / 2));
                    Hit(r, delegate { Raise(PowerModeRequested, m); });
                }
            }
            return y + 24;
        }

        private bool IsCopied(string text)
        {
            return text != null && text == _copied && DateTime.Now - _copiedAt < CopiedFor;
        }

        private void Hit(Rect r, Action a)
        {
            _hits.Add(new KeyValuePair<Rect, Action>(r, a));
        }

        private static void Raise(Action a)
        {
            if (a != null) a();
        }

        private static void Raise<T>(Action<T> a, T value)
        {
            if (a != null) a(value);
        }
    }

    // The panel window: borderless, topmost while open, beside the card; closes on Esc or when it loses activation.
    internal sealed class DetailsPanel : Window
    {
        private readonly PanelView _view = new PanelView();
        private Box _card, _workArea;

        public DetailsPanel()
        {
            Title = "Desktop Monitor details";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Content = _view;
            KeyDown += (s, e) => { if (e.Key == Key.Escape) Hide(); };
            Deactivated += delegate
            {
                if (!IsVisible) return;
                LastAutoHide = DateTime.Now;
                Hide();
            };
            SizeChanged += delegate { if (IsVisible) Place(); };
            SourceInitialized += delegate
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                long ex = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64() | Native.WS_EX_TOOLWINDOW; // not in Alt+Tab
                Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(ex));
            };
        }

        public PanelView View { get { return _view; } }

        // When the panel last closed because it lost activation. A tray double-click right after that is the user
        // closing the panel, not asking to reopen it.
        public DateTime LastAutoHide { get; private set; }

        public void ShowBeside(Box card, Box workArea)
        {
            _card = card;
            _workArea = workArea;
            _view.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Place();
            Show();
            Activate();
        }

        private void Place()
        {
            double h = IsVisible && ActualHeight > 0 ? ActualHeight : _view.DesiredSize.Height;
            Box p = Rules.PanelPlacement(_card, _workArea, PanelView.PanelWidth, h, 10);
            Left = p.X;
            Top = p.Y;
        }
    }
}
