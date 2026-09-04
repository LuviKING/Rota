using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Rota.Desktop.LocalAI;

public sealed record WindowsHardwareSnapshot(
    string CpuName,
    int LogicalProcessorCount,
    long SystemMemoryBytes,
    string GpuName,
    long? DedicatedGpuMemoryBytes,
    IReadOnlyList<string> Warnings);

public interface IWindowsHardwareProbe
{
    Task<WindowsHardwareSnapshot> CaptureAsync(CancellationToken cancellationToken = default);
}

public static class AiProfileRecommendationPolicy
{
    public const long Gibibyte = 1024L * 1024 * 1024;
    public const long BalancedMinimumSystemMemoryBytes = 12 * Gibibyte;
    public const long BalancedMinimumGpuMemoryBytes = 4 * Gibibyte;
    public const int BalancedMinimumLogicalProcessors = 6;
    public const long PerformanceMinimumSystemMemoryBytes = 15 * Gibibyte;
    public const long PerformanceMinimumGpuMemoryBytes = 15 * Gibibyte / 2;
    public const int PerformanceMinimumLogicalProcessors = 8;

    public static AiProfile Recommend(WindowsHardwareSnapshot hardware)
    {
        ValidateHardware(hardware);
        var gpuMemory = hardware.DedicatedGpuMemoryBytes.GetValueOrDefault();

        if (hardware.SystemMemoryBytes >= PerformanceMinimumSystemMemoryBytes &&
            gpuMemory >= PerformanceMinimumGpuMemoryBytes &&
            hardware.LogicalProcessorCount >= PerformanceMinimumLogicalProcessors)
        {
            return AiProfile.Performance;
        }

        if (hardware.SystemMemoryBytes >= BalancedMinimumSystemMemoryBytes &&
            (gpuMemory >= BalancedMinimumGpuMemoryBytes ||
             hardware.LogicalProcessorCount >= BalancedMinimumLogicalProcessors))
        {
            return AiProfile.Balanced;
        }

        return AiProfile.Lightweight;
    }

    public static AiProfile Resolve(AiProfile configuredProfile, AiHardwareProfile hardware)
    {
        ArgumentNullException.ThrowIfNull(hardware);
        if (!Enum.IsDefined(configuredProfile))
            throw new AiContractValidationException("O perfil de IA configurado é inválido.");
        if (!Enum.IsDefined(hardware.RecommendedProfile) || hardware.RecommendedProfile == AiProfile.Automatic)
            throw new AiContractValidationException("O perfil recomendado pelo detector é inválido.");

        return configuredProfile == AiProfile.Automatic
            ? hardware.RecommendedProfile
            : configuredProfile;
    }

    private static void ValidateHardware(WindowsHardwareSnapshot hardware)
    {
        ArgumentNullException.ThrowIfNull(hardware);
        if (hardware.SystemMemoryBytes <= 0)
            throw new AiContractValidationException("A memória física detectada precisa ser maior que zero.");
        if (hardware.LogicalProcessorCount <= 0)
            throw new AiContractValidationException("A quantidade de processadores lógicos precisa ser maior que zero.");
        if (hardware.DedicatedGpuMemoryBytes is < 0)
            throw new AiContractValidationException("A memória dedicada da GPU não pode ser negativa.");
        if (hardware.Warnings is null)
            throw new AiContractValidationException("A lista de avisos de hardware não pode ser nula.");
    }
}

public sealed class WindowsAiHardwareProfileDetector : IAiHardwareProfileDetector
{
    private readonly IWindowsHardwareProbe _probe;

    public WindowsAiHardwareProfileDetector(IWindowsHardwareProbe? probe = null)
    {
        _probe = probe ?? new WindowsHardwareProbe();
    }

    public async Task<AiHardwareProfile> DetectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = await _probe.CaptureAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var recommended = AiProfileRecommendationPolicy.Recommend(snapshot);

        return new AiHardwareProfile(
            snapshot.CpuName,
            snapshot.LogicalProcessorCount,
            snapshot.SystemMemoryBytes,
            snapshot.GpuName,
            snapshot.DedicatedGpuMemoryBytes,
            recommended,
            snapshot.Warnings.ToArray());
    }
}

public sealed class WindowsHardwareProbe : IWindowsHardwareProbe
{
    private const int DxgiErrorNotFound = unchecked((int)0x887A0002);
    private const uint DxgiAdapterFlagSoftware = 2;

