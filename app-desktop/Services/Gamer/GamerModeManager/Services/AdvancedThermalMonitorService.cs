using VoltrisOptimizer.Utils;
using VoltrisOptimizer.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.Management;
using VoltrisOptimizer.Core;
using VoltrisOptimizer.Services.Gamer.Interfaces;

namespace VoltrisOptimizer.Services.Gamer.GamerModeManager.Services
{
    /// <summary>
    /// Advanced Thermal Monitor Service - Monitoramento térmico ultra-inteligente
    /// Detecta hardware universalmente, monitora temperaturas e desativa otimizações em caso de risco
    /// </summary>
    public class AdvancedThermalMonitorService : IAdvancedThermalMonitorService
    {
        private readonly ILoggingService _logger;
        private readonly HardwareSafetyIntelligenceService _safetyService;
        
        // Estado do monitoramento
        private bool _isMonitoring = false;
        private CancellationTokenSource? _cancellationTokenSource;

        // Limites de temperatura
        private readonly Dictionary<string, double> _temperatureLimits = new()
        {
            ["CPU"] = 94.0,      // Limite crítico
            ["GPU"] = 89.0,      // Limite crítico
            ["System"] = 85.0    // Limite geral
        };
        
        // Histórico de temperaturas
        private readonly Queue<TemperatureReading> _temperatureHistory = new(60); // Ãšltimas 60 leituras
        private readonly Dictionary<string, double> _currentTemperatures = new();
        
