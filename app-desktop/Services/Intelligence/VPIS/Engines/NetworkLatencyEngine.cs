using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using VoltrisOptimizer.Services.Intelligence.VPIS.Models;

namespace VoltrisOptimizer.Services.Intelligence.VPIS.Engines
{
    public class NetworkLatencyEngine : IVpisDiagnosticEngine
    {
        public string EngineName => "Network Latency Forensics Engine";

        private string _targetProcess;
        private bool _isMonitoring;
        private int _consecutiveHighPingTicks;
        private long _lastPing;
        private int _tickCounter;

        private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(5);

        public void StartMonitoring(string processName)
        {
            _targetProcess = processName;
            _consecutiveHighPingTicks = 0;
            _tickCounter = 0;
            _isMonitoring = true;
        }

        public void StopMonitoring()
        {
            _isMonitoring = false;
        }

        public PerformanceInsightEvent AnalyzeTick()
        {
            if (!_isMonitoring) return null;

            _tickCounter++;

            if (_tickCounter % 5 != 0)
                return null;

            try
            {
                using var pinger = new Ping();
                var reply = pinger.Send("8.8.8.8", 2000);

                if (reply.Status == IPStatus.Success)
                {
                    _lastPing = reply.RoundtripTime;

                    if (_lastPing >= 120)
                    {
                        _consecutiveHighPingTicks++;
                    }
                    else
                    {
                        if (_consecutiveHighPingTicks > 0)
                            _consecutiveHighPingTicks--;
                    }
                }
                else
                {
                    _consecutiveHighPingTicks += 2;
                }
            }
            catch
            {
                _consecutiveHighPingTicks++;
            }

            if (_consecutiveHighPingTicks >= 10)
            {
                _consecutiveHighPingTicks = 0;
                long recPing = _lastPing;

                return new PerformanceInsightEvent
                {
                    ProcessName = _targetProcess,
                    Category = EventCategory.Network,
                    Severity = DiagnosticSeverity.Warning,
                    Confidence = ConfidenceLevel.High,
                    ConfidenceScore = 85,
                    Timestamp = DateTime.UtcNow,
                    Evidences = new List<string>
                    {
                        $"Latência de rede elevada: ~{recPing} ms",
                        $"Instabilidade de rota detectada"
                    },
                    Diagnosis = "Latência de Rede Elevada",
                    Recommendations = new List<string>
                    {
                        "use conexão Ethernet no lugar de Wi-Fi",
                        "feche downloads em segundo plano (Steam, Torrents)",
                        "reinicie o roteador se o problema persistir"
                    }
                };
            }

            return null;
        }
    }
}
