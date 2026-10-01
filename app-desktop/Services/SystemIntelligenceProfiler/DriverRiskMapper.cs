using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services.SystemChanges;

namespace VoltrisOptimizer.Services.SystemIntelligenceProfiler
{
    /// <summary>
    /// Implementação do mapeador de riscos de drivers.
    /// </summary>
    public class DriverRiskMapper : IDriverRiskMapper
    {
        private readonly ISystemInfoService _systemInfoService;
        private readonly DriverHeuristics _driverHeuristics;
        private readonly DriverDatabase _driverDatabase;
        
        private CancellationTokenSource _cancellationTokenSource;
        private Task _monitoringTask;
        private readonly object _lockObject = new object();
        
        private bool _isRunning = false;
        private DriverRiskIndex _currentRiskIndex;
        private List<DriverRiskProfile> _lastScanResults;
        
        private const int MonitoringIntervalMs = 30000; // 30 segundos

        /// <summary>
        /// Sensibilidade da detecção de riscos (0.0 a 1.0).
        /// </summary>
        public double Sensitivity { get; set; } = 0.5;

        /// <summary>
        /// Evento disparado quando um driver de risco é encontrado.
        /// </summary>
        public event EventHandler<DriverRiskDetectedEventArgs> DriverRiskDetected;

        /// <summary>
        /// Evento disparado quando o índice de risco do sistema é atualizado.
        /// </summary>
        public event EventHandler<DriverRiskIndexChangedEventArgs> RiskIndexChanged;

        /// <summary>
        /// Inicializa uma nova instância de <see cref="DriverRiskMapper"/>.
        /// </summary>
        /// <param name="systemInfoService">Serviço de informações do sistema.</param>
        /// <param name="driverHeuristics">Heurísticas para análise de drivers.</param>
        /// <param name="driverDatabase">Base de dados de drivers.</param>
        public DriverRiskMapper(
            ISystemInfoService systemInfoService,
            DriverHeuristics driverHeuristics,
            DriverDatabase driverDatabase)
        {
            _systemInfoService = systemInfoService ?? throw new ArgumentNullException(nameof(systemInfoService));
            _driverHeuristics = driverHeuristics ?? throw new ArgumentNullException(nameof(driverHeuristics));
            _driverDatabase = driverDatabase ?? throw new ArgumentNullException(nameof(driverDatabase));
            
            _currentRiskIndex = new DriverRiskIndex
            {
                OverallRisk = 0.0,
                RiskCategory = DriverRiskCategory.Safe,
                TotalDrivers = 0,
                RiskyDrivers = 0
            };
            
            _lastScanResults = new List<DriverRiskProfile>();
        }

        /// <summary>
        /// Inicia a verificação contínua de drivers.
        /// </summary>
        /// <returns>Tarefa assíncrona.</returns>
        public async Task StartMonitoringAsync()
        {
            lock (_lockObject)
            {
                if (_isRunning)
                    return;

                _isRunning = true;
                _cancellationTokenSource = new CancellationTokenSource();
            }

            // Realiza uma verificação inicial
            await PerformDriverScanAsync();

            // Inicia o loop de monitoramento
            _monitoringTask = MonitorDriversAsync(_cancellationTokenSource.Token);
            
            await Task.CompletedTask;
        }

        /// <summary>
        /// Para a verificação contínua de drivers.
        /// </summary>
        /// <returns>Tarefa assíncrona.</returns>
        public async Task StopMonitoringAsync()
        {
            lock (_lockObject)
            {
                if (!_isRunning)
                    return;

                _isRunning = false;
                _cancellationTokenSource?.Cancel();
            }

            // Aguarda o término do loop de monitoramento
            if (_monitoringTask != null)
            {
                try
                {
                    await _monitoringTask;
                }
                catch (OperationCanceledException)
                {
                    // Esperado quando cancelamos a tarefa
                }
            }
            
            await Task.CompletedTask;
        }

        /// <summary>
        /// Realiza uma verificação pontual de todos os drivers.
        /// </summary>
        /// <returns>Lista de perfis de drivers com risco.</returns>
        public async Task<List<DriverRiskProfile>> ScanDriversAsync()
        {
            // Implementação simplificada - retorna lista vazia por enquanto
            // Uma implementação real exigiria acesso a APIs específicas do Windows
            var riskProfiles = new List<DriverRiskProfile>();
            
            await Task.CompletedTask;
            
            // Atualiza os resultados da última varredura
            lock (_lockObject)
            {
                _lastScanResults = riskProfiles.ToList();
            }
            
            // Atualiza o índice de risco do sistema
            await UpdateSystemRiskIndexAsync(riskProfiles, 0);
            
            return riskProfiles;
        }

        /// <summary>
        /// Obtém o índice de risco atual do sistema.
        /// </summary>
        /// <returns>Índice de risco do sistema.</returns>
        public async Task<DriverRiskIndex> GetSystemRiskIndexAsync()
        {
            // Se não temos um índice atualizado, realiza uma verificação
            if (_currentRiskIndex == null || _currentRiskIndex.Timestamp < DateTime.UtcNow.AddMinutes(-5))
            {
                await ScanDriversAsync();
            }
            
            lock (_lockObject)
            {
                return _currentRiskIndex.Clone();
            }
        }

