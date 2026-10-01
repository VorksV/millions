using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.Personalize
{
    public class GpuInfo
    {
        public string Name { get; set; } = string.Empty;
        public string DriverVersion { get; set; } = string.Empty;
        public ulong DedicatedMemoryMb { get; set; }
        public string StatusColor => DedicatedMemoryMb >= 4096 ? "#00FF88" : DedicatedMemoryMb >= 2048 ? "#FFAA00" : "#FF4466";
    }

    public sealed class GpuControlService
    {
        private const string TAG = "[GpuControl]";
        private readonly ILoggingService _logger;

        // Chave de prioridade de GPU para jogos/apps
        private const string GpuPrefKey = @"SOFTWARE\Microsoft\DirectX\UserGpuPreferences";
        private const string GraphicsDriversKey = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";

        public GpuControlService(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogInfo($"{TAG} Instância criada.");
        }

        // ── Informações da GPU ───────────────────────────────────────────────────

        public Task<List<GpuInfo>> GetGpuInfoAsync() => Task.Run(() =>
        {
            _logger.LogInfo($"{TAG} [GetGpuInfoAsync] Consultando GPUs via WMI...");
            var result = new List<GpuInfo>();
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
                foreach (ManagementObject obj in searcher.Get())
                {
                using var __dispose_obj = obj;
                    var gpu = new GpuInfo
                    {
                        Name = obj["Name"]?.ToString() ?? "GPU Desconhecida",
                        DriverVersion = obj["DriverVersion"]?.ToString() ?? "N/A",
                        DedicatedMemoryMb = Convert.ToUInt64(obj["AdapterRAM"] ?? 0UL) / (1024 * 1024)
                    };
                    result.Add(gpu);
                    _logger.LogInfo($"{TAG} GPU: {gpu.Name} | Driver: {gpu.DriverVersion} | VRAM: {gpu.DedicatedMemoryMb}MB");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"{TAG} [GetGpuInfoAsync] Erro WMI: {ex.Message}", ex);
            }
            return result;
        });

        // ── Preferência de GPU por app ───────────────────────────────────────────

        /// <summary>
        /// Define preferência de GPU para um executável específico.
        /// GpuPreference: 0=Default, 1=PowerSaving, 2=HighPerformance
        /// </summary>
        public Task<bool> SetGpuPreferenceForAppAsync(string exePath, int preference) => Task.Run(() =>
        {
            _logger.LogInfo($"{TAG} [SetGpuPreferenceForAppAsync] exePath={exePath} | preference={preference}");
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(GpuPrefKey);
                if (key == null)
                {
                    _logger.LogWarning($"{TAG} [SetGpuPreferenceForAppAsync] Não foi possível abrir chave de preferência de GPU.");
                    return false;
                }
                string value = $"GpuPreference={preference};";
                key.SetValue(exePath, value, RegistryValueKind.String);
                _logger.LogSuccess($"{TAG} [SetGpuPreferenceForAppAsync] GPU preference={preference} definida para {exePath}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"{TAG} [SetGpuPreferenceForAppAsync] Erro: {ex.Message}", ex);
                return false;
            }
        });

        // ── Hardware Scheduling (HAGS) ───────────────────────────────────────────

        public Task<bool> GetHardwareSchedulingEnabledAsync() => Task.Run(() =>
        {
            _logger.LogInfo($"{TAG} [GetHardwareSchedulingEnabledAsync] Verificando HAGS...");
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(GraphicsDriversKey);
                var val = key?.GetValue("HwSchMode");
                bool enabled = val != null && Convert.ToInt32(val) == 2;
                _logger.LogInfo($"{TAG} [GetHardwareSchedulingEnabledAsync] HAGS={enabled} (valor={val ?? "ausente"})");
                return enabled;
            }
            catch (Exception ex)
            {
                _logger.LogError($"{TAG} [GetHardwareSchedulingEnabledAsync] Erro: {ex.Message}", ex);
                return false;
            }
        });

        public Task<bool> SetHardwareSchedulingAsync(bool enable) => Task.Run(() =>
        {
            _logger.LogInfo($"{TAG} [SetHardwareSchedulingAsync] enable={enable}");
            try
            {
                using var key = Registry.LocalMachine.CreateSubKey(GraphicsDriversKey);
                if (key == null)
                {
                    _logger.LogWarning($"{TAG} [SetHardwareSchedulingAsync] Sem acesso à chave de drivers gráficos.");
                    return false;
                }
                // 2 = habilitado, 1 = desabilitado
                key.SetValue("HwSchMode", enable ? 2 : 1, RegistryValueKind.DWord);
                _logger.LogSuccess($"{TAG} [SetHardwareSchedulingAsync] HAGS={enable} (HwSchMode={( enable ? 2 : 1)})");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"{TAG} [SetHardwareSchedulingAsync] Erro: {ex.Message}", ex);
                return false;
            }
        });

        // ── Prioridade de GPU para jogos ─────────────────────────────────────────

        public Task SetGamingGpuPriorityAsync(bool highPriority) => Task.Run(() =>
        {
            _logger.LogInfo($"{TAG} [SetGamingGpuPriorityAsync] highPriority={highPriority}");
            try
            {
                using var key = Registry.LocalMachine.CreateSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games");
                if (key != null)
                {
                    key.SetValue("GPU Priority", highPriority ? 8 : 2, RegistryValueKind.DWord);
                    key.SetValue("Priority", highPriority ? 6 : 2, RegistryValueKind.DWord);
                    key.SetValue("Scheduling Category", highPriority ? "High" : "Medium", RegistryValueKind.String);
                    _logger.LogSuccess($"{TAG} [SetGamingGpuPriorityAsync] GPU Priority={( highPriority ? 8 : 2)} | Scheduling={( highPriority ? "High" : "Medium")}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"{TAG} [SetGamingGpuPriorityAsync] Erro: {ex.Message}", ex);
            }
        });

        // ── MPO (Multi-Plane Overlay) — reduz tearing ────────────────────────────

        public Task<bool> GetMpoEnabledAsync() => Task.Run(() =>
        {
            _logger.LogInfo($"{TAG} [GetMpoEnabledAsync] Verificando MPO...");
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\Dwm");
                var val = key?.GetValue("OverlayTestMode");
                bool disabled = val != null && Convert.ToInt32(val) == 5;
                _logger.LogInfo($"{TAG} [GetMpoEnabledAsync] MPO habilitado={!disabled}");
                return !disabled;
            }
            catch (Exception ex)
            {
                _logger.LogError($"{TAG} [GetMpoEnabledAsync] Erro: {ex.Message}", ex);
                return true;
            }
        });

        public Task<bool> SetMpoAsync(bool enable) => Task.Run(() =>
        {
            _logger.LogInfo($"{TAG} [SetMpoAsync] enable={enable}");
            try
            {
                using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\Dwm");
                if (!enable)
                    key?.SetValue("OverlayTestMode", 5, RegistryValueKind.DWord);
                else
                    key?.DeleteValue("OverlayTestMode", false);
                _logger.LogSuccess($"{TAG} [SetMpoAsync] MPO={enable}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"{TAG} [SetMpoAsync] Erro: {ex.Message}", ex);
                return false;
            }
        });
    }
}
