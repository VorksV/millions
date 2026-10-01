using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.Thermal.Models;
using VoltrisOptimizer.Services.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using VoltrisOptimizer.Services.Hardware;
using VoltrisOptimizer.Utils;

namespace VoltrisOptimizer.Services.Thermal
{
    /// <summary>
    /// Serviço global de monitoramento térmico.
    /// Phase 1 Refactor: Agora atua como consumidor do HardwareTelemetryHub unificado.
    /// </summary>
    public class GlobalThermalMonitorService : IGlobalThermalMonitorService
    {
        private readonly ILoggingService _logger;
        private readonly SettingsService _settingsService;
        private readonly IHardwareTelemetryHub _telemetryHub;
        
        private static readonly Lazy<GlobalThermalMonitorService> _lazyInstance = new(() =>
        {
            try
            {
                var instance = App.Services?.GetService<IGlobalThermalMonitorService>() as GlobalThermalMonitorService;
                if (instance != null) return instance;
            }
            catch { }
            var log = App.LoggingService;
            var settings = SettingsService.Instance;
            var hub = App.Services?.GetService<IHardwareTelemetryHub>();
            return new GlobalThermalMonitorService(log, settings, hub!);
        }, LazyThreadSafetyMode.ExecutionAndPublication);
        public static GlobalThermalMonitorService Instance => _lazyInstance.Value;
        
        private DateTime _lastAlertTime = DateTime.MinValue;
        private DateTime _alertsSuppressedUntil = DateTime.MinValue;
        private bool _libreHwAllNan;
        private int _libreHwNanStreak;
        private int _cyclesSinceLastForcePoll;

        /// <summary>Ultimo estado de disponibilidade registrado no log (null = ainda nao logou).</summary>
        private bool? _ultimoEstadoSemSensor;

        /// <summary>Contador de publicacoes desde o ultimo log detalhado (amostragem).</summary>
        private int _contadorLogDetalhado;

        /// <summary>Registra UMA VEZ a virada para estimativa (evita spam por tique).</summary>
        private bool _estimativaCpuRegistrada;

        /// <summary>Idem para a GPU.</summary>
        private bool _estimativaGpuRegistrada;
        private bool _subscribedToCache;
        
        private const int AlertCooldownMinutes = 30;
        
        // Cache local
        public event EventHandler<ThermalMetrics>? MetricsUpdated;
        public event EventHandler<ThermalAlert>? AlertGenerated;
        
        public ThermalMetrics? CurrentMetrics { get; private set; }
        public bool IsMonitoring => _subscribedToCache;
        
        public double GetCpuTemperature() => CurrentMetrics?.CpuTemperature ?? 0;
        public double GetGpuTemperature() => CurrentMetrics?.GpuTemperature ?? 0;
        public double GetGpuUsage() => CurrentMetrics?.GpuUsage ?? 0;
        public bool IsCpuThrottling() => CurrentMetrics?.CpuThrottling ?? false;
        public bool IsGpuThrottling() => CurrentMetrics?.GpuThrottling ?? false;
        
        public async Task<ThermalMetrics> GetCurrentMetricsAsync()
        {
            if (CurrentMetrics != null && CurrentMetrics.IsValid && (DateTime.Now - CurrentMetrics.Timestamp).TotalSeconds < 5)
                return CurrentMetrics;

            var fresh = await CollectThermalMetricsAsync();
            if (fresh.IsValid)
                CurrentMetrics = fresh;

            return CurrentMetrics ?? fresh;
        }

        public GlobalThermalMonitorService(ILoggingService logger, SettingsService settingsService, IHardwareTelemetryHub telemetryHub)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _telemetryHub = telemetryHub ?? throw new ArgumentNullException(nameof(telemetryHub));
            
            if (_settingsService != null)
                _settingsService.ProfileChanged += OnProfileChanged;
        }
        
        private void OnProfileChanged(object? sender, IntelligentProfileType newProfile)
        {
            _logger.LogInfo($"[GlobalThermal] Perfil mudou para {newProfile}");
        }

