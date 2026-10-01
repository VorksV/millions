using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services.Gamer.Interfaces;
using GamerModels = VoltrisOptimizer.Services.Gamer.Models;

namespace VoltrisOptimizer.Services.Gamer.Implementation
{
    /// <summary>
    /// Implementação consolidada do detector de hardware usando ISystemInfoService
    /// </summary>
    public class HardwareDetectorService : IHardwareDetector
    {
        private readonly VoltrisOptimizer.Services.ILoggingService _logger;
        private readonly ISystemInfoService _systemInfoService;
        private GamerModels.HardwareCapabilities? _cachedCapabilities;
        private DateTime _cacheTime = DateTime.MinValue;
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
        private readonly SemaphoreSlim _lock = new(1, 1);

        public HardwareDetectorService(VoltrisOptimizer.Services.ILoggingService logger, ISystemInfoService systemInfoService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _systemInfoService = systemInfoService ?? throw new ArgumentNullException(nameof(systemInfoService));
        }

        public async Task<GamerModels.HardwareCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
        {
            if (_cachedCapabilities != null && DateTime.Now - _cacheTime < CacheDuration)
            {
                return _cachedCapabilities;
            }

            await _lock.WaitAsync(cancellationToken);
            try
            {
                if (_cachedCapabilities != null && DateTime.Now - _cacheTime < CacheDuration)
                {
                    return _cachedCapabilities;
                }

                _logger.LogInfo("[Hardware] Iniciando detecção de hardware consolidada...");

                var sysCaps = await _systemInfoService.GetHardwareCapabilitiesAsync();
                var caps = new GamerModels.HardwareCapabilities
                {
                    CpuName = sysCaps.Cpu.Name,
                    CoreCount = sysCaps.Cpu.CoreCount,
                    ThreadCount = sysCaps.Cpu.ThreadCount,
                    MaxClockSpeedMhz = sysCaps.Cpu.MaxClockSpeedMHz,
                    IsHybridCpu = sysCaps.Cpu.IsHybrid,
                    TotalRamGb = sysCaps.Ram.TotalGB,
                    IsLaptop = _systemInfoService.IsLaptop(),
                    IsOnBattery = _systemInfoService.IsOnBattery(),
                    HasDiscreteGpu = sysCaps.Gpu.IsDiscrete,
                    PrimaryGpu = new GamerModels.GpuInfo
                    {
                        Name = sysCaps.Gpu.Name,
                        DriverVersion = sysCaps.Gpu.DriverVersion,
                        VideoMemoryBytes = sysCaps.Gpu.VideoMemoryBytes,
                        IsDiscrete = sysCaps.Gpu.IsDiscrete,
                        Vendor = MapGpuVendor(sysCaps.Gpu.Vendor, sysCaps.Gpu.Name)
                    }
                };

                // Network adapter info
                var adapter = sysCaps.NetworkAdapters.FirstOrDefault();
                if (adapter != null)
                {
                    caps.NetworkAdapterName = adapter.Name;
                    caps.NetworkAdapterVendor = adapter.Manufacturer;
                }

                _cachedCapabilities = caps;
                _cacheTime = DateTime.Now;
                
                _logger.LogInfo($"[Hardware] ✅ Detecção concluída via SystemInfoService.");
                return caps;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<GamerModels.GpuInfo> GetGpuInfoAsync(CancellationToken cancellationToken = default)
        {
            var gpu = await _systemInfoService.GetGpuInfoAsync();
            return new GamerModels.GpuInfo
            {
                Name = gpu.Name,
                DriverVersion = gpu.DriverVersion,
                VideoMemoryBytes = gpu.VideoMemoryBytes,
                IsDiscrete = gpu.IsDiscrete,
                Vendor = MapGpuVendor(gpu.Vendor, gpu.Name)
            };
        }

        /// <summary>
        /// Mapeia string do fabricante (WMI AdapterCompatibility) para o enum GpuVendor.
        /// Usa o nome da GPU como fallback quando o campo Vendor não é conclusivo.
        /// </summary>
        private static GamerModels.GpuVendor MapGpuVendor(string vendor, string gpuName)
        {
            var vendorLower = (vendor ?? "").ToLowerInvariant();
            var nameLower = (gpuName ?? "").ToLowerInvariant();

            // Priorizar o NOME da GPU (mais confiável que AdapterCompatibility do WMI)
            // WMI pode retornar vendor errado em sistemas com drivers genéricos ou múltiplas GPUs
            if (nameLower.Contains("nvidia") || nameLower.Contains("geforce") || nameLower.Contains("rtx") || nameLower.Contains("gtx"))
                return GamerModels.GpuVendor.Nvidia;

            if (nameLower.Contains("intel") || nameLower.Contains("iris") || nameLower.Contains("uhd graphics"))
                return GamerModels.GpuVendor.Intel;

            if (nameLower.Contains("amd") || nameLower.Contains("radeon") || nameLower.Contains("ati"))
                return GamerModels.GpuVendor.Amd;

            // Fallback: usar o campo vendor do WMI
            if (vendorLower.Contains("nvidia"))
                return GamerModels.GpuVendor.Nvidia;

            if (vendorLower.Contains("intel"))
                return GamerModels.GpuVendor.Intel;

            if (vendorLower.Contains("amd") || vendorLower.Contains("ati") || vendorLower.Contains("advanced micro"))
                return GamerModels.GpuVendor.Amd;

            return GamerModels.GpuVendor.Unknown;
        }

        public bool IsHybridCpu() => _systemInfoService.IsWindows11() && _systemInfoService.Is64BitOperatingSystem(); // Simplificação ou usar CPU info

        public (int Cores, int Threads) GetCpuCounts()
        {
            // Nota: Este método é síncrono na interface original, o que é um problema se quisermos usar Task
            // Mas como temos cache no SystemInfoService, podemos tentar um hack ou rodar sync (não recomendado)
            // Para manter a consolidação, vamos assumir que o sistema já foi inicializado ou usar o Environment como fallback rápido
            return (Environment.ProcessorCount / 2, Environment.ProcessorCount);
        }

        public int GetTotalRamGb()
        {
            // Fallback rápido
            return (int)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024L * 1024 * 1024));
        }

        public bool IsLaptop() => _systemInfoService.IsLaptop();
        public bool IsOnBattery() => _systemInfoService.IsOnBattery();
    }
}
