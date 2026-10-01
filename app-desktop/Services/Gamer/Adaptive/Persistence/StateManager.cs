using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services.Gamer.Adaptive.Models;
using VoltrisOptimizer.Services.Gamer.Adaptive.Execution;

namespace VoltrisOptimizer.Services.Gamer.Adaptive.Persistence
{
    /// <summary>
    /// Gerenciador de estado persistente para sessões do Gamer Mode
    /// </summary>
    public class StateManager
    {
        private readonly ILoggingService _logger;
        private readonly string _stateDirectory;
        private readonly string _activeSessionFile;
        
        public StateManager(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            _stateDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Voltris", "GamerMode", "State"
            );
            
            _activeSessionFile = Path.Combine(_stateDirectory, "active_session.json");
            
            // Ensure directory exists
            Directory.CreateDirectory(_stateDirectory);
        }
        
        /// <summary>
        /// Salva estado da sessão ativa
        /// </summary>
        public async Task SaveActiveSessionAsync(ExecutionPlan plan, GamerSessionState state)
        {
            try
            {
                var sessionData = new ActiveSessionData
                {
                    SessionId = plan.SessionId,
                    StartedAt = DateTime.UtcNow,
                    ExecutionPlan = plan,
                    State = state,
                    SavedAt = DateTime.UtcNow
                };
                
                var json = JsonSerializer.Serialize(sessionData, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                
                await File.WriteAllTextAsync(_activeSessionFile, json);
                
                _logger.LogDebug($"[StateManager] Estado da sessão salvo: {plan.SessionId}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[StateManager] Erro ao salvar estado da sessão: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Carrega estado da sessão ativa (se existir)
        /// </summary>
        public async Task<ActiveSessionData?> LoadActiveSessionAsync()
        {
            try
            {
                if (!File.Exists(_activeSessionFile))
                    return null;
                
                var json = await File.ReadAllTextAsync(_activeSessionFile);
                var sessionData = JsonSerializer.Deserialize<ActiveSessionData>(json, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                
                if (sessionData != null)
                {
                    _logger.LogDebug($"[StateManager] Estado da sessão carregado: {sessionData.SessionId}");
                }
                
                return sessionData;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[StateManager] Erro ao carregar estado da sessão: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Remove estado da sessão ativa (após finalização normal)
        /// </summary>
        public async Task ClearActiveSessionAsync()
        {
            try
            {
                if (File.Exists(_activeSessionFile))
                {
                    File.Delete(_activeSessionFile);
                    _logger.LogDebug("[StateManager] Estado da sessão ativa removido");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[StateManager] Erro ao limpar estado da sessão: {ex.Message}");
            }
            
            await Task.CompletedTask;
        }
        
        /// <summary>
        /// Salva histórico da sessão para análise
        /// </summary>
        public async Task SaveSessionHistoryAsync(ExecutionPlan plan, GamerSessionState finalState, TimeSpan duration)
        {
            try
            {
                var historyFile = Path.Combine(_stateDirectory, $"session_{plan.SessionId}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
                
                var historyData = new SessionHistoryData
                {
                    SessionId = plan.SessionId,
                    GameName = plan.Game.Name,
                    StartedAt = DateTime.UtcNow - duration,
                    Duration = duration,
                    ExecutionPlan = plan,
                    FinalState = finalState,
                    HardwareTier = plan.Hardware.OverallTier,
                    AppliedOptimizations = plan.ApplyCount,
                    SkippedOptimizations = plan.SkipCount,
                    ArchivedAt = DateTime.UtcNow
                };
                
                var json = JsonSerializer.Serialize(historyData, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                
                await File.WriteAllTextAsync(historyFile, json);
                
                _logger.LogInfo($"[StateManager] Histórico da sessão salvo: {plan.SessionId}");
                
                // Cleanup old history files (keep last 30)
                await CleanupOldHistoryAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[StateManager] Erro ao salvar histórico da sessão: {ex.Message}");
            }
        }
        
        private async Task CleanupOldHistoryAsync()
        {
            try
            {
                var historyFiles = Directory.GetFiles(_stateDirectory, "session_*.json")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.CreationTime)
                    .ToArray();
                
                if (historyFiles.Length > 30)
                {
                    var filesToDelete = historyFiles.Skip(30);
                    foreach (var file in filesToDelete)
                    {
                        file.Delete();
                    }
                    
                    _logger.LogDebug($"[StateManager] {filesToDelete.Count()} arquivos de histórico antigos removidos");
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[StateManager] Erro na limpeza de histórico: {ex.Message}");
            }
            
            await Task.CompletedTask;
        }
    }
    
    /// <summary>
    /// Dados da sessão ativa (para recovery)
    /// </summary>
    public class ActiveSessionData
    {
        public string SessionId { get; set; } = string.Empty;
        public DateTime StartedAt { get; set; }
        public ExecutionPlan ExecutionPlan { get; set; } = new();
        public GamerSessionState State { get; set; } = new();
        public DateTime SavedAt { get; set; }
    }
    
    /// <summary>
    /// Dados do histórico de sessão
    /// </summary>
    public class SessionHistoryData
    {
        public string SessionId { get; set; } = string.Empty;
        public string GameName { get; set; } = string.Empty;
        public DateTime StartedAt { get; set; }
        public TimeSpan Duration { get; set; }
        public ExecutionPlan ExecutionPlan { get; set; } = new();
        public GamerSessionState FinalState { get; set; } = new();
        public HardwareTier HardwareTier { get; set; }
        public int AppliedOptimizations { get; set; }
        public int SkippedOptimizations { get; set; }
        public DateTime ArchivedAt { get; set; }
    }
    
    /// <summary>
    /// Estado da sessão gamer
    /// </summary>
    public class GamerSessionState
    {
        public bool IsActive { get; set; }
        public DateTime? StartedAt { get; set; }
        public Dictionary<OptimizationType, OptimizationResult> AppliedOptimizations { get; set; } = new();
        public Dictionary<string, object> Metrics { get; set; } = new();
    }
}