using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DesktopMonitor
{
    // Notification-area icon (spec section 6): live CPU temperature, tooltip and the right-click menu.
    internal sealed class TrayIcon : IDisposable
    {
        private readonly NotifyIcon _icon = new NotifyIcon();
        private readonly ContextMenuStrip _menu = new ContextMenuStrip();
        private readonly ToolStripMenuItem _unlock;
        private readonly ToolStripMenuItem _autostart;
        private readonly string _settingsPath;
        private Icon _current;
        private IntPtr _currentHandle;
        private string _shownKey;

        public event Action UnlockToggled;
        public event Action ExitRequested;

        public TrayIcon(string settingsPath)
        {
            _settingsPath = settingsPath;
            _unlock = new ToolStripMenuItem("Unlock to move", null, delegate { Raise(UnlockToggled); });
            _autostart = new ToolStripMenuItem("Start with Windows", null, delegate { ToggleAutostart(); });
            _menu.Items.Add(_unlock);
            _menu.Items.Add(_autostart);
            _menu.Items.Add(new ToolStripMenuItem("Open settings", null, delegate { OpenSettings(); }));
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(new ToolStripMenuItem("Exit", null, delegate { Raise(ExitRequested); }));
            _menu.Opening += delegate { _autostart.Checked = Autostart.IsEnabled(Application.ExecutablePath); };
            _icon.ContextMenuStrip = _menu;
            _icon.Text = "Desktop Monitor";
            SetIcon("--", Level.Unknown);
            _icon.Visible = true;
        }

        public void SetUnlocked(bool unlocked)
        {
            _unlock.Text = unlocked ? "Lock position" : "Unlock to move";
        }

        public void Update(Snapshot s, AppSettings st)
        {
            string text = Rules.FormatNumber(s.CpuTempC);
            Level level = Rules.Classify(s.CpuTempC, st.TempAmber, st.TempRed);
            if (text + level != _shownKey) SetIcon(text, level);
            _icon.Text = Rules.Tooltip(s.CpuPercent, s.CpuTempC);
        }

        private void SetIcon(string text, Level level)
        {
            _shownKey = text + level;
            using (Bitmap bmp = RenderGlyph(text, TrayColour(level)))
            {
                IntPtr handle = bmp.GetHicon();
                Icon icon = Icon.FromHandle(handle);
                _icon.Icon = icon;
                ReleaseCurrentIcon();
                _current = icon;
                _currentHandle = handle;
            }
        }

        // 16 x 16 dark tile with the temperature number, so it reads on light and dark taskbars.
        internal static Bitmap RenderGlyph(string text, Color colour)
        {
            var bmp = new Bitmap(16, 16);
            using (Graphics g = Graphics.FromImage(bmp))
            using (var bg = new SolidBrush(Color.FromArgb(0x14, 0x14, 0x16)))
            using (var fg = new SolidBrush(colour))
            using (var font = new Font("Segoe UI", text.Length >= 3 ? 8f : 10f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var fmt = new StringFormat(StringFormat.GenericTypographic))
            {
                fmt.Alignment = StringAlignment.Center;
                fmt.LineAlignment = StringAlignment.Center;
                fmt.FormatFlags |= StringFormatFlags.NoWrap; // "100" must not wrap inside the 16 px tile
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.FillRectangle(bg, 0, 0, 16, 16);
                g.DrawString(text, font, fg, new RectangleF(0, 0, 16, 16), fmt);
            }
            return bmp;
        }

        private static Color TrayColour(Level level)
        {
            switch (level)
            {
                case Level.Ok: return Color.FromArgb(0x5D, 0xCA, 0xA5);
                case Level.Amber: return Color.FromArgb(0xFA, 0xC7, 0x75);
                case Level.Red: return Color.FromArgb(0xF0, 0x95, 0x95);
                default: return Color.FromArgb(0xA3, 0xA3, 0xA3);
            }
        }

        private void ReleaseCurrentIcon()
        {
            if (_current == null) return;
            _current.Dispose();
            Native.DestroyIcon(_currentHandle); // Icon.FromHandle does not own the handle
            _current = null;
        }

        private void ToggleAutostart()
        {
            try
            {
                bool on = !Autostart.IsEnabled(Application.ExecutablePath);
                Autostart.Set(on, Application.ExecutablePath);
                _autostart.Checked = on;
            }
            catch (Exception ex)
            {
                Log.Write("Tray: could not change Start with Windows, " + ex.Message);
            }
        }

        private void OpenSettings()
        {
            try
            {
                Process.Start(_settingsPath);
            }
            catch (Exception ex) // no app associated with .json
            {
                Log.Write("Tray: no .json handler (" + ex.Message + "), opening in Notepad");
                try { Process.Start("notepad.exe", "\"" + _settingsPath + "\""); }
                catch (Exception ex2) { Log.Write("Tray: could not open settings, " + ex2.Message); }
            }
        }

        private static void Raise(Action action)
        {
            if (action != null) action();
        }

        public void Dispose()
        {
            _icon.Visible = false;
            _icon.Dispose();
            _menu.Dispose();
            ReleaseCurrentIcon();
        }
    }

    // HKCU Run key is the single source of truth for "Start with Windows" (no admin needed).
    internal static class Autostart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "DesktopMonitor";

        // Only counts as enabled when the entry points at this exe, so a moved build shows unchecked.
        public static bool IsEnabled(string exePath)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey))
            {
                var value = key == null ? null : key.GetValue(ValueName) as string;
                return value != null && string.Equals(value.Trim('"'), exePath, StringComparison.OrdinalIgnoreCase);
            }
        }

        public static void Set(bool enabled, string exePath)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enabled) key.SetValue(ValueName, "\"" + exePath + "\"");
                else key.DeleteValue(ValueName, false);
            }
        }
    }
}
