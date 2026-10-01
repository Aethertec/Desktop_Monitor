using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace DesktopMonitor
{
    // Borderless transparent window hosting the card (spec section 5): click-through while locked,
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
        }

        public CardView Card { get { return _card; } }

        public bool Unlocked { get { return _card.Unlocked; } }

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

        private void Place(AppSettings s)
        {
            double scale = SystemDpiScale();
            var areas = new List<Box>();
            foreach (System.Windows.Forms.Screen screen in System.Windows.Forms.Screen.AllScreens)
            {
                System.Drawing.Rectangle r = screen.WorkingArea;
                areas.Add(new Box(r.X / scale, r.Y / scale, r.Width / scale, r.Height / scale));
            }
            var saved = new Box(s.X ?? 0, s.Y ?? 0, CardView.CardWidth, CardView.CardHeight(false));
            if (s.X.HasValue && s.Y.HasValue && Rules.IsMostlyOnScreen(saved, areas))
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
