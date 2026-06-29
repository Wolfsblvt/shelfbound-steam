using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Shelfbound.Core.Model;

namespace Shelfbound.Steam.Steam;

/// <summary>
/// Best-effort hardware specs for a device. Every read is defensive — on any failure the field is just
/// null rather than failing the scan. CPU cores / OS / architecture are always available; CPU name and
/// total RAM are per-OS; GPU is a later enhancement. No identifiers/serials are collected.
/// </summary>
public static class HardwareInfo
{
    public static DeviceSpecs Collect() => new()
    {
        LogicalCores = Environment.ProcessorCount,
        OsDescription = Clean(RuntimeInformation.OSDescription),
        Architecture = RuntimeInformation.OSArchitecture.ToString(),
        Cpu = TryGetCpu(),
        TotalMemoryBytes = TryGetTotalMemory(),
        Gpu = TryGetGpu(),
    };

    private static string? TryGetGpu()
    {
        try
        {
            // Hardware.Info handles the per-OS GPU lookup (WMI on Windows, /sys or system_profiler else).
            var hardware = new global::Hardware.Info.HardwareInfo();
            hardware.RefreshVideoControllerList();
            foreach (var controller in hardware.VideoControllerList)
            {
                string? name = Clean(controller.Name);
                if (name is not null)
                    return name;
            }
        }
        catch
        {
            // GPU detection is best-effort; never let it fail a scan.
        }
        return null;
    }

    private static string? TryGetCpu()
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return Clean(Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"));
            if (OperatingSystem.IsLinux())
            {
                foreach (string line in File.ReadLines("/proc/cpuinfo"))
                    if (line.StartsWith("model name", StringComparison.OrdinalIgnoreCase))
                        return Clean(line[(line.IndexOf(':') + 1)..]);
                return null;
            }
            if (OperatingSystem.IsMacOS())
                return RunCommand("sysctl", "-n machdep.cpu.brand_string");
        }
        catch { /* defensive */ }
        return null;
    }

    private static long? TryGetTotalMemory()
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return GetWindowsMemory();
            if (OperatingSystem.IsLinux())
            {
                foreach (string line in File.ReadLines("/proc/meminfo"))
                    if (line.StartsWith("MemTotal", StringComparison.OrdinalIgnoreCase))
                    {
                        string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2 && long.TryParse(parts[1], out long kb))
                            return kb * 1024;
                    }
                return null;
            }
            if (OperatingSystem.IsMacOS() && long.TryParse(RunCommand("sysctl", "-n hw.memsize"), out long bytes))
                return bytes;
        }
        catch { /* defensive */ }
        return null;
    }

    [SupportedOSPlatform("windows")]
    private static long? GetWindowsMemory()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status) ? (long)status.TotalPhys : null;
    }

    private static string? RunCommand(string fileName, string arguments)
    {
        try
        {
            using Process? process = Process.Start(new ProcessStartInfo(fileName, arguments)
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
            });
            if (process is null)
                return null;
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(2000);
            return Clean(output);
        }
        catch
        {
            return null;
        }
    }

    private static string? Clean(string? value)
    {
        value = value?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
