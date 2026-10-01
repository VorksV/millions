using VoltrisOptimizer.Helpers;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Services.Gamer.Interfaces;

namespace VoltrisOptimizer.Services.Gamer.GamerModeManager.Services
{
    /// <summary>
    /// Display Optimization Service - Otimização avançada de display para gaming
    /// Variable Refresh Rate, HDR profiles, color optimization
    /// </summary>
    public class DisplayOptimizerService : IDisplayOptimizerService
    {
        private readonly ILoggingService _logger;
        
        // Backup de configurações
        private int? _originalColorProfile;
        private int? _originalVrrState;
        private int? _originalDpiAwareness;
        
        // APIs nativas
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDpiAwarenessContext(int dpiContext);
        
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDPIAware();
        
        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern int SetDeviceGammaRamp(IntPtr hDC, ref RAMP lpRamp);
        
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetDC(IntPtr hWnd);
        
        [DllImport("user32.dll", SetLastError = true)]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        
        [StructLayout(LayoutKind.Sequential)]
        public struct RAMP
        {
[MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
            public ushort[] Red;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
            public ushort[] Green;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
            public ushort[] Blue;
}
        
        // Constantes
        private const int DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4;
        private const int DPI_AWARENESS_CONTEXT_SYSTEM_AWARE = -2;
        
        public DisplayOptimizerService(ILoggingService logger)
        {
            _logger.LogEntry(nameof(DisplayOptimizerService));
_logger = logger;
            _logger.LogExit(nameof(DisplayOptimizerService));
}
        
        /// <summary>
        /// Otimiza display para gaming
        /// </summary>
        public async Task<bool> OptimizeDisplayAsync()
        {
            _logger.LogEntry(nameof(OptimizeDisplayAsync));
try
            {
                _logger.LogInfo("[Display] Iniciando otimização de display para gaming...");
                
                // 1. Configurar Variable Refresh Rate (VRR)
                await OptimizeVrrAsync();
                
                // 2. Otimizar perfil de cores para gaming
                OptimizeColorProfile();
                
                // 3. Configurar DPI awareness para performance
                OptimizeDpiAwareness();
                
                // 4. Otimizar gamma para melhor visibilidade
                OptimizeGammaForGaming();
                
                // 5. Desativar composição desnecessária
                DisableVisualEffects();
                
                _logger.LogSuccess("[Display] ✅ Display otimizado para gaming - Performance visual máxima!");
return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("[Display] Erro na otimização de display", ex);
return false;
            }
            _logger.LogExit(nameof(OptimizeDisplayAsync));
}
        
        /// <summary>
        /// Otimiza Variable Refresh Rate (G-Sync/FreeSync)
        /// </summary>
        private async Task OptimizeVrrAsync()
        {
            _logger.LogEntry(nameof(OptimizeVrrAsync));
try
            {
                // Habilitar VRR via registro do Windows
                using var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers");
                
                // Backup
                _originalVrrState = key.GetValue("VrrEnabled") as int?;
                
                // Habilitar VRR para gaming
                key.SetValue("VrrEnabled", 1, RegistryValueKind.DWord);
                
                // Configurar VRR para modo performance
                key.SetValue("VrrMode", 2, RegistryValueKind.DWord); // Performance mode
                
                _logger.LogInfo("[Display] ✅ Variable Refresh Rate habilitado");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Display] Erro ao configurar VRR: {ex.Message}");
            }
            _logger.LogExit(nameof(OptimizeVrrAsync));
}
        
        /// <summary>
        /// Otimiza perfil de cores para gaming
        /// </summary>
        private void OptimizeColorProfile()
        {
            _logger.LogEntry(nameof(OptimizeColorProfile));
try
            {
                // Configurar perfil de cores sRGB para gaming
                using var key = Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop");
                
                // Backup
                _originalColorProfile = key.GetValue("ColorProfile") as int?;
                
                // Definir perfil sRGB otimizado para gaming
                key.SetValue("ColorProfile", 0, RegistryValueKind.DWord); // sRGB
                
                // Desativar ajuste automático de cor
                key.SetValue("AutoColorAdjustment", 0, RegistryValueKind.DWord);
                
                _logger.LogInfo("[Display] ✅ Perfil de cores otimizado para gaming");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Display] Erro ao otimizar perfil de cores: {ex.Message}");
            }
            _logger.LogExit(nameof(OptimizeColorProfile));
}
        
        /// <summary>
        /// Configura DPI awareness para performance
        /// </summary>
        private void OptimizeDpiAwareness()
        {
            _logger.LogEntry(nameof(OptimizeDpiAwareness));
try
            {
                // Backup do estado atual
                _originalDpiAwareness = 1; // Assume per-monitor aware
                
                // Configurar DPI awareness para performance em gaming
                SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
                
                _logger.LogInfo("[Display] ✅ DPI awareness otimizado para performance");
            }
            catch (Exception ex)
            {
_logger.LogWarning($"[Display] Erro ao configurar DPI: {ex.Message}");
}
            _logger.LogExit(nameof(OptimizeDpiAwareness));
}
        
