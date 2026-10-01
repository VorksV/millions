using System;
using System.Collections.Generic;
using System.Linq;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.StreamHub.Interfaces;
using VoltrisOptimizer.Services.StreamHub.Models;

namespace VoltrisOptimizer.Services.StreamHub.Implementation
{
    /// <summary>
    /// Monitor de saúde da stream com detecção inteligente de problemas.
    /// Analisa métricas do OBS e gera alertas com sugestões de ação.
    /// </summary>
    public class StreamHealthMonitor : IStreamHealthMonitor
    {
        private readonly ILoggingService _logger;
        private readonly List<StreamAlert> _activeAlerts = new();
        private readonly object _lock = new();
        private StreamMetrics _lastMetrics = new();
        private int _consecutiveBitrateDrops;
        private int _consecutiveFrameDrops;
        private DateTime _lastMicCheckTime = DateTime.Now;

        // Thresholds configuráveis
        public int BitrateDropThresholdKbps { get; set; } = 2000;
        public double DroppedFrameAlertPercent { get; set; } = 2.0;
        public double CpuAlertPercent { get; set; } = 85.0;
        public int MicInactiveAlertSeconds { get; set; } = 30;

        public event EventHandler<StreamAlert>? AlertRaised;
        public event EventHandler<StreamAlert>? AlertResolved;

        public IReadOnlyList<StreamAlert> ActiveAlerts
        {
            get { lock (_lock) return _activeAlerts.AsReadOnly(); }
        }

        public int HealthScore => _lastMetrics.HealthScore;

        public StreamHealthMonitor(ILoggingService logger)
        {
            _logger = logger;
            _logger.Log(LogLevel.Debug, LogCategory.General, "[HealthMonitor] Instanciado", source: "StreamHub");
        }

        public void UpdateMetrics(StreamMetrics metrics)
        {
            _lastMetrics = metrics;

            if (!metrics.IsLive) return;

            CheckBitrate(metrics);
            CheckDroppedFrames(metrics);
            CheckMicrophone(metrics);
            CheckCpuUsage(metrics);
        }

        private void CheckBitrate(StreamMetrics metrics)
        {
            bool isBitrateLow = metrics.BitrateKbps > 0 && metrics.BitrateKbps < BitrateDropThresholdKbps;

            if (isBitrateLow)
            {
                _consecutiveBitrateDrops++;
                if (_consecutiveBitrateDrops >= 3) // 3 leituras consecutivas = ~6s
                {
                    RaiseOrUpdateAlert(new StreamAlert
                    {
                        Type = AlertType.BitrateDropped,
                        Severity = metrics.BitrateKbps < 500 ? AlertSeverity.Critical : AlertSeverity.Warning,
                        Title = "Bitrate Baixo Detectado",
                        Message = $"Bitrate atual: {metrics.BitrateKbps} kbps (mínimo: {BitrateDropThresholdKbps} kbps)",
                        SuggestedAction = "Verifique sua conexão de internet. Considere reduzir a qualidade de stream temporariamente."
                    });
                }
            }
            else
            {
                if (_consecutiveBitrateDrops > 0)
                {
                    _consecutiveBitrateDrops = 0;
                    ResolveAlert(AlertType.BitrateDropped);
                }
            }
        }

        private void CheckDroppedFrames(StreamMetrics metrics)
        {
            bool hasDroppedFrames = metrics.DroppedFramePercent >= DroppedFrameAlertPercent;

            if (hasDroppedFrames)
            {
                _consecutiveFrameDrops++;
                if (_consecutiveFrameDrops >= 2)
                {
                    RaiseOrUpdateAlert(new StreamAlert
                    {
                        Type = AlertType.DroppedFrames,
                        Severity = metrics.DroppedFramePercent > 10 ? AlertSeverity.Critical : AlertSeverity.Warning,
                        Title = "Frames Perdidos",
                        Message = $"{metrics.DroppedFramePercent:F1}% de frames perdidos ({metrics.DroppedFrames} frames)",
                        SuggestedAction = "Reduza a resolução ou FPS da stream. Feche aplicativos pesados em background."
                    });
                }
            }
            else
            {
                if (_consecutiveFrameDrops > 0)
                {
                    _consecutiveFrameDrops = 0;
                    ResolveAlert(AlertType.DroppedFrames);
                }
            }
        }