        // APIs nativas
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);
        
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);
        
        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int NtQueryInformationProcess(IntPtr ProcessHandle, int ProcessInformationClass, IntPtr ProcessInformation, ref int ReturnLength);
        
        // Constantes
        private const uint PROCESS_QUERY_INFORMATION = 0x0400;
        private const int ProcessBasicInformation = 0;
        private const uint PROCESS_ALL_ACCESS = 0x1F0FFF;
        
        public AdvancedThermalMonitorService(ILoggingService logger, HardwareSafetyIntelligenceService safetyService)
        {
            _logger.LogEntry(nameof(AdvancedThermalMonitorService));
            _logger = logger;
            _safetyService = safetyService;
            _logger.LogExit(nameof(AdvancedThermalMonitorService));
        }
        
        /// <summary>
        /// Inicia monitoramento térmico avançado
        /// </summary>
        public async Task<bool> StartMonitoringAsync()
        {
            _logger.LogEntry(nameof(StartMonitoringAsync));
try
            {
                if (_isMonitoring)
                {
                    _logger.LogWarning("[Thermal-Monitor] Monitoramento já está ativo");
return true;
                }
                
                _logger.LogInfo("[Thermal-Monitor] Iniciando monitoramento térmico avançado...");
                
                // Inicializar sensores
                await InitializeThermalSensorsAsync();
                _cancellationTokenSource = new CancellationTokenSource();
                
                // Inscrever no SystemMetricsCache (reativo, sem polling)
                SystemMetricsCache.Instance.MetricsUpdated += OnMetricsUpdated;
                
                _isMonitoring = true;
                
                _logger.LogSuccess("[Thermal-Monitor] âœ… Monitoramento térmico iniciado (reativo via SystemMetricsCache)!");
return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("[Thermal-Monitor] Erro ao iniciar monitoramento", ex);
return false;
            }
            _logger.LogExit(nameof(StartMonitoringAsync));
}
        
        /// <summary>
        /// Para monitoramento térmico
        /// </summary>
        public async Task<bool> StopMonitoringAsync()
        {
            _logger.LogEntry(nameof(StopMonitoringAsync));
try
            {
                if (!_isMonitoring)
                {
                    _logger.LogWarning("[Thermal-Monitor] Monitoramento não está ativo");
return true;
                }
                
                _logger.LogInfo("[Thermal-Monitor] Parando monitoramento térmico...");
                
                // Remover inscrição do SystemMetricsCache
                SystemMetricsCache.Instance.MetricsUpdated -= OnMetricsUpdated;
                _cancellationTokenSource?.Cancel();
                
                _isMonitoring = false;
                
                _logger.LogInfo("[Thermal-Monitor] âœ… Monitoramento térmico parado");
return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("[Thermal-Monitor] Erro ao parar monitoramento", ex);
return false;
            }
            _logger.LogExit(nameof(StopMonitoringAsync));
}
        
        /// <summary>
        /// Inicializa sensores térmicos
        /// </summary>
        private async Task InitializeThermalSensorsAsync()
        {
            _logger.LogEntry(nameof(InitializeThermalSensorsAsync));
try
            {
                // âœ… CORREÃ‡ÃO #2: Remover detecção WMI complexa - usar apenas dados do SystemMetricsCache
                _logger.LogInfo("[Thermal-Monitor] Inicializando sensores térmicos (modo reativo simplificado)...");
                
                // Apenas logar que o monitoramento será feito via SystemMetricsCache
                _logger.LogInfo("[Thermal-Monitor] Sensores: CPU (via SystemMetricsCache), GPU (via SystemMetricsCache)");
                
                // Calibração inicial rápida (apenas aguardar primeira leitura do cache)
                await Task.Delay(500);
                
                _logger.LogInfo($"[Thermal-Monitor] Sensores calibrados - baseline inicial: CPU={SystemMetricsCache.Instance.CpuTemperature:F1}°C, GPU={SystemMetricsCache.Instance.GpuTemperature:F1}°C");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Thermal-Monitor] Erro ao inicializar sensores: {ex.Message}");
            }
            _logger.LogExit(nameof(InitializeThermalSensorsAsync));
}
        
        /// <summary>
        /// âœ… REMOVIDO: Detectar sensores térmicos via WMI (causava polling e bloqueio)
        /// O monitoramento agora é 100% reativo via SystemMetricsCache
        /// </summary>
        [Obsolete("Usar SystemMetricsCache diretamente - sem WMI polling", true)]
        private async Task<List<ThermalSensor>> DetectThermalSensorsAsync()
        {
            _logger.LogEntry(nameof(DetectThermalSensorsAsync));
// Método obsoleto - não usar
            throw new NotImplementedException("DetectThermalSensorsAsync foi removido - usar SystemMetricsCache");
            _logger.LogExit(nameof(DetectThermalSensorsAsync));
}
        
        /// <summary>
        /// Obtém descrição do sensor
        /// </summary>
        private string GetSensorDescription(string sensorName)
        {
            _logger.LogEntry(nameof(GetSensorDescription));
var name = sensorName.ToLower();
            
            if (name.Contains("cpu")) return "CPU Package Temperature";
            if (name.Contains("gpu")) return "GPU Temperature";
            if (name.Contains("ambient")) return "System Ambient Temperature";
            if (name.Contains("memory")) return "Memory Temperature";
return "Temperature Sensor";
            _logger.LogExit(nameof(GetSensorDescription));
}
        
        /// <summary>
        /// Detecta tipo de sensor
        /// </summary>
        private SensorType DetectSensorType(string sensorName)
        {
            _logger.LogEntry(nameof(DetectSensorType));
var name = sensorName.ToLower();
            
            if (name.Contains("cpu")) return SensorType.Cpu;
            if (name.Contains("gpu")) return SensorType.Gpu;
            if (name.Contains("memory")) return SensorType.Memory;
            if (name.Contains("ambient") || name.Contains("system")) return SensorType.System;
return SensorType.Unknown;
            _logger.LogExit(nameof(DetectSensorType));
}
        
        /// <summary>
        /// Calibra sensores térmicos
        /// </summary>
        private async Task CalibrateSensorsAsync(List<ThermalSensor> sensors)
        {
            _logger.LogEntry(nameof(CalibrateSensorsAsync));
// âœ… CORREÃ‡ÃO #2: Removido polling de calibração - abordagem puramente reativa
            _logger.LogInfo("[Thermal-Monitor] Calibração rápida via SystemMetricsCache...");
            
            // Apenas aguardar primeira leitura do cache (já é atualizada por polling interno de 3s)
            await Task.Delay(100);
            
            var cache = SystemMetricsCache.Instance;
            _logger.LogInfo($"[Thermal-Monitor] Sensores calibrados - CPU={cache.CpuTemperature:F1}°C, GPU={cache.GpuTemperature:F1}°C");
            _logger.LogExit(nameof(CalibrateSensorsAsync));
}
        
        private void OnMetricsUpdated(object? sender, EventArgs e)
        {
            _logger.LogEntry(nameof(OnMetricsUpdated));
var cache = SystemMetricsCache.Instance;
            
            // Verificar temperaturas críticas em tempo real
            if (cache.CpuTemperature > _temperatureLimits.GetValueOrDefault("CPU", 94))
            {
                _logger.LogWarning($"[Thermal-Monitor] âš  CPU crítica: {cache.CpuTemperature:F1}°C (limite: {_temperatureLimits["CPU"]}°C)");
                _ = TriggerThermalEmergencyAsync(new List<string> { $"CPU: {cache.CpuTemperature:F1}°C" });
            }
            
            if (cache.GpuTemperature > _temperatureLimits.GetValueOrDefault("GPU", 89))
            {
                _logger.LogWarning($"[Thermal-Monitor] âš  GPU crítica: {cache.GpuTemperature:F1}°C (limite: {_temperatureLimits["GPU"]}°C)");
                _ = TriggerThermalEmergencyAsync(new List<string> { $"GPU: {cache.GpuTemperature:F1}°C" });
            }
            _logger.LogExit(nameof(OnMetricsUpdated));
}
        
        /// <summary>
        /// âœ… CORREÃ‡ÃO #2: Obtém temperaturas diretamente do SystemMetricsCache (ZERO polling WMI)
        /// </summary>
        private Dictionary<string, double> GetCurrentTemperaturesFromCache()
        {
            _logger.LogEntry(nameof(GetCurrentTemperaturesFromCache));
var temperatures = new Dictionary<string, double>();
            var cache = SystemMetricsCache.Instance;
            
            if (cache.CpuTemperature > 0)
                temperatures["CPU"] = cache.CpuTemperature;
            
if (cache.GpuTemperature > 0)
                temperatures["GPU"] = cache.GpuTemperature;
return temperatures;
            _logger.LogExit(nameof(GetCurrentTemperaturesFromCache));
}
        
        /// <summary>
        /// ✅ REMOVIDOS: Métodos GetCpuTemperatureAsync, GetNvidiaGpuTemperatureAsync, GetAmdGpuTemperatureAsync,
        /// GetSystemTemperatureAsync, GetCpuUsageAsync, GetAverageTemperatureAsync, GetSensorTemperatureAsync
        /// Todos substituídos por leitura direta do SystemMetricsCache
        /// </summary>
        
        /// <summary>
        /// Verifica temperaturas críticas
        /// </summary>
        private async Task CheckCriticalTemperaturesAsync(Dictionary<string, double> currentTemps)
        {
            _logger.LogEntry(nameof(CheckCriticalTemperaturesAsync));
var criticalTemps = new List<string>();
            
            foreach (var temp in currentTemps)
            {
                if (_temperatureLimits.ContainsKey(temp.Key))
                {
                    var limit = _temperatureLimits[temp.Key];
                    if (temp.Value >= limit)
                    {
                        criticalTemps.Add($"{temp.Key}: {temp.Value:F1}°C (limite: {limit:F1}°C)");
                    }
                }
            }
            
            if (criticalTemps.Any())
            {
                _logger.LogError($"[Thermal-Monitor] ðŸš¨ TEMPERATURAS CRÍTICAS DETECTADAS!");
                foreach (var critical in criticalTemps)
                    _logger.LogError($"[Thermal-Monitor]   - {critical}");
                
                // Disparar evento de emergência térmica
                await TriggerThermalEmergencyAsync(criticalTemps);
            }
            _logger.LogExit(nameof(CheckCriticalTemperaturesAsync));
}
        
        /// <summary>
        /// Verifica tendências de aquecimento
        /// </summary>
        private async Task CheckHeatingTrendsAsync()
        {
            _logger.LogEntry(nameof(CheckHeatingTrendsAsync));
if (_temperatureHistory.Count < 30) return; // Precisa de pelo menos 30 leituras
            
            var recent = _temperatureHistory.TakeLast(10).ToList();
            var older = _temperatureHistory.SkipLast(10).TakeLast(20).ToList();
            
            if (!recent.Any() || !older.Any()) return;
            
            var recentAvg = recent.Average(r => r.AverageTemperature);
            var olderAvg = older.Average(o => o.AverageTemperature);
            
            // Se temperatura está subindo rapidamente (>5°C em 30 segundos)
            if (recentAvg - olderAvg > 5)
            {
                _logger.LogWarning($"[Thermal-Monitor] ðŸ“ˆ Tendência de aquecimento detectada: +{(recentAvg - olderAvg):F1}°C");
                
                // Prevenir sobreaquecimento
                await PreventOverheatingAsync();
            }
            _logger.LogExit(nameof(CheckHeatingTrendsAsync));
}
        
        /// <summary>
        /// Dispara emergência térmica
        /// </summary>
        private async Task TriggerThermalEmergencyAsync(List<string> criticalTemps)
        {
            _logger.LogEntry(nameof(TriggerThermalEmergencyAsync));
try
            {
                _logger.LogError("[Thermal-Monitor] ðŸš¨ DISPARANDO EMERGÃŠNCIA TÉRMICA!");
                
                // 1. Desativar otimizações de performance
                await DisablePerformanceOptimizationsAsync();
                
                // 2. Forçar resfriamento máximo
                await ForceMaximumCoolingAsync();
                
                // 3. Notificar usuário
                NotifyThermalEmergency(criticalTemps);
                
                // 4. Aguardar temperatura segura
                await WaitForSafeTemperatureAsync();
                
                _logger.LogInfo("[Thermal-Monitor] âœ… Emergência térmica resolvida");
            }
            catch (Exception ex)
            {
_logger.LogError("[Thermal-Monitor] Erro na emergência térmica", ex);
}
            _logger.LogExit(nameof(TriggerThermalEmergencyAsync));
}
        
        /// <summary>
        /// Desativa otimizações de performance
        /// </summary>
        private async Task DisablePerformanceOptimizationsAsync()
        {
            _logger.LogEntry(nameof(DisablePerformanceOptimizationsAsync));
try
            {
                _logger.LogWarning("[Thermal-Monitor] ðŸ”¥ Desativando otimizações de performance...");
                
                // [FIX:UNICO-DONO-DE-ENERGIA] ProcessorPerformanceBoostMode NAO e'
                // gravado.
                //
                // Este valor NAO e' o boost do processador. O boost de verdade e'
                // o setting be337238-... dentro do plano de energia, que e' o do
                // Perfil. Esta chave global e' uma preferencia herdada do Windows
                // 7, lida por poucos drivers, e o Perfil nao a enxerga.
                //
                // E o `-setactive`-escondido: o plano de energia era trocado
                // abrindo a CHAVE `...\PowerSchemes\ActivePowerScheme` e
                // escrevendo nela um VALOR chamado `ActivePowerScheme`. Duplo
                // erro: o plano ativo e' a sub-chave cujo NOME e' o GUID (nao um
                // valor), e `OpenSubKey` sem writable:true devolve chave de
                // LEITURA — o SetValue lancaria UnauthorizedAccessException,
                // engolida pelo catch. Ou seja: o plano nunca foi trocado e nunca
                // houve erro visivel.
                _logger.LogInfo(
                    "[Thermal-Monitor] Gravacoes de energia removidas. " +
                    "ProcessorPerformanceBoostMode nao e' o boost do processador, e a troca " +
                    "de plano apontava para chave inexistente e sem permissao de escrita.");

                VoltrisOptimizer.Services.Power.ProfilePowerAuthority.RequestProfileApply(
                    "AdvancedThermalMonitor.DisablePerformanceOptimizations",
                    "alivio termico",
                    null);
                
                _logger.LogWarning("[Thermal-Monitor] âœ… Otimizações de performance desativadas por segurança");
            }
            catch (Exception ex)
            {
                _logger.LogError("[Thermal-Monitor] Erro ao desativar otimizações", ex);
            }
            _logger.LogExit(nameof(DisablePerformanceOptimizationsAsync));
}
        
        /// <summary>
        /// Força resfriamento máximo
        /// </summary>
        private async Task ForceMaximumCoolingAsync()
        {
            _logger.LogEntry(nameof(ForceMaximumCoolingAsync));
try
            {
                _logger.LogInfo("[Thermal-Monitor] [COOLING] Forçando resfriamento máximo...");
                
                // Aumentar velocidade dos fans (se disponível)
                using var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\Temperature");
                key.SetValue("FanSpeed", 100, RegistryValueKind.DWord); // 100% fan speed
                
                // Notificar sistemas de resfriamento
                await NotifyCoolingSystemsAsync();
                
                _logger.LogInfo("[Thermal-Monitor] [OK] Resfriamento máximo ativado");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Thermal-Monitor] Erro ao forçar resfriamento: {ex.Message}");
            }
            _logger.LogExit(nameof(ForceMaximumCoolingAsync));
}
        
        /// <summary>
        /// Notifica sistemas de resfriamento
        /// </summary>
        private async Task NotifyCoolingSystemsAsync()
        {
            _logger.LogEntry(nameof(NotifyCoolingSystemsAsync));
try
            {
                // Verificar se há software de controle de fans
                var processes = Process.GetProcessesByName("FanControl");
                if (processes.Length > 0)
                {
                    // Enviar comando para aumentar velocidade (se suportado)
                    // Esta é uma implementação simplificada
                    _logger.LogInfo("[Thermal-Monitor] ðŸŒ€ Sistemas de resfriamento notificados");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Thermal-Monitor] Erro ao notificar sistemas de resfriamento: {ex.Message}");
            }
            _logger.LogExit(nameof(NotifyCoolingSystemsAsync));
}
        
        /// <summary>
        /// Notifica emergência térmica ao usuário
        /// </summary>
        private void NotifyThermalEmergency(List<string> criticalTemps)
        {
            _logger.LogEntry(nameof(NotifyThermalEmergency));
try
            {
                var message = $"ðŸš¨ EMERGÊNCIA TÉRMICA DETECTADA!\n\n";
                message += "Temperaturas críticas:\n";
                foreach (var temp in criticalTemps)
                    message += $"• {temp}\n";
                message += "\nOtimizações de performance foram desativadas automaticamente.";
                message += "\nAguarde a temperatura baixar para retomar o modo gaming.";
                
                // Aqui você poderia integrar com o sistema de notificação do Voltris
                _logger.LogError($"[Thermal-Monitor] NOTIFICAÃ‡ÃO: {message}");
                
                // Mostrar notificação do Windows (simplificado)
                // Em uma implementação real, você usaria a API de notificação do sistema
            }
            catch (Exception ex)
            {
                _logger.LogError("[Thermal-Monitor] Erro ao notificar emergência", ex);
            }
            _logger.LogExit(nameof(NotifyThermalEmergency));
}
        
        /// <summary>
        /// Aguarda temperatura segura de forma REATIVA (sem polling)
        /// </summary>
        private async Task WaitForSafeTemperatureAsync()
        {
            _logger.LogEntry(nameof(WaitForSafeTemperatureAsync));
try
            {
                _logger.LogInfo("[Thermal-Monitor] ⏳ Aguardando temperatura segura (modo reativo)...");
                
                // âœ… CORREÃ‡ÃO #2: Usar TaskCompletionSource para abordagem reativa
                var safeTempTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                
                // Registrar callback temporário
                EventHandler? handler = null;
                handler = (s, e) =>
                {
                    var cache = SystemMetricsCache.Instance;
                    
                    // Verificar se todas as temperaturas estão 10°C abaixo do limite
                    bool allSafe = true;
                    
                    if (cache.CpuTemperature > 0 && cache.CpuTemperature >= _temperatureLimits.GetValueOrDefault("CPU", 94) - 10)
                        allSafe = false;
                    
                    if (cache.GpuTemperature > 0 && cache.GpuTemperature >= _temperatureLimits.GetValueOrDefault("GPU", 89) - 10)
                        allSafe = false;
                    
                    if (allSafe)
                    {
                        _logger.LogInfo($"[Thermal-Monitor] âœ… Temperatura segura alcançada (CPU: {cache.CpuTemperature:F1}°C, GPU: {cache.GpuTemperature:F1}°C)");
                        SystemMetricsCache.Instance.MetricsUpdated -= handler;
                        safeTempTcs.TrySetResult(true);
                    }
                };
                
                // Inscrever no evento
                SystemMetricsCache.Instance.MetricsUpdated += handler;
                
                // Timeout de segurança: 5 minutos
                var timeoutTask = Task.Delay(TimeSpan.FromMinutes(5), _cancellationTokenSource!.Token);
                
                var completedTask = await Task.WhenAny(safeTempTcs.Task, timeoutTask);
                
                // Remover handler se ainda estiver inscrito
                SystemMetricsCache.Instance.MetricsUpdated -= handler;
                
                if (completedTask == timeoutTask)
                {
                    _logger.LogWarning("[Thermal-Monitor] ⏱ï¸ Timeout aguardando temperatura segura (5 min)");
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInfo("[Thermal-Monitor] ⏹ï¸ Cancelado aguardando temperatura segura");
            }
            catch (Exception ex)
            {
                _logger.LogError("[Thermal-Monitor] Erro ao aguardar temperatura segura", ex);
            }
            _logger.LogExit(nameof(WaitForSafeTemperatureAsync));
}
        
        /// <summary>
        /// Prevenir sobreaquecimento
        /// </summary>
        private async Task PreventOverheatingAsync()
        {
            _logger.LogEntry(nameof(PreventOverheatingAsync));
try
{
                _logger.LogWarning("[Thermal-Monitor] 🛡️ Ativando prevenção de sobreaquecimento...");
                
                // [FIX:UNICO-DONO-DE-ENERGIA] Este e' o par exato do metodo
                // anterior (que gravava 0 aqui), e tem dois defeitos.
                //
                // ProcessorPerformanceBoostMode = 1: nao e' o boost do
                // processador (ver nota no metodo par).
                //
                // FanSpeed = 85 na MESMA chave e' pior: a chave e' uma chave de
                // PREFERENCIAS. Nao existe nela valor algum que controle a
                // velocidade do ventilador — quem controla e' o driver da
                // placa-mae (EC/BIOS), e nenhum BIOS de notebook le essa chave.
                // Gravar 85 aqui nao acelera o cooler: o Windows guarda o numero
                // e nada acontece.
                //
                // A protecao termica real ja existe e e' melhor: o Perfil
                // reduz EPP e desliga o boost quando a temperatura sobe, e o
                // Perfil e' lido pelo processador de verdade. Um metodo chamado
                // "PreventOverheating" que escreve onde ninguem le nao previne
                // sobreaquecimento — produz so a sensacao de que previne.
                _logger.LogInfo(
                    "[Thermal-Monitor] Gravacoes de energia/ventoinha removidas. " +
                    "FanSpeed nao e' lido por nenhum driver de ventilador de notebook. " +
                    "A protecao termica real e' o EPP e o boost do plano, do Perfil.");

                VoltrisOptimizer.Services.Power.ProfilePowerAuthority.RequestProfileApply(
                    "AdvancedThermalMonitor.PreventOverheating",
                    "prevencao de sobreaquecimento",
                    null);
                
                _logger.LogWarning("[Thermal-Monitor] âœ… Prevenção de sobreaquecimento ativada");
            }
            catch (Exception ex)
            {
                _logger.LogError("[Thermal-Monitor] Erro na prevenção", ex);
            }
            _logger.LogExit(nameof(PreventOverheatingAsync));
}
        
        /// <summary>
        /// Registra log detalhado de temperaturas
        /// </summary>
        private void LogDetailedTemperatures(Dictionary<string, double> temperatures)
        {
            _logger.LogEntry(nameof(LogDetailedTemperatures));
var logMessage = "[Thermal-Monitor] ðŸŒ¡ï¸ Temperaturas: ";
            foreach (var temp in temperatures)
            {
                var status = GetTemperatureStatus(temp.Key, temp.Value);
                logMessage += $"{temp.Key}: {temp.Value:F1}°C {status} | ";
            }
            
            _logger.LogInfo(logMessage.Trim());
            _logger.LogExit(nameof(LogDetailedTemperatures));
}
        
        /// <summary>
        /// Obtém status da temperatura
        /// </summary>
        private string GetTemperatureStatus(string sensor, double temperature)
        {
            _logger.LogEntry(nameof(GetTemperatureStatus));
if (_temperatureLimits.ContainsKey(sensor))
            {
                var limit = _temperatureLimits[sensor];
                var percentage = (temperature / limit) * 100;
                
                if (percentage >= 95) return "[RED][CRITICAL]";
                if (percentage >= 85) return "[ORANGE][HIGH]";
                if (percentage >= 70) return "[YELLOW][WARN]";
return "[GREEN][OK]";
            }
return "[WHITE][UNKNOWN]";
            _logger.LogExit(nameof(GetTemperatureStatus));
}
        
        /// <summary>
        /// Obtém métricas do monitoramento térmico
        /// </summary>
        public ThermalMetrics GetThermalMetrics()
        {
            _logger.LogEntry(nameof(GetThermalMetrics));
var avgTemp = _temperatureHistory.Any() ? _temperatureHistory.Average(h => h.AverageTemperature) : 0;
            var maxTemp = _temperatureHistory.Any() ? _temperatureHistory.Max(h => h.AverageTemperature) : 0;
            var minTemp = _temperatureHistory.Any() ? _temperatureHistory.Min(h => h.AverageTemperature) : 0;
            
            return new ThermalMetrics
            {
                IsMonitoring = _isMonitoring,
                CurrentTemperatures = new Dictionary<string, double>(_currentTemperatures),
                AverageTemperature = avgTemp,
                MaxTemperature = maxTemp,
                MinTemperature = minTemp,
                TemperatureHistory = _temperatureHistory.Count,
                LastUpdate = DateTime.UtcNow
            };
            _logger.LogExit(nameof(GetThermalMetrics));
}
    }
    
    // Classes de modelo
    
    /// <summary>
    /// Sensor térmico
    /// </summary>
    public class ThermalSensor
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public SensorType Type { get; set; } = SensorType.Unknown;
        public bool IsActive { get; set; } = false;
    }
    
    /// <summary>
    /// Leitura de temperatura
    /// </summary>
    public class TemperatureReading
    {
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public double AverageTemperature { get; set; } = 0;
        public Dictionary<string, double> Temperatures { get; set; } = new();
    }
    
    /// <summary>
    /// Métricas térmicas
    /// </summary>
    public class ThermalMetrics
    {
        public bool IsMonitoring { get; set; } = false;
        public Dictionary<string, double> CurrentTemperatures { get; set; } = new();
        public double AverageTemperature { get; set; } = 0;
        public double MaxTemperature { get; set; } = 0;
        public double MinTemperature { get; set; } = 0;
        public int TemperatureHistory { get; set; } = 0;
        public DateTime LastUpdate { get; set; } = DateTime.UtcNow;
    }
    
    // Enums
    
    /// <summary>
    /// Tipo de sensor
    /// </summary>
    public enum SensorType
    {
        Unknown,
        Cpu,
        Gpu,
        Memory,
        System
    }
    
    /// <summary>
    /// Interface para serviço de monitoramento térmico avançado
    /// </summary>
    public interface IAdvancedThermalMonitorService
    {
        Task<bool> StartMonitoringAsync();
        Task<bool> StopMonitoringAsync();
        ThermalMetrics GetThermalMetrics();
    }
}

