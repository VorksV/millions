using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services.Performance.Models;

namespace VoltrisOptimizer.Services.Performance
{
    /// <summary>
    /// Armazena e recupera estado de performance com validação de integridade
    /// CORREÇÃO: RISCO #3 - Persistência sem validação de integridade
    /// </summary>
    public class PerformanceStateStore
    {
        private readonly ILoggingService _logger;
        private const int CURRENT_VERSION = 1;
        private readonly string _stateFilePath;
        private readonly string _tempFilePath;

        public PerformanceStateStore(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VoltrisOptimizer"
            );

            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _stateFilePath = Path.Combine(directory, "performance_state.json");
            _tempFilePath = Path.Combine(directory, "performance_state.tmp");
        }

        /// <summary>
        /// Persiste estado com checksum e escrita atômica
        /// </summary>
        public bool PersistState(PerformanceStateSnapshot snapshot)
        {
            try
            {
                var wrapper = new PersistedStateWrapper
                {
                    Version = CURRENT_VERSION,
                    Timestamp = DateTime.UtcNow,
                    Snapshot = snapshot
                };

                // Serializar
                var json = JsonSerializer.Serialize(wrapper, new JsonSerializerOptions { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,  
                    WriteIndented = true 
                });

                // Calcular checksum
                wrapper.Checksum = ComputeChecksum(json);

                // Re-serializar com checksum
                json = JsonSerializer.Serialize(wrapper, new JsonSerializerOptions {
                    WriteIndented = true,
                    ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles
                });

                // Escrita atômica: escrever em temp, depois renomear
                File.WriteAllText(_tempFilePath, json, Encoding.UTF8);

                // Renomear (operação atômica no Windows)
                if (File.Exists(_stateFilePath))
                {
                    File.Delete(_stateFilePath);
                }
                File.Move(_tempFilePath, _stateFilePath);

                _logger.LogDebug($"[StateStore] Estado persistido com sucesso: {_stateFilePath}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("[StateStore] Erro ao persistir estado", ex);
                
                // Limpar arquivo temporário se existir
                try
                {
                    if (File.Exists(_tempFilePath))
                    {
                        File.Delete(_tempFilePath);
                    }
                }
                catch { }

                return false;
            }
        }

        /// <summary>
        /// Carrega estado com validação de integridade
        /// </summary>
        public PerformanceStateSnapshot? LoadState()
        {
            try
            {
                if (!File.Exists(_stateFilePath))
                {
                    _logger.LogDebug("[StateStore] Arquivo de estado não existe");
                    return null;
                }

                var json = File.ReadAllText(_stateFilePath, Encoding.UTF8);

                // Deserializar wrapper
                var wrapper = JsonSerializer.Deserialize<PersistedStateWrapper>(json);
                if (wrapper == null)
                {
                    _logger.LogError("[StateStore] Falha ao deserializar estado");
                    return null;
                }

                // Validar versão
                if (wrapper.Version != CURRENT_VERSION)
                {
                    _logger.LogWarning($"[StateStore] Versão incompatível: {wrapper.Version} (esperado: {CURRENT_VERSION})");
                    return null;
                }

                // Validar checksum
                var storedChecksum = wrapper.Checksum;
                wrapper.Checksum = string.Empty; // Zerar para recalcular

                var jsonForValidation = JsonSerializer.Serialize(wrapper, new JsonSerializerOptions {
                    WriteIndented = true,
                    ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles
                });
                var calculatedChecksum = ComputeChecksum(jsonForValidation);

                if (storedChecksum != calculatedChecksum)
                {
                    _logger.LogError("[StateStore] Checksum inválido - estado corrompido");
                    _logger.LogError($"[StateStore] Esperado: {storedChecksum}, Calculado: {calculatedChecksum}");
                    return null;
                }

                // Validar snapshot
                if (!ValidateSnapshot(wrapper.Snapshot))
                {
                    _logger.LogError("[StateStore] Snapshot inválido");
                    return null;
                }

                _logger.LogInfo($"[StateStore] Estado carregado com sucesso (idade: {(DateTime.UtcNow - wrapper.Timestamp).TotalMinutes:F1} min)");
                return wrapper.Snapshot;
            }
            catch (Exception ex)
            {
                _logger.LogError("[StateStore] Erro ao carregar estado", ex);
                return null;
            }
        }

        /// <summary>
        /// Remove arquivo de estado
        /// </summary>
        public void ClearState()
        {
            try
            {
                if (File.Exists(_stateFilePath))
                {
                    File.Delete(_stateFilePath);
                    _logger.LogDebug("[StateStore] Estado removido");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[StateStore] Erro ao remover estado: {ex.Message}");
            }
        }

        private bool ValidateSnapshot(PerformanceStateSnapshot? snapshot)
        {
            if (snapshot == null)
            {
                _logger.LogError("[StateStore] Snapshot é null");
                return false;
            }

            // Validar GUID
            if (snapshot.ActivePowerScheme == Guid.Empty)
            {
                _logger.LogError("[StateStore] ActivePowerScheme é Guid.Empty");
                return false;
            }

            // Validar configurações
            if (snapshot.PowerSettings == null || snapshot.PowerSettings.Count == 0)
            {
                _logger.LogError("[StateStore] PowerSettings vazio");
                return false;
            }

            // Validar ranges de valores (0-100 para percentuais)
            foreach (var kvp in snapshot.PowerSettings)
            {
                if (kvp.Value > 100 && !kvp.Key.Contains("Threshold"))
                {
                    _logger.LogWarning($"[StateStore] Valor suspeito para {kvp.Key}: {kvp.Value}");
                }
            }

            // Validar timestamp (não pode ser muito antigo - máx 7 dias)
            var age = DateTime.UtcNow - snapshot.CapturedAt;
            if (age.TotalDays > 7)
            {
                _logger.LogWarning($"[StateStore] Estado muito antigo: {age.TotalDays:F1} dias");
                return false;
            }

            return true;
        }

        private string ComputeChecksum(string content)
        {
            using (var sha256 = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(content);
                var hash = sha256.ComputeHash(bytes);
                return Convert.ToBase64String(hash);
            }
        }
    }

    /// <summary>
    /// Wrapper para persistência com metadados
    /// </summary>
    internal class PersistedStateWrapper
    {
        public int Version { get; set; }
        public DateTime Timestamp { get; set; }
        public string Checksum { get; set; } = string.Empty;
        public PerformanceStateSnapshot? Snapshot { get; set; }
    }
}
