using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace DesktopMonitor
{
    // One connected adapter as shown in the panel's Network section.
    public sealed class AdapterInfo
    {
        public string Kind;          // "Wi-Fi", "Ethernet" or the adapter's own name
        public string WifiName;      // null when not Wi-Fi or when Windows hides it
        public int? SignalPercent;
        public string Ipv4;
        public string Gateway;
    }

    // Adapters that are up, not loopback/tunnel, and have an IPv4 address; Wi-Fi name and signal from the Native Wi-Fi API.
    internal static class NetworkInfo
    {
        private const int OpcodeCurrentConnection = 7;
        private const int InterfaceListHeader = 8, InterfaceInfoSize = 532;
        private const int OffSsidLength = 520, OffSsid = 524, OffSignal = 576;

        [DllImport("wlanapi.dll")]
        private static extern uint WlanOpenHandle(uint clientVersion, IntPtr reserved, out uint negotiatedVersion, out IntPtr handle);

        [DllImport("wlanapi.dll")]
        private static extern uint WlanCloseHandle(IntPtr handle, IntPtr reserved);

        [DllImport("wlanapi.dll")]
        private static extern uint WlanEnumInterfaces(IntPtr handle, IntPtr reserved, out IntPtr interfaceList);

        [DllImport("wlanapi.dll")]
        private static extern uint WlanQueryInterface(IntPtr handle, ref Guid interfaceGuid, int opCode, IntPtr reserved, out int dataSize, out IntPtr data, out int valueType);

        [DllImport("wlanapi.dll")]
        private static extern void WlanFreeMemory(IntPtr memory);

        public static List<AdapterInfo> Read()
        {
            Dictionary<Guid, KeyValuePair<string, int>> wifi = ReadWifi();
            var list = new List<AdapterInfo>();
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback || ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                if (IsVirtualAdapter(ni.Description)) continue;
                IPInterfaceProperties props = ni.GetIPProperties();
                string ip = null, gateway = null;
                foreach (UnicastIPAddressInformation a in props.UnicastAddresses)
                    if (a.Address.AddressFamily == AddressFamily.InterNetwork) { ip = a.Address.ToString(); break; }
                if (ip == null) continue;
                foreach (GatewayIPAddressInformation g in props.GatewayAddresses)
                    if (g.Address.AddressFamily == AddressFamily.InterNetwork) { gateway = g.Address.ToString(); break; }
                var info = new AdapterInfo { Ipv4 = ip, Gateway = gateway };
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                {
                    info.Kind = "Wi-Fi";
                    Guid id;
                    KeyValuePair<string, int> w;
                    if (Guid.TryParse(ni.Id, out id) && wifi.TryGetValue(id, out w))
                    {
                        info.WifiName = w.Key;
                        info.SignalPercent = w.Value;
                    }
                }
                else info.Kind = ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? "Ethernet" : ni.Name;
                list.Add(info);
            }
            return list;
        }

        // VMware, VirtualBox and Hyper-V/WSL adapters all say "Virtual" in their description. Physical adapters without a
        // gateway (a cable straight to a board) are kept, because their IP is exactly what you need then.
        public static bool IsVirtualAdapter(string description)
        {
            return description != null && description.IndexOf("virtual", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Interface GUID -> (network name, signal %), for connected Wi-Fi interfaces. Empty when the API is unavailable.
        private static Dictionary<Guid, KeyValuePair<string, int>> ReadWifi()
        {
            var result = new Dictionary<Guid, KeyValuePair<string, int>>();
            IntPtr handle, interfaces = IntPtr.Zero;
            uint version;
            try
            {
                if (WlanOpenHandle(2, IntPtr.Zero, out version, out handle) != 0) return result;
            }
            catch (DllNotFoundException)
            {
                return result; // no WLAN service on this machine
            }
            try
            {
                if (WlanEnumInterfaces(handle, IntPtr.Zero, out interfaces) != 0) return result;
                int count = Marshal.ReadInt32(interfaces, 0);
                for (int i = 0; i < count; i++)
                {
                    var item = new IntPtr(interfaces.ToInt64() + InterfaceListHeader + i * InterfaceInfoSize);
                    var guid = (Guid)Marshal.PtrToStructure(item, typeof(Guid));
                    int size, type;
                    IntPtr data;
                    if (WlanQueryInterface(handle, ref guid, OpcodeCurrentConnection, IntPtr.Zero, out size, out data, out type) != 0) continue;
                    try
                    {
                        int ssidLength = Math.Min(32, Marshal.ReadInt32(data, OffSsidLength));
                        var ssid = new byte[ssidLength];
                        Marshal.Copy(new IntPtr(data.ToInt64() + OffSsid), ssid, 0, ssidLength);
                        int signal = Marshal.ReadInt32(data, OffSignal);
                        result[guid] = new KeyValuePair<string, int>(ssidLength > 0 ? Encoding.UTF8.GetString(ssid) : null, signal);
                    }
                    finally
                    {
                        WlanFreeMemory(data);
                    }
                }
            }
            finally
            {
                if (interfaces != IntPtr.Zero) WlanFreeMemory(interfaces);
                WlanCloseHandle(handle, IntPtr.Zero);
            }
            return result;
        }
    }
}
