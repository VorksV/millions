using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.SystemIntelligenceProfiler
{
    /// <summary>
    /// Serviço para geração de relatórios de drivers.
    /// </summary>
    public class DriverReportService
    {
        private readonly IDriverRiskMapper _driverRiskMapper;
        private readonly DriverRiskReportingService _reportingService;

        /// <summary>
        /// Inicializa uma nova instância de <see cref="DriverReportService"/>.
        /// </summary>
        /// <param name="driverRiskMapper">Mapeador de riscos de drivers.</param>
        /// <param name="reportingService">Serviço de geração de relatórios.</param>
        public DriverReportService(
            IDriverRiskMapper driverRiskMapper,
            DriverRiskReportingService reportingService)
        {
            _driverRiskMapper = driverRiskMapper ?? throw new ArgumentNullException(nameof(driverRiskMapper));
            _reportingService = reportingService ?? throw new ArgumentNullException(nameof(reportingService));
        }

        /// <summary>
        /// Gera um relatório de drivers em formato texto.
        /// </summary>
        /// <returns>Texto do relatório.</returns>
        public async Task<string> GenerateTextReportAsync()
        {
            var report = await _reportingService.GenerateRiskReportAsync();
            var sb = new StringBuilder();
            
            // Cabeçalho do relatório
            sb.AppendLine("RELATÓRIO DE ANÁLISE DE DRIVERS");
            sb.AppendLine("===============================");
            sb.AppendLine($"Data da análise: {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
            sb.AppendLine();
            
            // Resumo do índice de risco
            sb.AppendLine("RESUMO DO ÍNDICE DE RISCO");
            sb.AppendLine("-------------------------");
            sb.AppendLine($"Total de drivers: {report.RiskIndex.TotalDrivers}");
            sb.AppendLine($"Drivers com risco: {report.RiskIndex.RiskyDrivers}");
            sb.AppendLine($"Índice de risco geral: {report.RiskIndex.OverallRisk:P2}");
            sb.AppendLine($"Categoria de risco: {report.RiskIndex.GetRiskCategoryDescription()}");
            sb.AppendLine();
            
            // Recomendação geral
            sb.AppendLine("RECOMENDAÇÃO GERAL");
            sb.AppendLine("------------------");
            sb.AppendLine(report.RiskIndex.GetRecommendation());
            sb.AppendLine();
            
            // Drivers mais problemáticos
            if (report.TopRiskyDrivers.Length > 0)
            {
                sb.AppendLine("DRIVERS MAIS PROBLEMÁTICOS");
                sb.AppendLine("--------------------------");
                foreach (var driver in report.TopRiskyDrivers)
                {
                    sb.AppendLine($"Nome: {driver.FriendlyName ?? driver.Name}");
                    sb.AppendLine($"  Risco: {driver.RiskIndex:P2} ({driver.RiskCategory})");
                    sb.AppendLine($"  Fabricante: {driver.Manufacturer}");
                    sb.AppendLine($"  Versão: {driver.Version}");
                    if (driver.Issues.Count > 0)
                    {
                        sb.AppendLine("  Problemas identificados:");
                        foreach (var issue in driver.Issues)
                        {
                            sb.AppendLine($"    - {issue.Description} ({issue.Severity})");
                        }
                    }
                    if (driver.Recommendations.Count > 0)
                    {
                        sb.AppendLine("  Recomendações:");
                        foreach (var recommendation in driver.Recommendations)
                        {
                            sb.AppendLine($"    - {recommendation}");
                        }
                    }
                    sb.AppendLine();
                }
            }
            
            // Distribuição de riscos
            sb.AppendLine("DISTRIBUIÇÃO DE RISCOS");
            sb.AppendLine("---------------------");
            sb.AppendLine($"Seguros: {report.Distribution.SafeDrivers}");
            sb.AppendLine($"Risco baixo: {report.Distribution.LowRiskDrivers}");
            sb.AppendLine($"Risco moderado: {report.Distribution.ModerateRiskDrivers}");
            sb.AppendLine($"Risco alto: {report.Distribution.HighRiskDrivers}");
            sb.AppendLine($"Perigosos: {report.Distribution.DangerousDrivers}");
            
            return sb.ToString();
        }

        /// <summary>
        /// Gera um relatório de drivers em formato CSV.
        /// </summary>
        /// <returns>Texto CSV do relatório.</returns>
        public async Task<string> GenerateCsvReportAsync()
        {
            var riskyDrivers = await _driverRiskMapper.ScanDriversAsync();
            var sb = new StringBuilder();
            
            // Cabeçalho CSV
            sb.AppendLine("Nome;Nome Amigável;Versão;Fabricante;Data;Caminho;Risco;Categoria;Problemas;Recomendações");
            
            // Dados dos drivers
            foreach (var driver in riskyDrivers)
            {
                var issues = string.Join("|", driver.Issues.ConvertAll(i => $"{i.Type}:{i.Severity}:{i.Description}"));
                var recommendations = string.Join("|", driver.Recommendations);
                
                sb.AppendLine($"{EscapeCsvField(driver.Name)};" +
                             $"{EscapeCsvField(driver.FriendlyName)};" +
                             $"{EscapeCsvField(driver.Version)};" +
                             $"{EscapeCsvField(driver.Manufacturer)};" +
                             $"{driver.Date:yyyy-MM-dd};" +
                             $"{EscapeCsvField(driver.FilePath)};" +
                             $"{driver.RiskIndex};" +
                             $"{driver.RiskCategory};" +
                             $"{EscapeCsvField(issues)};" +
                             $"{EscapeCsvField(recommendations)}");
            }
            
            return sb.ToString();
        }

        /// <summary>
        /// Gera um relatório de drivers em formato HTML.
        /// </summary>
        /// <returns>Texto HTML do relatório.</returns>
        public async Task<string> GenerateHtmlReportAsync()
        {
            var report = await _reportingService.GenerateRiskReportAsync();
            var sb = new StringBuilder();
            
            // Cabeçalho HTML
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html>");
            sb.AppendLine("<head>");
            sb.AppendLine("    <meta charset=\"UTF-8\">");
            sb.AppendLine("    <title>Relatório de Análise de Drivers</title>");
            sb.AppendLine("    <style>");
            sb.AppendLine("        body { font-family: Arial, sans-serif; margin: 20px; }");
            sb.AppendLine("        h1, h2, h3 { color: #333; }");
            sb.AppendLine("        .risk-high { color: #d32f2f; font-weight: bold; }");
            sb.AppendLine("        .risk-medium { color: #f57c00; font-weight: bold; }");
            sb.AppendLine("        .risk-low { color: #fbc02d; font-weight: bold; }");
            sb.AppendLine("        .risk-safe { color: #388e3c; font-weight: bold; }");
            sb.AppendLine("        table { border-collapse: collapse; width: 100%; margin: 20px 0; }");
            sb.AppendLine("        th, td { border: 1px solid #ddd; padding: 8px; text-align: left; }");
            sb.AppendLine("        th { background-color: #f2f2f2; }");
            sb.AppendLine("        tr:nth-child(even) { background-color: #f9f9f9; }");
            sb.AppendLine("    </style>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");
            
            // Conteúdo do relatório
            sb.AppendLine("<h1>RELATÓRIO DE ANÁLISE DE DRIVERS</h1>");
            sb.AppendLine($"<p><strong>Data da análise:</strong> {DateTime.Now:dd/MM/yyyy HH:mm:ss}</p>");
            
            // Resumo do índice de risco
            sb.AppendLine("<h2>Resumo do Índice de Risco</h2>");
            sb.AppendLine("<table>");
            sb.AppendLine("    <tr><th>Total de drivers</th><td>" + report.RiskIndex.TotalDrivers + "</td></tr>");
            sb.AppendLine("    <tr><th>Drivers com risco</th><td>" + report.RiskIndex.RiskyDrivers + "</td></tr>");
            sb.AppendLine("    <tr><th>Índice de risco geral</th><td>" + report.RiskIndex.OverallRisk.ToString("P2") + "</td></tr>");
            sb.AppendLine("    <tr><th>Categoria de risco</th><td>" + report.RiskIndex.GetRiskCategoryDescription() + "</td></tr>");
            sb.AppendLine("</table>");
            
            // Recomendação geral
            sb.AppendLine("<h2>Recomendação Geral</h2>");
            sb.AppendLine("<p>" + report.RiskIndex.GetRecommendation() + "</p>");
            
            // Drivers mais problemáticos
            if (report.TopRiskyDrivers.Length > 0)
            {
                sb.AppendLine("<h2>Drivers Mais Problemáticos</h2>");
                sb.AppendLine("<table>");
                sb.AppendLine("    <thead>");
                sb.AppendLine("        <tr>");
                sb.AppendLine("            <th>Nome</th>");
                sb.AppendLine("            <th>Risco</th>");
                sb.AppendLine("            <th>Fabricante</th>");
                sb.AppendLine("            <th>Versão</th>");
                sb.AppendLine("        </tr>");
                sb.AppendLine("    </thead>");
                sb.AppendLine("    <tbody>");
                
                foreach (var driver in report.TopRiskyDrivers)
                {
                    string riskClass = GetRiskCssClass(driver.RiskCategory);
                    
                    sb.AppendLine("        <tr>");
                    sb.AppendLine($"            <td>{driver.FriendlyName ?? driver.Name}</td>");
                    sb.AppendLine($"            <td class=\"{riskClass}\">{driver.RiskIndex:P2}</td>");
                    sb.AppendLine($"            <td>{driver.Manufacturer}</td>");
                    sb.AppendLine($"            <td>{driver.Version}</td>");
                    sb.AppendLine("        </tr>");
                }
                
                sb.AppendLine("    </tbody>");
                sb.AppendLine("</table>");
            }
            
            // Rodapé
            sb.AppendLine("</body>");
            sb.AppendLine("</html>");
            
            return sb.ToString();
        }

        /// <summary>
        /// Salva um relatório em um arquivo.
        /// </summary>
        /// <param name="reportContent">Conteúdo do relatório.</param>
        /// <param name="filePath">Caminho do arquivo.</param>
        /// <param name="format">Formato do relatório.</param>
        /// <returns>Tarefa assíncrona.</returns>
        public async Task SaveReportAsync(string reportContent, string filePath, ReportFormat format)
        {
            if (string.IsNullOrEmpty(reportContent))
                throw new ArgumentException("O conteúdo do relatório não pode ser vazio.", nameof(reportContent));
            
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("O caminho do arquivo não pode ser vazio.", nameof(filePath));

            // Cria o diretório se não existir
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            // Salva o arquivo
            await File.WriteAllTextAsync(filePath, reportContent, Encoding.UTF8);
        }

        /// <summary>
        /// Escapa um campo para formato CSV.
        /// </summary>
        /// <param name="field">Campo a ser escapado.</param>
        /// <returns>Campo escapado.</returns>
        private string EscapeCsvField(string field)
        {
            if (string.IsNullOrEmpty(field))
                return string.Empty;
            
            // Se contém caracteres especiais, envolve em aspas e escapa aspas internas
            if (field.Contains(";") || field.Contains("\"") || field.Contains("\n") || field.Contains("\r"))
            {
                return "\"" + field.Replace("\"", "\"\"") + "\"";
            }
            
            return field;
        }

        /// <summary>
        /// Obtém a classe CSS para um nível de risco.
        /// </summary>
        /// <param name="riskCategory">Categoria de risco.</param>
        /// <returns>Classe CSS.</returns>
        private string GetRiskCssClass(DriverRiskCategory riskCategory)
        {
            switch (riskCategory)
            {
                case DriverRiskCategory.Dangerous:
                    return "risk-high";
                case DriverRiskCategory.HighRisk:
                    return "risk-high";
                case DriverRiskCategory.ModerateRisk:
                    return "risk-medium";
                case DriverRiskCategory.LowRisk:
                    return "risk-low";
                case DriverRiskCategory.Safe:
                    return "risk-safe";
                default:
                    return "";
            }
        }
    }

    /// <summary>
    /// Formatos de relatório suportados.
    /// </summary>
    public enum ReportFormat
    {
        /// <summary>
        /// Formato de texto simples.
        /// </summary>
        Text,

        /// <summary>
        /// Formato CSV.
        /// </summary>
        Csv,

        /// <summary>
        /// Formato HTML.
        /// </summary>
        Html,

        /// <summary>
        /// Formato JSON.
        /// </summary>
        Json
    }
}
