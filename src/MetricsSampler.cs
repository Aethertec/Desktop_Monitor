using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DesktopMonitor
{
    // Reads every metric (spec section 4). Each reader is isolated: a failure is logged once, yields null
    // until it recovers, and never escapes Sample().
    internal sealed class MetricsSampler : IDisposable
    {
        private const string ThermalCategory = "Thermal Zone Information";
        private const double TempEvery = 2, BatteryEvery = 10, DiskEvery = 60; // seconds

        private readonly HashSet<string> _failing = new HashSet<string>();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private AppSettings _settings;

        private PerformanceCounter _cpu;
        private Dictionary<string, CounterSample> _gpuPrev;
        private long _rxPrev, _txPrev;
        private double _netPrevAt = -1;

        private PerformanceCounter _tCpu, _tSkin, _tBattery;
        private bool _thermalOpen, _thermalTenths;
        private double _tempAt = -1, _batteryAt = -1, _diskAt = -1;
        private double? _cpuTemp, _skinTemp, _batteryTemp, _battery, _disk;
        private bool _onAc;

        public MetricsSampler(AppSettings settings)
        {
            _settings = settings;
        }

        public void ApplySettings(AppSettings settings)
        {
            bool zonesChanged = settings.ZoneCpu != _settings.ZoneCpu || settings.ZoneSkin != _settings.ZoneSkin || settings.ZoneBattery != _settings.ZoneBattery;
            _settings = settings;
            if (zonesChanged) CloseThermal();
        }

        // After resume from sleep counters and baselines may be stale: rebuild everything on the next Sample().
        public void Reset()
        {
            CloseCpu();
            _gpuPrev = null;
            _netPrevAt = -1;
            CloseThermal();
            _batteryAt = -1;
            _diskAt = -1;
        }

        public Snapshot Sample()
        {
            double now = _clock.Elapsed.TotalSeconds;
            var s = new Snapshot();
            s.CpuPercent = Read("cpu", ReadCpu, CloseCpu);
            s.RamPercent = Read("ram", ReadRam, null);
            s.GpuPercent = Read("gpu", ReadGpu, delegate { _gpuPrev = null; });
            ReadNetwork(s, now);
            if (Due(ref _tempAt, now, TempEvery)) ReadTemps();
            if (Due(ref _batteryAt, now, BatteryEvery)) ReadBattery();
            if (Due(ref _diskAt, now, DiskEvery)) _disk = Read("disk", ReadDisk, null);
            s.CpuTempC = _cpuTemp;
            s.SkinTempC = _skinTemp;
            s.BatteryTempC = _batteryTemp;
            s.BatteryPercent = _battery;
            s.OnAc = _onAc;
            s.DiskPercent = _disk;
            return s;
        }

        private static bool Due(ref double last, double now, double every)
        {
            if (last >= 0 && now - last < every) return false;
            last = now;
            return true;
        }

        private double? Read(string name, Func<double?> reader, Action reset)
        {
            try
            {
                double? v = reader();
                Ok(name);
                return v;
            }
            catch (Exception ex)
            {
                Fail(name, ex);
                if (reset != null) reset();
                return null;
            }
        }

        private void Ok(string name)
        {
            if (_failing.Remove(name)) Log.Write("Metrics: " + name + " recovered");
        }

        private void Fail(string name, Exception ex)
        {
            if (_failing.Add(name)) Log.Write("Metrics: " + name + " unavailable, " + ex.GetType().Name + ": " + ex.Message);
        }

        private double? ReadCpu()
        {
            if (_cpu == null)
            {
                _cpu = new PerformanceCounter("Processor Information", "% Processor Utility", "_Total", true);
                _cpu.NextValue(); // rate counter: the first read is always 0, so discard it
                return null;
            }
            return Rules.ClampPercent(_cpu.NextValue());
        }

        private void CloseCpu()
        {
            if (_cpu != null) _cpu.Dispose();
            _cpu = null;
        }

        private static double? ReadRam()
        {
            var m = new Native.MEMORYSTATUSEX();
            m.dwLength = (uint)Marshal.SizeOf(typeof(Native.MEMORYSTATUSEX));
            if (!Native.GlobalMemoryStatusEx(ref m)) throw new Win32Exception(Marshal.GetLastWin32Error());
            return Rules.Percent(m.ullTotalPhys - m.ullAvailPhys, m.ullTotalPhys);
        }

        // One category read per tick (1-3 ms on this PC); utilisation is the rate between consecutive raw samples.
        private double? ReadGpu()
        {
            InstanceDataCollection data = new PerformanceCounterCategory("GPU Engine").ReadCategory()["Utilization Percentage"];
            var current = new Dictionary<string, CounterSample>();
            double sum = 0;
            foreach (InstanceData d in data.Values)
            {
                if (d.InstanceName.IndexOf("engtype_3D", StringComparison.OrdinalIgnoreCase) < 0) continue;
                current[d.InstanceName] = d.Sample;
                CounterSample prev;
                if (_gpuPrev != null && _gpuPrev.TryGetValue(d.InstanceName, out prev)) sum += CounterSample.Calculate(prev, d.Sample);
            }
            bool primed = _gpuPrev != null;
            _gpuPrev = current;
            return primed ? Rules.ClampPercent(sum) : (double?)null;
        }

        private void ReadNetwork(Snapshot s, double now)
        {
            try
            {
                long rx = 0, tx = 0;
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback || ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    IPInterfaceStatistics st = ni.GetIPStatistics();
                    rx += st.BytesReceived;
                    tx += st.BytesSent;
                }
                if (_netPrevAt >= 0)
                {
                    s.DownBytesPerSec = Rules.Rate(_rxPrev, rx, now - _netPrevAt);
                    s.UpBytesPerSec = Rules.Rate(_txPrev, tx, now - _netPrevAt);
                }
                _rxPrev = rx;
                _txPrev = tx;
                _netPrevAt = now;
                Ok("net");
            }
            catch (Exception ex)
            {
                Fail("net", ex);
                _netPrevAt = -1;
            }
        }

        private void ReadTemps()
        {
            try
            {
                if (!_thermalOpen) OpenThermal();
                _cpuTemp = ReadZone(_tCpu);
                _skinTemp = ReadZone(_tSkin);
                _batteryTemp = ReadZone(_tBattery);
                Ok("temps");
            }
            catch (Exception ex)
            {
                Fail("temps", ex);
                CloseThermal();
                _cpuTemp = _skinTemp = _batteryTemp = null;
            }
        }

        private void OpenThermal()
        {
            string[] instances = new PerformanceCounterCategory(ThermalCategory).GetInstanceNames();
            _thermalTenths = PerformanceCounterCategory.CounterExists("High Precision Temperature", ThermalCategory);
            _tCpu = OpenZone(instances, _settings.ZoneCpu);
            _tSkin = OpenZone(instances, _settings.ZoneSkin);
            _tBattery = OpenZone(instances, _settings.ZoneBattery);
            _thermalOpen = true;
        }

        // Instances look like "\_TZ.CPUZ"; a zone that does not exist on this machine stays null and reads as "--".
        private PerformanceCounter OpenZone(string[] instances, string zone)
        {
            foreach (string inst in instances)
            {
                if (inst.EndsWith(zone, StringComparison.OrdinalIgnoreCase))
                    return new PerformanceCounter(ThermalCategory, _thermalTenths ? "High Precision Temperature" : "Temperature", inst, true);
            }
            return null;
        }

        private double? ReadZone(PerformanceCounter counter)
        {
            return counter == null ? (double?)null : Rules.ThermalToCelsius(counter.NextValue(), _thermalTenths);
        }

        private void CloseThermal()
        {
            foreach (PerformanceCounter c in new[] { _tCpu, _tSkin, _tBattery })
                if (c != null) c.Dispose();
            _tCpu = _tSkin = _tBattery = null;
            _thermalOpen = false;
            _tempAt = -1; // re-read on the next Sample()
        }

        private void ReadBattery()
        {
            try
            {
                PowerStatus p = SystemInformation.PowerStatus;
                _battery = Rules.BatteryPercent(p.BatteryLifePercent);
                _onAc = p.PowerLineStatus == PowerLineStatus.Online;
                Ok("battery");
            }
            catch (Exception ex)
            {
                Fail("battery", ex);
                _battery = null;
            }
        }

        private static double? ReadDisk()
        {
            var c = new DriveInfo("C");
            return Rules.Percent(c.TotalSize - c.TotalFreeSpace, c.TotalSize);
        }

        public void Dispose()
        {
            CloseCpu();
            CloseThermal();
        }
    }
}
