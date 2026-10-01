using System.Drawing;

namespace DesktopMonitor.Tests
{
    internal static class TrayGlyphTests
    {
        public static void Run()
        {
            TextStaysOnOneLine("62");
            TextStaysOnOneLine("100");
            TextStaysOnOneLine("--");
        }

        // A CPU temperature of 100 wrapped onto two lines inside the 16 px tray icon.
        private static void TextStaysOnOneLine(string text)
        {
            using (Bitmap bmp = TrayIcon.RenderGlyph(text, Color.White))
            {
                int top = -1, bottom = -1;
                for (int y = 0; y < bmp.Height; y++)
                {
                    for (int x = 0; x < bmp.Width; x++)
                    {
                        if (bmp.GetPixel(x, y).R <= 0x60) continue;
                        if (top < 0) top = y;
                        bottom = y;
                    }
                }
                TestMain.True(top >= 0, "tray glyph \"" + text + "\" draws something");
                TestMain.True(bottom - top + 1 <= 10, "tray glyph \"" + text + "\" stays on one line (text rows " + top + ".." + bottom + ")");
            }
        }
    }
}
