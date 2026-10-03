using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace DesktopMonitor
{
    // Borderless transparent window hosting the card (v1 spec section 5): click-through while locked,
    // draggable while unlocked, pinned to the desktop layer.
    internal sealed class CardWindow : Window
    {
        private const double EdgeMargin = 12;
        private readonly CardView _card = new CardView();
        private readonly bool _useOwner;
        private IntPtr _hwnd;
        private DesktopPin _pin;

        public event Action<double, double> PositionCommitted;
        public event Action<bool> UnlockedChanged;

        public CardWindow(AppSettings settings, bool useOwner)
        {
            _useOwner = useOwner;
            Title = "Desktop Monitor";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Content = _card;
            _card.ApplySettings(settings);
            _card.MouseLeftButtonDown += OnCardMouseDown;
            Place(settings);
            SourceInitialized += OnSourceInitialized;
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            Closed += delegate
            {
                SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
                if (_pin != null) _pin.Stop(); // a recreated card must not leave the old watchdog running
            };
        }

        public CardView Card { get { return _card; } }

        public bool Unlocked { get { return _card.Unlocked; } }

        public Box Bounds { get { return new Box(Left, Top, CardView.CardWidth, CardView.CardHeight(_card.Unlocked)); } }

        public void ApplySettings(AppSettings settings)
        {
            _card.ApplySettings(settings);
        }

        public void SetUnlocked(bool unlocked)
        {
            if (_card.Unlocked == unlocked) return;
            _card.Unlocked = unlocked;
            SetClickThrough(!unlocked);
            Action<bool> handler = UnlockedChanged;
            if (handler != null) handler(unlocked);
        }

        // Work areas of all screens in DIPs (the app is system-DPI aware).
        public static List<Box> WorkAreas()
        {
            double scale = SystemDpiScale();
            var areas = new List<Box>();
            foreach (System.Windows.Forms.Screen screen in System.Windows.Forms.Screen.AllScreens)
            {
                System.Drawing.Rectangle r = screen.WorkingArea;
                areas.Add(new Box(r.X / scale, r.Y / scale, r.Width / scale, r.Height / scale));
            }
            return areas;
        }

        // The work area holding the centre of `b`, or the primary one.
        public static Box WorkAreaContaining(Box b)
        {
            double cx = b.X + b.W / 2, cy = b.Y + b.H / 2;
            foreach (Box wa in WorkAreas())
                if (cx >= wa.X && cx < wa.X + wa.W && cy >= wa.Y && cy < wa.Y + wa.H) return wa;
            Rect p = SystemParameters.WorkArea;
            return new Box(p.X, p.Y, p.Width, p.Height);
        }

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            long ex = Native.GetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE).ToInt64();
            ex |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TRANSPARENT;
            Native.SetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE, new IntPtr(ex));
            _pin = new DesktopPin(_hwnd, _useOwner);
            _pin.Attach();
        }

        private void SetClickThrough(bool on)
        {
            if (_hwnd == IntPtr.Zero) return;
            long ex = Native.GetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE).ToInt64();
            ex = on ? ex | Native.WS_EX_TRANSPARENT : ex & ~Native.WS_EX_TRANSPARENT;
            Native.SetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE, new IntPtr(ex));
        }

        // DragMove returns when the button is released; that is when the position is saved and the card re-locks.
        private void OnCardMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!_card.Unlocked) return;
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                return; // button already released
            }
            Action<double, double> handler = PositionCommitted;
            if (handler != null) handler(Left, Top);
            SetUnlocked(false);
        }

        // A projector unplugged or a resolution change while running: bring the card back on screen without
        // overwriting the saved position, so it returns there when the old display comes back and the app restarts.
        private void OnDisplaySettingsChanged(object sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (Rules.IsMostlyOnScreen(Bounds, WorkAreas())) return;
                Rect wa = SystemParameters.WorkArea;
                Box p = Rules.DefaultPlacement(new Box(wa.X, wa.Y, wa.Width, wa.Height), CardView.CardWidth, CardView.CardHeight(false), EdgeMargin);
                Left = p.X;
                Top = p.Y;
            }));
        }

        private void Place(AppSettings s)
        {
            var saved = new Box(s.X ?? 0, s.Y ?? 0, CardView.CardWidth, CardView.CardHeight(false));
            if (s.X.HasValue && s.Y.HasValue && Rules.IsMostlyOnScreen(saved, WorkAreas()))
            {
                Left = s.X.Value;
                Top = s.Y.Value;
                return;
            }
            Rect wa = SystemParameters.WorkArea; // primary screen, in DIPs
            Box p = Rules.DefaultPlacement(new Box(wa.X, wa.Y, wa.Width, wa.Height), CardView.CardWidth, CardView.CardHeight(false), EdgeMargin);
            Left = p.X;
            Top = p.Y;
        }

        private static double SystemDpiScale()
        {
            using (System.Drawing.Graphics g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero))
                return g.DpiX / 96.0;
        }
    }
}
