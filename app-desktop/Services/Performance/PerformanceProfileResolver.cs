using System;
using System.Collections.Generic;
using System.Linq;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.Performance
{
    /// <summary>
    /// Resolve plano de execução de otimizações de performance baseado no Perfil Inteligente
    /// Similar ao UltraCleanProfileResolver, mas para otimizações de performance
    /// Enterprise-grade: Políticas declarativas, auditável, extensível
    /// </summary>
    public class PerformanceProfileResolver
    {
        private readonly SettingsService _settingsService;
        private readonly ILoggingService _logger;

        public PerformanceProfileResolver(SettingsService settingsService, ILoggingService logger)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Resolve plano de execução baseado no Perfil Inteligente ativo
        /// </summary>
        public PerformanceExecutionPlan ResolveExecutionPlan()
        {
            try
            {
                var profile = _settingsService.Settings.IntelligentProfile;
                _logger.LogInfo($"[PerformanceProfileResolver] Resolvendo plano para perfil: {profile}");

                var policy = ExtractPolicyFromProfile(profile);
                var plan = GeneratePlanFromPolicy(policy, profile);

                _logger.LogSuccess($"[PerformanceProfileResolver] Plano gerado: {plan.AuthorizedOptimizations.Count} otimizações autorizadas");

                return plan;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[PerformanceProfileResolver] Erro ao resolver plano: {ex.Message}", ex);
                
                // Fallback seguro: plano conservador
                return CreateFallbackPlan();
            }
        }

        /// <summary>
        /// Extrai política de performance do perfil inteligente
        /// </summary>
        private PerformancePolicy ExtractPolicyFromProfile(IntelligentProfileType profile)
        {
            return profile switch
            {
                // Perfis Gamer: Máxima performance, todas otimizações permitidas
                IntelligentProfileType.GamerCompetitive => new PerformancePolicy
                {
                    AllowRAMOptimization = true,
                    AllowRegistryChanges = true,
                    AllowServiceChanges = true,
                    AllowStartupChanges = true,
                    AllowPowerPlanChanges = true,
                    AllowVisualEffectsChanges = true,
                    MaxRiskLevel = RiskLevel.High,
                    RequiresAudit = false
                },
                
                IntelligentProfileType.GamerSinglePlayer => new PerformancePolicy
                {
                    AllowRAMOptimization = true,
                    AllowRegistryChanges = true,
                    AllowServiceChanges = true,
                    AllowStartupChanges = true,
                    AllowPowerPlanChanges = true,
                    AllowVisualEffectsChanges = true,
                    MaxRiskLevel = RiskLevel.High,
                    RequiresAudit = false
                },

                // [FIX:GAMER-VARIANTS] AS TRÊS VARIANTES QUE FALTAVAM.
                //
                // BUG REAL: o switch mapeava 7 dos 10 perfis. Simulação, MMO e
                // Estratégia caíam no `_ =>` da linha 148, que devolve
                //     new PerformancePolicy { MaxRiskLevel = RiskLevel.Medium }
                // — e um `PerformancePolicy` novo tem TODOS os `Allow*` em
                // false por default. Ou seja: escolher "Gamer Simulação"
                // produzia uma política de risco MÉDIO com TODAS as otimizações
                // PROIBIDAS. O mais permissivo dos perfis virava o mais
                // restritivo, silenciosamente.
                //
                // Isto importa mais agora: com a sub-escolha gamer o usuário
                // passou a PODER escolher essas três variantes, e a política
                // errada teria efeitos reais de sistema.
                //
                // As três são de família gamer, então compartilham a mesma
                // política de `GamerSinglePlayer`. A diferença de comportamento
                // entre elas é feita pelo plano de energia e pelas regras de
                // jogo, que são decididos em outro lugar — não pela lista de
                // permissões, que é o que este método devolve.
                IntelligentProfileType.GamerSimulation => new PerformancePolicy
                {
                    AllowRAMOptimization = true,
                    AllowRegistryChanges = true,
                    AllowServiceChanges = true,
                    AllowStartupChanges = true,
                    AllowPowerPlanChanges = true,
                    AllowVisualEffectsChanges = true,
                    MaxRiskLevel = RiskLevel.High,
                    RequiresAudit = false
                },

                IntelligentProfileType.GamerMMO => new PerformancePolicy
                {
                    AllowRAMOptimization = true,
                    AllowRegistryChanges = true,
                    AllowServiceChanges = true,
                    AllowStartupChanges = true,
                    AllowPowerPlanChanges = true,
                    AllowVisualEffectsChanges = true,
                    MaxRiskLevel = RiskLevel.High,
                    RequiresAudit = false
                },

                IntelligentProfileType.GamerStrategy => new PerformancePolicy
                {
                    AllowRAMOptimization = true,
                    AllowRegistryChanges = true,
                    AllowServiceChanges = true,
                    AllowStartupChanges = true,
                    AllowPowerPlanChanges = true,
                    AllowVisualEffectsChanges = true,
                    MaxRiskLevel = RiskLevel.High,
                    RequiresAudit = false
                },

                // Perfil Work: Conservador, sem mudanças agressivas
                IntelligentProfileType.WorkOffice => new PerformancePolicy
                {
                    AllowRAMOptimization = false, // Não otimizar RAM agressivamente
                    AllowRegistryChanges = false, // Não modificar registro
                    AllowServiceChanges = false,  // Não desabilitar serviços
                    AllowStartupChanges = true,   // Permitir otimizar inicialização
                    AllowPowerPlanChanges = false, // Não mudar plano de energia
                    AllowVisualEffectsChanges = false, // Manter efeitos visuais
                    MaxRiskLevel = RiskLevel.Low,
                    RequiresAudit = false
                },

                // Perfil Enterprise: Máxima segurança, auditoria obrigatória
                IntelligentProfileType.EnterpriseSecure => new PerformancePolicy
                {
                    AllowRAMOptimization = false,
                    AllowRegistryChanges = false,
                    AllowServiceChanges = false,
                    AllowStartupChanges = false,
                    AllowPowerPlanChanges = false,
                    AllowVisualEffectsChanges = false,
                    MaxRiskLevel = RiskLevel.None,
                    RequiresAudit = true
                },

                // Perfil Creative: Balanceado, prioriza estabilidade
                IntelligentProfileType.CreativeVideoEditing => new PerformancePolicy
                {
                    AllowRAMOptimization = true,  // Permitir otimização de RAM
                    AllowRegistryChanges = false, // Não modificar registro
                    AllowServiceChanges = false,  // Não desabilitar serviços
                    AllowStartupChanges = true,
                    AllowPowerPlanChanges = true, // Permitir plano de energia alto desempenho
                    AllowVisualEffectsChanges = false,
                    MaxRiskLevel = RiskLevel.Medium,
                    RequiresAudit = false
                },

                // Perfil Developer: Balanceado
                IntelligentProfileType.DeveloperProgramming => new PerformancePolicy
                {
                    AllowRAMOptimization = true,
                    AllowRegistryChanges = false,
                    AllowServiceChanges = false,
                    AllowStartupChanges = true,
                    AllowPowerPlanChanges = true,
                    AllowVisualEffectsChanges = false,
                    MaxRiskLevel = RiskLevel.Medium,
                    RequiresAudit = false
                },

                // Perfil General: Balanceado padrão
                IntelligentProfileType.GeneralBalanced => new PerformancePolicy
                {
                    AllowRAMOptimization = true,
                    AllowRegistryChanges = true,
                    AllowServiceChanges = true,
                    AllowStartupChanges = true,
                    AllowPowerPlanChanges = true,
                    AllowVisualEffectsChanges = true,
                    MaxRiskLevel = RiskLevel.Medium,
                    RequiresAudit = false
                },

                // [FIX:GAMER-VARIANTS] O `default` NÃO PODE SER SILENCIOSO.
                //
                // Este `_ =>` era o que escondia as três variantes gamer: elas
                // caíam aqui e recebiam um `PerformancePolicy` com todos os
                // `Allow*` em false — o perfil mais permissivo do sistema virava
                // o mais restritivo, sem erro, sem log, sem nada na tela.
                //
                // Um `default` que se limita a "médio" é o tipo de coisa que
                // passa despercebida por anos: o código compila, os testes
                // passam, e o defeito só aparece quando alguém escolhe o perfil
                // na interface.
                //
                // A partir de agora o `default` é `GeneralBalanced` de verdade —
                // a política completa, com as permissões explícitas — e qualquer
                // `IntelligentProfileType` novo cai no comportamento SENSATO em
                // vez de silenciosamente restrito.
                //
                // O comentário acima é deliberado: o objetivo é que este default
                // pareça óbvio demais e force quem mexer aqui a conferir o enum
                // ANTES de acrescentar um perfil novo. Foi a distância entre o
                // que o código presume e o que ele realmente faz — o `default`
                // calado oposto ao switch explícito logo acima — que escondeu as
                // três variantes gamer por anos.
                _ => new PerformancePolicy
                {
                    AllowRAMOptimization = true,
                    AllowRegistryChanges = true,
                    AllowServiceChanges = true,
                    AllowStartupChanges = true,
                    AllowPowerPlanChanges = true,
                    AllowVisualEffectsChanges = true,
                    MaxRiskLevel = RiskLevel.Medium,
                    RequiresAudit = false
                }
            };
        }

        /// <summary>
        /// Gera plano de execução baseado na política
        /// </summary>
        private PerformanceExecutionPlan GeneratePlanFromPolicy(PerformancePolicy policy, IntelligentProfileType profile)
        {
            var authorizedOptimizations = new Dictionary<string, OptimizationAuthorization>(StringComparer.OrdinalIgnoreCase);

            // Definir otimizações disponíveis e suas características - NOMES DEVEM BATER COM UltraPerformanceService
            var optimizations = new Dictionary<string, OptimizationMetadata>
            {
                // Memória
                { "Otimização de Compressão de Memória", new OptimizationMetadata { RequiresRAM = true, RiskLevel = RiskLevel.Low } },
                { "Ajuste de Prioridade de Memória", new OptimizationMetadata { RequiresRAM = true, RiskLevel = RiskLevel.Low } },
                { "Limite de Cache do Sistema", new OptimizationMetadata { RequiresRAM = true, RiskLevel = RiskLevel.Medium } },
                { "Otimização de Superfetch", new OptimizationMetadata { RequiresRAM = true, RiskLevel = RiskLevel.Medium } },
                
                // CPU
                { "Governor Adaptativo de CPU", new OptimizationMetadata { RequiresPowerPlan = true, RiskLevel = RiskLevel.High } },
                { "Otimização de Threads", new OptimizationMetadata { RequiresStartup = true, RiskLevel = RiskLevel.Medium } },
                { "Desativação de Mitigações Spectre/Meltdown", new OptimizationMetadata { RequiresRegistry = true, RiskLevel = RiskLevel.High } },
                { "Ajuste de Quantum Length", new OptimizationMetadata { RequiresRegistry = true, RiskLevel = RiskLevel.Medium } },
                { "Prioridade Multimídia & Jogos", new OptimizationMetadata { RequiresPowerPlan = true, RiskLevel = RiskLevel.Low } },
                { "Power Throttling & Core Parking", new OptimizationMetadata { RequiresPowerPlan = true, RiskLevel = RiskLevel.Low } },
                
                // Disco
                { "Otimização de Prefetch", new OptimizationMetadata { RequiresRegistry = true, RiskLevel = RiskLevel.Low } },
                { "Desfragmentação Inteligente", new OptimizationMetadata { RequiresStartup = true, RiskLevel = RiskLevel.Low } },
                { "Otimização de NVMe", new OptimizationMetadata { RequiresRegistry = true, RiskLevel = RiskLevel.Medium } },
                { "Ajuste de Latência de Disco", new OptimizationMetadata { RequiresRegistry = true, RiskLevel = RiskLevel.Medium } },
                { "Otimização de Write-Cache", new OptimizationMetadata { RequiresRegistry = true, RiskLevel = RiskLevel.Medium } },
                { "SSD Boost Mode (Replica SSD Booster)", new OptimizationMetadata { RequiresRegistry = true, RiskLevel = RiskLevel.Medium } },
                
                // Rede
                { "TCP/IP Tuning", new OptimizationMetadata { RequiresRegistry = true, RiskLevel = RiskLevel.Medium } },
                { "Buffer de Rede", new OptimizationMetadata { RequiresRegistry = true, RiskLevel = RiskLevel.Medium } },
                { "Desativação de NetBIOS", new OptimizationMetadata { RequiresRegistry = true, RiskLevel = RiskLevel.Low } },
                { "Otimização de Placa de Rede", new OptimizationMetadata { RequiresRegistry = true, RiskLevel = RiskLevel.High } },
                
                // Visual
                { "Animações do Sistema", new OptimizationMetadata { RequiresVisualEffects = true, RiskLevel = RiskLevel.Low } },
                { "Efeitos Visuais", new OptimizationMetadata { RequiresVisualEffects = true, RiskLevel = RiskLevel.Low } },
                { "Transparências", new OptimizationMetadata { RequiresVisualEffects = true, RiskLevel = RiskLevel.Low } },
                { "Aceleração de Hardware", new OptimizationMetadata { RequiresVisualEffects = true, RiskLevel = RiskLevel.Medium } },
                { "Otimização de Janela DirectX", new OptimizationMetadata { RequiresVisualEffects = true, RiskLevel = RiskLevel.Low } },
                
                // Inicialização
                { "Timeout de Boot", new OptimizationMetadata { RequiresStartup = true, RiskLevel = RiskLevel.Low } },
                { "Programas na Inicialização", new OptimizationMetadata { RequiresStartup = true, RiskLevel = RiskLevel.Low } },
                { "Serviços de Inicialização", new OptimizationMetadata { RequiresStartup = true, RiskLevel = RiskLevel.High } },
                { "Fast Startup", new OptimizationMetadata { RequiresStartup = true, RiskLevel = RiskLevel.Low } },
                
                // Essenciais
                { "Telemetria", new OptimizationMetadata { RequiresRegistry = true, RiskLevel = RiskLevel.Low } },
                { "Defender", new OptimizationMetadata { RequiresRegistry = true, RiskLevel = RiskLevel.Low } },
                { "Updates", new OptimizationMetadata { RequiresRegistry = true, RiskLevel = RiskLevel.Low } },
                { "Power Plan", new OptimizationMetadata { RequiresPowerPlan = true, RiskLevel = RiskLevel.Low } },
                { "Ajustes de Sistema Avançados", new OptimizationMetadata { RequiresRegistry = true, RiskLevel = RiskLevel.Low } },
                { "Privacidade e Apps Sugeridos", new OptimizationMetadata { RequiresRegistry = true, RiskLevel = RiskLevel.Low } },
                { "Refinamentos de Produtividade", new OptimizationMetadata { RequiresRegistry = true, RiskLevel = RiskLevel.Low } },
                { "Desativar Tarefas Agendadas", new OptimizationMetadata { RequiresStartup = true, RiskLevel = RiskLevel.Low } },
                
                // Responsividade do Sistema
                { "Desabilitar Atraso de Exibição do Menu", new OptimizationMetadata { RequiresRegistry = true, RiskLevel = RiskLevel.Low } },
                { "Desabilitar Tempo de Foco do Mouse", new OptimizationMetadata { RequiresRegistry = true, RiskLevel = RiskLevel.Low } },
                { "Desabilitar Apps em Segundo Plano", new OptimizationMetadata { RequiresRegistry = true, RiskLevel = RiskLevel.Low } },
                { "Desabilitar Crash Dump", new OptimizationMetadata { RequiresRegistry = true, RiskLevel = RiskLevel.Medium } },
                { "Desabilitar Preenchimento Automático", new OptimizationMetadata { RequiresRegistry = true, RiskLevel = RiskLevel.Low } },
                { "Desabilitar Nagle's Algorithm", new OptimizationMetadata { RequiresRegistry = true, RiskLevel = RiskLevel.Medium } },
                { "Desabilitar Delivery Optimization", new OptimizationMetadata { RequiresStartup = true, RiskLevel = RiskLevel.Low } },
                { "Otimizar SvcHost Split Threshold", new OptimizationMetadata { RequiresRAM = true, RiskLevel = RiskLevel.Low } },
                { "Desabilitar Cortana", new OptimizationMetadata { RequiresRegistry = true, RiskLevel = RiskLevel.Low } },
                { "Desabilitar GameDVR e Game Bar", new OptimizationMetadata { RequiresRegistry = true, RiskLevel = RiskLevel.Low } }
            };

            _logger.LogInfo($"[PerformanceProfileResolver] Avaliando {optimizations.Count} otimizações...");

            foreach (var opt in optimizations)
            {
                var authorization = EvaluateOptimization(opt.Key, opt.Value, policy);
                authorizedOptimizations[opt.Key] = authorization;

                if (!authorization.IsAuthorized)
                    _logger.LogWarning($"[PerformanceProfileResolver] '{opt.Key}' - BLOQUEADO: {authorization.Reason}");
            }

            return new PerformanceExecutionPlan
            {
                AuthorizedOptimizations = authorizedOptimizations,
                SourceProfile = profile,
                GeneratedAt = DateTimeOffset.UtcNow,
                TotalOptimizations = optimizations.Count,
                AuthorizedCount = authorizedOptimizations.Count(x => x.Value.IsAuthorized)
            };
        }

        /// <summary>
        /// Avalia se uma otimização está autorizada pela política
        /// </summary>
        private OptimizationAuthorization EvaluateOptimization(
            string optimizationName,
            OptimizationMetadata metadata,
            PerformancePolicy policy)
        {
            // Se política exige auditoria (Enterprise), bloquear tudo por padrão
            if (policy.RequiresAudit)
            {
                return new OptimizationAuthorization
                {
                    IsAuthorized = false,
                    Reason = "Requer auditoria manual (Perfil Enterprise)"
                };
            }

            // Verificar nível de risco
            if (metadata.RiskLevel > policy.MaxRiskLevel)
            {
                return new OptimizationAuthorization
                {
                    IsAuthorized = false,
                    Reason = $"Nível de risco {metadata.RiskLevel} excede máximo permitido {policy.MaxRiskLevel}"
                };
            }

            // Verificar permissões específicas baseadas nos metadados
            if (metadata.RequiresRAM && !policy.AllowRAMOptimization)
            {
                return new OptimizationAuthorization { IsAuthorized = false, Reason = "Perfil não permite otimização de RAM" };
            }

            if (metadata.RequiresRegistry && !policy.AllowRegistryChanges)
            {
                return new OptimizationAuthorization { IsAuthorized = false, Reason = "Perfil não permite modificações no registro" };
            }

            if (metadata.RequiresServices && !policy.AllowServiceChanges)
            {
                return new OptimizationAuthorization { IsAuthorized = false, Reason = "Perfil não permite modificações em serviços" };
            }

            if (metadata.RequiresStartup && !policy.AllowStartupChanges)
            {
                return new OptimizationAuthorization { IsAuthorized = false, Reason = "Perfil não permite modificações na inicialização" };
            }

            if (metadata.RequiresPowerPlan && !policy.AllowPowerPlanChanges)
            {
                return new OptimizationAuthorization { IsAuthorized = false, Reason = "Perfil não permite modificações no plano de energia" };
            }

            if (metadata.RequiresVisualEffects && !policy.AllowVisualEffectsChanges)
            {
                return new OptimizationAuthorization { IsAuthorized = false, Reason = "Perfil não permite modificações em efeitos visuais" };
            }

            // Autorizado
            return new OptimizationAuthorization
            {
                IsAuthorized = true,
                Reason = "Autorizado pelo perfil"
            };
        }

        /// <summary>
        /// Cria plano de fallback conservador em caso de erro
        /// </summary>
        private PerformanceExecutionPlan CreateFallbackPlan()
        {
            _logger.LogWarning("[PerformanceProfileResolver] Usando plano de fallback conservador");

            // No fallback, autorizamos apenas coisas de baixíssimo risco
            return new PerformanceExecutionPlan
            {
                AuthorizedOptimizations = new Dictionary<string, OptimizationAuthorization>(StringComparer.OrdinalIgnoreCase)
                {
                    { "Timeout de Boot", new OptimizationAuthorization { IsAuthorized = true } },
                    { "Updates", new OptimizationAuthorization { IsAuthorized = true } },
                    { "Telemetria", new OptimizationAuthorization { IsAuthorized = true } }
                },
                SourceProfile = IntelligentProfileType.GeneralBalanced,
                GeneratedAt = DateTimeOffset.UtcNow,
                TotalOptimizations = 3,
                AuthorizedCount = 3
            };
        }
    }

    #region Models

    /// <summary>
    /// Política de performance extraída do perfil inteligente
    /// </summary>
    public class PerformancePolicy
    {
        public bool AllowRAMOptimization { get; set; }
        public bool AllowRegistryChanges { get; set; }
        public bool AllowServiceChanges { get; set; }
        public bool AllowStartupChanges { get; set; }
        public bool AllowPowerPlanChanges { get; set; }
        public bool AllowVisualEffectsChanges { get; set; }
        public RiskLevel MaxRiskLevel { get; set; }
        public bool RequiresAudit { get; set; }
    }

    /// <summary>
    /// Plano de execução de otimizações
    /// </summary>
    public class PerformanceExecutionPlan
    {
        public Dictionary<string, OptimizationAuthorization> AuthorizedOptimizations { get; set; } = new();
        public IntelligentProfileType SourceProfile { get; set; }
        public DateTimeOffset GeneratedAt { get; set; }
        public int TotalOptimizations { get; set; }
        public int AuthorizedCount { get; set; }

        public bool IsAuthorized(string optimizationName)
        {
            return AuthorizedOptimizations.TryGetValue(optimizationName, out var auth) && auth.IsAuthorized;
        }
    }

    /// <summary>
    /// Autorização de uma otimização específica
    /// </summary>
    public class OptimizationAuthorization
    {
        public bool IsAuthorized { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    /// <summary>
    /// Metadados de uma otimização
    /// </summary>
    public class OptimizationMetadata
    {
        public bool RequiresRAM { get; set; }
        public bool RequiresRegistry { get; set; }
        public bool RequiresServices { get; set; }
        public bool RequiresStartup { get; set; }
        public bool RequiresPowerPlan { get; set; }
        public bool RequiresVisualEffects { get; set; }
        public RiskLevel RiskLevel { get; set; }
    }

    /// <summary>
    /// Nível de risco de uma otimização
    /// </summary>
    public enum RiskLevel
    {
        None = 0,
        Low = 1,
        Medium = 2,
        High = 3
    }

    #endregion
}
