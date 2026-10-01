using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.SystemIntelligenceProfiler
{
    /// <summary>
    /// Serviço de geração de relatórios ETW.
    /// </summary>
    public class EtwReportingService
    {
        private readonly IEtwAnalyzer _etwAnalyzer;

        /// <summary>
        /// Inicializa uma nova instância de <see cref="EtwReportingService"/>.
        /// </summary>
        /// <param name="etwAnalyzer">Analisador ETW.</param>
        public EtwReportingService(IEtwAnalyzer etwAnalyzer)
        {
            _etwAnalyzer = etwAnalyzer ?? throw new ArgumentNullException(nameof(etwAnalyzer));
            App.LoggingService?.LogTrace("[ETW_REPORT] Serviço de relatório ETW inicializado");
        }

        /// <summary>
        /// Gera um relatório de análise ETW em formato texto.
        /// </summary>
        /// <returns>Texto do relatório.</returns>
        public async Task<string> GenerateTextReportAsync()
        {
            App.LoggingService?.LogInfo("[ETW_REPORT] Gerando relatório em formato TEXTO");
            var report = await _etwAnalyzer.GenerateAnalysisReportAsync();
            var sb = new StringBuilder();
            
            // Cabeçalho
            sb.AppendLine("RELATÓRIO DE ANÁLISE ETW");
            sb.AppendLine("========================");
            sb.AppendLine($"Gerado em: {report.GeneratedAt:dd/MM/yyyy HH:mm:ss}");
            sb.AppendLine();
            
            // Resumo
            sb.AppendLine("RESUMO");
            sb.AppendLine("------");
            sb.AppendLine($"Total de eventos coletados: {report.TotalEventsCollected}");
            sb.AppendLine($"Eventos analisados: {report.EventsAnalyzed}");
            sb.AppendLine($"Eventos críticos: {report.CriticalEvents}");
            sb.AppendLine($"Eventos de erro: {report.ErrorEvents}");
            sb.AppendLine($"Eventos de aviso: {report.WarningEvents}");
            sb.AppendLine();
            
            // Frequência de eventos
            sb.AppendLine("FREQUÊNCIA DE EVENTOS POR NÍVEL");
            sb.AppendLine("-------------------------------");
            foreach (var kvp in report.EventFrequency)
            {
                sb.AppendLine($"{kvp.Key}: {kvp.Value} eventos");
            }
            sb.AppendLine();
            
            // Principais provedores
            sb.AppendLine("PRINCIPAIS PROVEDORES DE EVENTOS");
            sb.AppendLine("-------------------------------");
            foreach (var kvp in report.TopEventProviders)
            {
                sb.AppendLine($"{kvp.Key}: {kvp.Value} eventos");
            }
            sb.AppendLine();
            
            // Insights de performance
            if (report.PerformanceInsights.Count > 0)
            {
                sb.AppendLine("INSIGHTS DE PERFORMANCE");
                sb.AppendLine("----------------------");
                foreach (var insight in report.PerformanceInsights)
                {
                    sb.AppendLine($"• {insight}");
                }
            }
            
            return sb.ToString();
        }

        /// <summary>
        /// Gera um relatório de análise ETW em formato HTML.
        /// </summary>
        /// <returns>HTML do relatório.</returns>
        public async Task<string> GenerateHtmlReportAsync()
        {
            App.LoggingService?.LogInfo("[ETW_REPORT] Gerando relatório em formato HTML");
            var report = await _etwAnalyzer.GenerateAnalysisReportAsync();
            var sb = new StringBuilder();
            
            // Cabeçalho HTML
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html>");
            sb.AppendLine("<head>");
            sb.AppendLine("    <meta charset=\"UTF-8\">");
            sb.AppendLine("    <title>Relatório de Análise ETW</title>");
            sb.AppendLine("    <style>");
            sb.AppendLine("        body { font-family: Arial, sans-serif; margin: 20px; }");
            sb.AppendLine("        h1, h2, h3 { color: #333; }");
            sb.AppendLine("        .critical { color: #d32f2f; font-weight: bold; }");
            sb.AppendLine("        .error { color: #f57c00; font-weight: bold; }");
            sb.AppendLine("        .warning { color: #fbc02d; font-weight: bold; }");
            sb.AppendLine("        table { border-collapse: collapse; width: 100%; margin: 20px 0; }");
            sb.AppendLine("        th, td { border: 1px solid #ddd; padding: 8px; text-align: left; }");
            sb.AppendLine("        th { background-color: #f2f2f2; }");
            sb.AppendLine("        tr:nth-child(even) { background-color: #f9f9f9; }");
            sb.AppendLine("    </style>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");
            
            // Conteúdo
            sb.AppendLine("<h1>RELATÓRIO DE ANÁLISE ETW</h1>");
            sb.AppendLine($"<p><strong>Gerado em:</strong> {report.GeneratedAt:dd/MM/yyyy HH:mm:ss}</p>");
            
            // Resumo
            sb.AppendLine("<h2>Resumo</h2>");
            sb.AppendLine("<table>");
            sb.AppendLine("    <tr><th>Métrica</th><th>Valor</th></tr>");
            sb.AppendLine($"    <tr><td>Total de eventos coletados</td><td>{report.TotalEventsCollected}</td></tr>");
            sb.AppendLine($"    <tr><td>Eventos analisados</td><td>{report.EventsAnalyzed}</td></tr>");
            sb.AppendLine($"    <tr><td>Eventos críticos</td><td class=\"critical\">{report.CriticalEvents}</td></tr>");
            sb.AppendLine($"    <tr><td>Eventos de erro</td><td class=\"error\">{report.ErrorEvents}</td></tr>");
            sb.AppendLine($"    <tr><td>Eventos de aviso</td><td class=\"warning\">{report.WarningEvents}</td></tr>");
            sb.AppendLine("</table>");
            
            // Frequência de eventos
            sb.AppendLine("<h2>Frequência de Eventos por Nível</h2>");
            sb.AppendLine("<table>");
            sb.AppendLine("    <tr><th>Nível</th><th>Quantidade</th></tr>");
            foreach (var kvp in report.EventFrequency)
            {
                string cssClass = "";
                if (kvp.Key == EtwEventLevel.Critical) cssClass = " class=\"critical\"";
                else if (kvp.Key == EtwEventLevel.Error) cssClass = " class=\"error\"";
                else if (kvp.Key == EtwEventLevel.Warning) cssClass = " class=\"warning\"";
                
                sb.AppendLine($"    <tr><td>{kvp.Key}</td><td{cssClass}>{kvp.Value}</td></tr>");
            }
            sb.AppendLine("</table>");
            
            // Principais provedores
            sb.AppendLine("<h2>Principais Provedores de Eventos</h2>");
            sb.AppendLine("<table>");
            sb.AppendLine("    <tr><th>Provedor</th><th>Quantidade</th></tr>");
            foreach (var kvp in report.TopEventProviders)
            {
                sb.AppendLine($"    <tr><td>{kvp.Key}</td><td>{kvp.Value}</td></tr>");
            }
            sb.AppendLine("</table>");
            
            // Insights de performance
            if (report.PerformanceInsights.Count > 0)
            {
                sb.AppendLine("<h2>Insights de Performance</h2>");
                sb.AppendLine("<ul>");
                foreach (var insight in report.PerformanceInsights)
                {
                    sb.AppendLine($"    <li>{insight}</li>");
                }
                sb.AppendLine("</ul>");
            }
            
            // Rodapé
            sb.AppendLine("</body>");
            sb.AppendLine("</html>");
            
            return sb.ToString();
        }

        /// <summary>
        /// Gera um relatório de análise ETW em formato JSON.
        /// </summary>
        /// <returns>JSON do relatório.</returns>
        public async Task<string> GenerateJsonReportAsync()
        {
            App.LoggingService?.LogInfo("[ETW_REPORT] Gerando relatório em formato JSON");
            var report = await _etwAnalyzer.GenerateAnalysisReportAsync();
            
            // Em uma implementação real, serializaríamos o objeto para JSON
            // Usando System.Text.Json ou Newtonsoft.Json
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"generatedAt\": \"{report.GeneratedAt:O}\",");
            sb.AppendLine($"  \"totalEventsCollected\": {report.TotalEventsCollected},");
            sb.AppendLine($"  \"eventsAnalyzed\": {report.EventsAnalyzed},");
            sb.AppendLine($"  \"criticalEvents\": {report.CriticalEvents},");
            sb.AppendLine($"  \"errorEvents\": {report.ErrorEvents},");
            sb.AppendLine($"  \"warningEvents\": {report.WarningEvents},");
            sb.AppendLine("  \"eventFrequency\": {");
            
            var freqItems = new List<string>();
            foreach (var kvp in report.EventFrequency)
            {
                freqItems.Add($"    \"{kvp.Key}\": {kvp.Value}");
            }
            sb.AppendLine(string.Join(",\n", freqItems));
            
            sb.AppendLine("  },");
            sb.AppendLine("  \"topEventProviders\": {");
            
            var providerItems = new List<string>();
            foreach (var kvp in report.TopEventProviders)
            {
                providerItems.Add($"    \"{kvp.Key}\": {kvp.Value}");
            }
            sb.AppendLine(string.Join(",\n", providerItems));
            
            sb.AppendLine("  },");
            sb.AppendLine("  \"performanceInsights\": [");
            
            var insightItems = new List<string>();
            foreach (var insight in report.PerformanceInsights)
            {
                insightItems.Add($"    \"{insight.Replace("\"", "\\\"")}\"");
            }
            sb.AppendLine(string.Join(",\n", insightItems));
            
            sb.AppendLine("  ]");
            sb.AppendLine("}");
            
            return sb.ToString();
        }

        /// <summary>
        /// Salva um relatório em arquivo.
        /// </summary>
        /// <param name="reportContent">Conteúdo do relatório.</param>
        /// <param name="filePath">Caminho do arquivo.</param>
        /// <param name="format">Formato do relatório.</param>
        /// <returns>Tarefa assíncrona.</returns>
        public async Task SaveReportAsync(string reportContent, string filePath, EtwReportFormat format)
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
                App.LoggingService?.LogTrace($"[ETW_REPORT] Criado diretório de destino: {directory}");
            }
            
            // Determina a extensão do arquivo com base no formato
            var extension = GetFileExtension(format);
            if (!filePath.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                filePath += extension;
            }
            
            // Salva o arquivo
            await File.WriteAllTextAsync(filePath, reportContent, Encoding.UTF8);
            App.LoggingService?.LogSuccess($"[ETW_REPORT] Relatório salvo com sucesso em: {filePath}");
        }

        /// <summary>
        /// Obtém a extensão de arquivo para um formato de relatório.
        /// </summary>
        /// <param name="format">Formato do relatório.</param>
        /// <returns>Extensão do arquivo.</returns>
        private string GetFileExtension(EtwReportFormat format)
        {
            switch (format)
            {
                case EtwReportFormat.Text:
                    return ".txt";
                case EtwReportFormat.Html:
                    return ".html";
                case EtwReportFormat.Json:
                    return ".json";
                case EtwReportFormat.Xml:
                    return ".xml";
                default:
                    return ".txt";
            }
        }
    }

    /// <summary>
    /// Formatos de relatório ETW suportados.
    /// </summary>
    public enum EtwReportFormat
    {
        /// <summary>
        /// Formato de texto simples.
        /// </summary>
        Text,

        /// <summary>
        /// Formato HTML.
        /// </summary>
        Html,

        /// <summary>
        /// Formato JSON.
        /// </summary>
        Json,

        /// <summary>
        /// Formato XML.
        /// </summary>
        Xml
    }

    /// <summary>
    /// Gerenciador de perfis de coleta ETW.
    /// </summary>
    public class EtwProfileManager
    {
        private readonly Dictionary<string, EtwCollectionProfile> _profiles;

        /// <summary>
        /// Inicializa uma nova instância de <see cref="EtwProfileManager"/>.
        /// </summary>
        public EtwProfileManager()
        {
            _profiles = new Dictionary<string, EtwCollectionProfile>();
            InitializeDefaultProfiles();
            App.LoggingService?.LogTrace($"[ETW_PROFILE] Gerenciador inicializado com {_profiles.Count} perfis padrão");
        }

        /// <summary>
        /// Obtém um perfil de coleta pelo nome.
        /// </summary>
        /// <param name="profileName">Nome do perfil.</param>
        /// <returns>Perfil de coleta.</returns>
        public EtwCollectionProfile GetProfile(string profileName)
        {
            if (string.IsNullOrEmpty(profileName))
                throw new ArgumentException("O nome do perfil não pode ser vazio.", nameof(profileName));

            _profiles.TryGetValue(profileName, out var profile);
            return profile?.Clone();
        }

        /// <summary>
        /// Obtém todos os perfis disponíveis.
        /// </summary>
        /// <returns>Lista de perfis.</returns>
        public List<EtwCollectionProfile> GetAllProfiles()
        {
            var profiles = new List<EtwCollectionProfile>();
            foreach (var profile in _profiles.Values)
            {
                profiles.Add(profile.Clone());
            }
            return profiles;
        }

        /// <summary>
        /// Inicializa perfis padrão.
        /// </summary>
        private void InitializeDefaultProfiles()
        {
            // Perfil para análise de performance de jogos
            _profiles["GamingPerformance"] = new EtwCollectionProfile
            {
                Name = "GamingPerformance",
                Description = "Perfil otimizado para análise de performance em jogos",
                Providers = new List<EtwProvider>
                {
                    new EtwProvider
                    {
                        NameOrGuid = "Microsoft-Windows-Kernel-Process",
                        Level = EtwEventLevel.Informational,
                        Keywords = 0x1000000000000000 // WINEVENT_KEYWORD_PROCESS
                    },
                    new EtwProvider
                    {
                        NameOrGuid = "Microsoft-Windows-Kernel-Threading",
                        Level = EtwEventLevel.Informational,
                        Keywords = 0x8000000000000000 // WINEVENT_KEYWORD_THREAD
                    },
                    new EtwProvider
                    {
                        NameOrGuid = "Microsoft-Windows-DxgKrnl",
                        Level = EtwEventLevel.Informational,
                        Keywords = 0x1 // Eventos de renderização
                    }
                },
                Filters = new List<EtwFilter>
                {
                    new EtwFilter
                    {
                        FilterType = EtwFilterType.ProcessId,
                        Operator = EtwFilterOperator.GreaterThan,
                        FilterValue = "0"
                    }
                }
            };
            
            // Perfil para análise de stutter
            _profiles["AntiStutter"] = new EtwCollectionProfile
            {
                Name = "AntiStutter",
                Description = "Perfil para identificação e análise de causas de stutter",
                Providers = new List<EtwProvider>
                {
                    new EtwProvider
                    {
                        NameOrGuid = "Microsoft-Windows-Kernel-Process",
                        Level = EtwEventLevel.Verbose,
                        Keywords = 0xFFFFFFFFFFFFFFFF
                    },
                    new EtwProvider
                    {
                        NameOrGuid = "Microsoft-Windows-Kernel-File",
                        Level = EtwEventLevel.Informational,
                        Keywords = 0x8000000000000000 // WINEVENT_KEYWORD_FILE_IO
                    }
                },
                Filters = new List<EtwFilter>
                {
                    new EtwFilter
                    {
                        FilterType = EtwFilterType.Level,
                        Operator = EtwFilterOperator.GreaterThan,
                        FilterValue = "2" // Apenas warnings, errors e critical
                    }
                }
            };
        }
    }

    /// <summary>
    /// Perfil de coleta ETW.
    /// </summary>
    public class EtwCollectionProfile
    {
        /// <summary>
        /// Nome do perfil.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Descrição do perfil.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Provedores de eventos a serem coletados.
        /// </summary>
        public List<EtwProvider> Providers { get; set; } = new List<EtwProvider>();

        /// <summary>
        /// Filtros a serem aplicados durante a coleta.
        /// </summary>
        public List<EtwFilter> Filters { get; set; } = new List<EtwFilter>();

        /// <summary>
        /// Cria uma cópia deste perfil.
        /// </summary>
        /// <returns>Cópia do perfil.</returns>
        public EtwCollectionProfile Clone()
        {
            var providers = new List<EtwProvider>();
            foreach (var provider in Providers)
            {
                providers.Add(new EtwProvider
                {
                    NameOrGuid = provider.NameOrGuid,
                    Level = provider.Level,
                    Keywords = provider.Keywords
                });
            }
            
            var filters = new List<EtwFilter>();
            foreach (var filter in Filters)
            {
                filters.Add(new EtwFilter
                {
                    FilterType = filter.FilterType,
                    FilterValue = filter.FilterValue,
                    Operator = filter.Operator
                });
            }
            
            return new EtwCollectionProfile
            {
                Name = Name,
                Description = Description,
                Providers = providers,
                Filters = filters
            };
        }
    }
}
