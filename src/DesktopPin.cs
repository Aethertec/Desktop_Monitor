using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;

namespace DesktopMonitor
{
    // Keeps a window on the desktop layer: owned by the window that hosts the desktop icons (owner mode)
    // and always placed at the bottom of the z-order. Re-attaches after Explorer restarts.
    internal sealed class DesktopPin
    {
        private readonly IntPtr _hwnd;
        private readonly bool _useOwner;
        private readonly uint _taskbarCreated;
        private readonly SafeTimer _watchdog;
        private IntPtr _host;

        public DesktopPin(IntPtr hwnd, bool useOwner)
        {
            _hwnd = hwnd;
            _useOwner = useOwner;
            _taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");
            HwndSource.FromHwnd(hwnd).AddHook(WndProc);
            // Covers Explorer not being ready at login and a missed TaskbarCreated broadcast.
            _watchdog = new SafeTimer(TimeSpan.FromSeconds(5), "DesktopPin watchdog", delegate
            {
                if (_useOwner && !Native.IsWindow(_host)) Attach();
            });
            _watchdog.Start();
        }

        public IntPtr Host { get { return _host; } }

        public void Stop()
        {
            _watchdog.Stop();
        }

        public void Attach()
        {
            if (_useOwner)
            {
                _host = FindDesktopHost();
                if (_host == IntPtr.Zero) Log.Write("DesktopPin: desktop host not found yet, retrying in 5 s");
                else Native.SetWindowLongPtr(_hwnd, Native.GWLP_HWNDPARENT, _host);
            }
            if (!Native.IsWindowVisible(_hwnd)) Native.ShowWindow(_hwnd, Native.SW_SHOWNOACTIVATE);
            Native.SetWindowPos(_hwnd, Native.HWND_BOTTOM, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        // The desktop icons live in SHELLDLL_DefView, hosted either by Progman or by a top-level WorkerW.
        internal static IntPtr FindDesktopHost()
        {
            IntPtr progman = Native.FindWindow("Progman", null);
            if (progman != IntPtr.Zero && Native.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero) return progman;
            IntPtr found = IntPtr.Zero;
            Native.EnumWindows(delegate(IntPtr h, IntPtr lParam)
            {
                if (Native.FindWindowEx(h, IntPtr.Zero, "SHELLDLL_DefView", null) == IntPtr.Zero) return true;
                found = h;
                return false;
            }, IntPtr.Zero);
            return found;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Native.WM_WINDOWPOSCHANGING)
            {
                var pos = (Native.WINDOWPOS)Marshal.PtrToStructure(lParam, typeof(Native.WINDOWPOS));
                if ((pos.flags & Native.SWP_NOZORDER) == 0 && pos.hwndInsertAfter != Native.HWND_BOTTOM)
                {
                    pos.hwndInsertAfter = Native.HWND_BOTTOM;
                    Marshal.StructureToPtr(pos, lParam, false);
                }
            }
            else if (_taskbarCreated != 0 && (uint)msg == _taskbarCreated)
            {
                Log.Write("DesktopPin: Explorer restarted, re-attaching");
                Dispatcher.CurrentDispatcher.BeginInvoke(new Action(Attach));
            }
            return IntPtr.Zero;
        }
    }
}
