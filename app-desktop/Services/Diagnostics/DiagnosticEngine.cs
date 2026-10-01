using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Monitoring;

namespace VoltrisOptimizer.Services.Diagnostics
{
    /// <summary>
    /// Engine diagnóstica: traduz problemas técnicos em diagnósticos legíveis
    /// OBJETIVO: Explicar O QUE está errado, POR QUE e COMO corrigir
    /// </summary>
    public class DiagnosticEngine
    {

        private readonly MetricsCollector _metrics;
        private readonly SelfImpactMonitor _selfImpactMonitor;
        private readonly ILoggingService _logger;
        
        public DiagnosticEngine(
            MetricsCollector metrics,
            SelfImpactMonitor selfImpactMonitor,
            ILoggingService logger)
        {
            _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
            _selfImpactMonitor = selfImpactMonitor ?? throw new ArgumentNullException(nameof(selfImpactMonitor));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        /// <summary>
        /// Gera relatório diagnóstico completo
        /// </summary>
        public async Task<DiagnosticReport> GenerateReportAsync()
        {
            var report = new DiagnosticReport();
            
            try
            {
                _logger.LogInfo("[Diagnostic] Gerando relatório diagnóstico...");
                
                // Obter métricas atuais
                var metrics = await _metrics.GetCurrentMetricsAsync();
                report.Metrics = metrics;
                
                // ANÁLISE 1: Performance Gaming
                await AnalyzeGamingPerformanceAsync(report, metrics);
                
                // ANÁLISE 2: Input Latency
                await AnalyzeInputLatencyAsync(report, metrics);
                
                // ANÁLISE 3: Micro-Stutter
                await AnalyzeMicroStutterAsync(report, metrics);
                
                // ANÁLISE 4: Recursos do Sistema
                await AnalyzeSystemResourcesAsync(report, metrics);
                
                // ANÁLISE 5: Auto-Impacto do VOLTRIS
                await AnalyzeSelfImpactAsync(report);
                
                // Calcular saúde geral
                report.OverallHealth = CalculateOverallHealth(report);
                
                _logger.LogSuccess($"[Diagnostic] Relatório gerado: {report.Diagnoses.Count} diagnósticos, saúde={report.OverallHealth}");
            }
            catch (Exception ex)
            {
                _logger.LogError("[Diagnostic] Erro ao gerar relatório", ex);
            }
            
            return report;
        }
        
        /// <summary>
        /// Analisa performance em jogos
        /// </summary>
        private async Task AnalyzeGamingPerformanceAsync(DiagnosticReport report, VoltrisOptimizer.Services.Monitoring.SystemMetrics metrics)
        {
            // FPS baixo
            if (metrics.Fps > 0 && metrics.Fps < 60)
            {
                report.AddDiagnosis(new Diagnosis
                {
                    Severity = metrics.Fps < 30 ? Severity.Critical : Severity.Warning,
                    Category = "Performance",
                    Title = "FPS Abaixo do Esperado",
                    Description = $"Seu jogo está rodando a {metrics.Fps:F0} FPS (esperado: ≥60 FPS)",
                    Impact = metrics.Fps < 30 
                        ? "Experiência de jogo severamente degradada, jogo quase injogável"
                        : "Experiência de jogo degradada, stuttering visível",
                    TechnicalContext = $"FrameTime médio: {metrics.FrameTime:F2}ms (target: 16.67ms @ 60fps)",
                    PossibleCauses = new[]
                    {
                        "GPU sobrecarregada ou subutilizada",
                        "Processos em background consumindo recursos",
                        "Driver de vídeo desatualizado ou corrompido",
                        "Thermal throttling (CPU/GPU superaquecendo)",
                        "VRAM insuficiente para configurações gráficas atuais",
                        "Modo de energia não otimizado (Balanced ou Power Saver)"
                    },
                    RecommendedActions = new[]
                    {
                        "Ativar Modo Gamer para fechar processos desnecessários",
                        "Reduzir configurações gráficas do jogo (textura, sombras, anti-aliasing)",
                        "Atualizar drivers de vídeo (NVIDIA/AMD)",
                        "Verificar temperaturas (usar HWMonitor ou MSI Afterburner)",
                        "Ativar High Performance Power Plan",
                        "Verificar se VRAM está saturada (usar GPU-Z)"
                    }
                });
            }
        }
        
        /// <summary>
        /// Analisa latência de input
        /// </summary>
        private async Task AnalyzeInputLatencyAsync(DiagnosticReport report, VoltrisOptimizer.Services.Monitoring.SystemMetrics metrics)
        {
            if (metrics.InputLatency > 30)
            {
                report.AddDiagnosis(new Diagnosis
                {
                    Severity = metrics.InputLatency > 50 ? Severity.Critical : Severity.Warning,
                    Category = "Latência",
                    Title = "Latência de Input Elevada",
                    Description = $"Input lag detectado: {metrics.InputLatency:F1}ms (ideal: <20ms)",
                    Impact = metrics.InputLatency > 50
                        ? "Controles extremamente lentos, impossível jogos competitivos"
                        : "Controles lentos, resposta atrasada em jogos competitivos",
                    TechnicalContext = $"Dispatcher round-trip time: {metrics.InputLatency:F1}ms",
                    PossibleCauses = new[]
                    {
                        "VSync ativado (triple buffering adiciona 2-3 frames de lag)",
                        "Modo de energia não é High Performance",
                        "DPC latency alta (drivers problemáticos)",
                        "Polling rate do mouse/teclado baixo (125Hz)",
                        "Jogo em modo janela (windowed mode) em vez de fullscreen",
                        "Wireless mouse/keyboard com alta latência",
                        "Background processes interferindo"
                    },
                    RecommendedActions = new[]
                    {
                        "Desabilitar VSync nas configurações do jogo",
                        "Ativar High Performance Power Plan",
                        "Executar DPC Latency Checker (LatencyMon)",
                        "Aumentar polling rate para 1000Hz (mouse gamer)",
                        "Usar Fullscreen Exclusive mode",
                        "Usar mouse/teclado com fio",
                        "Ativar Modo Gamer do VOLTRIS"
                    }
                });
            }
        }
        
        /// <summary>
        /// Analisa micro-stutter
        /// </summary>
        private async Task AnalyzeMicroStutterAsync(DiagnosticReport report, VoltrisOptimizer.Services.Monitoring.SystemMetrics metrics)
        {
            if (metrics.FrameTimeVariance > 8)
            {
                report.AddDiagnosis(new Diagnosis
                {
                    Severity = metrics.FrameTimeVariance > 15 ? Severity.Critical : Severity.Warning,
                    Category = "Stuttering",
                    Title = "Micro-Stutter Detectado",
                    Description = $"Variação de frametime: {metrics.FrameTimeVariance:F2}ms (ideal: <5ms)",
                    Impact = "Jogo parece 'travado' mesmo com FPS médio alto - experiência inconsistente",
                    TechnicalContext = $"Desvio padrão de frametime: {metrics.FrameTimeVariance:F2}ms indica instabilidade",
                    PossibleCauses = new[]
                    {
                        "CPU throttling (power management agressivo)",
                        "Background tasks interrompendo jogo (antivírus, Windows Update)",
                        "RAM com latência alta (timings ruins no BIOS)",
                        "VOLTRIS executando operações pesadas (Adaptive Governor)",
                        "CPU parking ativo (cores desligando/religando)",
                        "CPU híbrida (P-cores/E-cores) com scheduling ruim",
                        "Game Bar / DVR do Windows ativo"
                    },
                    RecommendedActions = new[]
                    {
                        "Desabilitar CPU parking (usando Park Control)",
                        "Desabilitar Adaptive Governor no VOLTRIS",
                        "Usar Process Lasso para afinidade de CPU",
                        "Verificar timings da RAM no BIOS (XMP profile)",
                        "Desabilitar Game Bar (Configurações > Jogos > Game Bar)",
                        "Desabilitar Windows Update temporariamente",
                        "Isolar jogo em P-cores (CPUs híbridas)"
                    }
                });
            }
        }
        
        /// <summary>
        /// Analisa recursos do sistema
        /// </summary>
        private async Task AnalyzeSystemResourcesAsync(DiagnosticReport report, VoltrisOptimizer.Services.Monitoring.SystemMetrics metrics)
        {
            // CPU alta
            if (metrics.CpuUsage > 90)
            {
                report.AddDiagnosis(new Diagnosis
                {
                    Severity = Severity.Warning,
                    Category = "Recursos",
                    Title = "CPU Sobrecarregada",
                    Description = $"CPU em {metrics.CpuUsage:F1}% (crítico acima de 90%)",
                    Impact = "Bottleneck de CPU - jogo limitado por processador",
                    PossibleCauses = new[]
                    {
                        "Processos em background consumindo CPU",
                        "Jogo CPU-bound (física, IA, simulação)",
                        "Drivers problemáticos (DPC alta)"
                    },
                    RecommendedActions = new[]
                    {
                        "Ativar Modo Gamer para fechar processos",
                        "Reduzir configurações CPU-intensive (física, NPCs)",
                        "Verificar Task Manager para processos suspeitos"
                    }
                });
            }
            
            // RAM alta
            if (metrics.RamPressure > 85)
            {
                report.AddDiagnosis(new Diagnosis
                {
                    Severity = metrics.RamPressure > 95 ? Severity.Critical : Severity.Warning,
                    Category = "Recursos",
                    Title = "Pressão de Memória RAM",
                    Description = $"RAM em {metrics.RamPressure:F1}% (crítico acima de 90%)",
                    Impact = metrics.RamPressure > 95
                        ? "Sistema começará a usar disco (swap) - stuttering severo garantido"
                        : "Memória próxima do limite - risco de stuttering",
                    PossibleCauses = new[]
                    {
                        "Muitos programas abertos simultaneamente",
                        "Memory leak em jogo ou aplicação",
                        "RAM insuficiente para jogo + sistema"
                    },
                    RecommendedActions = new[]
                    {
                        "Executar Limpeza do VOLTRIS",
                        "Fechar navegadores e programas desnecessários",
                        "Considerar upgrade de RAM (mínimo 16GB para gaming)"
                    }
                });
            }
        }
        
        /// <summary>
        /// Analisa se VOLTRIS está causando problemas
        /// </summary>
        private async Task AnalyzeSelfImpactAsync(DiagnosticReport report)
        {
            var selfImpact = await _selfImpactMonitor.AnalyzeSelfImpactAsync();
            
            if (selfImpact.ImpactDetected)
            {
                report.AddDiagnosis(new Diagnosis
                {
                    Severity = selfImpact.TotalImpactMs > 10 ? Severity.Critical : Severity.Warning,
                    Category = "Auto-Diagnóstico",
                    Title = "VOLTRIS Pode Estar Causando Degradação",
                    Description = $"Módulo '{selfImpact.CulpritModule}' adicionou {selfImpact.TotalImpactMs:F1}ms de latência",
                    Impact = "O próprio otimizador está causando o problema que tenta resolver (irônico)",
                    TechnicalContext = $"Correlação temporal detectada: {selfImpact.SuspiciousActions.Count} ações suspeitas",
                    PossibleCauses = new[]
                    {
                        $"Módulo '{selfImpact.CulpritModule}' executando operações pesadas",
                        "Monitoramento muito agressivo (polling rápido demais)",
                        "WMI queries bloqueando thread principal",
                        "Adaptive Governor causando CPU spikes"
                    },
                    RecommendedActions = new[]
                    {
                        $"Desabilitar temporariamente: {selfImpact.CulpritModule}",
                        "Reduzir frequência de monitoramento (5s → 30s)",
                        "Aumentar cache TTL (30s → 60s)",
                        "Desabilitar Adaptive Governor",
                        "Reportar issue aos desenvolvedores no GitHub"
                    }
                });
            }
        }
        
        /// <summary>
        /// Calcula saúde geral do sistema
        /// </summary>
        private string CalculateOverallHealth(DiagnosticReport report)
        {
            var criticalCount = report.Diagnoses.Count(d => d.Severity == Severity.Critical);
            var warningCount = report.Diagnoses.Count(d => d.Severity == Severity.Warning);
            
            if (criticalCount > 0)
                return LocalizationService.Instance.GetString("DiagnosticStatusCritical");
            if (warningCount > 2)
                return LocalizationService.Instance.GetString("DiagnosticStatusPoor");
            if (warningCount > 0)
                return LocalizationService.Instance.GetString("DiagnosticStatusModerate");
                
            return LocalizationService.Instance.GetString("DiagnosticStatusExcellent");
        }
        
        /// <summary>
        /// Gera relatório legível para usuário
        /// </summary>
        public string GenerateUserFriendlyReport(DiagnosticReport report)
        {
            var sb = new StringBuilder();
            sb.AppendLine("═══════════════════════════════════════════════════");
            sb.AppendLine("🩺 DIAGNÓSTICO VOLTRIS - ANÁLISE COMPLETA");
            sb.AppendLine("═══════════════════════════════════════════════════");
            sb.AppendLine();
            sb.AppendLine($"📊 Saúde Geral: {GetHealthEmoji(report.OverallHealth)} {report.OverallHealth}");
            sb.AppendLine($"⏰ {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
            sb.AppendLine();
            sb.AppendLine("📈 Métricas Atuais:");
            sb.AppendLine($"   CPU: {report.Metrics.CpuUsage:F1}% | RAM: {report.Metrics.RamPressure:F1}%");
            if (report.Metrics.Fps > 0)
            {
                sb.AppendLine($"   FPS: {report.Metrics.Fps:F0} | FrameTime: {report.Metrics.FrameTime:F2}ms");
                sb.AppendLine($"   Input Lag: {report.Metrics.InputLatency:F1}ms | Variance: {report.Metrics.FrameTimeVariance:F2}ms");
            }
            sb.AppendLine();
            
            if (report.Diagnoses.Count == 0)
            {
                sb.AppendLine("✅ SISTEMA SAUDÁVEL");
                sb.AppendLine("Nenhum problema detectado. Seu sistema está otimizado!");
            }
            else
            {
                sb.AppendLine($"⚠️ {report.Diagnoses.Count} PROBLEMA(S) DETECTADO(S)");
                sb.AppendLine();
                
                foreach (var diagnosis in report.Diagnoses.OrderByDescending(d => d.Severity))
                {
                    var icon = GetSeverityEmoji(diagnosis.Severity);
                    
                    sb.AppendLine($"{icon} {diagnosis.Title}");
                    sb.AppendLine($"   📝 {diagnosis.Description}");
                    sb.AppendLine($"   💥 Impacto: {diagnosis.Impact}");
                    sb.AppendLine($"   🔍 Possíveis Causas:");
                    foreach (var cause in diagnosis.PossibleCauses.Take(3))
                        sb.AppendLine($"      • {cause}");
                    sb.AppendLine($"   ✅ Ações Recomendadas:");
                    foreach (var action in diagnosis.RecommendedActions.Take(3))
                        sb.AppendLine($"      → {action}");
                    sb.AppendLine();
                }
            }
            
            sb.AppendLine("═══════════════════════════════════════════════════");
            
            return sb.ToString();
        }
        
        private string GetHealthEmoji(string health)
        {
            // Funciona com PT, ES, EN
            if (health.StartsWith("Exc", StringComparison.OrdinalIgnoreCase) || 
                health.StartsWith("Exc", StringComparison.OrdinalIgnoreCase) ||
                health.Equals("Excellent", StringComparison.OrdinalIgnoreCase))
                return "🟢";
            
            if (health.StartsWith("Mod", StringComparison.OrdinalIgnoreCase) ||
                health.Equals("Moderate", StringComparison.OrdinalIgnoreCase))
                return "🟡";
                
            if (health.StartsWith("Ru", StringComparison.OrdinalIgnoreCase) ||
                health.StartsWith("Mal", StringComparison.OrdinalIgnoreCase) ||
                health.Equals("Poor", StringComparison.OrdinalIgnoreCase))
                return "🟠";
                
            if (health.StartsWith("Cr", StringComparison.OrdinalIgnoreCase) ||
                health.Equals("Critical", StringComparison.OrdinalIgnoreCase))
                return "🔴";
                
            return "⚪";
        }
        
        private string GetSeverityEmoji(Severity severity)
        {
            return severity switch
            {
                Severity.Critical => "🔴",
                Severity.Warning => "🟡",
                Severity.Info => "🟢",
                _ => "⚪"
            };
        }
    }
    
    #region Data Classes
    
    /// <summary>
    /// Relatório diagnóstico completo
    /// </summary>
    public class DiagnosticReport
    {
        public VoltrisOptimizer.Services.Monitoring.SystemMetrics Metrics { get; set; } = new();
        public List<Diagnosis> Diagnoses { get; set; } = new();
        public string OverallHealth { get; set; } = "Desconhecido";
        
        public void AddDiagnosis(Diagnosis diagnosis)
        {
            Diagnoses.Add(diagnosis);
        }
    }
    
    /// <summary>
    /// Diagnóstico individual
    /// </summary>
    public class Diagnosis
    {
        public Severity Severity { get; set; }
        public string Category { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Impact { get; set; } = string.Empty;
        public string TechnicalContext { get; set; } = string.Empty;
        public string[] PossibleCauses { get; set; } = Array.Empty<string>();
        public string[] RecommendedActions { get; set; } = Array.Empty<string>();
    }
    
    /// <summary>
    /// Severidade do diagnóstico
    /// </summary>
    public enum Severity
    {
        Info,
        Warning,
        Critical
    }
    
    #endregion
}