        public Task StartMonitoringAsync()
        {
            if (IsMonitoring) return Task.CompletedTask;

            _logger.LogInfo("[GlobalThermal] 🌡️ Iniciando monitoramento — subscrevendo ao SystemMetricsCache...");

            // Inicializar o HardwareTelemetryHub (LibreHardwareMonitor)
            // O hub NÃO tem loop próprio — SystemMetricsCache chama ForcePoll
            _ = Task.Run(async () =>
            {
                try
                {
                    await _telemetryHub.StartMonitoringAsync();
                    _logger.LogSuccess("[GlobalThermal] ✅ TelemetryHub inicializado.");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[GlobalThermal] Erro ao iniciar TelemetryHub: {ex.Message}");
                }
            });

            // Subscrever ao cache central — ele nos notificará quando houver dados novos
            if (!_subscribedToCache)
            {
                VoltrisOptimizer.Core.SystemMetricsCache.Instance.MetricsUpdated += OnCacheMetricsUpdated;
                _subscribedToCache = true;
                _logger.LogInfo("[GlobalThermal] ✅ Subscrito ao SystemMetricsCache.MetricsUpdated.");
            }

            return Task.CompletedTask;
        }

        public async Task StopMonitoringAsync()
        {
            if (!_subscribedToCache) return;

            VoltrisOptimizer.Core.SystemMetricsCache.Instance.MetricsUpdated -= OnCacheMetricsUpdated;
            _subscribedToCache = false;

            await _telemetryHub.StopMonitoringAsync();
            _logger.LogInfo("[GlobalThermal] Monitoramento térmico parado.");
        }

