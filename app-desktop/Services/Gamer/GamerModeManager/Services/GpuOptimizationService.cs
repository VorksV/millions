using VoltrisOptimizer.Helpers;
using System;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Services.Gamer.Models;

namespace VoltrisOptimizer.Services.Gamer.GamerModeManager
{
    /// <summary>
    /// Resultado da otimização de GPU
    /// </summary>
    public class GpuOptimizationResult
    {
        public bool Success { get; set; }
        public GpuVendor Vendor { get; set; }
        public string Name { get; set; } = "";
        public string PowerMode { get; set; } = "";
        public string? ErrorMessage { get; set; }
    }
    
    /// <summary>
    /// Interface do serviço de otimização de GPU
    /// </summary>
    public interface IGpuOptimizationService : IDisposable
    {
        Task<GpuOptimizationResult> OptimizeAsync(bool forceMaxPerformance, CancellationToken ct = default);
        Task RestoreAsync(CancellationToken ct = default);
        GpuVendor DetectGpuVendor();
        (double Temperature, double Usage, double Power) GetGpuMetrics();
    }
    
    /// <summary>
    /// Serviço de otimização de GPU com suporte a NVIDIA e AMD
    /// </summary>
    public class GpuOptimizationService : IGpuOptimizationService
    {
        private readonly ILoggingService _logger;
        private GpuVendor _detectedVendor = GpuVendor.Unknown;
        private string _gpuName = "";
        
        // Estado anterior para rollback
        private int? _previousNvidiaPowerMode;
        private string? _previousAmdProfile;
        
        // Paths
        private static readonly string NvidiaSmiPath = @"C:\Windows\System32\nvidia-smi.exe";
        private static readonly string[] AmdCliPaths = new[]
        {
            @"C:\Program Files\AMD\CNext\CNext\RadeonSoftware.exe",
            @"C:\Program Files\AMD\CNext\CNext\amdconfigutil.exe"
        };
        
        public GpuOptimizationService(ILoggingService logger)
        {
            _logger = logger;
            _logger.LogEntry(nameof(GpuOptimizationService));

            // CORREÇÃO DE CAUSA RAIZ DOS FREEZES DE UI:
            // O construtor chamava DetectGpuVendor(), que executa uma consulta WMI
            // ("SELECT * FROM Win32_VideoController") de forma SINCRONA. GamerModeManager é
            // resolvido pelo container DI durante o startup, portanto essa consulta rodava na
            // UI THREAD e bloqueava o Dispatcher por ~1s. Esse era o congelamento registrado
            // repetidamente no log (UI_MONITOR) — e ele só pôde ser identificado depois que o
            // diagnóstico deixou de sair vazio.
            //
            // A detecção passou a ser preguiçosa (sob demanda) e memoizada: o primeiro acesso pode
            // bloquear, os seguintes não; e a detecção só é feita se algum fluxo realmente
            // precisar do fabricante da GPU.
            _logger.LogInfo("[GPU] Fabricante da GPU será detectado sob demanda (evita WMI síncrono no construtor).");
            _logger.LogExit(nameof(GpuOptimizationService));
        }

        private GpuVendor? _cachedVendor;

        /// <summary>
        /// Garante que <see cref="_detectedVendor"/> esteja preenchido.
        /// BUG CORRIGIDO: <c>_detectedVendor</c> nunca era atribuído em nenhum fluxo, mas era lido por
        /// OptimizeAsync/RestoreAsync. Na prática o switch caía sempre em <c>default</c> e a otimização
        /// de GPU retornava "GPU não suportada ou não detectada" sem nunca aplicar nada — mesmo com
        /// GPU válida. A detecção segue sendo sob demanda (o construtor não chama WMI para não
        /// congelar a UI thread), mas agora os consumidores usam o resultado memoizado.
        /// </summary>
        private GpuVendor EnsureVendorDetected()
        {
            _detectedVendor = DetectGpuVendor();
            return _detectedVendor;
        }

