using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Interfaces
{
    /// <summary>
    /// Informações sobre a CPU
    /// </summary>
    public class CpuInfo
    {
        public string Name { get; set; } = string.Empty;
        public int CoreCount { get; set; }
        public int ThreadCount { get; set; }
        public double MaxClockSpeedMHz { get; set; }
        public double CurrentClockSpeedMHz { get; set; }
        public bool IsHybrid { get; set; }
        public string Architecture { get; set; } = string.Empty;
        public string Socket { get; set; } = string.Empty;
        public long L3CacheKB { get; set; }
    }

    /// <summary>
    /// Informações sobre a GPU
    /// </summary>
    public class GpuInfo
    {
        public string Name { get; set; } = string.Empty;
        public string DriverVersion { get; set; } = string.Empty;
        public string Vendor { get; set; } = string.Empty;
        public long VideoMemoryBytes { get; set; }
        public bool IsDiscrete { get; set; }
        public bool SupportsHags { get; set; }
        public bool SupportsVrr { get; set; }
    }

    /// <summary>
    /// Informações sobre a memória RAM
    /// </summary>
    public class RamInfo
    {
        public long TotalBytes { get; set; }
        public long AvailableBytes { get; set; }
        public long UsedBytes => TotalBytes - AvailableBytes;
        public double UsagePercent => TotalBytes > 0 ? (double)UsedBytes / TotalBytes * 100 : 0;
        public int TotalGB => (int)(TotalBytes / (1024L * 1024 * 1024));
        public string MemoryType { get; set; } = string.Empty;
        public int SpeedMHz { get; set; }
    }

    /// <summary>
    /// Informações sobre o armazenamento
    /// </summary>
    public class DriveInfo
    {
        public string Letter { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public long TotalBytes { get; set; }
        public long FreeBytes { get; set; }
        public bool IsSsd { get; set; }
        public string FileSystem { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
    }

    /// <summary>
    /// Informações sobre a interface de rede
    /// </summary>
    public class NetworkInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Manufacturer { get; set; } = string.Empty;
        public string MacAddress { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
        public long SpeedBps { get; set; }
    }

    /// <summary>
    /// Capacidades do hardware do sistema
    /// </summary>
    public class HardwareCapabilities
    {
        public CpuInfo Cpu { get; set; } = new();
        public GpuInfo Gpu { get; set; } = new();
        public RamInfo Ram { get; set; } = new();
        public DriveInfo[] Drives { get; set; } = Array.Empty<DriveInfo>();
        public NetworkInfo[] NetworkAdapters { get; set; } = Array.Empty<NetworkInfo>();
        
        // Flags de capacidade derivados
        public bool IsHybridCpu => Cpu.IsHybrid;
        public bool HasDiscreteGpu => Gpu.IsDiscrete;
        public bool HasSufficientRam => Ram.TotalGB >= 8;
        public bool SupportsHags => Gpu.SupportsHags;
        public bool SupportsVrr => Gpu.SupportsVrr;
    }

    /// <summary>
    /// Interface para obtenção de informações do sistema
    /// </summary>
    public interface ISystemInfoService
    {
        /// <summary>
        /// Obtém informações da CPU
        /// </summary>
        Task<CpuInfo> GetCpuInfoAsync();

        /// <summary>
        /// Obtém informações da GPU principal
        /// </summary>
        Task<GpuInfo> GetGpuInfoAsync();

        /// <summary>
        /// Obtém informações de todas as GPUs
        /// </summary>
        Task<GpuInfo[]> GetAllGpusAsync();

        /// <summary>
        /// Obtém informações da memória RAM
        /// </summary>
        Task<RamInfo> GetRamInfoAsync();

        /// <summary>
        /// Obtém informações de todos os drives
        /// </summary>
        Task<DriveInfo[]> GetDrivesInfoAsync();

        /// <summary>
        /// Obtém informações das interfaces de rede
        /// </summary>
        Task<NetworkInfo[]> GetNetworkInfoAsync();

        /// <summary>
        /// Obtém todas as capacidades do hardware
        /// </summary>
        Task<HardwareCapabilities> GetHardwareCapabilitiesAsync();

        /// <summary>
        /// Obtém o uso atual da CPU em porcentagem
        /// </summary>
        Task<double> GetCpuUsageAsync();

        /// <summary>
        /// Obtém a versão do Windows
        /// </summary>
        string GetWindowsVersion();

        /// <summary>
        /// Obtém a edição do Windows (LTSC, Pro, Home, Enterprise, etc)
        /// </summary>
        string GetWindowsEdition();

        /// <summary>
        /// Obtém o build do Windows
        /// </summary>
        int GetWindowsBuild();

        /// <summary>
        /// Verifica se o sistema é Windows 11
        /// </summary>
        bool IsWindows11();

        /// <summary>
        /// Verifica se o sistema é 64 bits
        /// </summary>
        bool Is64BitOperatingSystem();

        /// <summary>
        /// Verifica se o aplicativo está sendo executado com permissões de administrador
        /// </summary>
        Task<bool> IsRunningAsAdministratorAsync();

        /// <summary>
        /// Obtém o espaço disponível em disco em MB
        /// </summary>
        Task<long> GetAvailableDiskSpaceMBAsync();

        /// <summary>
        /// Verifica processos em conflito
        /// </summary>
        Task<List<string>> CheckConflictingProcessesAsync(List<string> conflictingProcesses);

        /// <summary>
        /// Verifica a integridade do sistema
        /// </summary>
        Task<List<string>> CheckSystemIntegrityAsync();

        /// <summary>
        /// Obtém o uso da GPU
        /// </summary>
        Task<double> GetGpuUsageAsync();

        /// <summary>
        /// Obtém o uso da memória
        /// </summary>
        Task<double> GetMemoryUsageAsync();

        /// <summary>
        /// Obtém a atividade de I/O
        /// </summary>
        Task<double> GetIoActivityAsync();

        /// <summary>
        /// Obtém informações dos processos ativos
        /// </summary>
        Task<List<object>> GetActiveProcessInfoAsync();

        /// <summary>
        /// Verifica se o sistema é um notebook
        /// </summary>
        bool IsLaptop();

        /// <summary>
        /// Verifica se o sistema está operando na bateria
        /// </summary>
        bool IsOnBattery();
    }
}

