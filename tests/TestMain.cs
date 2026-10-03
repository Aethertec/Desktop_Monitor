using System;
using System.Collections.Generic;
using System.IO;

namespace DesktopMonitor.Tests
{
    // Minimal test runner (no framework available with the built-in compiler). build.ps1 -Test compiles and runs it.
    internal static class TestMain
    {
        private static int _passed, _failed;

        [STAThread]
        private static int Main()
        {
            Log.FilePath = Path.Combine(Path.GetTempPath(), "DesktopMonitorTests.log");
            RulesTests.Run();
            SettingsTests.Run();
            TrayGlyphTests.Run();
            SafeTimerTests.Run();
            PowerModeTests.Run();
            Console.WriteLine(_passed + " passed, " + _failed + " failed");
            return _failed == 0 ? 0 : 1;
        }

        public static void Equal<T>(T expected, T actual, string name)
        {
            if (EqualityComparer<T>.Default.Equals(expected, actual)) { _passed++; return; }
            _failed++;
            Console.WriteLine("FAIL " + name + ": expected <" + Show(expected) + "> but got <" + Show(actual) + ">");
        }

        public static void Near(double expected, double? actual, string name)
        {
            if (actual.HasValue && Math.Abs(expected - actual.Value) < 1e-6) { _passed++; return; }
            _failed++;
            Console.WriteLine("FAIL " + name + ": expected <" + expected + "> but got <" + Show(actual) + ">");
        }

        public static void True(bool condition, string name)
        {
            Equal(true, condition, name);
        }

        public static void Throws<TException>(Action action, string name) where TException : Exception
        {
            try
            {
                action();
                _failed++;
                Console.WriteLine("FAIL " + name + ": expected " + typeof(TException).Name + " but nothing was thrown");
            }
            catch (TException)
            {
                _passed++;
            }
            catch (Exception ex)
            {
                _failed++;
                Console.WriteLine("FAIL " + name + ": expected " + typeof(TException).Name + " but got " + ex.GetType().Name);
            }
        }

        private static string Show(object o)
        {
            return o == null ? "null" : o.ToString();
        }
    }
}