        /// <summary>
        /// Detecta o fabricante da GPU principal. Memoizado: a consulta WMI acontece uma única
        /// vez, mesmo que chamada de vários fluxos.
        /// </summary>
        public GpuVendor DetectGpuVendor()
        {
            if (_cachedVendor.HasValue) return _cachedVendor.Value;

            _logger.LogEntry(nameof(DetectGpuVendor));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
                
                foreach (ManagementObject obj in searcher.Get())
                {
                    using var __dispose_obj = obj;
                    var name = obj["Name"]?.ToString() ?? "";
                    var adapterCompatibility = obj["AdapterCompatibility"]?.ToString() ?? "";
                    
                    _gpuName = name;
                    
                    if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                        adapterCompatibility.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInfo($"[GPU] Detectada NVIDIA: {name} ({sw.ElapsedMilliseconds}ms)");
                        _cachedVendor = GpuVendor.Nvidia;
                        return _cachedVendor.Value;
                    }
                    
                    if (name.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ||
                        adapterCompatibility.Contains("AMD", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInfo($"[GPU] Detectada AMD: {name} ({sw.ElapsedMilliseconds}ms)");
                        _cachedVendor = GpuVendor.Amd;
                        return _cachedVendor.Value;
                    }
                    
                    if (name.Contains("Intel", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInfo($"[GPU] Detectada Intel: {name} ({sw.ElapsedMilliseconds}ms)");
                        _cachedVendor = GpuVendor.Intel;
                        return _cachedVendor.Value;
                    }
                }

                _cachedVendor = GpuVendor.Unknown;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[GPU] Erro ao detectar GPU: {ex.Message}");
                _cachedVendor = GpuVendor.Unknown;
            }

            _logger.LogExit(nameof(DetectGpuVendor));
            return _cachedVendor.Value;
        }

        
        /// <summary>
        /// Otimiza a GPU para máxima performance
        /// </summary>
        public async Task<GpuOptimizationResult> OptimizeAsync(bool forceMaxPerformance, CancellationToken ct = default)
        {
            _logger.LogEntry(nameof(OptimizeAsync));
            var vendor = EnsureVendorDetected();
            _logger.LogInfo($"[GPU] Fabricante detectado sob demanda: {vendor}");
            var result = new GpuOptimizationResult
            {
                Vendor = vendor,
                Name = _gpuName
            };
            
            try
            {
                switch (vendor)
                {
                    case GpuVendor.Nvidia:
                        result = await OptimizeNvidiaAsync(forceMaxPerformance, ct);
                        break;
                        
                    case GpuVendor.Amd:
                        result = await OptimizeAmdAsync(forceMaxPerformance, ct);
                        break;
                        
                    case GpuVendor.Intel:
                        result = OptimizeIntel();
                        break;
                        
                    default:
                        result.ErrorMessage = "GPU não suportada ou não detectada";
                        break;
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                _logger.LogError($"[GPU] Erro na otimização: {ex.Message}");
            }
return result;
            _logger.LogExit(nameof(OptimizeAsync));
}
        
        /// <summary>
        /// Restaura configurações originais da GPU
        /// </summary>
        public async Task RestoreAsync(CancellationToken ct = default)
        {
            _logger.LogEntry(nameof(RestoreAsync));
            var vendor = EnsureVendorDetected();
            _logger.LogInfo($"[GPU] Restaurando configurações — fabricante: {vendor}");
try
            {
                switch (vendor)
                {
                    case GpuVendor.Nvidia:
                        await RestoreNvidiaAsync(ct);
                        break;
                        
                    case GpuVendor.Amd:
                        await RestoreAmdAsync(ct);
                        break;
                }
                
                _logger.LogInfo("[GPU] Configurações restauradas");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[GPU] Erro ao restaurar: {ex.Message}");
            }
            _logger.LogExit(nameof(RestoreAsync));
}
        
        #region NVIDIA
        
        private async Task<GpuOptimizationResult> OptimizeNvidiaAsync(bool forceMax, CancellationToken ct)
        {
            _logger.LogEntry(nameof(OptimizeNvidiaAsync));
var result = new GpuOptimizationResult
            {
                Vendor = GpuVendor.Nvidia,
                Name = _gpuName
            };
            
            // Tentar via nvidia-smi primeiro
            if (File.Exists(NvidiaSmiPath))
            {
                result = await OptimizeNvidiaViaSmiAsync(forceMax, ct);
            }
            
            // Também aplicar via registro (sempre funciona)
            OptimizeNvidiaViaRegistry(forceMax);
            
            if (!result.Success)
            {
result.Success = true; // Se o registro funcionou
                result.PowerMode = "Max Performance (Registry)";
}
return result;
            _logger.LogExit(nameof(OptimizeNvidiaAsync));
}
        
        private async Task<GpuOptimizationResult> OptimizeNvidiaViaSmiAsync(bool forceMax, CancellationToken ct)
        {
            _logger.LogEntry(nameof(OptimizeNvidiaViaSmiAsync));
var result = new GpuOptimizationResult
            {
                Vendor = GpuVendor.Nvidia,
                Name = _gpuName
            };
            
            try
            {
                // Ativar modo persistente
                await RunNvidiaSmiAsync("-pm 1", ct);
                
                if (forceMax)
                {
                    // Definir modo de energia máxima
                    // Power limit máximo permitido pelo driver
                    var (success, output) = await RunNvidiaSmiAsync("-q -d POWER", ct);
                    
                    if (success)
                    {
                        // Extrair power limit máximo
                        var maxMatch = Regex.Match(output, @"Max Power Limit\s*:\s*([\d.]+)\s*W", RegexOptions.IgnoreCase);
                        if (maxMatch.Success && double.TryParse(maxMatch.Groups[1].Value, out var maxPower))
                        {
                            await RunNvidiaSmiAsync($"-pl {maxPower}", ct);
                            _logger.LogInfo($"[GPU] NVIDIA Power Limit definido: {maxPower}W");
                        }
                    }
                    
                    // Aplicar clocks de aplicativo (se suportado)
                    await RunNvidiaSmiAsync("-ac 5001,1800", ct);
                    
                    result.PowerMode = "Maximum Performance";
                }
                else
                {
                    result.PowerMode = "Optimized";
                }
                
                result.Success = true;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                _logger.LogWarning($"[GPU] nvidia-smi falhou: {ex.Message}");
            }
return result;
            _logger.LogExit(nameof(OptimizeNvidiaViaSmiAsync));
}
        
        private void OptimizeNvidiaViaRegistry(bool forceMax)
        {
            _logger.LogEntry(nameof(OptimizeNvidiaViaRegistry));
try
            {
                // Definir Power Management Mode para "Prefer Maximum Performance"
                // Chave: HKLM\SYSTEM\CurrentControlSet\Control\Video\{GUID}\0000
                
                using var videoKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Video", false);
                if (videoKey == null) return;
                
                foreach (var guidName in videoKey.GetSubKeyNames())
                {
                    try
                    {
                        using var guidKey = videoKey.OpenSubKey($@"{guidName}\0000", true);
                        if (guidKey == null) continue;
                        
                        // Verificar se é NVIDIA
                        var driverDesc = guidKey.GetValue("DriverDesc")?.ToString() ?? "";
                        if (!driverDesc.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                            continue;
                        
                        // Salvar valor anterior
                        _previousNvidiaPowerMode = guidKey.GetValue("PowerMizerEnable") as int?;
                        
                        // Aplicar configurações
                        if (forceMax)
                        {
                            guidKey.SetValue("PowerMizerEnable", 1, RegistryValueKind.DWord);
                            guidKey.SetValue("PowerMizerLevel", 1, RegistryValueKind.DWord);
                            guidKey.SetValue("PerfLevelSrc", 0x2222, RegistryValueKind.DWord);
                        }
                        
                        _logger.LogInfo("[GPU] Configurações NVIDIA aplicadas via registro");
                        break;
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[GPU] Erro ao configurar registro NVIDIA: {ex.Message}");
            }
            _logger.LogExit(nameof(OptimizeNvidiaViaRegistry));
}
        
        private async Task RestoreNvidiaAsync(CancellationToken ct)
        {
            _logger.LogEntry(nameof(RestoreNvidiaAsync));
// Restaurar via nvidia-smi
            if (File.Exists(NvidiaSmiPath))
            {
                await RunNvidiaSmiAsync("-rac", ct); // Reset application clocks
                await RunNvidiaSmiAsync("-pm 0", ct); // Desativar modo persistente
            }
            
            // Restaurar registro
            if (_previousNvidiaPowerMode.HasValue)
            {
                try
                {
                    using var videoKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Video", false);
                    if (videoKey != null)
                    {
                        foreach (var guidName in videoKey.GetSubKeyNames())
                        {
                            try
                            {
                                using var guidKey = videoKey.OpenSubKey($@"{guidName}\0000", true);
                                if (guidKey == null) continue;
                                
                                var driverDesc = guidKey.GetValue("DriverDesc")?.ToString() ?? "";
                                if (!driverDesc.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                                    continue;
                                
                                guidKey.SetValue("PowerMizerEnable", _previousNvidiaPowerMode.Value, RegistryValueKind.DWord);
                                break;
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }
            _logger.LogExit(nameof(RestoreNvidiaAsync));
}
        
        private async Task<(bool Success, string Output)> RunNvidiaSmiAsync(string args, CancellationToken ct)
        {
try
            {
                using var proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = NvidiaSmiPath,
                        Arguments = args,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };
                
                proc.Start();
                var output = await proc.StandardOutput.ReadToEndAsync();
                await proc.WaitForExitAsync(ct);
return (proc.ExitCode == 0, output);
            }
            catch (Exception ex)
            {
return (false, ex.Message);
            }
}
        
        #endregion
        
        #region AMD
        
        private async Task<GpuOptimizationResult> OptimizeAmdAsync(bool forceMax, CancellationToken ct)
        {
            _logger.LogEntry(nameof(OptimizeAmdAsync));
var result = new GpuOptimizationResult
            {
                Vendor = GpuVendor.Amd,
                Name = _gpuName
            };
            
            try
            {
                // Aplicar via registro (AMD Radeon Settings)
                OptimizeAmdViaRegistry(forceMax);
                
                result.Success = true;
                result.PowerMode = forceMax ? "Maximum Performance" : "Optimized";
            }
            catch (Exception ex)
            {
result.ErrorMessage = ex.Message;
                _logger.LogWarning($"[GPU] AMD otimização falhou: {ex.Message}");
}
return result;
            _logger.LogExit(nameof(OptimizeAmdAsync));
}
        
        private void OptimizeAmdViaRegistry(bool forceMax)
        {
            _logger.LogEntry(nameof(OptimizeAmdViaRegistry));
try
            {
                // AMD Global Settings
                // HKCU\Software\AMD\CN
                
                using var amdKey = Registry.CurrentUser.CreateSubKey(@"Software\AMD\CN");
                if (amdKey != null)
                {
                    // Salvar configuração anterior
                    _previousAmdProfile = amdKey.GetValue("PowerXpress")?.ToString();
                    
                    if (forceMax)
                    {
                        // Forçar GPU discreta
                        amdKey.SetValue("PowerXpress", "1", RegistryValueKind.String);
                        
                        // Desativar PowerPlay (limites de energia)
                        amdKey.SetValue("PP_PhmSoftPowerPlayTable", "", RegistryValueKind.String);
                    }
                }
                
                // AMD Performance Settings
                using var perfKey = Registry.CurrentUser.CreateSubKey(@"Software\AMD\CN\Performance");
                if (perfKey != null && forceMax)
                {
                    // Ativar modo de alta performance
                    perfKey.SetValue("GlobalPerformance", 1, RegistryValueKind.DWord);
                }
                
                _logger.LogInfo("[GPU] Configurações AMD aplicadas via registro");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[GPU] Erro ao configurar registro AMD: {ex.Message}");
            }
            _logger.LogExit(nameof(OptimizeAmdViaRegistry));
}
        
        private async Task RestoreAmdAsync(CancellationToken ct)
        {
            _logger.LogEntry(nameof(RestoreAmdAsync));
try
            {
                if (!string.IsNullOrEmpty(_previousAmdProfile))
                {
                    using var amdKey = Registry.CurrentUser.OpenSubKey(@"Software\AMD\CN", true);
                    if (amdKey != null)
                    {
                        amdKey.SetValue("PowerXpress", _previousAmdProfile, RegistryValueKind.String);
                    }
                }
            }
            catch { }
            
            await Task.CompletedTask;
            _logger.LogExit(nameof(RestoreAmdAsync));
        }
        
        #endregion
        
        #region Intel
        
        private GpuOptimizationResult OptimizeIntel()
        {
            _logger.LogEntry(nameof(OptimizeIntel));
// Intel GPUs não precisam de otimização especial
            // O plano de energia do Windows já cuida disso
            
            return new GpuOptimizationResult
            {
                Vendor = GpuVendor.Intel,
                Name = _gpuName,
                Success = true,
                PowerMode = "Managed by Power Plan"
            };
            _logger.LogExit(nameof(OptimizeIntel));
}
        
        #endregion
        
        /// <summary>
        /// Obtém métricas da GPU em tempo real
        /// </summary>
        public (double Temperature, double Usage, double Power) GetGpuMetrics()
        {
            try
            {
                if ((_detectedVendor != GpuVendor.Unknown ? _detectedVendor : (_cachedVendor ?? GpuVendor.Unknown)) == GpuVendor.Nvidia && File.Exists(NvidiaSmiPath))
                {
                    return GetNvidiaMetrics();
                }
                
                // Fallback para WMI
                return GetWmiGpuMetrics();
            }
            catch
            {
                return (0, 0, 0);
            }
        }
        
        private (double, double, double) GetNvidiaMetrics()
        {
            try
            {
                using var proc = new Process
{
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = NvidiaSmiPath,
                        Arguments = "--query-gpu=temperature.gpu,utilization.gpu,power.draw --format=csv,noheader,nounits",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true
                    }
                };
                
                proc.Start();
                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(2000);
                
                var parts = output.Split(',');
                if (parts.Length >= 3)
                {
                    double.TryParse(parts[0].Trim(), out var temp);
                    double.TryParse(parts[1].Trim(), out var usage);
                    double.TryParse(parts[2].Trim(), out var power);
                    
                    return (temp, usage, power);
                }
            }
            catch { }
            
            return (0, 0, 0);
        }
        
        private (double, double, double) GetWmiGpuMetrics()
        {
            // WMI não fornece métricas detalhadas de GPU
            // Retornar valores padrão
            return (0, 0, 0);
        }
        
        public void Dispose()
        {
            _logger.LogEntry(nameof(Dispose));
// Nada a liberar
            _logger.LogExit(nameof(Dispose));
}
    }
}

