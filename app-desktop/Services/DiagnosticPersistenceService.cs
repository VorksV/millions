using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VoltrisOptimizer.Services
{
    /// <summary>
    /// Modelo de incidente de diagnóstico
    /// </summary>
    public class DiagnosticIncident
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public DateTime Timestamp { get; set; }
        public GameDiagnosticsService.DiagnosticCause Cause { get; set; } = GameDiagnosticsService.DiagnosticCause.Undefined;
        public string Severity { get; set; } = "Low"; // Low, Medium, High, Critical
        public Dictionary<string, double> Metrics { get; set; } = new();
        public string Description { get; set; } = "";
        public bool Resolved { get; set; } = false;
    }

    /// <summary>
    /// Modelo de sessão de diagnóstico
    /// </summary>
    public class DiagnosticSession
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public string? GameName { get; set; }
        public List<GameDiagnosticsService.Sample> Samples { get; set; } = new();
        public List<DiagnosticIncident> Incidents { get; set; } = new();
        
        public TimeSpan Duration => (EndTime ?? DateTime.UtcNow) - StartTime;
        public double AverageFps => Samples.Count > 0 ? Samples.Average(s => s.Fps) : 0;
        public double AverageCpu => Samples.Count > 0 ? Samples.Average(s => s.CpuPercent) : 0;
        public double AverageGpu => Samples.Count > 0 ? Samples.Where(s => s.GpuUtilPercent >= 0).DefaultIfEmpty().Average(s => s.GpuUtilPercent) : 0;
    }

    /// <summary>
    /// Serviço de persistência de diagnósticos (SaaS-ready)
    /// </summary>
    public class DiagnosticPersistenceService
    {
        private readonly string _dataDirectory;
        private readonly object _lock = new object();
        private DiagnosticSession? _currentSession;
        private readonly List<DiagnosticIncident> _incidents = new();
        
        public event Action<DiagnosticIncident>? IncidentDetected;
        public event Action<DiagnosticSession>? SessionSaved;
        
        public DiagnosticPersistenceService()
        {
            _dataDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Voltris", "Diagnostics");
            Directory.CreateDirectory(_dataDirectory);
        }
        
        /// <summary>
        /// Inicia uma nova sessão de diagnóstico
        /// </summary>
        public void StartSession(string? gameName = null)
        {
            lock (_lock)
            {
                // Salvar sessão anterior se existir
                if (_currentSession != null)
                {
                    EndSession();
                }
                
                _currentSession = new DiagnosticSession
                {
                    StartTime = DateTime.UtcNow,
                    GameName = gameName
                };
                
                App.LoggingService?.LogInfo($"[DiagnosticPersistence] Nova sessão iniciada: {_currentSession.Id}");
            }
        }
        
        /// <summary>
        /// Adiciona um sample à sessão atual
        /// </summary>
        public void AddSample(GameDiagnosticsService.Sample sample)
        {
            lock (_lock)
            {
                if (_currentSession == null) return;
                
                _currentSession.Samples.Add(sample);
                
                // Limitar a 600 samples (últimos 7.5 minutos a 750ms/sample)
                if (_currentSession.Samples.Count > 600)
                {
                    _currentSession.Samples.RemoveAt(0);
                }
                
                // Detectar incidentes
                DetectAndRecordIncident(sample);
            }
        }
        
        /// <summary>
        /// Detecta e registra incidentes baseado em samples
        /// </summary>
        private void DetectAndRecordIncident(GameDiagnosticsService.Sample sample)
        {
            if (sample.Cause == GameDiagnosticsService.DiagnosticCause.Undefined) return;
            
            // Verificar se já existe incidente similar nos últimos 30 segundos
            var recentIncident = _incidents
                .Where(i => !i.Resolved && i.Cause == sample.Cause)
                .Where(i => (DateTime.UtcNow - i.Timestamp).TotalSeconds < 30)
                .FirstOrDefault();
            
            if (recentIncident != null)
            {
                // Atualizar métricas do incidente existente
                recentIncident.Metrics["CpuPercent"] = sample.CpuPercent;
                recentIncident.Metrics["GpuUtilPercent"] = sample.GpuUtilPercent;
                recentIncident.Metrics["RamUsedGb"] = sample.RamUsedGb;
                return;
            }
            
            // Criar novo incidente
            var severity = DetermineSeverity(sample);
            var incident = new DiagnosticIncident
            {
                Timestamp = sample.T,
                Cause = sample.Cause,
                Severity = severity,
                Description = GenerateDescription(sample),
                Metrics = new Dictionary<string, double>
                {
                    { "CpuPercent", sample.CpuPercent },
                    { "CpuQueue", sample.CpuQueue },
                    { "CpuTemperature", sample.CpuTemperature },
                    { "GpuUtilPercent", sample.GpuUtilPercent },
                    { "GpuTemperature", sample.GpuTemperature },
                    { "RamUsedGb", sample.RamUsedGb },
                    { "RamTotalGb", sample.RamTotalGb },
                    { "DiskQueueLen", sample.DiskQueueLen },
                    { "Fps", sample.Fps },
                    { "CpuDpcPercent", sample.CpuDpcPercent },
                    { "CpuInterruptPercent", sample.CpuInterruptPercent },
                    { "DiskLatencySec", sample.DiskLatencySec },
                    { "CpuThrottling", sample.CpuThrottling ? 1.0 : 0.0 },
                    { "GpuThrottling", sample.GpuThrottling ? 1.0 : 0.0 },
                    { "CpuCurrentMhz", sample.CpuCurrentMhz },
                    { "CpuMaxMhz", sample.CpuMaxMhz }
                }
            };
            
            _incidents.Add(incident);
            _currentSession?.Incidents.Add(incident);
            
            // Limitar a 100 incidentes na memória
            if (_incidents.Count > 100)
            {
                _incidents.RemoveAt(0);
            }
            
            App.LoggingService?.LogWarning($"[DiagnosticPersistence] Incidente detectado: {incident.Cause} ({incident.Severity})");
            IncidentDetected?.Invoke(incident);
        }
        
                private string DetermineSeverity(GameDiagnosticsService.Sample sample)
        {
            // CORREÇÃO: a versão anterior classificava como "Critical" quando
            // `sample.Fps < 20`. Mas FPS == 0 significa que o monitor ETW não
            // está medindo (sem jogo em foreground, ou counters indisponíveis),
            // e não que o jogo está a 0 FPS. O resultado era um incidente CRÍTICO
            // garantido a cada janela de amostragem, com o sistema perfeitamente saudável.
            //
            // Agora, severidade só sobe com FONTES DISPONÍVEIS.
            bool fpsAvailable = sample.Fps > 0;
            bool thermalAvailable = (!double.IsNaN(sample.CpuTemperature) && sample.CpuTemperature > 0) ||
                                    (!double.IsNaN(sample.GpuTemperature) && sample.GpuTemperature > 0);

            // Critical: throttling térmico CONFIRMADO por temperatura real, ou FPS medido abaixo de 20.
            if ((sample.CpuThrottling && thermalAvailable) || (sample.GpuThrottling && thermalAvailable))
                return "Critical";
            if (fpsAvailable && sample.Fps < 20)
                return "Critical";

            // High
            if (sample.CpuPercent > 95 || sample.GpuUtilPercent > 95 ||
                (sample.RamTotalGb > 0 && (sample.RamUsedGb / sample.RamTotalGb) > 0.98) || sample.DiskLatencySec > 2)
                return "High";

            // Medium
            if (sample.CpuPercent > 85 || sample.GpuUtilPercent > 85 ||
                (sample.RamTotalGb > 0 && (sample.RamUsedGb / sample.RamTotalGb) > 0.94))
                return "Medium";

            return "Low";
        }        
                private string GenerateDescription(GameDiagnosticsService.Sample sample)
        {
            return sample.Cause switch
            {
                GameDiagnosticsService.DiagnosticCause.CpuScheduling =>
                    $"CPU saturada ({sample.CpuPercent:F1}%), fila: {sample.CpuQueue:F1}",
                GameDiagnosticsService.DiagnosticCause.DriversDpcInterrupt =>
                    $"DPC: {sample.CpuDpcPercent:F1}%, Interrupt: {sample.CpuInterruptPercent:F1}%",
                GameDiagnosticsService.DiagnosticCause.MemoryPaging =>
                    $"RAM: {sample.RamUsedGb:F1}/{sample.RamTotalGb:F1} GB ({(sample.RamUsedGb / sample.RamTotalGb * 100):F1}%)",
                GameDiagnosticsService.DiagnosticCause.DiskIo =>
                    $"Fila: {sample.DiskQueueLen:F2}, Latência: {sample.DiskLatencySec * 1000:F1}ms",
                GameDiagnosticsService.DiagnosticCause.GpuRender =>
                    $"GPU saturada ({sample.GpuUtilPercent:F1}%)",
                GameDiagnosticsService.DiagnosticCause.ThermalThrottling =>
                    (sample.CpuThrottling || sample.GpuThrottling) && !double.IsNaN(sample.CpuTemperature)
                        ? $"Throttling térmico - CPU: {sample.CpuTemperature:F1}°C, GPU: {(double.IsNaN(sample.GpuTemperature) ? "N/D" : $"{sample.GpuTemperature:F1}°C")}"
                        : $"Frequência reduzida: {sample.CpuCurrentMhz:F0}/{sample.CpuMaxMhz:F0} MHz",
                _ => GameDiagnosticsService.LocalizeCauseKey(sample.Cause)
            };
        }        
        /// <summary>
        /// Finaliza a sessão atual e salva em disco
        /// </summary>
        public void EndSession()
        {
            lock (_lock)
            {
                if (_currentSession == null) return;
                
                _currentSession.EndTime = DateTime.UtcNow;
                
                try
                {
                    SaveSession(_currentSession);
                    App.LoggingService?.LogSuccess($"[DiagnosticPersistence] Sessão salva: {_currentSession.Id} ({_currentSession.Samples.Count} samples, {_currentSession.Incidents.Count} incidentes)");
                    SessionSaved?.Invoke(_currentSession);
                }
                catch (Exception ex)
                {
                    App.LoggingService?.LogError($"[DiagnosticPersistence] Erro ao salvar sessão: {ex.Message}", ex);
                }
                finally
                {
                    _currentSession = null;
                }
            }
        }
        
        /// <summary>
        /// Salva sessão em disco
        /// </summary>
        private void SaveSession(DiagnosticSession session)
        {
            var fileName = $"session_{session.StartTime:yyyyMMdd_HHmmss}_{session.Id}.json";
            var filePath = Path.Combine(_dataDirectory, fileName);
            
            var json = JsonSerializer.Serialize(session, new JsonSerializerOptions { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,  
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
            
            File.WriteAllText(filePath, json, System.Text.Encoding.UTF8);
            
            // Limpar sessões antigas (manter últimas 30 dias)
            CleanupOldSessions();
        }
        
        /// <summary>
        /// Remove sessões com mais de 30 dias
        /// </summary>
        private void CleanupOldSessions()
        {
            try
            {
                var cutoffDate = DateTime.UtcNow.AddDays(-30);
                var files = Directory.GetFiles(_dataDirectory, "session_*.json");
                
                foreach (var file in files)
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.CreationTimeUtc < cutoffDate)
                    {
                        File.Delete(file);
                        App.LoggingService?.LogInfo($"[DiagnosticPersistence] Sessão antiga removida: {Path.GetFileName(file)}");
                    }
                }
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogWarning($"[DiagnosticPersistence] Erro ao limpar sessões antigas: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Carrega sessões salvas
        /// </summary>
        public List<DiagnosticSession> LoadSessions(int maxCount = 10)
        {
            var sessions = new List<DiagnosticSession>();
            
            try
            {
                var files = Directory.GetFiles(_dataDirectory, "session_*.json")
                    .OrderByDescending(f => new FileInfo(f).CreationTimeUtc)
                    .Take(maxCount);
                
                foreach (var file in files)
                {
                    try
                    {
                        var json = File.ReadAllText(file, System.Text.Encoding.UTF8);
                        var session = JsonSerializer.Deserialize<DiagnosticSession>(json, new JsonSerializerOptions {   ReferenceHandler = ReferenceHandler.IgnoreCycles });
                        if (session != null)
                        {
                            sessions.Add(session);
                        }
                    }
                    catch (Exception ex)
                    {
                        App.LoggingService?.LogWarning($"[DiagnosticPersistence] Erro ao carregar sessão {Path.GetFileName(file)}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[DiagnosticPersistence] Erro ao carregar sessões: {ex.Message}", ex);
            }
            
            return sessions;
        }
        
        /// <summary>
        /// Obtém incidentes ativos (não resolvidos)
        /// </summary>
        public List<DiagnosticIncident> GetActiveIncidents()
        {
            lock (_lock)
            {
                return _incidents.Where(i => !i.Resolved).ToList();
            }
        }
        
        /// <summary>
        /// Marca incidente como resolvido
        /// </summary>
        public void ResolveIncident(string incidentId)
        {
            lock (_lock)
            {
                var incident = _incidents.FirstOrDefault(i => i.Id == incidentId);
                if (incident != null)
                {
                    incident.Resolved = true;
                    App.LoggingService?.LogInfo($"[DiagnosticPersistence] Incidente resolvido: {incidentId}");
                }
            }
        }
        
        /// <summary>
        /// Limpa todos os incidentes
        /// </summary>
        public void ClearIncidents()
        {
            lock (_lock)
            {
                _incidents.Clear();
                App.LoggingService?.LogInfo("[DiagnosticPersistence] Incidentes limpos");
            }
        }
    }
}
