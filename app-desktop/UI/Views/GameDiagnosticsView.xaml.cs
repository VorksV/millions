using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Telemetry;
using MediaColor = System.Windows.Media.Color;
using AppLanguage = VoltrisOptimizer.Services.Language;

namespace VoltrisOptimizer.UI.Views
{
    public partial class GameDiagnosticsView : UserControl
    {
        private readonly GameDiagnosticsService _svc;
        private double _lastCpuTemp = 0;
        private double _lastGpuTemp = 0;
        private bool _cpuTempEstimated;
        private bool _gpuTempEstimated;

        public GameDiagnosticsView()
        {
            InitializeComponent();
            _svc = (App.Services?.GetService(typeof(GameDiagnosticsService)) as GameDiagnosticsService) ?? new GameDiagnosticsService();
            Loaded += GameDiagnosticsView_Loaded;
            Unloaded += GameDiagnosticsView_Unloaded;
        }

        private void GameDiagnosticsView_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _svc.SamplesUpdated += OnSamples;
                _svc.Start();
                
                // CORREÇÃO: Subscrever ao GlobalThermalMonitorService para temperaturas em tempo real
                if (App.ThermalMonitorService != null)
                {
                    App.ThermalMonitorService.MetricsUpdated += OnThermalMetricsUpdated;
                    App.LoggingService?.LogInfo("[GameDiagnosticsView] Subscrito ao GlobalThermalMonitorService");
                }
                else
                {
                    App.LoggingService?.LogWarning("[GameDiagnosticsView] ThermalMonitorService não disponível");
                }
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[GameDiagnosticsView] Erro ao carregar: {ex.Message}");
            }
        }

        private void GameDiagnosticsView_Unloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _svc.SamplesUpdated -= OnSamples;
                _svc.Stop();
                
                // CORREÇÃO: Desinscrever do GlobalThermalMonitorService
                if (App.ThermalMonitorService != null)
                {
                    App.ThermalMonitorService.MetricsUpdated -= OnThermalMetricsUpdated;
                }
            }
            catch { }
        }
        
        /// <summary>
        /// Callback para atualização de temperaturas em tempo real do GlobalThermalMonitorService
        /// </summary>
        private void OnThermalMetricsUpdated(object? sender, VoltrisOptimizer.Services.Thermal.Models.ThermalMetrics metrics)
        {
            try
            {
                // CORREÇÃO DE AUDITORIA:
                //  1. `double.IsNaN(t) ? 0 : t` converte "sem sensor" em 0 °C, que é
                //     fisicamente impossível e parecia uma medição válida.
                //  2. Os flags IsCpuTemperatureEstimated / IsGpuTemperatureEstimated
                //     eram recebidos e DESCARTADOS. A página exibia a mesma
                //     aparência para um valor real e para um valor estimado.
                //  3. As temperaturas nunca eram zeradas quando o evento parava de
                //     chegar, deixando um valor congelado na tela sem aviso.
                //
                // Agora NaN significa "indisponível" e é exibido como tal, com o
                // sufixo de estimativa quando aplicável.
                _lastCpuTemp = metrics.CpuTemperature;
                _lastGpuTemp = metrics.GpuTemperature;
                _cpuTempEstimated = metrics.IsCpuTemperatureEstimated;
                _gpuTempEstimated = metrics.IsGpuTemperatureEstimated;

                // Atualizar UI na thread principal
                Dispatcher.BeginInvoke(() =>
                {
                    UpdateTemperatureUI(_lastCpuTemp, _lastGpuTemp,
                        metrics.CpuThrottling, metrics.GpuThrottling,
                        _cpuTempEstimated, _gpuTempEstimated);
                });
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[GameDiagnosticsView] Erro ao processar métricas térmicas: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Atualiza a UI de temperatura com as métricas mais recentes.
        /// Temperatura NaN = sem sensor real: exibe "N/D" e coloração neutra,
        /// em vez de um número inventado.
        /// </summary>
        private void UpdateTemperatureUI(double cpuTemp, double gpuTemp,
            bool cpuThrottling, bool gpuThrottling,
            bool cpuEstimated, bool gpuEstimated)
        {
            try
            {
                string na = LocalizationService.Instance.GetString("GameDiagNotAvailable");
                bool cpuOk = !double.IsNaN(cpuTemp) && cpuTemp > 0;
                bool gpuOk = !double.IsNaN(gpuTemp) && gpuTemp > 0;

                // Status geral: só afirma "normal" quando existe leitura real.
                string overallThermalStatus;
                if (cpuThrottling || gpuThrottling)
                {
                    overallThermalStatus = LocalizationService.Instance.GetString("GameDiagThrottleActive");
                }
                else if (!cpuOk && !gpuOk)
                {
                    overallThermalStatus = LocalizationService.Instance.GetString("GameDiagThermalNotAvailable");
                }
                else
                {
                    overallThermalStatus = LocalizationService.Instance.GetString("GameDiagNormal");
                }

                if (TemperatureInfoText != null) TemperatureInfoText.Text = string.Format(LocalizationService.Instance.GetString("GameDiagStatusFormat"), overallThermalStatus);

                var neutral = new SolidColorBrush(Color.FromRgb(148, 163, 184));

                if (CpuTempText != null)
                {
                    if (cpuOk)
                    {
                        string suffix = cpuEstimated ? " ~" : "";
                        CpuTempText.Text = string.Format(LocalizationService.Instance.GetString("GameDiagCpuTempFormat"), $"{cpuTemp:F0}°C{suffix}");
                        CpuTempText.Foreground = cpuTemp >= 85
                            ? new SolidColorBrush(Color.FromRgb(239, 68, 68))
                            : cpuTemp >= 70
                                ? new SolidColorBrush(Color.FromRgb(245, 158, 11))
                                : new SolidColorBrush(Color.FromRgb(34, 197, 94));
                    }
                    else
                    {
                        CpuTempText.Text = string.Format(LocalizationService.Instance.GetString("GameDiagCpuTempFormat"), na);
                        CpuTempText.Foreground = neutral;
                    }
                }

                if (GpuTempText != null)
                {
                    if (gpuOk)
                    {
                        string suffix = gpuEstimated ? " ~" : "";
                        GpuTempText.Text = string.Format(LocalizationService.Instance.GetString("GameDiagGpuTempFormat"), $"{gpuTemp:F0}°C{suffix}");
                        GpuTempText.Foreground = gpuTemp >= 80
                            ? new SolidColorBrush(Color.FromRgb(239, 68, 68))
                            : gpuTemp >= 70
                                ? new SolidColorBrush(Color.FromRgb(245, 158, 11))
                                : new SolidColorBrush(Color.FromRgb(34, 197, 94));
                    }
                    else
                    {
                        GpuTempText.Text = string.Format(LocalizationService.Instance.GetString("GameDiagGpuTempFormat"), na);
                        GpuTempText.Foreground = neutral;
                    }
                }

                if (CpuTempIndicator != null)
                {
                    CpuTempIndicator.Background = !cpuOk
                        ? neutral
                        : cpuTemp >= 85
                            ? new SolidColorBrush(Color.FromRgb(239, 68, 68))
                            : cpuTemp >= 70
                                ? new SolidColorBrush(Color.FromRgb(245, 158, 11))
                                : new SolidColorBrush(Color.FromRgb(34, 197, 94));
                }

                if (GpuTempIndicator != null)
                {
                    GpuTempIndicator.Background = !gpuOk
                        ? neutral
                        : gpuTemp >= 80
                            ? new SolidColorBrush(Color.FromRgb(239, 68, 68))
                            : gpuTemp >= 70
                                ? new SolidColorBrush(Color.FromRgb(245, 158, 11))
                                : new SolidColorBrush(Color.FromRgb(34, 197, 94));
                }
            }
            catch (Exception ex)
            {
                // Antes era `catch { }`: um erro ao pintar a UI era totalmente silencioso.
                App.LoggingService?.LogWarning(
                    $"[GameDiagnosticsView] Erro ao atualizar a UI de temperatura: {ex.Message}");
            }
        }

        private void OnSamples(System.Collections.Generic.IReadOnlyList<GameDiagnosticsService.Sample> samples)
        {
            try
            {
                var last = samples.Count > 0 ? samples[^1] : null;
                if (last != null)
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        // Temperaturas: NaN significa "sem sensor real". Não usar 0 nem
                        // converter para um número plausível — exibir "N/D".
                        string na = LocalizationService.Instance.GetString("GameDiagNotAvailable");
                        double cpuTemp = !double.IsNaN(_lastCpuTemp) && _lastCpuTemp > 0 ? _lastCpuTemp : last.CpuTemperature;
                        double gpuTemp = !double.IsNaN(_lastGpuTemp) && _lastGpuTemp > 0 ? _lastGpuTemp : last.GpuTemperature;
                        bool cpuTempOk = !double.IsNaN(cpuTemp) && cpuTemp > 0;
                        bool gpuTempOk = !double.IsNaN(gpuTemp) && gpuTemp > 0;
                        string cpuTempText = cpuTempOk ? $"{cpuTemp:F0}°C{(_cpuTempEstimated ? "~" : "")}" : na;
                        string gpuTempText = gpuTempOk ? $"{gpuTemp:F0}°C{(_gpuTempEstimated ? "~" : "")}" : na;

                        // ── Métricas de contador ──
                        // Valores NaN significam "contador indisponível". Antes, a
                        // falha de leitura virava 0 e a tela mostrava "DPC 0.0%",
                        // "Lat 0 ms", "PF 0/s" — dados inventados com aparência normal.
                        string Fmt(double v, string suffix, int dec = 1) =>
                            (double.IsNaN(v) || double.IsInfinity(v)) ? na : $"{v.ToString("F" + dec)}{suffix}";

                        string cpuThermalStatus = last.CpuThrottling && cpuTempOk ? " ⚠ THROTTLE" : "";
                        CpuInfoText.Text = $"CPU {last.CpuPercent:F0}% | Q {last.CpuQueue:F1} | {last.CpuCurrentMhz:F0}/{last.CpuMaxMhz:F0} MHz | " +
                                           $"DPC {Fmt(last.CpuDpcPercent, "%")} | INT {Fmt(last.CpuInterruptPercent, "%")} | " +
                                           $"TEMP {cpuTempText}{cpuThermalStatus}";

                        // Atualizar informações da GPU com temperatura
                        string gpuThermalStatus = last.GpuThrottling && gpuTempOk ? " ⚠ THROTTLE" : "";
                        GpuInfoText.Text = last.GpuUtilPercent >= 0 ?
                            $"GPU {last.GpuUtilPercent:F0}% | VRAM {last.GpuVramUsedMb:F0} MB | TEMP {gpuTempText}{gpuThermalStatus}" :
                            LocalizationService.Instance.GetString("GameDiagGpuNoCounters");
                            
                        FpsText.Text = string.Format(LocalizationService.Instance.GetString("GameDiagFpsFormat"), last.Fps > 0 ? $"{last.Fps:F0}" : na);

                        // RAM/Disco também podem ter leitura indisponível.
                        RamInfoText.Text = (double.IsNaN(last.RamUsedGb) || double.IsNaN(last.RamTotalGb))
                            ? $"RAM {na}"
                            : $"RAM {last.RamUsedGb:F1}/{last.RamTotalGb:F1} GB | standby {last.RamStandbyGb:F1} | PF {Fmt(last.RamPageFaultsPerSec, "/s", 0)}";

                        DiskInfoText.Text = $"R {Fmt(last.DiskReadsPerSec, "/s", 0)} - W {Fmt(last.DiskWritesPerSec, "/s", 0)} | " +
                                            $"Q {Fmt(last.DiskQueueLen, "", 2)} | Lat {Fmt(last.DiskLatencySec * 1000, " ms", 0)}";

                        PowerInfoText.Text = last.CpuMaxMhz > 0
                            ? $"Clock {last.CpuCurrentMhz:F0}/{last.CpuMaxMhz:F0} MHz"
                            : $"Clock {na}";
                        
                        // Atualizar UI de temperatura com valores mais recentes
                        UpdateTemperatureUI(cpuTemp, gpuTemp, last.CpuThrottling, last.GpuThrottling, _cpuTempEstimated, _gpuTempEstimated);
                        
                        UpdateTopProcessesUI(last.TopProcesses);
                        
                        CauseText.Text = last.Cause == GameDiagnosticsService.DiagnosticCause.Undefined
                            ? LocalizationService.Instance.GetString("GameDiagScanning")
                            : LocalizeCause(last.Cause);
                        AnalysisText.Text = BuildAnalysis(last);
                        DrawSpark(CpuGraph, samples.Select(s => s.CpuPercent).ToArray());
                        DrawSpark(GpuGraph, samples.Select(s => Math.Max(0, s.GpuUtilPercent)).ToArray());
                        DrawSpark(FpsGraph, samples.Select(s => Math.Max(0, s.Fps)).ToArray());
                        DrawSpark(RamGraph, samples.Select(s => s.RamUsedGb).ToArray());
                        DrawSpark(DiskGraph, samples.Select(s => s.DiskLatencySec * 1000).ToArray());
                        DrawSpark(PowerGraph, samples.Select(s => s.CpuCurrentMhz).ToArray());
                        
                        // Desenhar gráfico de temperatura usando valores mais precisos
                        DrawSpark(TemperatureGraph, samples.Select(s => Math.Max(
                            _lastCpuTemp > 0 ? _lastCpuTemp : s.CpuTemperature, 
                            _lastGpuTemp > 0 ? _lastGpuTemp : s.GpuTemperature
                        )).ToArray());
                        
                        UpdateIncidentsList();
                    });
                }
            }
            catch { }
        }

        private string BuildAnalysis(GameDiagnosticsService.Sample s)
        {
            try
            {
                if (s.Cause == GameDiagnosticsService.DiagnosticCause.CpuScheduling) return LocalizationService.Instance.GetString("GameDiagCpuScheduling");
                if (s.Cause == GameDiagnosticsService.DiagnosticCause.DriversDpcInterrupt) return LocalizationService.Instance.GetString("GameDiagDriversDpc");
                if (s.Cause == GameDiagnosticsService.DiagnosticCause.MemoryPaging) return LocalizationService.Instance.GetString("GameDiagMemoryPaging");
                if (s.Cause == GameDiagnosticsService.DiagnosticCause.DiskIo) return LocalizationService.Instance.GetString("GameDiagDiskIo");
                if (s.Cause == GameDiagnosticsService.DiagnosticCause.GpuRender) return LocalizationService.Instance.GetString("GameDiagGpuRender");
                if (s.Cause == GameDiagnosticsService.DiagnosticCause.ThermalThrottling) 
                {
                    if (s.CpuThrottling || s.GpuThrottling)
                    {
                        string thermalMsg = LocalizationService.Instance.GetString("GameDiagThermalThrottle");
                        if (s.CpuThrottling) thermalMsg += string.Format(LocalizationService.Instance.GetString("GameDiagThermalCpu"), s.CpuTemperature);
                        if (s.GpuThrottling) thermalMsg += string.Format(LocalizationService.Instance.GetString("GameDiagThermalGpu"), s.GpuTemperature);
                        thermalMsg += LocalizationService.Instance.GetString("GameDiagThermalCleanup");
                        return thermalMsg;
                    }
                    return LocalizationService.Instance.GetString("GameDiagPowerLimit");
                }
            }
            catch { }
            return LocalizationService.Instance.GetString("GameDiagStable");
        }

        private void UpdateTopProcessesUI(System.Collections.Generic.List<GameDiagnosticsService.ProcessInfo> processes)
        {
            if (processes == null || TopProcessesList == null) return;
            
            App.LoggingService?.LogInfo($"[GameDiagnosticsView] Atualizando TopProcesses com {processes.Count} processos:");
            foreach (var p in processes)
            {
                string memType = p.IsPrivateMemory ? "PRIV" : "WS";
                App.LoggingService?.LogInfo($"[GameDiagnosticsView]   {p.Name} (PID={p.Pid}): {p.RamBytes} bytes ({p.RamBytes/1024.0/1024.0:F1} MB) [{memType}]");
            }
            
            long maxRam = processes.Count > 0 ? processes.Max(p => p.RamBytes) : 1;
            if (maxRam <= 0) maxRam = 1;
            
            var items = processes.Select(p => new {
                p.Name,
                DisplayValue = FormatBytes(p.RamBytes),
                Percent = (double)p.RamBytes / maxRam * 100
            }).ToList();
            
            TopProcessesList.ItemsSource = items;
        }

        private string FormatBytes(long bytes)
        {
            if (bytes < 0)
            {
                App.LoggingService?.LogWarning($"[GameDiagnosticsView] FormatBytes recebeu valor negativo: {bytes}");
                return "0 B";
            }
            if (bytes == 0) return "0 B";

            long originalBytes = bytes;
            string[] Suffix = { "B", "KB", "MB", "GB", "TB" };
            int i;
            double dblSByte = bytes;
            for (i = 0; i < Suffix.Length && bytes >= 1024; i++, bytes /= 1024)
            {
                dblSByte = bytes / 1024.0;
            }
            string result = string.Format("{0:0.0} {1}", dblSByte, Suffix[i]);
            App.LoggingService?.LogDebug($"[GameDiagnosticsView] FormatBytes: {originalBytes} bytes -> {result}");
            return result;
        }

        private void DrawSpark(Canvas canvas, double[] values)
        {
            try
            {
                if (canvas == null || values == null || values.Length < 2) return;
                canvas.Children.Clear();
                var w = canvas.ActualWidth > 0 ? canvas.ActualWidth : canvas.Width;
                var h = canvas.ActualHeight > 0 ? canvas.ActualHeight : canvas.Height;
                if (w <= 0 || h <= 0) { w = 300; h = 100; }
                var max = values.Max();
                var min = values.Min();
                if (Math.Abs(max - min) < 1e-6) max = min + 1;
                var pl = new Polyline { Stroke = (Brush)Application.Current.FindResource("AccentBrush"), StrokeThickness = 2 };
                int n = values.Length;
                for (int i = 0; i < n; i++)
                {
                    var x = (w - 4) * i / (double)(n - 1) + 2;
                    var norm = (values[i] - min) / (max - min);
                    var y = h - 2 - norm * (h - 4);
                    pl.Points.Add(new System.Windows.Point(x, y));
                }
                canvas.Children.Add(pl);
            }
            catch { }
        }

        private void UpdateIncidentsList()
        {
            try
            {
                if (IncidentsList == null) return;
                
                var incidents = _svc?.GetActiveIncidents() ?? new System.Collections.Generic.List<DiagnosticIncident>();
                
                if (incidents.Count == 0)
                {
                    IncidentsList.ItemsSource = new[] { LocalizationService.Instance.GetString("GameDiagNoIncidents") };
                    return;
                }
                
                var items = incidents
                    .OrderByDescending(i => i.Timestamp)
                    .Take(20)
                    .Select(i => FormatIncident(i))
                    .ToArray();
                
                IncidentsList.ItemsSource = items;
            }
            catch { }
        }
        
        private string FormatIncident(DiagnosticIncident incident)
        {
            var severityIcon = incident.Severity switch
            {
                "Critical" => "!!",
                "High" => "!",
                "Medium" => "i",
                _ => "."
            };
            
            var time = incident.Timestamp.ToLocalTime().ToString("HH:mm:ss");
            var cause = LocalizeCause(incident.Cause);
            var desc = LocalizeDescription(incident);
            return $"{severityIcon} {time} - {cause} - {desc}";
        }

        /// <summary>
        /// Traduz a categoria de causa raiz para exibição.
        ///
        /// ANTES: recebia uma string JÁ TRADUZIDA e re-traduzia por comparação com
        /// literais em português. Em inglês/espanhol nenhuma comparação batia, e o
        /// método devolvia a entrada como estava. Pior: a string traduzida era usada
        /// como CHAVE DE DICIONÁRIO dentro de <c>Infer()</c>, o que fazia a
        /// pontuação ser sobrescrita e a análise cair sempre no caso padrão.
        ///
        /// AGORA: recebe o enum de domínio e delega ao LocalizationService.
        /// </summary>
        private string LocalizeCause(GameDiagnosticsService.DiagnosticCause cause)
        {
            if (cause == GameDiagnosticsService.DiagnosticCause.Undefined)
                return LocalizationService.Instance.GetString("GameDiagUndefined");

            return LocalizationService.Instance.GetString(
                GameDiagnosticsService.LocalizeCauseKey(cause));
        }

        private string LocalizeDescription(DiagnosticIncident incident)
        {
            var lang = LocalizationService.Instance.CurrentLanguage;
            if (lang == AppLanguage.Portuguese) return incident.Description;

            var cause = incident.Cause;
            var metrics = incident.Metrics;

            if (cause == GameDiagnosticsService.DiagnosticCause.CpuScheduling && metrics != null && metrics.ContainsKey("CpuPercent") && metrics.ContainsKey("CpuQueue"))
            {
                return lang == AppLanguage.English 
                    ? $"CPU saturated ({metrics["CpuPercent"]:F1}%), queue: {metrics["CpuQueue"]:F1}"
                    : $"CPU saturada ({metrics["CpuPercent"]:F1}%), cola: {metrics["CpuQueue"]:F1}";
            }
            if (cause == GameDiagnosticsService.DiagnosticCause.DriversDpcInterrupt && metrics != null && metrics.ContainsKey("CpuDpcPercent") && metrics.ContainsKey("CpuInterruptPercent"))
            {
                return lang == AppLanguage.English
                    ? $"DPC: {metrics["CpuDpcPercent"]:F1}%, Interrupt: {metrics["CpuInterruptPercent"]:F1}%"
                    : $"DPC: {metrics["CpuDpcPercent"]:F1}%, Interrupción: {metrics["CpuInterruptPercent"]:F1}%";
            }
            if (cause == GameDiagnosticsService.DiagnosticCause.MemoryPaging && metrics != null && metrics.ContainsKey("RamUsedGb") && metrics.ContainsKey("RamTotalGb"))
            {
                double total = metrics["RamTotalGb"];
                double pct = total > 0 ? (metrics["RamUsedGb"] / total * 100) : 0;
                return $"RAM: {metrics["RamUsedGb"]:F1}/{total:F1} GB ({pct:F1}%)";
            }
            if (cause == GameDiagnosticsService.DiagnosticCause.DiskIo && metrics != null && metrics.ContainsKey("DiskQueueLen") && metrics.ContainsKey("DiskLatencySec"))
            {
                return lang == AppLanguage.English
                    ? $"Queue: {metrics["DiskQueueLen"]:F2}, Latency: {metrics["DiskLatencySec"]*1000:F1}ms"
                    : $"Cola: {metrics["DiskQueueLen"]:F2}, Latencia: {metrics["DiskLatencySec"]*1000:F1}ms";
            }
            if (cause == GameDiagnosticsService.DiagnosticCause.GpuRender && metrics != null && metrics.ContainsKey("GpuUtilPercent"))
            {
                return lang == AppLanguage.English
                    ? $"GPU saturated ({metrics["GpuUtilPercent"]:F1}%)"
                    : $"GPU saturada ({metrics["GpuUtilPercent"]:F1}%)";
            }
            if (cause == GameDiagnosticsService.DiagnosticCause.ThermalThrottling && metrics != null && metrics.ContainsKey("CpuTemperature") && metrics.ContainsKey("GpuTemperature"))
            {
                bool hasThrottle = (metrics.TryGetValue("CpuThrottling", out var ct) && ct > 0) || 
                                   (metrics.TryGetValue("GpuThrottling", out var gt) && gt > 0);
                if (hasThrottle)
                {
                    return lang == AppLanguage.English
                        ? $"Thermal throttling - CPU: {metrics["CpuTemperature"]:F1}°C, GPU: {metrics["GpuTemperature"]:F1}°C"
                        : $"Estrangulamiento térmico - CPU: {metrics["CpuTemperature"]:F1}°C, GPU: {metrics["GpuTemperature"]:F1}°C";
                }
                else if (metrics.ContainsKey("CpuCurrentMhz") && metrics.ContainsKey("CpuMaxMhz"))
                {
                    return lang == AppLanguage.English
                        ? $"Reduced frequency: {metrics["CpuCurrentMhz"]:F0}/{metrics["CpuMaxMhz"]:F0} MHz"
                        : $"Frecuencia reducida: {metrics["CpuCurrentMhz"]:F0}/{metrics["CpuMaxMhz"]:F0} MHz";
                }
            }

            // Fallback
            return TranslateDescriptionFallback(incident.Description);
        }

        private string TranslateDescriptionFallback(string desc)
        {
            if (string.IsNullOrEmpty(desc)) return desc;
            var lang = LocalizationService.Instance.CurrentLanguage;
            if (lang == AppLanguage.Portuguese) return desc;

            if (desc.StartsWith("CPU saturada"))
            {
                return desc.Replace("CPU saturada", lang == AppLanguage.English ? "CPU saturated" : "CPU saturada")
                           .Replace("fila:", lang == AppLanguage.English ? "queue:" : "cola:");
            }
            if (desc.StartsWith("DPC:"))
            {
                return desc.Replace("Interrupt:", lang == AppLanguage.English ? "Interrupt:" : "Interrupción:");
            }
            if (desc.StartsWith("Fila:"))
            {
                return desc.Replace("Fila:", lang == AppLanguage.English ? "Queue:" : "Cola:")
                           .Replace("Latência:", lang == AppLanguage.English ? "Latency:" : "Latencia:");
            }
            if (desc.StartsWith("GPU saturada"))
            {
                return desc.Replace("GPU saturada", lang == AppLanguage.English ? "GPU saturated" : "GPU saturada");
            }
            if (desc.StartsWith("Throttling térmico"))
            {
                return desc.Replace("Throttling térmico", lang == AppLanguage.English ? "Thermal throttling" : "Estrangulamiento térmico");
            }
            if (desc.StartsWith("Frequência reduzida"))
            {
                return desc.Replace("Frequência reduzida:", lang == AppLanguage.English ? "Reduced frequency:" : "Frecuencia reducida:");
            }
            return desc;
        }

        private void ExportCsvButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dir = LogDirectoryResolver.Resolve();
                System.IO.Directory.CreateDirectory(dir);
                var path = System.IO.Path.Combine(dir, $"diagnostics_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                using var sw = new System.IO.StreamWriter(path);
                sw.WriteLine("time,cpu%,queue,currentMHz,maxMHz,dpc%,interrupt%,cpuTemp,cpuThrottle,gpu%,vramMB,gpuTemp,gpuThrottle,ramUsedGB,ramTotalGB,pageFaults,diskRps,diskWps,diskQueue,diskLatencySec,fps,cause");
                var svc = _svc;
                var samples = typeof(GameDiagnosticsService)
                    .GetField("_samples", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
                    .GetValue(svc) as System.Collections.Generic.List<GameDiagnosticsService.Sample>;
                if (samples != null)
                {
                    foreach (var s in samples)
                    {
                        sw.WriteLine($"{s.T:o},{s.CpuPercent:F2},{s.CpuQueue:F2},{s.CpuCurrentMhz:F0},{s.CpuMaxMhz:F0},{s.CpuDpcPercent:F2},{s.CpuInterruptPercent:F2},{s.CpuTemperature:F1},{s.CpuThrottling},{s.GpuUtilPercent:F2},{s.GpuVramUsedMb:F0},{s.GpuTemperature:F1},{s.GpuThrottling},{s.RamUsedGb:F2},{s.RamTotalGb:F2},{s.RamPageFaultsPerSec:F0},{s.DiskReadsPerSec:F0},{s.DiskWritesPerSec:F0},{s.DiskQueueLen:F2},{s.DiskLatencySec:F4},{s.Fps:F0},{s.Cause}");
                    }
                }
                VoltrisOptimizer.UI.Controls.ModernMessageBox.Show(string.Format(LocalizationService.Instance.GetString("ExportedTo"), path), LocalizationService.Instance.GetString("ExportCSVTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                VoltrisOptimizer.UI.Controls.ModernMessageBox.Show(string.Format(LocalizationService.Instance.GetString("ExportError"), ex.Message), LocalizationService.Instance.GetString("ExportCSVTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