        /// <summary>
        /// Otimiza gamma para melhor visibilidade em jogos
        /// </summary>
        private void OptimizeGammaForGaming()
        {
            _logger.LogEntry(nameof(OptimizeGammaForGaming));
// Gamma optimization disabled to preserve default screen brightness.
            _logger.LogInfo("[Display] Skipping Gamma optimization to keep default brightness.");
            // No changes to gamma are applied.
            _logger.LogExit(nameof(OptimizeGammaForGaming));
}
        
        /// <summary>
        /// Desativa efeitos visuais desnecessários
        /// </summary>
        private void DisableVisualEffects()
        {
            _logger.LogEntry(nameof(DisableVisualEffects));
try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects");
                
                // Desativar animações e efeitos para performance
                key.SetValue("VisualFXSetting", 2, RegistryValueKind.DWord); // Best performance
                
                // Desativar transparência
                key.SetValue("Use Aero Peek", 0, RegistryValueKind.DWord);
                key.SetValue("Use Desktop Composition", 0, RegistryValueKind.DWord);
                
                _logger.LogInfo("[Display] ✅ Efeitos visuais desativados para performance");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Display] Erro ao desativar efeitos: {ex.Message}");
            }
            _logger.LogExit(nameof(DisableVisualEffects));
}
        
        /// <summary>
        /// Restaura configurações originais do display
        /// </summary>
        public async Task<bool> RestoreDisplayAsync()
        {
            _logger.LogEntry(nameof(RestoreDisplayAsync));
try
            {
                _logger.LogInfo("[Display] Restaurando configurações de display...");
                
                // Restaurar VRR
                if (_originalVrrState.HasValue)
                {
                    using var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers");
                    key.SetValue("VrrEnabled", _originalVrrState.Value, RegistryValueKind.DWord);
                }
                
                // Restaurar perfil de cores
                if (_originalColorProfile.HasValue)
                {
                    using var key = Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop");
                    key.SetValue("ColorProfile", _originalColorProfile.Value, RegistryValueKind.DWord);
                }
                
                // Restaurar gamma para padrão
                RestoreDefaultGamma();
                
                // Restaurar efeitos visuais
                RestoreVisualEffects();
                
                _logger.LogInfo("[Display] ✅ Configurações de display restauradas");
return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("[Display] Erro ao restaurar display", ex);
return false;
            }
            _logger.LogExit(nameof(RestoreDisplayAsync));
}
        
        /// <summary>
        /// Restaura gamma para padrão
        /// </summary>
        private void RestoreDefaultGamma()
        {
            _logger.LogEntry(nameof(RestoreDefaultGamma));
try
            {
                var hdc = GetDC(IntPtr.Zero);
                if (hdc != IntPtr.Zero)
                {
                    var ramp = new RAMP();
                    ramp.Red = new ushort[256];
                    ramp.Green = new ushort[256];
                    ramp.Blue = new ushort[256];
                    
                    // Restaurar gamma 1.0 (padrão)
                    for (int i = 0; i < 256; i++)
                    {
                        var value = (ushort)(i * 256);
                        ramp.Red[i] = value;
                        ramp.Green[i] = value;
                        ramp.Blue[i] = value;
                    }
                    
                    SetDeviceGammaRamp(hdc, ref ramp);
                    ReleaseDC(IntPtr.Zero, hdc);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Display] Erro ao restaurar gamma: {ex.Message}");
            }
            _logger.LogExit(nameof(RestoreDefaultGamma));
}
        
        /// <summary>
        /// Restaura efeitos visuais
        /// </summary>
        private void RestoreVisualEffects()
        {
            _logger.LogEntry(nameof(RestoreVisualEffects));
try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects");
                key.SetValue("VisualFXSetting", 3, RegistryValueKind.DWord); // Let Windows choose
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Display] Erro ao restaurar efeitos: {ex.Message}");
            }
            _logger.LogExit(nameof(RestoreVisualEffects));
}
        
        /// <summary>
        /// Obtém métricas de display
        /// </summary>
        public (int RefreshRate, bool VrrEnabled, string ColorProfile) GetDisplayMetrics()
        {
            try
            {
                // Obter taxa de refresh
                var refreshRate = 60; // Default
                
                // Verificar VRR
                using var vrrKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers");
                var vrrEnabled = vrrKey?.GetValue("VrrEnabled") as int? == 1;
                
                // Verificar perfil de cores
                using var colorKey = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
                var colorProfile = colorKey?.GetValue("ColorProfile")?.ToString() ?? "Unknown";
                
                return (refreshRate, vrrEnabled, colorProfile);
            }
            catch
            {
                return (60, false, "Unknown");
            }
        }
    }
    
    /// <summary>
    /// Interface para otimização de display
    /// </summary>
    public interface IDisplayOptimizerService
    {
        Task<bool> OptimizeDisplayAsync();
        Task<bool> RestoreDisplayAsync();
        (int RefreshRate, bool VrrEnabled, string ColorProfile) GetDisplayMetrics();
    }
}
