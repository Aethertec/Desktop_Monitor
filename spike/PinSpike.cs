using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace DesktopMonitor
{
    // Throwaway experiment (plan Task 1): proves desktop-layer pinning on this PC before the real card exists.
    //   PinSpike.exe              owner mode, click-through
    //   PinSpike.exe --no-owner   bottom-of-stack only, click-through
    //   PinSpike.exe --unlocked   owner mode, not click-through, drag to move
    internal static class PinSpike
    {
        [STAThread]
        private static void Main(string[] args)
        {
            bool useOwner = Array.IndexOf(args, "--no-owner") < 0;
            bool unlocked = Array.IndexOf(args, "--unlocked") >= 0;
            var app = new Application();
            var window = new Window
            {
                Title = "PinSpike",
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ShowInTaskbar = false,
                ShowActivated = false,
                ResizeMode = ResizeMode.NoResize,
                Width = 250,
                Height = 204,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = SystemParameters.WorkArea.Right - 262,
                Top = SystemParameters.WorkArea.Top + 12
            };
            window.Content = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(237, 0x14, 0x14, 0x16)),
                CornerRadius = new CornerRadius(10),
                Child = new TextBlock
                {
                    Text = "Pin spike\nowner mode: " + useOwner + "\nunlocked: " + unlocked,
                    Foreground = Brushes.White,
                    FontSize = 13,
                    Margin = new Thickness(14)
                }
            };
            window.SourceInitialized += delegate
            {
                IntPtr hwnd = new WindowInteropHelper(window).Handle;
                long ex = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64() | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                if (!unlocked) ex |= Native.WS_EX_TRANSPARENT;
                Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(ex));
                var pin = new DesktopPin(hwnd, useOwner);
                pin.Attach();
                Log.Write("PinSpike: owner mode=" + useOwner + ", unlocked=" + unlocked + ", host=0x" + pin.Host.ToString("X"));
            };
            window.MouseLeftButtonDown += delegate { if (unlocked) window.DragMove(); };
            app.Run(window);
        }
    }
}
