using System;
using System.Collections.Generic;
using System.Linq;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Core.SystemIntelligenceProfiler
{
    /// <summary>
    /// MOTOR DE DECISÃO INTELIGENTE (ENTERPRISE GRADE) v3.0
    /// Arquitetura em 5 Camadas: Detecção, Classificação, Matriz de Decisão, Regressão, Simulação.
    /// Foco: Confiabilidade, Segurança e Explicabilidade.
    /// </summary>
    public class DecisionEngine : IDecisionEngine
    {
        public ProfilerReport Evaluate(AuditData audit, UserAnswers answers)
        {
            var report = new ProfilerReport
            {
                Audit = audit,
                Answers = answers,
                Timestamp = DateTime.Now
            };

            // -------------------------------------------------------------
            // CAMADA 1: DETECÇÃO & PRÉ-VALIDAÇÃO (HARDWARE STATE)
            // -------------------------------------------------------------
            report.HealthAlerts = AnalyzeHardwareHealth(audit);
            bool isThermalCritical = audit.ThermalStatus == ThermalTier.Critical;
            bool isThermalWarning = audit.ThermalStatus == ThermalTier.Warm;
            bool isMemoryCritical = audit.Ram.IsMemoryPressure || audit.Ram.AvailableMb < 1024;
            
            // Log inicial de estado
            LogDecision("SYSTEM_STATE", $"Perf: {audit.PerfTier} | Thermal: {audit.ThermalStatus} | RAM Free: {audit.Ram.AvailableMb}MB");

            if (isThermalCritical)
            {
                LogDecision("BLOCK_ALL", "Sistema em estado TÉRMICO CRÍTICO. Abortando otimizações.");
                report.HealthAlerts.Add("🔥 CRÍTICO: Sistema superaquecido. Otimizações suspensas para evitar danos.");
                return report; // Retorna apenas alertas, sem otimizações
            }

            var recommendations = new List<ActionRecommendation>();
            var profile = answers.Profile;
            // "É notebook" não é mais perguntado ao usuário: a presença da bateria é
            // detectada na auditoria (AuditCollector -> BatteryInfo.Present). O
            // checkbox foi removido da tela porque era redundante — e errar ao
            // desmarcar desligava a proteção contra plano de alto consumo em bateria.
            var isLaptop = answers.IsLaptop || audit.Battery.Present;

            // -------------------------------------------------------------
            // CAMADA 2 & 3: MATRIZ DE DECISÃO ESTRUTURADA
            // -------------------------------------------------------------

            // --- A. CPU OPTIMIZATION MATRIX ---
            if (audit.Cpu.PhysicalCores >= 6 && !isThermalWarning && !isThermalCritical)
            {
                // REMOVIDO: aqui havia ActionType.Process_Optimize rotulado como
                // "CPU Core Unparking". O rótulo mentia — Process_Optimize não faz
                // unparking de núcleo: ele baixa a prioridade de ScheduledTasks,
                // WSearch e MsMpEng (o Windows Defender) para BelowNormal.
                // Rebaixar o Defender não traz ganho de CPU para o usuário e
                // reduz a proteção antimalware durante e depois da otimização.
                LogDecision("CPU_SKIP", "Process_Optimize removido: rebaixava o Windows Defender (MsMpEng) sem qualquer ganho real.");
            }
            else
            {
                LogDecision("CPU_SKIP", "CPU com poucos núcleos ou quente. Mantendo padrão do Windows.");
            }

            // Power Plan Logic
            if (isLaptop && audit.Battery.Status != "Carregando" && audit.Battery.EstimatedChargePercent < 20)
            {
                // Force Eco
                AddRec(recommendations,
                    LocalizationService.Instance.GetString("Modo de Emergência de Bateria"),
                    ActionType.PowerPlan_Balanced,
                    RecommendationCategory.Safe, 90, 0,
                    LocalizationService.Instance.GetString("ProfilerEmergencyBatteryDesc"),
                    profile);
            }
            else if (isThermalWarning)
            {
                // Force Balanced/Cool
                AddRec(recommendations,
                    LocalizationService.Instance.GetString("Modo de Eficiência Térmica"),
                    ActionType.PowerPlan_Balanced,
                    RecommendationCategory.Safe, 80, 0,
                    LocalizationService.Instance.GetString("ProfilerThermalEfficiencyDesc"),
                    profile);
            }
            else if ((profile == UserProfile.GamerCompetitive || profile == UserProfile.GamerSinglePlayer || profile == UserProfile.CreativeVideoEditing) && audit.SupportsHighPerformancePlan)
            {
                 AddRec(recommendations,
                    LocalizationService.Instance.GetString("Plano de Alto Desempenho"),
                    ActionType.PowerPlan_HighPerformance,
                    RecommendationCategory.Conditional, 40, isLaptop ? 10 : 0,
                    LocalizationService.Instance.GetString("ProfilerHighPerfPlanDesc"),
                    profile);
            }

            // --- B. GPU & DISPLAY MATRIX ---
            // Regra: Monitor < 144Hz não precisa de tweaks de latência extrema.
            bool highRefresh = audit.Display.RefreshRateHz >= 144;
            
            if (highRefresh && (profile == UserProfile.GamerCompetitive || profile == UserProfile.GamerSinglePlayer))
            {
                // REBAIXADO: Visual_Optimize desliga animações, sombras e suavização
                // de lista. Em um setup de alta taxa de refresh isso remove
                // justamente a fluidez que o usuário está pagando para ter.
                AddRec(recommendations,
                    LocalizationService.Instance.GetString("Otimização de Latência de Display"),
                    ActionType.Visual_Optimize,
                    RecommendationCategory.Conditional, 20, 10,
                    string.Format(LocalizationService.Instance.GetString("ProfilerDisplayLatencyDetected"), audit.Display.RefreshRateHz),
                    profile);
            }

            // Regra: Em hardware fraco (baixa RAM ou poucos núcleos), recomendar otimização visual
            // para reduzir overhead do sistema, independente do perfil de display.
            if (audit.Ram.TotalMb < 8192 || audit.Cpu.LogicalCores <= 2)
            {
                if (!recommendations.Any(r => r.Type == ActionType.Visual_Optimize))
                {
                    AddRec(recommendations,
                        LocalizationService.Instance.GetString("Otimização Visual (Hardware Limitado)"),
                        ActionType.Visual_Optimize,
                        RecommendationCategory.Safe, 35, 0,
                        LocalizationService.Instance.GetString("Adjusts drivers and video buffers for maximum performance on limited hardware."),
                        profile);
                }
            }

            if (!audit.Gpu.IsIntegrated && audit.Gpu.HagsSupported && !isThermalWarning && answers.OptimizeGPU)
            {
                 AddRec(recommendations,
                    LocalizationService.Instance.GetString("HAGS (GPU Scheduling)"),
                    ActionType.Advanced_EnableHags,
                    RecommendationCategory.Safe, 15, 0,
                    LocalizationService.Instance.GetString("ProfilerHagsDesc"),
                    profile);
            }

            // --- C. MEMORY MATRIX ---
            bool allowCaching = audit.Ram.TotalMb >= 16384; // 16GB
            
            if (allowCaching && !isMemoryCritical)
            {
                 // Manter Kernel na RAM (Paging Executive)
                 // REBAIXADO: DisablePagingExecutive impede o SO de paginar
                 // kernel/driver/executáveis. Com little RAM o pageset trava;
                 // mesmo com muita RAM o ganho real é mínimo e não é medido.
                 // Fora do caminho automático.
                 AddRec(recommendations,
                    LocalizationService.Instance.GetString("Manter Kernel na RAM"),
                    ActionType.Memory_Optimize,
                    RecommendationCategory.Risky, 10, 25,
                    LocalizationService.Instance.GetString("ProfilerKernelRamDesc"),
                    profile);
            }
            else if (audit.Ram.TotalMb < 8192)
            {
                 // Bloquear tweaks de cache, focar em limpeza
                 LogDecision("RAM_LIMIT", "RAM baixa. Bloqueando otimizações de cache que consomem memória.");
                 AddRec(recommendations,
                    LocalizationService.Instance.GetString("Otimização de Pagefile (Baixa RAM)"),
                    ActionType.Memory_Optimize,
                    RecommendationCategory.Risky, 60, 30,
                    LocalizationService.Instance.GetString("ProfilerPagefileLowRamDesc"),
                    profile);
            }

            // --- D. STORAGE MATRIX (SSD vs HDD Aware) ---
            bool isSsd = audit.Storage.SystemDiskType.Contains("SSD") || audit.Storage.SystemDiskType.Contains("NVMe");
            
            if (isSsd)
            {
                LogDecision("SSD_DETECTED", $"Identificado drive {audit.Storage.SystemDiskType}. Aplicando matriz SSD Booster.");

                // GANHO REAL E ESPERADO: TRIM devolve blocos ao SSD. É a única
                // desta matriz que o Windows genuinamente precisa.
                AddRec(recommendations, LocalizationService.Instance.GetString("Optimize TRIM (SSD)"), ActionType.Storage_EnableTrim, 
                    RecommendationCategory.Safe, 85, 0, LocalizationService.Instance.GetString("ProfilerStorageTrimDesc"), profile);

                // Daqui para baixo, os itens REMOVEM FUNCIONALIDADE do Windows em
                // troca de ganho de performance não medido. Marcados como
                // Conditional: não entram na aplicação automática.
                // Justificativa de cada um:
                //  - Hibernation: apaga hiberfil.sys e remove o Fast Startup
                //  - Search Indexing: remove a indexação (usuário perde a busca)
                //  - Superfetch/Prefetch: Microsoft recomenda para SSD; desligar
                //    piora o tempo de lançamento de apps
                //  - 8.3 naming: quebra apps legados que dependem de NOME8.3
                //  - Event logs: desliga ETW de boot, prejudica diagnóstico
                AddRec(recommendations, LocalizationService.Instance.GetString("Disable Hibernation (SSD)"), ActionType.Storage_DisableHibernation, 
                    RecommendationCategory.Conditional, 30, 15, LocalizationService.Instance.GetString("ProfilerStorageHibernationDesc"), profile);

                AddRec(recommendations, LocalizationService.Instance.GetString("Optimize Search Indexing (SSD)"), ActionType.Storage_DisableSearchIndexing, 
                    RecommendationCategory.Conditional, 20, 15, LocalizationService.Instance.GetString("ProfilerStorageSearchIndexDesc"), profile);

                AddRec(recommendations, LocalizationService.Instance.GetString("Disable Superfetch/Prefetch (SSD)"), ActionType.Storage_DisableSuperfetch, 
                    RecommendationCategory.Conditional, 20, 10, LocalizationService.Instance.GetString("ProfilerStorageSuperfetchDesc"), profile);

                AddRec(recommendations, LocalizationService.Instance.GetString("Disable 8.3 Naming (SSD)"), ActionType.Storage_Disable83Naming, 
                    RecommendationCategory.Conditional, 15, 15, LocalizationService.Instance.GetString("ProfilerStorage83NamingDesc"), profile);

                AddRec(recommendations, LocalizationService.Instance.GetString("Optimize Event Logs (SSD)"), ActionType.Storage_DisableEventLogging, 
                    RecommendationCategory.Conditional, 10, 10, LocalizationService.Instance.GetString("ProfilerStorageEventLoggingDesc"), profile);

                if (answers.OptimizeDisk)
                {
                    // NtfsDisableLastAccessUpdate quebra backup incremental,
                    // indexadores e antivírus que usam a data de acesso.
                    AddRec(recommendations,
                        LocalizationService.Instance.GetString("Desativar carimbo de Último Acesso"),
                        ActionType.Storage_DisableLastAccess,
                        RecommendationCategory.Conditional, 10, 10,
                        LocalizationService.Instance.GetString("ProfilerStorageLastAccessDesc"),
                        profile);
                }
            }
            else // HDD
            {
                 AddRec(recommendations,
                    LocalizationService.Instance.GetString("Desfragmentação Inteligente"),
                    ActionType.Storage_Defrag,
                    RecommendationCategory.Conditional, 70, 5,
                    LocalizationService.Instance.GetString("ProfilerDefragDesc"),
                    profile);
            }
            
            // --- E. USER-SPECIFIC OVERRIDES (FROM QUESTIONNAIRE) ---

            // Regra: Ativar GamerMode para perfis gamer com hardware suficiente
            bool isGamerProfile = profile == UserProfile.GamerCompetitive || profile == UserProfile.GamerSinglePlayer;
            bool isGamerUseCase = answers.UseCase.Contains("Jogos", StringComparison.OrdinalIgnoreCase)
                                || answers.UseCase.Contains("game", StringComparison.OrdinalIgnoreCase)
                                || answers.UseCase.Contains("gamer", StringComparison.OrdinalIgnoreCase);
            if ((isGamerProfile || isGamerUseCase)
                && audit.Cpu.LogicalCores >= 4 && audit.Ram.TotalMb >= 8192)
            {
                AddRec(recommendations,
                    LocalizationService.Instance.GetString("Ativar Modo Gamer"),
                    ActionType.GamerMode_Activate,
                    RecommendationCategory.Conditional, 50, 5,
                    LocalizationService.Instance.GetString("Activates competitive gaming optimizations for maximum frame rate and minimum latency."),
                    profile);
            }

            if (answers.OptimizeGPU && !audit.Gpu.IsIntegrated)
            {
                // REBAIXADO: este caminho executa ExtremeOptimizationsService,
                // que grava TdrLevel=0 — ou seja, DESABILITA o TDR. Com o TDR
                // desligado, um travamento de driver de GPU não é mais recuperado
                // pelo Windows: a tela fica morta até reinício físico.
                // A própria NVIDIA desaconselha desabilitar o TDR.
                // Não entra em aplicação automática.
                AddRec(recommendations,
                    LocalizationService.Instance.GetString("Otimização Avançada de GPU"),
                    ActionType.General_Optimize,
                    RecommendationCategory.Risky, 35, 40,
                    LocalizationService.Instance.GetString("ProfilerGpuOptimizationDesc"),
                    profile);
            }

            if (answers.ResetNetwork)
            {
                AddRec(recommendations,
                    LocalizationService.Instance.GetString("Reset de Stack de Rede"),
                    ActionType.Network_ResetStack,
                    RecommendationCategory.Safe, 50, 0,
                    LocalizationService.Instance.GetString("ProfilerNetworkResetDesc"),
                    profile);
            }

            // Regra: Flush DNS para casos de uso de rede (streaming, jogos) ou quando ResetNetwork solicitado
            if (answers.ResetNetwork
                || answers.UseCase.Contains("Streaming", StringComparison.OrdinalIgnoreCase)
                || answers.UseCase.Contains("Jogos", StringComparison.OrdinalIgnoreCase))
            {
                AddRec(recommendations,
                    LocalizationService.Instance.GetString("Flush DNS Cache"),
                    ActionType.Network_FlushDns,
                    RecommendationCategory.Safe, 40, 0,
                    LocalizationService.Instance.GetString("Cleans and resets DNS resolver cache to improve network name resolution and reduce latency."),
                    profile);
            }

            if (answers.OptimizeDisk)
            {
                // REBAIXADO: escreve NetworkThrottlingIndex=-1 e
                // SystemResponsiveness=5. O valor 5 é PIOR que o padrão para
                // multimedia (0 é o mais responsivo) — este tweak PREJUDICA
                // jogos em vez de ajudar. Além disso Tasks\Games altera a
                // prioridade de TODOS os jogos da máquina, não só o que estiver
                // rodando, e églobal (não reverte sozinho).
                AddRec(recommendations,
                    LocalizationService.Instance.GetString("Priorização de I/O de Disco"),
                    ActionType.Advanced_OptimizeIrq,
                    RecommendationCategory.Conditional, 25, 20,
                    LocalizationService.Instance.GetString("ProfilerDiskIoPriorityDesc"),
                    profile);
            }

            if (answers.CleanSystem)
            {
                AddRec(recommendations,
                    LocalizationService.Instance.GetString("Limpeza Profunda do Sistema"),
                    ActionType.SystemCleanup,
                    RecommendationCategory.Safe, 45, 0,
                    LocalizationService.Instance.GetString("ProfilerDeepSystemCleanupDesc"),
                    profile);
            }

            if (answers.AutoRestartServices)
            {
                // REBAIXADO: reinicia Spooler (impressora), WSearch, Themes e
                // SysMain. Reiniciar o Spooler corta qualquer trabalho de
                // impressão em andamento, e Themes derruba o visual do shell
                // por um instante. Não é ganho de performance, é manutenção.
                AddRec(recommendations,
                    LocalizationService.Instance.GetString("Reiniciar Serviços Críticos"),
                    ActionType.Advanced_RestartCriticalServices,
                    RecommendationCategory.Conditional, 10, 10,
                    LocalizationService.Instance.GetString("ProfilerRestartCriticalServicesDesc"),
                    profile);
            }

            // Universal Storage (Keep as fallback if not already added)
            if (!answers.CleanSystem && !recommendations.Any(r => r.Type == ActionType.SystemCleanup))
            {
                AddRec(recommendations,
                    LocalizationService.Instance.GetString("Limpeza de Sistema"),
                    ActionType.SystemCleanup,
                    RecommendationCategory.Safe, 20, 0,
                    LocalizationService.Instance.GetString("ProfilerSystemCleanupDesc"),
                    profile);
            }


            // -------------------------------------------------------------
            // CAMADA 4 & 5: PREVENÇÃO DE REGRESSÃO E SIMULAÇÃO
            // -------------------------------------------------------------
            // Filtro final: Simular risco acumulado
            
            var finalRecommendations = new List<ActionRecommendation>();
            int accumulatedRisk = 0;

            foreach (var rec in recommendations)
            {
                // Check simulado
                if (SimulateRisk(rec, audit, isLaptop, accumulatedRisk))
                {
                    finalRecommendations.Add(rec);
                    accumulatedRisk += rec.RiskScore;
                }
                else
                {
                    LogDecision("RISK_REJECT", $"Rejeitado por risco excessivo: {rec.Name}");
                }
            }

            report.Recommendations = SortRecommendations(finalRecommendations);
            return report;
        }

        private List<string> AnalyzeHardwareHealth(AuditData audit)
        {
            var alerts = new List<string>();

            if (audit.CpuTemp > 90) alerts.Add($"🔥 PERIGO: CPU em {audit.CpuTemp:F1}°C.");
            if (audit.GpuTemp > 90) alerts.Add($"🔥 PERIGO: GPU em {audit.GpuTemp:F1}°C.");
            if (!audit.Storage.SmartOk) alerts.Add("💀 FALHA DE DISCO: Erro SMART detectado.");
            if (audit.Ram.IsMemoryPressure) alerts.Add("⚠️ MEMÓRIA CHEIA: Sistema operando no limite da RAM.");

            return alerts;
        }

        private void AddRec(List<ActionRecommendation> list, string name, ActionType type, RecommendationCategory cat, int gain, int risk, string explanation, UserProfile profile)
        {
            // Validação de Perfil na entrada
            if (cat == RecommendationCategory.Risky && (profile == UserProfile.WorkOffice || profile == UserProfile.EnterpriseSecure))
            {
                LogDecision("PROFILE_REJECT", $"Blocked Risky tweak '{name}' for Enterprise profile.");
                return;
            }

            list.Add(new ActionRecommendation
            {
                Name = name,
                Type = type,
                Category = cat,
                ExpectedGainScore = gain,
                RiskScore = risk,
                Supported = true,
                Explanation = explanation,
                Module = "DecisionEngine"
            });
            LogDecision("CANDIDATE", $"Proposed: {name} (Gain: {gain}, Risk: {risk})");
        }

        private bool SimulateRisk(ActionRecommendation rec, AuditData audit, bool isLaptop, int currentSystemRisk)
        {
            // 1. Laptop Battery Protection
            if (isLaptop && rec.Type == ActionType.PowerPlan_HighPerformance && audit.Battery.Status != "Carregando")
            {
                 LogDecision("SIM_FAIL", "High Perf blocked on battery.");
                 return false;
            }

            // 2. Thermal Protection
            if (audit.ThermalStatus != ThermalTier.Stable && rec.Type == ActionType.GamerMode_Activate)
            {
                LogDecision("SIM_FAIL", "Gamer Mode blocked due to thermals.");
                return false;
            }
            
            // 3. Cumulative Risk verification
            if (currentSystemRisk + rec.RiskScore > 30) // Hard limit de risco acumulado por sessão
            {
                LogDecision("SIM_FAIL", "Risk budget exceeded.");
                return false;
            }

            return true;
        }

        private List<ActionRecommendation> SortRecommendations(List<ActionRecommendation> list)
        {
            return list.OrderByDescending(r => r.Category == RecommendationCategory.Safe)
                       .ThenByDescending(r => r.ExpectedGainScore)
                       .ToList();
        }

        private void LogDecision(string tag, string message)
        {
            try { App.LoggingService?.LogInfo($"[DECISION::{tag}] {message}"); } catch {}
        }
    }
}
