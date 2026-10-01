using System;
using VoltrisOptimizer.Services.Performance.CpuTuning.Core.Interfaces;
using VoltrisOptimizer.Services.Performance.CpuTuning.Core.Models;
using VoltrisOptimizer.Services.Performance.CpuTuning.Models;

namespace VoltrisOptimizer.Services.Performance.CpuTuning.Core.Backends
{
    /// <summary>
    /// Backend seguro e SEM acesso ao kernel (substitui o WinRing0).
    ///
    /// Motivo: o WinRing0 distribuía um driver de kernel não assinado
    /// (WinRing0x64.sys) que faz o Windows Defender marcar o app como
    /// HackTool/Riskware e permite vetores BYOVD. Este fallback nunca
    /// carrega driver, nunca escreve MSR e nunca acessa memória física.
    ///
    /// Consequência: o ajuste fino de CPU via MSR/MMIO (power limits,
    /// ThrottleStop-like) fica desativado até existir uma alternativa
    /// assinada. A leitura de telemetria continua pela camada segura
    /// (LibreHardwareMonitor/Windows).
    /// </summary>
    public class SafeFallbackBackend : IHardwareBackend, IDisposable
    {
        private readonly ILoggingService _logger;

        public SafeFallbackBackend(ILoggingService logger)
        {
            _logger = logger;
            _logger?.LogInfo("[SafeFallbackBackend] Ativo — acesso ao kernel desativado (substitui WinRing0).");
        }

        public bool Initialize()
        {
            _logger?.LogWarning("[SafeFallbackBackend] Inicialização sem driver: acesso a MSR/MMIO indisponível por design.");
            return false;
        }

        public void Shutdown()
        {
            // Nada a descarregar: nunca carregamos driver.
        }

        public bool IsDriverLoaded() => false;

        public bool ReadMsr(uint index, out ulong value)
        {
            value = 0;
            return false;
        }

        public bool WriteMsr(uint index, ulong value) => false;

        public CpuVendor GetCpuVendor()
        {
            try
            {
                var identifier = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "";
                if (identifier.Contains("GenuineIntel", StringComparison.OrdinalIgnoreCase) ||
                    identifier.Contains("Intel", StringComparison.OrdinalIgnoreCase))
                    return CpuVendor.Intel;
                if (identifier.Contains("AuthenticAMD", StringComparison.OrdinalIgnoreCase) ||
                    identifier.Contains("AMD", StringComparison.OrdinalIgnoreCase))
                    return CpuVendor.AMD;
            }
            catch { /* fallback abaixo */ }
            return CpuVendor.Unknown;
        }

        public CpuGeneration DetectGeneration() => CpuGeneration.Unknown;

        public bool SupportsPowerLimitControl() => false;

        public bool GetCurrentPowerLimits(out PowerLimitState limits)
        {
            limits = new PowerLimitState();
            return false;
        }

        public bool SetPowerLimits(PowerLimitRequest request) => false;

        public BackendStatus GetStatus() => BackendStatus.SecurityBlocked;

        public bool ReadPciConfig(uint pciAddress, uint regAddress, out uint value)
        {
            value = 0;
            return false;
        }

        public ulong DiscoverMchBar() => 0;

        public bool ReadPhysicalMemory(ulong address, uint size, out ulong value)
        {
            value = 0;
            return false;
        }

        public bool WritePhysicalMemory(ulong address, ulong data, uint size) => false;

        public void Dispose()
        {
            Shutdown();
        }
    }
}
