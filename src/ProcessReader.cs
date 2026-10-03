using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DesktopMonitor
{
    // Every process in one call to NtQuerySystemInformation(SystemProcessInformation), as Task Manager does:
    // a few milliseconds instead of opening ~400 process handles. 64-bit layout of SYSTEM_PROCESS_INFORMATION.
    internal sealed class ProcessReader : IDisposable
    {
        private const int SystemProcessInformation = 5;
        private const uint StatusInfoLengthMismatch = 0xC0000004;
        private const int OffNext = 0x00, OffPrivateWs = 0x08, OffCreateTime = 0x20, OffUserTime = 0x28, OffKernelTime = 0x30;
        private const int OffNameLength = 0x38, OffNameBuffer = 0x40, OffPid = 0x50;

        [DllImport("ntdll.dll")]
        private static extern uint NtQuerySystemInformation(int infoClass, IntPtr buffer, int length, out int returnLength);

        private IntPtr _buffer;
        private int _size = 1 << 20;

        public List<ProcessSample> Read()
        {
            if (_buffer == IntPtr.Zero) _buffer = Marshal.AllocHGlobal(_size);
            int needed;
            uint status;
            while ((status = NtQuerySystemInformation(SystemProcessInformation, _buffer, _size, out needed)) == StatusInfoLengthMismatch)
            {
                Marshal.FreeHGlobal(_buffer);
                _size = Math.Max(_size * 2, needed + 65536);
                _buffer = Marshal.AllocHGlobal(_size);
            }
            if (status != 0) throw new Win32Exception("NtQuerySystemInformation failed with status 0x" + status.ToString("X8"));

            var list = new List<ProcessSample>(512);
            long offset = 0;
            while (true)
            {
                var p = new IntPtr(_buffer.ToInt64() + offset);
                int pid = (int)Marshal.ReadIntPtr(p, OffPid).ToInt64();
                int nameBytes = Marshal.ReadInt16(p, OffNameLength) & 0xFFFF;
                IntPtr name = Marshal.ReadIntPtr(p, OffNameBuffer);
                list.Add(new ProcessSample(
                    pid,
                    Marshal.ReadInt64(p, OffCreateTime),
                    name == IntPtr.Zero ? (pid == 0 ? "Idle" : "System") : Marshal.PtrToStringUni(name, nameBytes / 2),
                    Marshal.ReadInt64(p, OffUserTime) + Marshal.ReadInt64(p, OffKernelTime),
                    Marshal.ReadInt64(p, OffPrivateWs)));
                int next = Marshal.ReadInt32(p, OffNext);
                if (next == 0) break;
                offset += next;
            }
            return list;
        }

        public void Dispose()
        {
            if (_buffer != IntPtr.Zero) Marshal.FreeHGlobal(_buffer);
            _buffer = IntPtr.Zero;
        }
    }
}
