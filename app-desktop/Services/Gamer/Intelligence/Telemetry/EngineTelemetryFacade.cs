using System;
using System.Collections.Generic;
using System.Linq;
using VoltrisOptimizer.Services.Gamer.Intelligence.Models;

namespace VoltrisOptimizer.Services.Gamer.Intelligence.Telemetry
{
    public interface IEngineTelemetryFacade
    {
        EngineInsight GetLatestInsight();
        void UpdateState(SystemState state);
        void UpdateClassification(GameClassificationInsight classification);
        void RecordDecision(ActiveDecisionInsight decision);
        void StartSession(string gameName);
        EngineSessionReport EndSession();
        void RecordMetric(string name, double value);
        
        // NOVO: Evento para notificar observers em tempo real
        event EventHandler<EngineInsight>? InsightUpdated;
    }

    public class EngineTelemetryFacade : IEngineTelemetryFacade
    {
        private EngineInsight _currentInsight = new();
        private EngineSessionReport? _currentSession;
        private DateTime _sessionStartTime;
        private readonly List<double> _tempHistory = new();
        private readonly List<double> _fpsHistory = new();
        private readonly object _lock = new();
        
        // 🔥 CORREÇÃO: Adicionar Instance estático
        private static EngineTelemetryFacade? _instance;
        public static EngineTelemetryFacade Instance => _instance ??= new EngineTelemetryFacade();
        
        // NOVO: Evento para notificar observers em tempo real
        public event EventHandler<EngineInsight>? InsightUpdated;

        public EngineInsight GetLatestInsight()
        {
            lock (_lock)
            {
                return _currentInsight;
            }
        }

        public void UpdateState(SystemState state)
        {
            lock (_lock)
            {
                _currentInsight.State = state;
                _tempHistory.Add(state.CpuTemp);
                UpdateStability();
                
                // NOVO: Notificar observers
                NotifyObservers();
            }
        }

        public void UpdateClassification(GameClassificationInsight classification)
        {
            lock (_lock)
            {
                _currentInsight.Classification = classification;
                
                // NOVO: Notificar observers
                NotifyObservers();
            }
        }

        public void RecordDecision(ActiveDecisionInsight decision)
        {
            lock (_lock)
            {
                _currentInsight.Decision = decision;
                _currentInsight.RecentDecisions.Insert(0, $"{DateTime.Now:HH:mm:ss}: {decision.Action} ({decision.Reason})");
                if (_currentInsight.RecentDecisions.Count > 5) _currentInsight.RecentDecisions.RemoveAt(5);

                if (_currentSession != null)
                {
                    if (decision.Action.Contains("PL1")) _currentSession.Pl1Adjustments++;
                    if (decision.Action.Contains("EPP")) _currentSession.EppAdjustments++;
                }
                
                // NOVO: Notificar observers
                NotifyObservers();
            }
        }
        
        // NOVO: Método para notificar observers
        private void NotifyObservers()
        {
            try
            {
                InsightUpdated?.Invoke(this, _currentInsight);
            }
            catch
            {
                // Silently ignore observer errors
            }
        }

        public void StartSession(string gameName)
        {
            lock (_lock)
            {
                _currentSession = new EngineSessionReport { GameName = gameName };
                _sessionStartTime = DateTime.Now;
                _tempHistory.Clear();
                _fpsHistory.Clear();
            }
        }

        public EngineSessionReport EndSession()
        {
            lock (_lock)
            {
                if (_currentSession == null) return new EngineSessionReport();

                _currentSession.Duration = DateTime.Now - _sessionStartTime;
                if (_tempHistory.Any())
                {
                    _currentSession.MaxTemp = _tempHistory.Max();
                    _currentSession.AvgTemp = _tempHistory.Average();
                }
                _currentSession.FinalClassification = _currentInsight.Classification.Type;
                
                var report = _currentSession;
                _currentSession = null;
                return report;
            }
        }

        public void RecordMetric(string name, double value)
        {
            lock (_lock)
            {
                if (name == "FPS") _fpsHistory.Add(value);
                if (name == "AffinityChange") _currentSession!.AffinityChanges++;
                if (name == "ThermalThrottle") _currentSession!.ThermalThrottleEvents++;
            }
        }

        private void UpdateStability()
        {
            if (_currentInsight.Decision.Action.Contains("Emergency"))
                _currentInsight.Stability = StabilityStatus.EmergencyThrottle;
            else if (_currentInsight.State.CpuTemp > 93 || _currentInsight.Decision.Action.Contains("Throttle"))
                _currentInsight.Stability = StabilityStatus.Throttling;
            else if (_currentInsight.Decision.Action.Contains("Thermal Mitigation"))
                _currentInsight.Stability = StabilityStatus.ThermalProtection;
            else if (_currentInsight.Decision.Action != "Monitoring" && _currentInsight.Decision.Action != "Routine")
                _currentInsight.Stability = StabilityStatus.Adapting;
            else
                _currentInsight.Stability = StabilityStatus.Stable;
        }
    }
}