    public Task<WindowsHardwareSnapshot> CaptureAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Capture(cancellationToken), cancellationToken);

    private static WindowsHardwareSnapshot Capture(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("A detecção de hardware da IA local está disponível somente no Windows.");

        cancellationToken.ThrowIfCancellationRequested();
        var warnings = new List<string>();
        var memoryBytes = ReadPhysicalMemoryBytes();
        var processorCount = Math.Max(1, Environment.ProcessorCount);
        var cpuName = ReadCpuName(warnings);

        cancellationToken.ThrowIfCancellationRequested();
        var gpu = TryReadPrimaryHardwareGpu(warnings);
        cancellationToken.ThrowIfCancellationRequested();

        return new WindowsHardwareSnapshot(
            cpuName,
            processorCount,
            memoryBytes,
            gpu.Name,
            gpu.DedicatedMemoryBytes,
            warnings.ToArray());
    }

    private static long ReadPhysicalMemoryBytes()
    {
        var memory = new MemoryStatusEx
        {
            Length = (uint)Marshal.SizeOf<MemoryStatusEx>()
        };
        if (!GlobalMemoryStatusEx(ref memory))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Não foi possível detectar a memória física do computador.");
        return memory.TotalPhysical > long.MaxValue ? long.MaxValue : (long)memory.TotalPhysical;
    }

    private static string ReadCpuName(List<string> warnings)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0",
                writable: false);
            var name = key?.GetValue("ProcessorNameString") as string;
            if (!string.IsNullOrWhiteSpace(name)) return name.Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            warnings.Add("O nome comercial da CPU não pôde ser lido; a arquitetura do processo foi usada.");
        }

        return $"CPU {RuntimeInformation.ProcessArchitecture}";
    }

    private static (string Name, long? DedicatedMemoryBytes) ReadPrimaryHardwareGpu(List<string> warnings)
    {
        var factoryId = typeof(IDxgiFactory1).GUID;
        var result = CreateDXGIFactory1(ref factoryId, out var factoryPointer);
        if (result < 0)
        {
            warnings.Add("A GPU não pôde ser consultada pelo DXGI; o perfil automático usará CPU e RAM.");
            return ("", null);
        }

        IDxgiFactory1? factory = null;
        try
        {
            factory = (IDxgiFactory1)Marshal.GetObjectForIUnknown(factoryPointer);
        }
        finally
        {
            Marshal.Release(factoryPointer);
        }

        try
        {
            string selectedName = "";
            long? selectedMemory = null;
            for (uint index = 0; ; index++)
            {
                result = factory.EnumAdapters1(index, out var adapter);
                if (result == DxgiErrorNotFound) break;
                if (result < 0)
                {
                    warnings.Add("A enumeração de GPUs foi interrompida; o melhor adaptador já detectado será usado.");
                    break;
                }

                try
                {
                    result = adapter.GetDesc1(out var description);
                    if (result < 0 || (description.Flags & DxgiAdapterFlagSoftware) != 0) continue;

                    var memory = description.DedicatedVideoMemory.ToUInt64();
                    var memoryBytes = memory > long.MaxValue ? long.MaxValue : (long)memory;
                    if (selectedMemory.HasValue && memoryBytes <= selectedMemory.Value) continue;

                    selectedName = (description.Description ?? "").TrimEnd('\0').Trim();
                    selectedMemory = memoryBytes;
                }
                finally
                {
                    Marshal.FinalReleaseComObject(adapter);
                }
            }

            if (!selectedMemory.HasValue)
                warnings.Add("Nenhuma GPU de hardware foi detectada; o perfil automático usará CPU e RAM.");
            return (selectedName, selectedMemory);
        }
        finally
        {
            Marshal.FinalReleaseComObject(factory);
        }
    }

    private static (string Name, long? DedicatedMemoryBytes) TryReadPrimaryHardwareGpu(List<string> warnings)
    {
        try
        {
            return ReadPrimaryHardwareGpu(warnings);
        }
        catch (Exception ex) when (ex is COMException or DllNotFoundException or EntryPointNotFoundException or InvalidCastException)
        {
            warnings.Add("A GPU não pôde ser consultada pelo DXGI; o perfil automático usará CPU e RAM.");
            return ("", null);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1(ref Guid interfaceId, out IntPtr factory);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DxgiAdapterDescription1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSystemId;
        public uint Revision;
        public UIntPtr DedicatedVideoMemory;
        public UIntPtr DedicatedSystemMemory;
        public UIntPtr SharedSystemMemory;
        public long AdapterLuid;
        public uint Flags;
    }

    [ComImport]
    [Guid("770AAE78-F26F-4DBA-A829-253C83D1B387")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDxgiFactory1
    {
        [PreserveSig] int SetPrivateData(ref Guid name, uint dataSize, IntPtr data);
        [PreserveSig] int SetPrivateDataInterface(ref Guid name, [MarshalAs(UnmanagedType.IUnknown)] object unknown);
        [PreserveSig] int GetPrivateData(ref Guid name, ref uint dataSize, IntPtr data);
        [PreserveSig] int GetParent(ref Guid interfaceId, out IntPtr parent);
        [PreserveSig] int EnumAdapters(uint adapterIndex, out IntPtr adapter);
        [PreserveSig] int MakeWindowAssociation(IntPtr windowHandle, uint flags);
        [PreserveSig] int GetWindowAssociation(out IntPtr windowHandle);
        [PreserveSig] int CreateSwapChain(IntPtr device, IntPtr description, out IntPtr swapChain);
        [PreserveSig] int CreateSoftwareAdapter(IntPtr module, out IntPtr adapter);
        [PreserveSig] int EnumAdapters1(uint adapterIndex, [MarshalAs(UnmanagedType.Interface)] out IDxgiAdapter1 adapter);
        [return: MarshalAs(UnmanagedType.Bool)] bool IsCurrent();
    }

    [ComImport]
    [Guid("29038F61-3839-4626-91FD-086879011A05")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDxgiAdapter1
    {
        [PreserveSig] int SetPrivateData(ref Guid name, uint dataSize, IntPtr data);
        [PreserveSig] int SetPrivateDataInterface(ref Guid name, [MarshalAs(UnmanagedType.IUnknown)] object unknown);
        [PreserveSig] int GetPrivateData(ref Guid name, ref uint dataSize, IntPtr data);
        [PreserveSig] int GetParent(ref Guid interfaceId, out IntPtr parent);
        [PreserveSig] int EnumOutputs(uint outputIndex, out IntPtr output);
        [PreserveSig] int GetDesc(IntPtr description);
        [PreserveSig] int CheckInterfaceSupport(ref Guid interfaceId, out long userModeDriverVersion);
        [PreserveSig] int GetDesc1(out DxgiAdapterDescription1 description);
    }
}
