using System;
using System.Collections.Generic;
using System.Management;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace DesktopMonitor
{
    // One serial port as shown in the panel. Name is null until the WMI lookup has finished.
    public sealed class ComPortInfo
    {
        public string Port;
        public string Name;
        public DateTime FirstSeen;
        public bool AppearedLive; // false for ports already present when the app started (they never alert)
        public bool IsNew;        // appeared live less than 10 s ago
    }

    // Tracks the port list across polls. Pure; tested in ComPortsTests.
    public sealed class ComPortTracker
    {
        private static readonly TimeSpan NewFor = TimeSpan.FromSeconds(10);
        private readonly Dictionary<string, ComPortInfo> _ports = new Dictionary<string, ComPortInfo>(StringComparer.OrdinalIgnoreCase);
        private bool _primed;

        // Feeds one poll. Returns the ports that appeared since the previous poll (none on the very first poll).
        public List<string> Update(IEnumerable<string> ports, DateTime now)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var appeared = new List<string>();
            foreach (string port in ports)
            {
                seen.Add(port);
                if (_ports.ContainsKey(port)) continue;
                _ports[port] = new ComPortInfo { Port = port, FirstSeen = now, AppearedLive = _primed };
                if (_primed) appeared.Add(port);
            }
            foreach (string gone in new List<string>(_ports.Keys))
                if (!seen.Contains(gone)) _ports.Remove(gone);
            _primed = true;
            return appeared;
        }

        public void SetName(string port, string name)
        {
            ComPortInfo info;
            if (_ports.TryGetValue(port, out info)) info.Name = name;
        }

        // A copy of the current ports in natural order (COM3 before COM10), with IsNew worked out for `now`.
        public List<ComPortInfo> Current(DateTime now)
        {
            var list = new List<ComPortInfo>();
            foreach (ComPortInfo p in _ports.Values)
                list.Add(new ComPortInfo { Port = p.Port, Name = p.Name, FirstSeen = p.FirstSeen, AppearedLive = p.AppearedLive, IsNew = p.AppearedLive && now - p.FirstSeen < NewFor });
            list.Sort(delegate(ComPortInfo a, ComPortInfo b) { return ComparePorts(a.Port, b.Port); });
            return list;
        }

        public static int ComparePorts(string a, string b)
        {
            int na = PortNumber(a), nb = PortNumber(b);
            if (na >= 0 && nb >= 0 && na != nb) return na.CompareTo(nb);
            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static int PortNumber(string port)
        {
            int n;
            return port != null && port.StartsWith("COM", StringComparison.OrdinalIgnoreCase) && int.TryParse(port.Substring(3), out n) ? n : -1;
        }
    }

    // Windows side: the port list from the registry (sub-millisecond) and friendly names from WMI (about 1 s).
    internal static class ComPortReader
    {
        private static readonly Regex PortInName = new Regex(@"^(.*?)\s*\((COM\d+)\)\s*$", RegexOptions.IgnoreCase);

        public static List<string> ReadPorts()
        {
            var ports = new List<string>();
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM"))
            {
                if (key == null) return ports; // no serial ports registered at all
                foreach (string valueName in key.GetValueNames())
                {
                    var port = key.GetValue(valueName) as string;
                    if (!string.IsNullOrEmpty(port)) ports.Add(port.Trim());
                }
            }
            return ports;
        }

        // Port -> device name, e.g. "COM5" -> "Silicon Labs CP210x USB to UART Bridge".
        public static Dictionary<string, string> ReadFriendlyNames()
        {
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using (var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'"))
            using (ManagementObjectCollection results = searcher.Get())
            {
                foreach (ManagementBaseObject o in results)
                {
                    var name = o["Name"] as string;
                    Match m = name == null ? Match.Empty : PortInName.Match(name);
                    if (m.Success) names[m.Groups[2].Value] = m.Groups[1].Value;
                    o.Dispose();
                }
            }
            return names;
        }
    }
}
