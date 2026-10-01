using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services.Gamer.Implementation
{
    /// <summary>
    /// Gerenciador de estado do Windows Shell
    /// Responsável por salvar e restaurar configurações da taskbar e shell
    /// Implementa persistência em disco para proteção contra crashes
    /// </summary>
    public class ShellStateManager
    {
        private readonly ILoggingService _logger;
        private readonly string _statePath;
        private readonly JsonSerializerOptions _jsonOptions;

        public ShellStateManager(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogDebug($"[ShellState] >>> ENTER .ctor");
            _statePath = AppDataPaths.ShellState;
            AppDataPaths.EnsureDirectory(Path.GetDirectoryName(_statePath));
            _jsonOptions = new JsonSerializerOptions { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,  WriteIndented = true };
            _logger.LogDebug($"[ShellState] <<< EXIT .ctor (statePath: {_statePath})");
        }

        /// <summary>
        /// Salva o estado atual da taskbar em disco
        /// </summary>
        public async Task SaveStateAsync(TaskbarState state)
        {
            if (state == null)
            {
                _logger.LogWarning("[ShellState] Estado nulo, nada a salvar");
                return;
            }

            try
            {
                _logger.LogInfo("[ShellState] 💾 Salvando estado da taskbar em disco...");
                
                var stateData = new ShellStateData
                {
                    TaskbarState = state,
                    SavedAt = DateTime.Now,
                    Version = "1.0"
                };
                
                // Estratégia de salvamento seguro: TMP -> BAK -> JSON
                string tempPath = _statePath + ".tmp";
                string backupPath = _statePath + ".bak";
                
                // 1. Escrever em arquivo temporário
                var json = JsonSerializer.Serialize(stateData, _jsonOptions);
                await File.WriteAllTextAsync(tempPath, json);
                
                // 2. Backup do arquivo atual (se existir)
                if (File.Exists(_statePath))
                {
                    if (File.Exists(backupPath))
                        File.Delete(backupPath);
                    File.Move(_statePath, backupPath);
                }
                
                // 3. Promover arquivo temporário
                File.Move(tempPath, _statePath);
                
                _logger.LogSuccess($"[ShellState] ✅ Estado salvo com sucesso em: {Path.GetFileName(_statePath)}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ShellState] Erro ao salvar estado: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Carrega o estado salvo da taskbar
        /// </summary>
        public async Task<TaskbarState?> LoadStateAsync()
        {
            string backupPath = _statePath + ".bak";
            
            try
            {
                // Tentar carregar do arquivo principal
                if (File.Exists(_statePath))
                {
                    _logger.LogInfo("[ShellState] 📂 Carregando estado salvo da taskbar...");
                    var json = await File.ReadAllTextAsync(_statePath);
                    var stateData = JsonSerializer.Deserialize<ShellStateData>(json, _jsonOptions);
                    
                    if (stateData?.TaskbarState != null)
                    {
                        _logger.LogSuccess($"[ShellState] ✅ Estado carregado (salvo em: {stateData.SavedAt})");
                        return stateData.TaskbarState;
                    }
                }
                
                // Fallback: tentar carregar do backup
                if (File.Exists(backupPath))
                {
                    _logger.LogWarning("[ShellState] Arquivo principal não encontrado, tentando backup...");
                    var json = await File.ReadAllTextAsync(backupPath);
                    var stateData = JsonSerializer.Deserialize<ShellStateData>(json, _jsonOptions);
                    
                    if (stateData?.TaskbarState != null)
                    {
                        _logger.LogSuccess($"[ShellState] ✅ Estado carregado do backup");
                        return stateData.TaskbarState;
                    }
                }
                
                _logger.LogInfo("[ShellState] Nenhum estado salvo encontrado");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ShellState] Erro ao carregar estado: {ex.Message}", ex);
                return null;
            }
        }

        /// <summary>
        /// Verifica se existe um estado salvo
        /// </summary>
        public bool HasSavedState()
        {
            _logger.LogDebug($"[ShellState] >>> ENTER HasSavedState");
            var result = File.Exists(_statePath) || File.Exists(_statePath + ".bak");
            _logger.LogDebug($"[ShellState] <<< EXIT HasSavedState (Result: {result})");
            return result;
        }

        /// <summary>
        /// Limpa o estado salvo
        /// </summary>
        public void ClearState()
        {
            try
            {
                _logger.LogInfo("[ShellState] 🗑️ Limpando estado salvo...");
                
                if (File.Exists(_statePath))
                    File.Delete(_statePath);
                
                if (File.Exists(_statePath + ".bak"))
                    File.Delete(_statePath + ".bak");
                
                if (File.Exists(_statePath + ".tmp"))
                    File.Delete(_statePath + ".tmp");
                
                _logger.LogSuccess("[ShellState] ✅ Estado limpo com sucesso");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[ShellState] Erro ao limpar estado: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Estrutura de dados para persistência do estado
    /// </summary>
    internal class ShellStateData
    {
        public TaskbarState? TaskbarState { get; set; }
        public DateTime SavedAt { get; set; }
        public string Version { get; set; } = "1.0";
    }
}