        private void CheckMicrophone(StreamMetrics metrics)
        {
            if (!metrics.IsMicrophoneActive)
            {
                var elapsed = (DateTime.Now - _lastMicCheckTime).TotalSeconds;
                if (elapsed >= MicInactiveAlertSeconds)
                {
                    RaiseOrUpdateAlert(new StreamAlert
                    {
                        Type = AlertType.MicrophoneMuted,
                        Severity = AlertSeverity.Warning,
                        Title = "Microfone Mudo/Inativo",
                        Message = $"Microfone está mudo há {(int)elapsed}s. Sua audiência não está te ouvindo!",
                        SuggestedAction = "Verifique se o microfone está mutado no OBS ou no hardware."
                    });
                }
            }
            else
            {
                _lastMicCheckTime = DateTime.Now;
                ResolveAlert(AlertType.MicrophoneMuted);
            }
        }

        private void CheckCpuUsage(StreamMetrics metrics)
        {
            if (metrics.CpuUsagePercent >= CpuAlertPercent)
            {
                RaiseOrUpdateAlert(new StreamAlert
                {
                    Type = AlertType.HighCpuUsage,
                    Severity = metrics.CpuUsagePercent >= 95 ? AlertSeverity.Critical : AlertSeverity.Warning,
                    Title = "CPU Sobrecarregada",
                    Message = $"CPU em {metrics.CpuUsagePercent:F0}%. Risco de travamento da stream.",
                    SuggestedAction = "Feche aplicativos desnecessários. Reduza as configurações de encoding no OBS."
                });
            }
            else
            {
                ResolveAlert(AlertType.HighCpuUsage);
            }
        }

        private void RaiseOrUpdateAlert(StreamAlert alert)
        {
            lock (_lock)
            {
                var existing = _activeAlerts.FirstOrDefault(a => a.Type == alert.Type);
                if (existing != null)
                {
                    // Atualizar mensagem sem criar novo alerta
                    existing.Message = alert.Message;
                    existing.Severity = alert.Severity;
                    return;
                }

                _activeAlerts.Add(alert);
                _logger.Log(
                    alert.Severity == AlertSeverity.Critical ? LogLevel.Critical : LogLevel.Warning,
                    LogCategory.General,
                    $"[HealthMonitor] ALERTA [{alert.Severity}] {alert.Title}: {alert.Message}",
                    source: "StreamHub");
            }

            AlertRaised?.Invoke(this, alert);
        }

        private void ResolveAlert(AlertType type)
        {
            StreamAlert? resolved = null;
            lock (_lock)
            {
                resolved = _activeAlerts.FirstOrDefault(a => a.Type == type);
                if (resolved != null)
                {
                    _activeAlerts.Remove(resolved);
                    _logger.Log(LogLevel.Info, LogCategory.General,
                        $"[HealthMonitor] Alerta resolvido: {type}", source: "StreamHub");
                }
            }

            if (resolved != null)
                AlertResolved?.Invoke(this, resolved);
        }

        public void AcknowledgeAlert(string alertId)
        {
            lock (_lock)
            {
                var alert = _activeAlerts.FirstOrDefault(a => a.Id == alertId);
                if (alert != null)
                {
                    alert.IsAcknowledged = true;
                    _logger.Log(LogLevel.Info, LogCategory.General,
                        $"[HealthMonitor] Alerta reconhecido: {alertId}", source: "StreamHub");
                }
            }
        }

        public void ClearAll()
        {
            lock (_lock)
            {
                _activeAlerts.Clear();
                _logger.Log(LogLevel.Info, LogCategory.General, "[HealthMonitor] Todos os alertas limpos", source: "StreamHub");
            }
        }
    }
}
