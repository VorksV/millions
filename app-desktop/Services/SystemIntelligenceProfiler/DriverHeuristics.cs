using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace VoltrisOptimizer.Services.SystemIntelligenceProfiler
{
    /// <summary>
    /// Heurísticas para avaliação de riscos em drivers.
    /// </summary>
    public class DriverHeuristics
    {
        private readonly HashSet<string> _knownDangerousManufacturers;
        private readonly HashSet<string> _knownUnsafeDriverNames;
        private readonly Dictionary<string, string> _recommendedReplacements;

        /// <summary>
        /// Inicializa uma nova instância de <see cref="DriverHeuristics"/>.
        /// </summary>
        public DriverHeuristics()
        {
            _knownDangerousManufacturers = InitializeDangerousManufacturers();
            _knownUnsafeDriverNames = InitializeUnsafeDriverNames();
            _recommendedReplacements = InitializeRecommendedReplacements();
        }

        /// <summary>
        /// Avalia um driver e determina seu perfil de risco.
        /// </summary>
        /// <param name="driverInfo">Informações do driver.</param>
        /// <returns>Resultado da avaliação.</returns>
        public DriverHeuristicResult EvaluateDriver(DriverInfo driverInfo)
        {
            var issues = new List<DriverIssue>();
            var recommendations = new List<string>();
            double riskScore = 0.0;
            
            // Verifica se o driver está assinado
            if (!driverInfo.IsSigned)
            {
                issues.Add(new DriverIssue
                {
                    Type = DriverIssueType.Unsigned,
                    Severity = IssueSeverity.High,
                    Description = "Driver não assinado digitalmente",
                    TechnicalDetails = "Drivers não assinados podem ser maliciosos ou incompatíveis"
                });
                
                riskScore += 0.4;
            }
            else if (driverInfo.SignatureInfo != null && !driverInfo.SignatureInfo.IsValid)
            {
                issues.Add(new DriverIssue
                {
                    Type = DriverIssueType.Unsigned,
                    Severity = IssueSeverity.Medium,
                    Description = "Assinatura digital inválida",
                    TechnicalDetails = "A assinatura digital do driver expirou ou é inválida"
                });
                
                riskScore += 0.2;
            }
            
            // Verifica a data do driver
            var daysOld = (DateTime.Now - driverInfo.Date).Days;
            if (daysOld > 365 * 5) // Mais de 5 anos
            {
                issues.Add(new DriverIssue
                {
                    Type = DriverIssueType.Outdated,
                    Severity = IssueSeverity.Medium,
                    Description = "Driver desatualizado",
                    TechnicalDetails = $"O driver tem {daysOld / 365:F1} anos"
                });
                
                riskScore += 0.15;
            }
            else if (daysOld > 365 * 10) // Mais de 10 anos
            {
                issues.Add(new DriverIssue
                {
                    Type = DriverIssueType.Obsolete,
                    Severity = IssueSeverity.High,
                    Description = "Driver obsoleto",
                    TechnicalDetails = $"O driver tem {daysOld / 365:F1} anos e pode ser incompatível"
                });
                
                riskScore += 0.3;
            }
            
            // Verifica o fabricante
            if (!string.IsNullOrEmpty(driverInfo.Manufacturer) &&
                _knownDangerousManufacturers.Contains(driverInfo.Manufacturer.ToLowerInvariant()))
            {
                issues.Add(new DriverIssue
                {
                    Type = DriverIssueType.UnknownManufacturer,
                    Severity = IssueSeverity.High,
                    Description = "Fabricante de driver duvidoso",
                    TechnicalDetails = $"O fabricante '{driverInfo.Manufacturer}' é conhecido por drivers problemáticos"
                });
                
                riskScore += 0.35;
            }
            
            // Verifica o nome do driver
            if (!string.IsNullOrEmpty(driverInfo.Name) &&
                _knownUnsafeDriverNames.Any(name => 
                    driverInfo.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                issues.Add(new DriverIssue
                {
                    Type = DriverIssueType.KnownIssues,
                    Severity = IssueSeverity.Critical,
                    Description = "Driver conhecido por causar problemas",
                    TechnicalDetails = $"O driver '{driverInfo.Name}' é conhecido por causar instabilidade"
                });
                
                riskScore += 0.5;
            }
            
            // Verifica o tamanho do arquivo
            if (driverInfo.FileSizeBytes > 50 * 1024 * 1024) // Mais de 50MB
            {
                issues.Add(new DriverIssue
                {
                    Type = DriverIssueType.SuspiciousBehavior,
                    Severity = IssueSeverity.Medium,
                    Description = "Driver excepcionalmente grande",
                    TechnicalDetails = $"O driver tem {driverInfo.FileSizeBytes / (1024 * 1024):F1} MB, o que é incomum"
                });
                
                riskScore += 0.1;
            }
            
            // Verifica o caminho do arquivo
            if (!string.IsNullOrEmpty(driverInfo.FilePath))
            {
                var suspiciousPaths = new[]
                {
                    @"\Temp\",
                    @"\Downloads\",
                    @"\AppData\Local\Temp\",
                    @"C:\Windows\Temp\"
                };
                
                if (suspiciousPaths.Any(path => 
                    driverInfo.FilePath.IndexOf(path, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    issues.Add(new DriverIssue
                    {
                        Type = DriverIssueType.SuspiciousBehavior,
                        Severity = IssueSeverity.High,
                        Description = "Driver em local suspeito",
                        TechnicalDetails = $"O driver está localizado em '{driverInfo.FilePath}'"
                    });
                    
                    riskScore += 0.25;
                }
            }
            
            // Determina recomendações
            if (issues.Any(i => i.Severity == IssueSeverity.Critical))
            {
                recommendations.Add("Remover este driver imediatamente");
            }
            else if (issues.Any(i => i.Severity == IssueSeverity.High))
            {
                recommendations.Add("Atualizar ou substituir este driver");
            }
            else if (issues.Any(i => i.Severity >= IssueSeverity.Medium))
            {
                recommendations.Add("Monitorar o comportamento deste driver");
            }
            
            // Adiciona recomendações específicas
            if (!string.IsNullOrEmpty(driverInfo.Name) && 
                _recommendedReplacements.TryGetValue(driverInfo.Name.ToLowerInvariant(), out var replacement))
            {
                recommendations.Add($"Substituir por: {replacement}");
            }
            
            // Limita o risco máximo
            riskScore = Math.Min(1.0, riskScore);
            
            // Determina a categoria de risco
            var riskCategory = GetRiskCategory(riskScore);
            
            return new DriverHeuristicResult
            {
                RiskScore = riskScore,
                RiskCategory = riskCategory,
                Issues = issues,
                Recommendations = recommendations
            };
        }

        /// <summary>
        /// Inicializa a lista de fabricantes perigosos.
        /// </summary>
        /// <returns>Conjunto de fabricantes perigosos.</returns>
        private HashSet<string> InitializeDangerousManufacturers()
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "unknown",
                "desconhecido",
                "generic",
                "freeware",
                "shareware",
                "untrusted",
                "suspicious"
            };
        }

        /// <summary>
        /// Inicializa a lista de nomes de drivers inseguros.
        /// </summary>
        /// <returns>Conjunto de nomes de drivers inseguros.</returns>
        private HashSet<string> InitializeUnsafeDriverNames()
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "crash",
                "freeze",
                "bsod",
                "blue screen",
                "infector",
                "malware",
                "virus",
                "trojan",
                "rootkit",
                "spyware",
                "adware",
                "hack",
                "exploit",
                "vulnerable",
                "unsafe",
                "dangerous",
                "suspicious"
            };
        }

        /// <summary>
        /// Inicializa o dicionário de substituições recomendadas.
        /// </summary>
        /// <returns>Dicionário de substituições recomendadas.</returns>
        private Dictionary<string, string> InitializeRecommendedReplacements()
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                {"oldnetdrv.sys", "Atualizar para o driver de rede oficial do fabricante"},
                {"legacyusb.sys", "Atualizar para drivers USB modernos"},
                {"outdatedgpu.sys", "Baixar driver oficial da GPU do site do fabricante"},
                {"ancientaudio.sys", "Atualizar drivers de áudio pela Central de Som do Windows"}
            };
        }

        /// <summary>
        /// Determina a categoria de risco com base no índice.
        /// </summary>
        /// <param name="riskScore">Índice de risco.</param>
        /// <returns>Categoria de risco.</returns>
        private DriverRiskCategory GetRiskCategory(double riskScore)
        {
            if (riskScore >= 0.8)
                return DriverRiskCategory.Dangerous;
            if (riskScore >= 0.6)
                return DriverRiskCategory.HighRisk;
            if (riskScore >= 0.4)
                return DriverRiskCategory.ModerateRisk;
            if (riskScore >= 0.2)
                return DriverRiskCategory.LowRisk;
                
            return DriverRiskCategory.Safe;
        }
    }

    /// <summary>
    /// Resultado da avaliação heurística de um driver.
    /// </summary>
    public class DriverHeuristicResult
    {
        /// <summary>
        /// Índice de risco calculado (0.0 a 1.0).
        /// </summary>
        public double RiskScore { get; set; }

        /// <summary>
        /// Categoria de risco.
        /// </summary>
        public DriverRiskCategory RiskCategory { get; set; }

        /// <summary>
        /// Lista de problemas identificados.
        /// </summary>
        public IEnumerable<DriverIssue> Issues { get; set; }

        /// <summary>
        /// Recomendações para mitigar riscos.
        /// </summary>
        public IEnumerable<string> Recommendations { get; set; }
    }
}
