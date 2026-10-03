using System;

namespace DesktopMonitor.Tests
{
    internal static class NetworkTests
    {
        public static void Run()
        {
            VirtualAdapters_AreHidden();
            Read_ReturnsOnlyAdaptersWithAnAddress();
        }

        private static void VirtualAdapters_AreHidden()
        {
            TestMain.True(NetworkInfo.IsVirtualAdapter("VMware Virtual Ethernet Adapter for VMnet1"), "VMware");
            TestMain.True(NetworkInfo.IsVirtualAdapter("VirtualBox Host-Only Ethernet Adapter"), "VirtualBox");
            TestMain.True(NetworkInfo.IsVirtualAdapter("Hyper-V Virtual Ethernet Adapter"), "Hyper-V / WSL");
            TestMain.True(!NetworkInfo.IsVirtualAdapter("Intel(R) Wi-Fi 6 AX201 160MHz"), "real Wi-Fi");
            TestMain.True(!NetworkInfo.IsVirtualAdapter("Realtek USB GbE Family Controller"), "USB Ethernet to a board");
        }

        private static void Read_ReturnsOnlyAdaptersWithAnAddress()
        {
            bool allHaveIp = true;
            foreach (AdapterInfo a in NetworkInfo.Read()) if (string.IsNullOrEmpty(a.Ipv4)) allHaveIp = false;
            TestMain.True(allHaveIp, "every listed adapter has an IPv4 address (reads this PC without throwing)");
        }
    }
}