        /// <summary>
        /// Loop principal de monitoramento de drivers.
        /// </summary>
        /// <param name="cancellationToken">Token de cancelamento.</param>
        /// <returns>Tarefa assíncrona.</returns>
        private async Task MonitorDriversAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && _isRunning)
            {
                try
                {
                    await PerformDriverScanAsync();
                    await Task.Delay(MonitoringIntervalMs, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // Tarefa cancelada, sair do loop
                    break;
                }
                catch (Exception ex)
                {
                    // Log de erro mas continua executando
                    System.Diagnostics.Debug.WriteLine($"Erro no monitoramento de drivers: {ex.Message}");
                    
                    // Aguarda mesmo em caso de erro para evitar loops muito rápidos
                    try
                    {
                        await Task.Delay(MonitoringIntervalMs, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Realiza uma varredura de drivers.
        /// </summary>
        /// <returns>Tarefa assíncrona.</returns>
        private async Task PerformDriverScanAsync()
        {
            try
            {
                var riskProfiles = await ScanDriversAsync();
                
                // Notifica sobre drivers de risco encontrados
                foreach (var profile in riskProfiles)
                {
                    if (profile.RiskIndex >= Sensitivity)
                    {
                        OnDriverRiskDetected(profile);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erro ao realizar varredura de drivers: {ex.Message}");
            }
        }

        /// <summary>
        /// Analisa um driver específico para determinar seu perfil de risco.
        /// </summary>
        /// <param name="driverInfo">Informações do driver.</param>
        /// <returns>Perfil de risco do driver.</returns>
        private async Task<DriverRiskProfile> AnalyzeDriverAsync(DriverInfo driverInfo)
        {
            var profile = new DriverRiskProfile
            {
                Name = driverInfo.Name,
                FriendlyName = driverInfo.FriendlyName,
                Version = driverInfo.Version,
                Date = driverInfo.Date,
                Manufacturer = driverInfo.Manufacturer,
                FilePath = driverInfo.FilePath
            };
            
            // Aplica heurísticas para calcular o índice de risco
            var heuristicResult = _driverHeuristics.EvaluateDriver(driverInfo);
            profile.RiskIndex = heuristicResult.RiskScore;
            profile.RiskCategory = heuristicResult.RiskCategory;
            profile.Issues = heuristicResult.Issues.ToList();
            profile.Recommendations = heuristicResult.Recommendations.ToList();
            
            // Verifica contra a base de dados de drivers conhecidos
            var dbResult = await _driverDatabase.CheckDriverAsync(driverInfo);
            if (dbResult.IsKnownRisky)
            {
                profile.RiskIndex = Math.Min(1.0, profile.RiskIndex + 0.3);
                profile.RiskCategory = GetRiskCategory(profile.RiskIndex);
                
                profile.Issues.Add(new DriverIssue
                {
                    Type = DriverIssueType.KnownIssues,
                    Severity = IssueSeverity.High,
                    Description = "Driver identificado como problemático em nossa base de dados",
                    TechnicalDetails = dbResult.Details
                });
                
                if (!string.IsNullOrEmpty(dbResult.Recommendation))
                {
                    profile.Recommendations.Add(dbResult.Recommendation);
                }
            }
            
            return profile;
        }

        /// <summary>
        /// Atualiza o índice de risco do sistema.
        /// </summary>
        /// <param name="riskProfiles">Perfis de drivers com risco.</param>
        /// <param name="totalDrivers">Número total de drivers.</param>
        /// <returns>Tarefa assíncrona.</returns>
        private async Task UpdateSystemRiskIndexAsync(List<DriverRiskProfile> riskProfiles, int totalDrivers)
        {
            var previousIndex = _currentRiskIndex;
            
            var newIndex = new DriverRiskIndex
            {
                Timestamp = DateTime.UtcNow,
                TotalDrivers = totalDrivers,
                RiskyDrivers = riskProfiles.Count,
                OverallRisk = CalculateOverallRisk(riskProfiles, totalDrivers),
                RiskCategory = GetSystemRiskCategory(riskProfiles)
            };
            
            lock (_lockObject)
            {
                _currentRiskIndex = newIndex;
            }
            
            // Dispara evento de mudança de índice de risco
            OnRiskIndexChanged(previousIndex, newIndex);
            
            await Task.CompletedTask;
        }

        /// <summary>
        /// Calcula o risco geral do sistema.
        /// </summary>
        /// <param name="riskProfiles">Perfis de drivers com risco.</param>
        /// <param name="totalDrivers">Número total de drivers.</param>
        /// <returns>Risco geral do sistema (0.0 a 1.0).</returns>
        private double CalculateOverallRisk(List<DriverRiskProfile> riskProfiles, int totalDrivers)
        {
            if (totalDrivers == 0)
                return 0.0;
                
            // Calcula uma média ponderada dos riscos
            double weightedSum = 0.0;
            foreach (var profile in riskProfiles)
            {
                // Drivers perigosos têm peso maior
                double weight = 1.0;
                if (profile.RiskCategory == DriverRiskCategory.Dangerous)
                    weight = 3.0;
                else if (profile.RiskCategory == DriverRiskCategory.HighRisk)
                    weight = 2.0;
                else if (profile.RiskCategory == DriverRiskCategory.ModerateRisk)
                    weight = 1.5;
                
                weightedSum += profile.RiskIndex * weight;
            }
            
            // Normaliza pelo número total de drivers
            return Math.Min(1.0, weightedSum / totalDrivers);
        }

        /// <summary>
        /// Determina a categoria de risco do sistema.
        /// </summary>
        /// <param name="riskProfiles">Perfis de drivers com risco.</param>
        /// <returns>Categoria de risco do sistema.</returns>
        private DriverRiskCategory GetSystemRiskCategory(List<DriverRiskProfile> riskProfiles)
        {
            // Conta drivers por categoria de risco
            int dangerous = riskProfiles.Count(p => p.RiskCategory == DriverRiskCategory.Dangerous);
            int highRisk = riskProfiles.Count(p => p.RiskCategory == DriverRiskCategory.HighRisk);
            int moderateRisk = riskProfiles.Count(p => p.RiskCategory == DriverRiskCategory.ModerateRisk);
            
            // Determina a categoria com base nos drivers mais críticos
            if (dangerous > 0)
                return DriverRiskCategory.Dangerous;
            if (highRisk > 2)
                return DriverRiskCategory.HighRisk;
            if (moderateRisk > 5)
                return DriverRiskCategory.ModerateRisk;
            if (riskProfiles.Count > 10)
                return DriverRiskCategory.LowRisk;
                
            return DriverRiskCategory.Safe;
        }

        /// <summary>
        /// Determina a categoria de risco com base no índice.
        /// </summary>
        /// <param name="riskIndex">Índice de risco.</param>
        /// <returns>Categoria de risco.</returns>
        private DriverRiskCategory GetRiskCategory(double riskIndex)
        {
            if (riskIndex >= 0.8)
                return DriverRiskCategory.Dangerous;
            if (riskIndex >= 0.6)
                return DriverRiskCategory.HighRisk;
            if (riskIndex >= 0.4)
                return DriverRiskCategory.ModerateRisk;
            if (riskIndex >= 0.2)
                return DriverRiskCategory.LowRisk;
                
            return DriverRiskCategory.Safe;
        }

        /// <summary>
        /// Dispara o evento de driver de risco detectado.
        /// </summary>
        /// <param name="profile">Perfil do driver com risco.</param>
        protected virtual void OnDriverRiskDetected(DriverRiskProfile profile)
        {
            DriverRiskDetected?.Invoke(this, new DriverRiskDetectedEventArgs(profile));
        }

        /// <summary>
        /// Dispara o evento de mudança no índice de risco.
        /// </summary>
        /// <param name="previousIndex">Índice anterior.</param>
        /// <param name="newIndex">Novo índice.</param>
        protected virtual void OnRiskIndexChanged(DriverRiskIndex previousIndex, DriverRiskIndex newIndex)
        {
            RiskIndexChanged?.Invoke(this, new DriverRiskIndexChangedEventArgs(previousIndex, newIndex));
        }
    }

    /// <summary>
    /// Informações de um driver instalado.
    /// </summary>
    public class DriverInfo
    {
        /// <summary>
        /// Nome do driver.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Nome amigável do driver.
        /// </summary>
        public string FriendlyName { get; set; }

        /// <summary>
        /// Versão do driver.
        /// </summary>
        public string Version { get; set; }

        /// <summary>
        /// Data do driver.
        /// </summary>
        public DateTime Date { get; set; }

        /// <summary>
        /// Fabricante do driver.
        /// </summary>
        public string Manufacturer { get; set; }

        /// <summary>
        /// Caminho do arquivo do driver.
        /// </summary>
        public string FilePath { get; set; }

        /// <summary>
        /// Tamanho do arquivo em bytes.
        /// </summary>
        public long FileSizeBytes { get; set; }

        /// <summary>
        /// Indica se o driver está assinado digitalmente.
        /// </summary>
        public bool IsSigned { get; set; }

        /// <summary>
        /// Informações da assinatura digital.
        /// </summary>
        public DigitalSignatureInfo SignatureInfo { get; set; }
    }

    /// <summary>
    /// Informações da assinatura digital de um driver.
    /// </summary>
    public class DigitalSignatureInfo
    {
        /// <summary>
        /// Nome do signatário.
        /// </summary>
        public string SignerName { get; set; }

        /// <summary>
        /// Certificadora.
        /// </summary>
        public string CertificateAuthority { get; set; }

        /// <summary>
        /// Data de expiração da assinatura.
        /// </summary>
        public DateTime ExpirationDate { get; set; }

        /// <summary>
        /// Indica se a assinatura é válida.
        /// </summary>
        public bool IsValid { get; set; }
    }
}