        private void OnCacheMetricsUpdated(object? sender, EventArgs e)
        {
            // Chamado a cada ~250ms pelo cache central (após cada tick do timer).
            // Executa coleta térmica LEVE (sem ForcePoll — o cache já fez).
            try
            {
                var sysCache = VoltrisOptimizer.Core.SystemMetricsCache.Instance;

                // ── CORREÇÃO DE AUDITORIA ──
                // Antes, sem sensor de temperatura, eram gravados CONSTANTES:
                //   CpuTemperature = 45.0
                //   GpuTemperature = max(35, CpuTemperature - 3)
                // Isso fazia a ausência total de sensor aparecer como uma medição
                // real de 45 °C, e o flag IsCpuTemperatureEstimated ficava
                // permanentemente falso (a condição testava o valor já substituído).
                // O cartão SAÚDE e a página de Diagnóstico herdavam esse número.
                //
                // Agora: sem sensor, a temperatura é double.NaN e o flag de
                // "estimada" é VERDADEIRO, para que a interface diga
                // "Não foi possível verificar" em vez de mostrar um número falso.
                //
                // ATUALIZADO: o cache agora entrega uma ESTIMATIVA rotulada
                // quando nenhuma fonte real responde (requisito do usuário:
                // temperatura sempre visível). Por isso o flag vem do próprio
                // cache, e NÃO de "o valor é NaN" — caso contrário a estimativa
                // passaria por medição real.
                double cpuTemp = sysCache.CpuTemperature;
                bool cpuTempOk = !double.IsNaN(cpuTemp) && cpuTemp > 0 && cpuTemp < 150;
                bool cpuTempEstimada = sysCache.IsCpuTemperatureEstimated;

                double gpuTemp = sysCache.GpuTemperature;
                bool gpuTempOk = !double.IsNaN(gpuTemp) && gpuTemp > 0 && gpuTemp < 150;
                bool gpuTempEstimada = sysCache.IsGpuTemperatureEstimated;

                var metrics = new ThermalMetrics
                {
                    Timestamp = DateTime.Now,
                    CpuTemperature = cpuTempOk ? cpuTemp : double.NaN,
                    GpuTemperature = gpuTempOk ? gpuTemp : double.NaN,
                    GpuUsage = sysCache.GpuUsagePercent,
                    IsCpuTemperatureEstimated = cpuTempEstimada || !cpuTempOk,
                    IsGpuTemperatureEstimated = gpuTempEstimada || !gpuTempOk,
                    CpuUsage = sysCache.CpuPercent > 0 ? sysCache.CpuPercent : 0,
                    RamUsagePercent = sysCache.MemoryUsedPercent > 0 ? sysCache.MemoryUsedPercent : 0
                };

                // SEGURANÇA — INVARIANTE INEGOCIÁVEL
                // Throttling térmico é uma ação de SEGURANÇA. Ela só pode ser
                // afirmada a partir de uma MEDIÇÃO REAL de sensor. Uma
                // estimativa pode errar vários graus e, num sistema realmente
                // quente, o valor subestimado desligaria justamente a proteção
                // que deveria ter disparado. Por isso o flag estimado bloqueia
                // a afirmação de throttling por completo.
                metrics.CpuThrottling = !cpuTempEstimada && cpuTempOk && metrics.CpuTemperature > 95.0;
                metrics.GpuThrottling = !gpuTempEstimada && gpuTempOk && metrics.GpuTemperature > 95.0;

                // Publicar sempre, inclusive quando não há sensor: nesse caso as
                // temperaturas são NaN e o consumidor precisa saber que mudou
                // de "medido" para "indisponível" (ou vice-versa).
                {
                    // Publicar apenas quando os valores realmente mudam. O cache central
                    // dispara a cada tique (~2s) mesmo com temperaturas estáveis; reemitir
                    // o mesmo ThermalMetrics força alocação, propagação de evento, análise
                    // e log sem trazer informação nova para a UI.
                    var anterior = CurrentMetrics;
                    bool mudou = anterior == null
                        || TemperaturaMudou(anterior.CpuTemperature, metrics.CpuTemperature)
                        || TemperaturaMudou(anterior.GpuTemperature, metrics.GpuTemperature)
                        || Math.Abs(anterior.CpuUsage - metrics.CpuUsage) >= 0.5
                        || Math.Abs(anterior.RamUsagePercent - metrics.RamUsagePercent) >= 0.5
                        || Math.Abs(anterior.GpuUsage - metrics.GpuUsage) >= 0.5
                        || anterior.CpuThrottling != metrics.CpuThrottling
                        || anterior.GpuThrottling != metrics.GpuThrottling;

                    if (!mudou) return;

                    CurrentMetrics = metrics;
                    MetricsUpdated?.Invoke(this, metrics);
                    _ = AnalyzeAndAlertAsync(metrics, CancellationToken.None);

                    // BUG CORRIGIDO: o log de disponibilidade estava DENTRO do bloco
                    // que decide publicar. Como a publicação acompanha CPU/RAM/GPU e
                    // muda a cada ~2s, a mensagem "sensores indisponiveis" era
                    // escrita a cada tique (centenas de linhas por sessao), sem
                    // nenhuma informacao nova. Publicar continua a cada tique --
                    // a UI precisa disso -- mas o log so acontece na TRANSICAO
                    // de disponibilidade, e no caso normal permanece amostrado.
                    bool semSensor = double.IsNaN(metrics.CpuTemperature) && double.IsNaN(metrics.GpuTemperature);

                    if (_ultimoEstadoSemSensor != semSensor)
                    {
                        _ultimoEstadoSemSensor = semSensor;
                        _contadorLogDetalhado = 0;

                        _logger.Log(LogLevel.Info, LogCategory.General,
                            semSensor
                                ? "[GlobalThermal] Sensores de temperatura indisponíveis neste hardware. " +
                                  "Publicando NaN para que a interface informe 'Não foi possível verificar'. " +
                                  "A partir de agora isto só é registrado se o estado mudar."
                                : $"[GlobalThermal] ✅ Sensores responders: " +
                                  $"CPU={FormatTemp(metrics.CpuTemperature, metrics.IsCpuTemperatureEstimated)}, " +
                                  $"GPU={FormatTemp(metrics.GpuTemperature, metrics.IsGpuTemperatureEstimated)}");
                    }
                    else if (!semSensor && ++_contadorLogDetalhado >= 30)
                    {
                        // Amostragem de 1 a cada 30 publicacoes (~1min) quando ha sensor.
                        _contadorLogDetalhado = 0;
                        _logger.Log(LogLevel.Info, LogCategory.General,
                            $"[GlobalThermal] MetricsUpdated: CPU={FormatTemp(metrics.CpuTemperature, metrics.IsCpuTemperatureEstimated)}, " +
                            $"GPU={FormatTemp(metrics.GpuTemperature, metrics.IsGpuTemperatureEstimated)}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[GlobalThermal] ❌ Erro no OnCacheMetricsUpdated: {ex.Message}");
            }
        }

        /// <summary>
        /// Compara temperaturas tratando NaN corretamente (NaN != NaN faria o
        /// "não mudou" falhar sempre).
        /// </summary>
        private static bool TemperaturaMudou(double anterior, double atual)
        {
            if (double.IsNaN(anterior) && double.IsNaN(atual)) return false;
            if (double.IsNaN(anterior) || double.IsNaN(atual)) return true;
            return Math.Abs(anterior - atual) >= 0.1;
        }

        private static string FormatTemp(double value, bool estimada) =>
            double.IsNaN(value) ? "N/D" : $"{value:F1}°C{(estimada ? " (est.)" : "")}";

        private async Task<ThermalMetrics> CollectThermalMetricsAsync()
        {
            var sysCache = VoltrisOptimizer.Core.SystemMetricsCache.Instance;

            // Quando LibreHardwareMonitor já mostrou que todos os sensores são NaN,
            // pula o ForcePoll caro (percorre árvore COM inteira) e vai direto para WMI ACPI.
            // A cada 30s tenta ForcePoll novamente para detectar se o driver voltou a funcionar.
            double cpuTemp = double.NaN;
            double gpuTemp = double.NaN;
            bool cpuTempIsReal = false;
            bool gpuTempIsReal = false;

            _cyclesSinceLastForcePoll++;
            bool shouldForcePoll = !_libreHwAllNan || _cyclesSinceLastForcePoll >= 30;

            if (shouldForcePoll)
            {
                _cyclesSinceLastForcePoll = 0;
                _logger.LogDebug("[GlobalThermal] ForcePoll via TelemetryHub...");
                var hubMetrics = await _telemetryHub.ForcePollAsync();
                cpuTemp = hubMetrics.CpuTemperature;
                gpuTemp = hubMetrics.GpuTemperature;
                cpuTempIsReal = !double.IsNaN(cpuTemp) && cpuTemp > 0;
                gpuTempIsReal = !double.IsNaN(gpuTemp) && gpuTemp > 0;

                _logger.LogDebug($"[GlobalThermal] ForcePoll result: CPU={cpuTemp} (real={cpuTempIsReal}), GPU={gpuTemp} (real={gpuTempIsReal})");

                if (!cpuTempIsReal && !gpuTempIsReal)
                {
                    _libreHwNanStreak++;
                    if (_libreHwNanStreak == 1)
                        _logger.LogInfo("[GlobalThermal] LibreHardwareMonitor retornou NaN. Iniciando fallback chain...");
                    if (_libreHwNanStreak >= 5)
                    {
                        _libreHwAllNan = true;
                        _logger.LogInfo("[GlobalThermal] Todos os sensores NaN por 5 ciclos consecutivos. Pulando ForcePoll até próxima tentativa em 30 ciclos.");
                    }
                }
                else
                {
                    if (_libreHwAllNan)
                        _logger.LogSuccess("[GlobalThermal] LibreHardwareMonitor sensors recovered! Voltando a usar dados reais.");
                    _libreHwAllNan = false;
                    _libreHwNanStreak = 0;
                }
            }

            var metrics = new ThermalMetrics
            {
                Timestamp = DateTime.UtcNow,
                CpuTemperature = cpuTempIsReal ? cpuTemp : double.NaN,
                GpuTemperature = gpuTempIsReal ? gpuTemp : double.NaN,
                GpuUsage = sysCache.GpuUsagePercent,
                IsCpuTemperatureEstimated = !cpuTempIsReal,
                IsGpuTemperatureEstimated = !gpuTempIsReal,
                CpuUsage = sysCache.CpuPercent > 0 ? sysCache.CpuPercent : 0,
                RamUsagePercent = sysCache.MemoryUsedPercent > 0 ? sysCache.MemoryUsedPercent : 0
            };

            // Fallback 1: WMI ACPI (lento ~500-1500ms, lock global)
            if (!cpuTempIsReal)
            {
                double wmiTemp = await TryReadAcpiThermalZoneTempAsync();
                if (!double.IsNaN(wmiTemp) && wmiTemp > 0)
                {
                    _logger.LogInfo($"[GlobalThermal] Fallback CPU temp: ACPI WMI → {wmiTemp}°C");
                    metrics.CpuTemperature = wmiTemp;
                    metrics.IsCpuTemperatureEstimated = false;
                    cpuTempIsReal = true;
                }
                else
                {
                    _logger.LogDebug("[GlobalThermal] Fallback ACPI WMI: temperatura não disponível (NaN ou zero).");
                }
            }

            // Fallback 2: PerformanceCounter "Thermal Zone Information\Temperature" (INSTANTÂNEO ~μs)
            if (!cpuTempIsReal)
            {
                double pcTemp = TryReadThermalZonePerformanceCounter();
                if (!double.IsNaN(pcTemp) && pcTemp > 0)
                {
                    _logger.LogInfo($"[GlobalThermal] Fallback CPU temp: PerfCounter → {pcTemp}°C");
                    metrics.CpuTemperature = pcTemp;
                    metrics.IsCpuTemperatureEstimated = false;
                    cpuTempIsReal = true;
                }
                else
                {
                    _logger.LogDebug("[GlobalThermal] Fallback Thermal Zone PerfCounter: temperatura não disponível (NaN ou zero).");
                }
            }

            // ── Sem sensor real: aplica ESTIMATIVA rotulada. ──
            // Histórico desta parte: a versão original estimava por carga
            // (35 + (uso/100)^1.25*55) e em último caso gravava a CONSTANTE
            // 45.0, número plausível e indistinguível de uma medição — que
            // alimentava o score de saúde e a página de diagnóstico.
            //
            // Agora, por requisito do usuário, a temperatura aparece sempre:
            // se não há sensor, usa-se o estimador por TDP + razão de throttle
            // (ThermalEstimateService), SEMPRE com o flag de estimativa ligado.
            // O invariante de segurança abaixo garante que uma estimativa
            // nunca afirme throttling térmico.
            if (!cpuTempIsReal)
            {
                var cpuEstimate = VoltrisOptimizer.Services.Hardware.ThermalEstimateService
                    .EstimateCpu(metrics.CpuUsage);

                metrics.CpuTemperature = cpuEstimate.Celsius;
                metrics.IsCpuTemperatureEstimated = true;

                if (!_estimativaCpuRegistrada)
                {
                    _estimativaCpuRegistrada = true;
                    _logger.LogInfo(
                        "[GlobalThermal] Nenhuma fonte real de CPU respondeu (LibreHardwareMonitor, " +
                        "ACPI WMI e PerfCounter). Usando ESTIMATIVA por TDP+throttle, rotulada como " +
                        $"estimada e isolada das decisões de segurança. Estimativa inicial: {cpuEstimate.Celsius:F1}°C.");
                }
            }
            else
            {
                _estimativaCpuRegistrada = false;
            }

            if (!gpuTempIsReal)
            {
                var gpuEstimate = VoltrisOptimizer.Services.Hardware.ThermalEstimateService
                    .EstimateGpu(metrics.GpuUsage, metrics.CpuTemperature, metrics.CpuUsage);

                metrics.GpuTemperature = gpuEstimate.Celsius;
                metrics.IsGpuTemperatureEstimated = true;

                if (!_estimativaGpuRegistrada)
                {
                    _estimativaGpuRegistrada = true;
                    _logger.LogInfo(
                        "[GlobalThermal] Nenhuma API de fabricante de GPU respondeu (NVML/ADL) e não há " +
                        $"sensor. Usando ESTIMATIVA rotulada: {gpuEstimate.Celsius:F1}°C.");
                }
            }
            else
            {
                _estimativaGpuRegistrada = false;
            }

            // SEGURANÇA — INVARIANTE INEGOCIÁVEL
            // Throttling térmico é uma ação de SEGURANÇA e só pode ser afirmada
            // a partir de uma MEDIÇÃO REAL de sensor acima de 95 °C. Uma
            // estimativa pode errar vários graus e, num sistema realmente
            // quente, o valor subestimado desligaria justamente a proteção que
            // deveria ter disparado.
            metrics.CpuThrottling = cpuTempIsReal && metrics.CpuTemperature > 95.0;
            metrics.GpuThrottling = gpuTempIsReal && metrics.GpuTemperature > 95.0;

            return metrics;
        }

        /// <summary>
        /// Tenta ler temperatura real via WMI ACPI (MSAcpi_ThermalZoneTemperature).
        /// Retorna temperatura em °C, ou double.NaN se falhar.
        /// </summary>
        private static async Task<double> TryReadAcpiThermalZoneTempAsync()
        {
            try
            {
                var results = await WmiHelper.QuerySafeAsync(
                    "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature",
                    @"root\WMI",
                    TimeSpan.FromSeconds(3));

                var result = results.FirstOrDefault();
                if (result != null)
                {
                    var val = result["CurrentTemperature"];
                    if (val == null) return double.NaN;
                    double kelvinTimes10 = Convert.ToDouble(val);
                    double celsius = (kelvinTimes10 / 10.0) - 273.15;
                    if (celsius > 0 && celsius < 120)
                        return Math.Round(celsius, 1);
                }
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogDebug($"[GlobalThermal] WMI ACPI temp read failed: {ex.Message}");
            }
            return double.NaN;
        }

        /// <summary>
        /// Lê temperatura via PerformanceCounter "Thermal Zone Information\Temperature".
        /// É INSTANTÂNEO (μs), diferente do WMI ACPI que leva 500-1500ms.
        /// Retorna temperatura em °C, ou double.NaN se falhar.
        /// </summary>
        private static double TryReadThermalZonePerformanceCounter()
        {
            try
            {
                if (!PerformanceCounterCategory.Exists("Thermal Zone Information"))
                    return double.NaN;

                var pcc = new PerformanceCounterCategory("Thermal Zone Information");
                string[] instances = pcc.GetInstanceNames();
                foreach (string instance in instances)
                {
                    try
                    {
                        using var pc = new PerformanceCounter("Thermal Zone Information", "Temperature", instance);
                        float val = pc.NextValue();
                        // PerformanceCounter retorna Kelvin*10 (mesmo formato do WMI ACPI)
                        double celsius = (val / 10.0) - 273.15;
                        if (celsius > 0 && celsius < 120)
                        {
                            // Segunda leitura para garantir valor estabilizado
                            float val2 = pc.NextValue();
                            double celsius2 = (val2 / 10.0) - 273.15;
                            if (celsius2 > 0 && celsius2 < 120)
                                return Math.Round(celsius2, 1);
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return double.NaN;
        }

        /// <summary>
        /// REMOVIDO POR AUDITORIA: <c>EstimateCpuTemperature</c>.
        ///
        /// Este método produzia <c>35 + (uso/100)^1.25 * 55</c> — uma curva
        /// INVENTADA, sem qualquer base térmica. O resultado era um número plausível
        /// e, portanto, indistinguível de uma medição real: aparecia como 35 °C em
        /// repouso e cruzava 70/75/82/88 °C conforme a carga, acionando alertas de
        /// "temperatura alta" em máquinas com refrigeração normal.
        ///
        /// Além disso, seu <c>catch</c> devolvia 45.0 — mais uma constante.
        ///
        /// A ausência de sensor agora é propagada como <c>double.NaN</c> com
        /// <c>IsCpuTemperatureEstimated = true</c>, e a interface informa
        /// "Não foi possível verificar". Um número inventado é pior do que
        /// informação ausente, porque não pode ser distinguido da verdade.
        /// </summary>

        private async Task AnalyzeAndAlertAsync(ThermalMetrics metrics, CancellationToken cancellationToken)
        {
            if (DateTime.Now < _alertsSuppressedUntil) return;
            if ((DateTime.Now - _lastAlertTime).TotalMinutes < AlertCooldownMinutes) return;

            var activeProfile = _settingsService.Settings.IntelligentProfile;
            var thresholds = ThermalThresholds.GetForProfile(activeProfile);

            bool alertNeeded = false;
            string component = "";
            double temp = 0;
            ThermalAlertLevel level = ThermalAlertLevel.Warning;

            if (metrics.CpuTemperature > thresholds.CpuCriticalThreshold) { 
                alertNeeded = true; component = "CPU"; temp = metrics.CpuTemperature; level = ThermalAlertLevel.Critical; 
            }

            if (alertNeeded)
            {
                _lastAlertTime = DateTime.Now;
                var alert = new ThermalAlert 
                { 
                    Level = level, 
                    Component = component, 
                    Temperature = temp,
                    Message = $"Temperatura {component} elevada: {temp:F0}°C",
                    Recommendation = level == ThermalAlertLevel.Critical 
                        ? "Verifique refrigeração e feche apps pesados" 
                        : "Verifique ventilação do sistema"
                };
                
                AlertGenerated?.Invoke(this, alert);
                _logger.LogWarning($"[GlobalThermal] Alerta: {alert.Message}");
            }
        }
        
        public void SuppressAlertsFor(TimeSpan duration) => _alertsSuppressedUntil = DateTime.Now.Add(duration);

        public void Dispose()
        {
            if (_settingsService != null) _settingsService.ProfileChanged -= OnProfileChanged;

            try
            {
                if (_subscribedToCache)
                {
                    VoltrisOptimizer.Core.SystemMetricsCache.Instance.MetricsUpdated -= OnCacheMetricsUpdated;
                    _subscribedToCache = false;
                }
                _telemetryHub?.StopMonitoringAsync().Wait(1000);
            }
            catch { }
        }
    }
}
