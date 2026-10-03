using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace DesktopMonitor
{
    // Reads every metric (v1 spec section 4, v2 spec section 5). Each reader is isolated: a failure is logged once,
    // yields null until it recovers, and never escapes Sample(). Used from the sampler thread only.
    internal sealed class MetricsSampler : IDisposable
    {
        private const string ThermalCategory = "Thermal Zone Information";
        private const double TempEvery = 2, ComEvery = 2, ProcessEvery = 3, BatteryEvery = 10, DiskEvery = 60; // seconds

        private readonly HashSet<string> _failing = new HashSet<string>();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private AppSettings _settings;

        private PerformanceCounter _cpu, _limit;
        private Dictionary<string, CounterSample> _gpuPrev;
        private Dictionary<string, long> _rxPrev, _txPrev; // per adapter Id
        private double _netPrevAt = -1;

        private PerformanceCounter _tCpu, _tSkin, _tBattery;
        private bool _thermalOpen, _thermalTenths;
        private double _tempAt = -1, _comAt = -1, _processAt = -1, _batteryAt = -1, _diskAt = -1;
        private double? _cpuTemp, _skinTemp, _batteryTemp, _battery, _disk, _diskFree, _cpuLimit;
        private bool _onAc, _charging;
        private int? _minutesLeft;
        private readonly Throttle _throttle = new Throttle();

        private ProcessReader _processReader;
        private List<ProcessSample> _processPrev;
        private double _processPrevAt;
        private List<AppUsage> _topApps = new List<AppUsage>();

        private readonly ComPortTracker _comTracker = new ComPortTracker();
        private List<ComPortInfo> _comPorts = new List<ComPortInfo>();
        private static readonly TimeSpan LookupRetryAfter = TimeSpan.FromSeconds(30);
        private volatile NameLookup _lookupDone;  // latest successful WMI lookup, handed over by the thread pool
        private NameLookup _lookupApplied;
        private string _lookupFor;                // port set of the lookup that is running or last started
        private long _lookupStartedTicks, _lookupFailedTicks;
        private int _lookupBusy;

        // A finished name lookup: the names it found and the ports it covered.
        private sealed class NameLookup
        {
            public Dictionary<string, string> Names;
            public List<string> Ports;
        }

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
            CloseLimit();
            _gpuPrev = null;
            _netPrevAt = -1;
            CloseThermal();
            _processPrev = null;
            _batteryAt = -1;
            _diskAt = -1;
        }

        public Snapshot Sample()
        {
            double now = _clock.Elapsed.TotalSeconds;
            DateTime wall = DateTime.Now;
            var s = new Snapshot { Time = wall };
            s.CpuPercent = Read("cpu", ReadCpu, CloseCpu);
            s.RamPercent = Read("ram", ReadRam, null);
            s.GpuPercent = Read("gpu", ReadGpu, delegate { _gpuPrev = null; });
            ReadNetwork(s, now);
            if (Due(ref _tempAt, now, TempEvery))
            {
                ReadTemps();
                _cpuLimit = Read("limit", ReadLimit, CloseLimit);
                _throttle.Feed(_cpuLimit);
            }
            if (Due(ref _comAt, now, ComEvery)) ReadComPorts(wall);
            else ApplyFinishedLookup(wall);
            if (Due(ref _processAt, now, ProcessEvery)) ReadProcesses(now);
            if (Due(ref _batteryAt, now, BatteryEvery)) ReadBattery();
            if (Due(ref _diskAt, now, DiskEvery)) _disk = Read("disk", ReadDisk, null);
            s.CpuTempC = _cpuTemp;
            s.SkinTempC = _skinTemp;
            s.BatteryTempC = _batteryTemp;
            s.CpuLimitPercent = _cpuLimit;
            s.Throttled = _throttle.Shown;
            s.BatteryPercent = _battery;
            s.OnAc = _onAc;
            s.Charging = _charging;
            s.BatteryMinutesLeft = _minutesLeft;
            s.DiskPercent = _disk;
            s.DiskFreeBytes = _diskFree;
            s.TopApps = _topApps;
            s.ComPorts = _comPorts;
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

        // 100 = full speed; lower while the CPU is held back by heat or power limits.
        private double? ReadLimit()
        {
            if (_limit == null)
            {
                _limit = new PerformanceCounter("Processor Information", "% Performance Limit", "_Total", true);
                _limit.NextValue(); // discard the first read, as for the other counters
                return null;
            }
            return Rules.ClampPercent(_limit.NextValue());
        }

        private void CloseLimit()
        {
            if (_limit != null) _limit.Dispose();
            _limit = null;
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
                var rx = new Dictionary<string, long>();
                var tx = new Dictionary<string, long>();
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback || ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    IPInterfaceStatistics st = ni.GetIPStatistics();
                    rx[ni.Id] = st.BytesReceived;
                    tx[ni.Id] = st.BytesSent;
                }
                if (_netPrevAt >= 0)
                {
                    s.DownBytesPerSec = Rules.AdapterRate(_rxPrev, rx, now - _netPrevAt);
                    s.UpBytesPerSec = Rules.AdapterRate(_txPrev, tx, now - _netPrevAt);
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
                _charging = (p.BatteryChargeStatus & BatteryChargeStatus.Charging) != 0;
                _minutesLeft = p.BatteryLifeRemaining > 0 ? p.BatteryLifeRemaining / 60 : (int?)null;
                Ok("battery");
            }
            catch (Exception ex)
            {
                Fail("battery", ex);
                _battery = null;
                _minutesLeft = null;
            }
        }

        private double? ReadDisk()
        {
            var c = new DriveInfo("C");
            _diskFree = c.TotalFreeSpace;
            return Rules.Percent(c.TotalSize - c.TotalFreeSpace, c.TotalSize);
        }

        private void ReadProcesses(double now)
        {
            try
            {
                if (_processReader == null) _processReader = new ProcessReader();
                List<ProcessSample> current = _processReader.Read();
                if (_processPrev != null)
                {
                    List<AppUsage> apps = ProcessTable.Compare(_processPrev, current, now - _processPrevAt, Environment.ProcessorCount);
                    _topApps = apps.GetRange(0, Math.Min(5, apps.Count));
                }
                _processPrev = current;
                _processPrevAt = now;
                Ok("processes");
            }
            catch (Exception ex)
            {
                Fail("processes", ex);
                _processPrev = null;
                _topApps = new List<AppUsage>();
            }
        }

        // The port list is cheap (registry); device names need WMI (about 1 s), so that runs on the thread pool
        // whenever the set of ports changes and some of them have no name yet.
        private void ReadComPorts(DateTime now)
        {
            try
            {
                List<string> ports = ComPortReader.ReadPorts();
                _comTracker.Update(ports, now);
                NameLookup done = _lookupDone;
                if (done != null) _comTracker.ApplyNames(done.Names, done.Ports); // also renames a replugged port at once
                bool missing = _comTracker.Current(now).Exists(p => p.Name == null);
                ports.Sort(ComPortTracker.ComparePorts);
                string key = string.Join(",", ports);
                long failedAt = Interlocked.Read(ref _lookupFailedTicks);
                bool retryDue = failedAt > _lookupStartedTicks && DateTime.Now.Ticks - failedAt >= LookupRetryAfter.Ticks;
                if (missing && (key != _lookupFor || retryDue)) StartNameLookup(key, ports);
                _comPorts = _comTracker.Current(now);
                Ok("com");
            }
            catch (Exception ex)
            {
                Fail("com", ex);
            }
        }

        // Every tick, not only on the 2 s poll: a lookup that has just finished shows its names within a second.
        private void ApplyFinishedLookup(DateTime now)
        {
            NameLookup done = _lookupDone;
            if (done == null || ReferenceEquals(done, _lookupApplied)) return;
            _lookupApplied = done;
            _comTracker.ApplyNames(done.Names, done.Ports);
            _comPorts = _comTracker.Current(now);
        }

        private void StartNameLookup(string key, List<string> ports)
        {
            if (Interlocked.CompareExchange(ref _lookupBusy, 1, 0) != 0) return;
            _lookupFor = key;
            _lookupStartedTicks = DateTime.Now.Ticks;
            var covered = new List<string>(ports);
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    _lookupDone = new NameLookup { Names = ComPortReader.ReadFriendlyNames(), Ports = covered };
                }
                catch (Exception ex)
                {
                    Interlocked.Exchange(ref _lookupFailedTicks, DateTime.Now.Ticks); // retried after LookupRetryAfter
                    Log.Write("Metrics: COM port names unavailable, " + ex.Message);
                }
                finally
                {
                    Interlocked.Exchange(ref _lookupBusy, 0);
                }
            });
        }

        public void Dispose()
        {
            CloseCpu();
            CloseLimit();
            CloseThermal();
            if (_processReader != null) _processReader.Dispose();
        }
    }
}
