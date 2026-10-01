using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.SystemIntelligenceProfiler
{
    /// <summary>
    /// Interface para o mapeador de riscos de drivers.
    /// </summary>
    public interface IDriverRiskMapper
    {
        /// <summary>
        /// Inicia a verificação contínua de drivers.
        /// </summary>
        /// <returns>Tarefa assíncrona.</returns>
        Task StartMonitoringAsync();

        /// <summary>
        /// Para a verificação contínua de drivers.
        /// </summary>
        /// <returns>Tarefa assíncrona.</returns>
        Task StopMonitoringAsync();

        /// <summary>
        /// Realiza uma verificação pontual de todos os drivers.
        /// </summary>
        /// <returns>Lista de perfis de drivers com risco.</returns>
        Task<List<DriverRiskProfile>> ScanDriversAsync();

        /// <summary>
        /// Obtém o índice de risco atual do sistema.
        /// </summary>
        /// <returns>Índice de risco do sistema.</returns>
        Task<DriverRiskIndex> GetSystemRiskIndexAsync();

        /// <summary>
        /// Evento disparado quando um driver de risco é encontrado.
        /// </summary>
        event EventHandler<DriverRiskDetectedEventArgs> DriverRiskDetected;

        /// <summary>
        /// Evento disparado quando o índice de risco do sistema é atualizado.
        /// </summary>
        event EventHandler<DriverRiskIndexChangedEventArgs> RiskIndexChanged;
    }

    /// <summary>
    /// Perfil de um driver com informações de risco.
    /// </summary>
    public class DriverRiskProfile
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
        /// Índice de risco do driver (0.0 a 1.0).
        /// </summary>
        public double RiskIndex { get; set; }

        /// <summary>
        /// Categoria de risco.
        /// </summary>
        public DriverRiskCategory RiskCategory { get; set; }

        /// <summary>
        /// Lista de problemas identificados.
        /// </summary>
        public List<DriverIssue> Issues { get; set; } = new List<DriverIssue>();

        /// <summary>
        /// Recomendações para mitigar riscos.
        /// </summary>
        public List<string> Recommendations { get; set; } = new List<string>();
    }

    /// <summary>
    /// Categoria de risco de driver.
    /// </summary>
    public enum DriverRiskCategory
    {
        /// <summary>
        /// Driver seguro.
        /// </summary>
        Safe,

        /// <summary>
        /// Driver com risco baixo.
        /// </summary>
        LowRisk,

        /// <summary>
        /// Driver com risco moderado.
        /// </summary>
        ModerateRisk,

        /// <summary>
        /// Driver com risco alto.
        /// </summary>
        HighRisk,

        /// <summary>
        /// Driver perigoso.
        /// </summary>
        Dangerous
    }

    /// <summary>
    /// Problema identificado em um driver.
    /// </summary>
    public class DriverIssue
    {
        /// <summary>
        /// Tipo do problema.
        /// </summary>
        public DriverIssueType Type { get; set; }

        /// <summary>
        /// Severidade do problema.
        /// </summary>
        public IssueSeverity Severity { get; set; }

        /// <summary>
        /// Descrição do problema.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Detalhes técnicos do problema.
        /// </summary>
        public string TechnicalDetails { get; set; }
    }

    /// <summary>
    /// Tipos de problemas em drivers.
    /// </summary>
    public enum DriverIssueType
    {
        /// <summary>
        /// Driver desatualizado.
        /// </summary>
        Outdated,

        /// <summary>
        /// Driver não assinado.
        /// </summary>
        Unsigned,

        /// <summary>
        /// Driver conhecido por causar problemas.
        /// </summary>
        KnownIssues,

        /// <summary>
        /// Driver de fabricante desconhecido.
        /// </summary>
        UnknownManufacturer,

        /// <summary>
        /// Driver com vulnerabilidades de segurança.
        /// </summary>
        SecurityVulnerabilities,

        /// <summary>
        /// Driver com comportamento suspeito.
        /// </summary>
        SuspiciousBehavior,

        /// <summary>
        /// Driver em conflito com outros.
        /// </summary>
        Conflicts,

        /// <summary>
        /// Driver obsoleto.
        /// </summary>
        Obsolete
    }

    /// <summary>
    /// Severidade de um problema.
    /// </summary>
    public enum IssueSeverity
    {
        /// <summary>
        /// Informacional.
        /// </summary>
        Informational,

        /// <summary>
        /// Baixa severidade.
        /// </summary>
        Low,

        /// <summary>
        /// Média severidade.
        /// </summary>
        Medium,

        /// <summary>
        /// Alta severidade.
        /// </summary>
        High,

        /// <summary>
        /// Crítica.
        /// </summary>
        Critical
    }

    /// <summary>
    /// Argumentos para evento de driver de risco detectado.
    /// </summary>
    public class DriverRiskDetectedEventArgs : EventArgs
    {
        /// <summary>
        /// Perfil do driver com risco.
        /// </summary>
        public DriverRiskProfile DriverProfile { get; }

        /// <summary>
        /// Inicializa uma nova instância de <see cref="DriverRiskDetectedEventArgs"/>.
        /// </summary>
        /// <param name="driverProfile">Perfil do driver com risco.</param>
        public DriverRiskDetectedEventArgs(DriverRiskProfile driverProfile)
        {
            DriverProfile = driverProfile;
        }
    }

    /// <summary>
    /// Argumentos para evento de mudança no índice de risco.
    /// </summary>
    public class DriverRiskIndexChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Índice de risco anterior.
        /// </summary>
        public DriverRiskIndex PreviousIndex { get; }

        /// <summary>
        /// Novo índice de risco.
        /// </summary>
        public DriverRiskIndex NewIndex { get; }

        /// <summary>
        /// Inicializa uma nova instância de <see cref="DriverRiskIndexChangedEventArgs"/>.
        /// </summary>
        /// <param name="previousIndex">Índice de risco anterior.</param>
        /// <param name="newIndex">Novo índice de risco.</param>
        public DriverRiskIndexChangedEventArgs(DriverRiskIndex previousIndex, DriverRiskIndex newIndex)
        {
            PreviousIndex = previousIndex;
            NewIndex = newIndex;
        }
    }
}
