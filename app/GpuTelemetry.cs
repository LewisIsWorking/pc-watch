using System.Runtime.InteropServices;
using System.Text;

namespace PcWatch;

/// <summary>What the GPU is doing, in watts.</summary>
public sealed record GpuReading(string Name, double Watts, double WattLimit, int UtilisationPercent, int TemperatureC);

/// <summary>
/// NVIDIA GPU power and load, read through NVML.
/// </summary>
/// <remarks>
/// 2026-09-02. NVML rather than shelling out to nvidia-smi: the app samples every second, and
/// spawning a process per tick costs more than everything else it does put together. nvml.dll ships
/// with the NVIDIA driver.
///
/// ⚠️ This is the ONE measured power figure available without a kernel driver. AMD CPU package power
/// needs ring-0 access (LibreHardwareMonitor installs a driver for exactly this and needs admin to
/// do it), so the CPU number elsewhere is an ESTIMATE and is labelled as one. Never present the two
/// as the same kind of quantity.
///
/// Everything degrades to null on a machine with no NVIDIA GPU, which matters now the project is
/// public: an AMD or Intel user must get a working app with one fewer row, not a crash.
/// </remarks>
public sealed partial class GpuTelemetry : IDisposable
{
    private const string Nvml = "nvml.dll";

    [LibraryImport(Nvml, EntryPoint = "nvmlInit_v2")] private static partial int Init();
    [LibraryImport(Nvml, EntryPoint = "nvmlShutdown")] private static partial int Shutdown();
    [LibraryImport(Nvml, EntryPoint = "nvmlDeviceGetHandleByIndex_v2")] private static partial int GetHandle(uint index, out IntPtr device);
    [LibraryImport(Nvml, EntryPoint = "nvmlDeviceGetPowerUsage")] private static partial int GetPowerMilliwatts(IntPtr device, out uint milliwatts);
    [LibraryImport(Nvml, EntryPoint = "nvmlDeviceGetEnforcedPowerLimit")] private static partial int GetPowerLimit(IntPtr device, out uint milliwatts);
    [LibraryImport(Nvml, EntryPoint = "nvmlDeviceGetTemperature")] private static partial int GetTemperature(IntPtr device, int sensor, out uint degrees);
    [LibraryImport(Nvml, EntryPoint = "nvmlDeviceGetUtilizationRates")] private static partial int GetUtilisation(IntPtr device, out Utilisation rates);
    [LibraryImport(Nvml, EntryPoint = "nvmlDeviceGetName")] private static partial int GetName(IntPtr device, Span<byte> name, uint length);

    [StructLayout(LayoutKind.Sequential)]
    private struct Utilisation
    {
        public uint Gpu;
        public uint Memory;
    }

    private IntPtr _device;
    private bool _ready;
    private string _name = "GPU";

    public GpuTelemetry()
    {
        try
        {
            if (Init() != 0) return;
            if (GetHandle(0, out _device) != 0) { Shutdown(); return; }

            Span<byte> buffer = stackalloc byte[96];
            if (GetName(_device, buffer, (uint)buffer.Length) == 0)
            {
                int end = buffer.IndexOf((byte)0);
                _name = Encoding.UTF8.GetString(buffer[..(end < 0 ? buffer.Length : end)]);
            }
            _ready = true;
        }
        catch (DllNotFoundException)
        {
            _ready = false;   // no NVIDIA driver: expected on AMD and Intel machines
        }
        catch (EntryPointNotFoundException)
        {
            _ready = false;   // an NVML too old for one of these calls
        }
    }

    /// <summary>Current GPU reading, or null when there is no NVIDIA GPU to read.</summary>
    public GpuReading? Read()
    {
        if (!_ready) return null;

        try
        {
            if (GetPowerMilliwatts(_device, out uint milliwatts) != 0) return null;

            double limit = GetPowerLimit(_device, out uint limitMw) == 0 ? limitMw / 1000.0 : 0;
            int util = GetUtilisation(_device, out Utilisation rates) == 0 ? (int)rates.Gpu : 0;
            int temp = GetTemperature(_device, 0, out uint degrees) == 0 ? (int)degrees : 0;

            return new GpuReading(_name, milliwatts / 1000.0, limit, util, temp);
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (!_ready) return;
        try { Shutdown(); } catch { /* nothing useful to do while tearing down */ }
        _ready = false;
    }
}
