using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.SystemIntelligenceProfiler
{
    /// <summary>
    /// Base de dados de drivers conhecidos.
    /// </summary>
    public class DriverDatabase
    {
        private readonly Dictionary<string, DriverDatabaseEntry> _driverEntries;
        private readonly string _databaseFilePath;
        private readonly object _lockObject = new object();
        
        private const string DefaultDatabaseFileName = "driver_database.json";

        /// <summary>
        /// Inicializa uma nova instância de <see cref="DriverDatabase"/>.
        /// </summary>
        /// <param name="databaseFilePath">Caminho para o arquivo de banco de dados.</param>
        public DriverDatabase(string databaseFilePath = null)
        {
            _databaseFilePath = databaseFilePath ?? Path.Combine(
                Path.GetDirectoryName(typeof(DriverDatabase).Assembly.Location),
                DefaultDatabaseFileName);
                
            _driverEntries = new Dictionary<string, DriverDatabaseEntry>(StringComparer.OrdinalIgnoreCase);
            
            // Carrega o banco de dados na inicialização
            LoadDatabase();
        }

        /// <summary>
        /// Verifica um driver contra a base de dados.
        /// </summary>
        /// <param name="driverInfo">Informações do driver.</param>
        /// <returns>Resultado da verificação.</returns>
        public async Task<DriverDatabaseCheckResult> CheckDriverAsync(DriverInfo driverInfo)
        {
            if (driverInfo == null)
                throw new ArgumentNullException(nameof(driverInfo));

            // Cria chave única para o driver
            var driverKey = GenerateDriverKey(driverInfo);
            
            DriverDatabaseEntry entry = null;
            lock (_lockObject)
            {
                _driverEntries.TryGetValue(driverKey, out entry);
            }
            
            // Se não encontrou com a chave completa, tenta com nome apenas
            if (entry == null && !string.IsNullOrEmpty(driverInfo.Name))
            {
                lock (_lockObject)
                {
                    _driverEntries.TryGetValue(driverInfo.Name.ToLowerInvariant(), out entry);
                }
            }
            
            if (entry != null)
            {
                return new DriverDatabaseCheckResult
                {
                    IsKnownRisky = entry.IsRisky,
                    RiskLevel = entry.RiskLevel,
                    Details = entry.Description,
                    Recommendation = entry.Recommendation,
                    LastUpdated = entry.LastUpdated
                };
            }
            
            // Se não encontrou na base de dados, retorna resultado negativo
            return new DriverDatabaseCheckResult
            {
                IsKnownRisky = false,
                RiskLevel = DriverRiskCategory.Safe,
                Details = "Driver não encontrado na base de dados",
                Recommendation = null,
                LastUpdated = DateTime.MinValue
            };
        }

        /// <summary>
        /// Adiciona ou atualiza uma entrada na base de dados.
        /// </summary>
        /// <param name="entry">Entrada a ser adicionada ou atualizada.</param>
        /// <returns>Tarefa assíncrona.</returns>
        public async Task AddOrUpdateEntryAsync(DriverDatabaseEntry entry)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));

            if (string.IsNullOrEmpty(entry.DriverName))
                throw new ArgumentException("O nome do driver não pode ser vazio.", nameof(entry.DriverName));

            lock (_lockObject)
            {
                _driverEntries[entry.DriverName.ToLowerInvariant()] = entry;
            }
            
            // Salva as alterações no banco de dados
            await SaveDatabaseAsync();
        }

        /// <summary>
        /// Remove uma entrada da base de dados.
        /// </summary>
        /// <param name="driverName">Nome do driver a ser removido.</param>
        /// <returns>Tarefa assíncrona.</returns>
        public async Task RemoveEntryAsync(string driverName)
        {
            if (string.IsNullOrEmpty(driverName))
                throw new ArgumentException("O nome do driver não pode ser vazio.", nameof(driverName));

            lock (_lockObject)
            {
                _driverEntries.Remove(driverName.ToLowerInvariant());
            }
            
            // Salva as alterações no banco de dados
            await SaveDatabaseAsync();
        }

        /// <summary>
        /// Gera uma chave única para identificar um driver.
        /// </summary>
        /// <param name="driverInfo">Informações do driver.</param>
        /// <returns>Chave única do driver.</returns>
        private string GenerateDriverKey(DriverInfo driverInfo)
        {
            if (driverInfo == null)
                return string.Empty;

            // Combina nome, versão e fabricante para criar uma chave única
            return $"{driverInfo.Name?.ToLowerInvariant() ?? ""}_" +
                   $"{driverInfo.Version?.ToLowerInvariant() ?? ""}_" +
                   $"{driverInfo.Manufacturer?.ToLowerInvariant() ?? ""}";
        }

        /// <summary>
        /// Carrega o banco de dados de drivers.
        /// </summary>
        private void LoadDatabase()
        {
            try
            {
                if (File.Exists(_databaseFilePath))
                {
                    var json = File.ReadAllText(_databaseFilePath);
                    var entries = JsonSerializer.Deserialize<List<DriverDatabaseEntry>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                    
                    lock (_lockObject)
                    {
                        _driverEntries.Clear();
                        foreach (var entry in entries)
                        {
                            if (!string.IsNullOrEmpty(entry.DriverName))
                            {
                                _driverEntries[entry.DriverName.ToLowerInvariant()] = entry;
                            }
                        }
                    }
                }
                else
                {
                    // Cria um banco de dados padrão com alguns drivers conhecidos
                    CreateDefaultDatabase();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erro ao carregar banco de dados de drivers: {ex.Message}");
                
                // Em caso de erro, cria um banco de dados padrão
                CreateDefaultDatabase();
            }
        }

        /// <summary>
        /// Salva o banco de dados de drivers.
        /// </summary>
        /// <returns>Tarefa assíncrona.</returns>
        private async Task SaveDatabaseAsync()
        {
            try
            {
                List<DriverDatabaseEntry> entries;
                lock (_lockObject)
                {
                    entries = new List<DriverDatabaseEntry>(_driverEntries.Values);
                }
                
                var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                
                await File.WriteAllTextAsync(_databaseFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erro ao salvar banco de dados de drivers: {ex.Message}");
            }
        }

        /// <summary>
        /// Cria um banco de dados padrão com drivers conhecidos.
        /// </summary>
        private void CreateDefaultDatabase()
        {
            var defaultEntries = new List<DriverDatabaseEntry>
            {
                new DriverDatabaseEntry
                {
                    DriverName = "nv4_mini.sys",
                    IsRisky = true,
                    RiskLevel = DriverRiskCategory.Dangerous,
                    Description = "Driver antigo da NVIDIA conhecido por causar bluescreens",
                    Recommendation = "Atualizar para o driver oficial mais recente da NVIDIA",
                    LastUpdated = DateTime.UtcNow
                },
                new DriverDatabaseEntry
                {
                    DriverName = "atikmdag.sys",
                    IsRisky = true,
                    RiskLevel = DriverRiskCategory.HighRisk,
                    Description = "Driver da ATI/AMD com problemas de estabilidade em sistemas modernos",
                    Recommendation = "Atualizar para o driver AMD Radeon Software mais recente",
                    LastUpdated = DateTime.UtcNow
                },
                new DriverDatabaseEntry
                {
                    DriverName = "usbhub.sys",
                    IsRisky = false,
                    RiskLevel = DriverRiskCategory.Safe,
                    Description = "Driver legítimo do hub USB da Microsoft",
                    Recommendation = null,
                    LastUpdated = DateTime.UtcNow
                },
                new DriverDatabaseEntry
                {
                    DriverName = "vmmouse.sys",
                    IsRisky = true,
                    RiskLevel = DriverRiskCategory.ModerateRisk,
                    Description = "Driver do mouse VMware que pode causar problemas em sistemas físicos",
                    Recommendation = "Desinstalar se não estiver usando máquina virtual VMware",
                    LastUpdated = DateTime.UtcNow
                }
            };
            
            lock (_lockObject)
            {
                _driverEntries.Clear();
                foreach (var entry in defaultEntries)
                {
                    if (!string.IsNullOrEmpty(entry.DriverName))
                    {
                        _driverEntries[entry.DriverName.ToLowerInvariant()] = entry;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Entrada na base de dados de drivers.
    /// </summary>
    public class DriverDatabaseEntry
    {
        /// <summary>
        /// Nome do driver.
        /// </summary>
        public string DriverName { get; set; }

        /// <summary>
        /// Indica se o driver é considerado de risco.
        /// </summary>
        public bool IsRisky { get; set; }

        /// <summary>
        /// Nível de risco do driver.
        /// </summary>
        public DriverRiskCategory RiskLevel { get; set; }

        /// <summary>
        /// Descrição do problema conhecido.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Recomendação para resolver o problema.
        /// </summary>
        public string Recommendation { get; set; }

        /// <summary>
        /// Data da última atualização desta entrada.
        /// </summary>
        public DateTime LastUpdated { get; set; }
    }

    /// <summary>
    /// Resultado da verificação de um driver na base de dados.
    /// </summary>
    public class DriverDatabaseCheckResult
    {
        /// <summary>
        /// Indica se o driver é conhecido como problemático.
        /// </summary>
        public bool IsKnownRisky { get; set; }

        /// <summary>
        /// Nível de risco do driver.
        /// </summary>
        public DriverRiskCategory RiskLevel { get; set; }

        /// <summary>
        /// Detalhes sobre o driver.
        /// </summary>
        public string Details { get; set; }

        /// <summary>
        /// Recomendação para lidar com o driver.
        /// </summary>
        public string Recommendation { get; set; }

        /// <summary>
        /// Data da última atualização da informação.
        /// </summary>
        public DateTime LastUpdated { get; set; }
    }
}
