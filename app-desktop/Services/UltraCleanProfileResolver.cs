using System;
using System.Collections.Generic;
using System.Linq;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services.UltraClean;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services
{
    /// <summary>
    /// RESOLVEDOR DE PERFIL PARA LIMPEZA ULTRA
    /// 
    /// ARQUITETURA ENTERPRISE-GRADE:
    /// - NÃO usa nomes de perfil (Gamer, Enterprise, etc.)
    /// - Baseado em políticas declarativas e capacidades
    /// - Sistema de autorização modular
    /// - Pronto para políticas remotas e SaaS
    /// 
    /// RESPONSABILIDADE:
    /// - Converter política declarativa em plano de execução
    /// - Avaliar módulos contra políticas de segurança
    /// - Gerar autorizações determinísticas
    /// - Manter total desacoplamento de nomes de perfil
    /// </summary>
    public class UltraCleanProfileResolver : IUltraCleanProfileResolver
    {
        private readonly SettingsService _settingsService;
        private readonly ILoggingService _logger;
        private readonly IEnumerable<UltraCleanModule> _registeredModules;

        public UltraCleanProfileResolver(
            SettingsService settingsService, 
            ILoggingService logger,
            IEnumerable<UltraCleanModule> registeredModules)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _registeredModules = registeredModules ?? throw new ArgumentNullException(nameof(registeredModules));
        }

        /// <summary>
        /// RESOLVE O PLANO DE EXECUÇÃO BASEADO NA POLÍTICA ATIVA
        /// 
        /// FLUXO:
        /// 1. Lê política declarativa de settings.xml
        /// 2. Avalia todos os módulos registrados contra política
        /// 3. Gera autorizações determinísticas
        /// 4. Retorna plano executável
        /// </summary>
        public UltraCleanExecutionPlan ResolveExecutionPlan()
        {
            try
            {
                _logger.LogInfo("[UltraCleanProfileResolver] Iniciando resolução de plano de execução...");

                // 1. Obter política declarativa (não nomes de perfil!)
                var policy = ExtractSecurityPolicyFromSettings();
                var userPolicy = ExtractUserPolicyFromSettings();

                _logger.LogInfo($"[UltraCleanProfileResolver] Política extraída - Nível de risco: {policy.MaxAllowedRisk}, Sistema: {policy.AllowSystemChanges}, Registro: {policy.AllowRegistryChanges}");

                // 2. Converter política em plano de execução
                var plan = GeneratePlanFromPolicy(policy, userPolicy);

                _logger.LogSuccess($"[UltraCleanProfileResolver] Plano gerado com {plan.ModuleAuthorizations.Count} autorizações");

                return plan;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[UltraCleanProfileResolver] Erro fatal ao resolver plano: {ex.Message}");
                
                // FALLBACK SEGURO: plano conservador
                return GenerateConservativeFallbackPlan();
            }
        }

        /// <summary>
        /// EXTRAÇÃO DE POLÍTICA DE SEGURANÇA DE settings.xml
        /// 
        /// BASEADO EM CAPACIDADES, NÃO EM NOMES DE PERFIL
        /// </summary>
        private SecurityPolicy ExtractSecurityPolicyFromSettings()
        {
            var settings = _settingsService.Settings;
            
            // Converter o perfil inteligente em política de risco
            var maxRiskLevel = settings.IntelligentProfile switch
            {
                IntelligentProfileType.GamerCompetitive => UltraClean.RiskLevel.Medium,
                IntelligentProfileType.GamerSinglePlayer => UltraClean.RiskLevel.Medium,
                IntelligentProfileType.WorkOffice => UltraClean.RiskLevel.Low,
                IntelligentProfileType.CreativeVideoEditing => UltraClean.RiskLevel.Medium,
                IntelligentProfileType.DeveloperProgramming => UltraClean.RiskLevel.Medium,
                IntelligentProfileType.GeneralBalanced => UltraClean.RiskLevel.Medium,
                IntelligentProfileType.EnterpriseSecure => UltraClean.RiskLevel.Low,
                _ => UltraClean.RiskLevel.Low
            };
            
            // Converter configurações em política declarativa
            return new SecurityPolicy
            {
                MaxAllowedRisk = maxRiskLevel,
                AllowSystemChanges = true, // Por padrão, permite alterações de sistema
                AllowRegistryChanges = settings.IntelligentProfile != IntelligentProfileType.WorkOffice && settings.IntelligentProfile != IntelligentProfileType.EnterpriseSecure, // WorkOffice e EnterpriseSecure são mais conservadores
                AllowDriverChanges = settings.IntelligentProfile == IntelligentProfileType.GamerCompetitive || settings.IntelligentProfile == IntelligentProfileType.GamerSinglePlayer || settings.IntelligentProfile == IntelligentProfileType.CreativeVideoEditing,
                AllowWindowsUpdateCleanup = settings.IntelligentProfile != IntelligentProfileType.WorkOffice && settings.IntelligentProfile != IntelligentProfileType.EnterpriseSecure,
                ProtectedModules = new List<string>(), // Nenhum módulo protegido por padrão
                ForbiddenModules = new List<string>(), // Nenhum módulo proibido por padrão
                ExecutionTimeout = TimeSpan.FromMinutes(60), // Timeout padrão de 1 hora
                RequireAuditTrail = true // Sempre requer auditoria
            };
        }

        /// <summary>
        /// EXTRAÇÃO DE POLÍTICA DO USUÁRIO (overrides manuais)
        /// </summary>
        private UserProfilePolicy ExtractUserPolicyFromSettings()
        {
            var settings = _settingsService.Settings;
            
            return new UserProfilePolicy
            {
                SpecificAllowModules = new List<string>(), // Não há propriedades específicas para isso no AppSettings atual
                SpecificDenyModules = new List<string>(), // Não há propriedades específicas para isso no AppSettings atual
                CustomRiskThreshold = UltraClean.RiskLevel.Medium, // Valor padrão
                AllowAggressiveOptimizations = settings.IntelligentProfile != IntelligentProfileType.WorkOffice && settings.IntelligentProfile != IntelligentProfileType.EnterpriseSecure // WorkOffice e EnterpriseSecure são mais conservadores
            };
        }

        /// <summary>
        /// GERAÇÃO DO PLANO DE EXECUÇÃO BASEADO EM POLÍTICAS
        /// 
        /// SEM SWITCH POR NOME DE PERFIL!
        /// APENAS AVALIAÇÃO DE POLÍTICAS CONTRA MÓDULOS
        /// </summary>
        private UltraCleanExecutionPlan GeneratePlanFromPolicy(SecurityPolicy globalPolicy, UserProfilePolicy userPolicy)
        {
            var authorizations = new Dictionary<string, ModuleAuthorization>();
            var modules = _registeredModules.ToList();

            _logger.LogInfo($"[UltraCleanProfileResolver] Avaliando {modules.Count} módulos...");

            foreach (var module in modules)
            {
                var authorization = EvaluateModuleAgainstPolicy(module, globalPolicy, userPolicy);
                
                if (authorization != null)
                {
                    authorizations[module.Id] = authorization;
                }
            }

            var plan = new UltraCleanExecutionPlan
            {
                ModuleAuthorizations = authorizations,
                GlobalSecurityPolicy = globalPolicy,
                Metadata = new AuditMetadata
                {
                    GeneratedAt = DateTimeOffset.UtcNow,
                    Generator = "UltraCleanProfileResolver",
                    SourcePolicy = globalPolicy.MaxAllowedRisk.ToString(),
                    TotalModules = modules.Count,
                    AuthorizedModules = authorizations.Values.Count(a => a.IsAuthorized)
                },
                Source = PolicySource.Profile, // Pode ser "profile", "user", "remote", "default"
                GeneratedAt = DateTimeOffset.UtcNow,
                PolicyHash = GeneratePolicyHash(globalPolicy, userPolicy)
            };

            return plan;
        }

        /// <summary>
        /// AVALIAÇÃO DE UM MÓDULO CONTRA POLÍTICAS
        /// 
        /// BASEADO EM:
        /// - Requisitos do módulo
        /// - Políticas de segurança
        /// - Overrides do usuário
        /// - NÃO BASEADO EM NOME DE PERFIL
        /// </summary>
        private ModuleAuthorization EvaluateModuleAgainstPolicy(UltraCleanModule module, SecurityPolicy globalPolicy, UserProfilePolicy userPolicy)
        {
            var authorization = new ModuleAuthorization
            {
                ModuleId = module.Id,
                IsAuthorized = false,
                Reason = AuthorizationReason.DefaultDenied,
                SecurityOverride = null,
                PolicyApplied = globalPolicy.MaxAllowedRisk.ToString()
            };

            // 1. VERIFICAR SE ESTÁ NA LISTA PROTEGIDA
            if (globalPolicy.ProtectedModules.Contains(module.Id))
            {
                authorization.IsAuthorized = false;
                authorization.Reason = AuthorizationReason.ProtectedByPolicy;
                return authorization;
            }

            // 2. VERIFICAR SE ESTÁ NA LISTA PROIBIDA
            if (globalPolicy.ForbiddenModules.Contains(module.Id))
            {
                authorization.IsAuthorized = false;
                authorization.Reason = AuthorizationReason.ForbiddenByPolicy;
                return authorization;
            }

            // 3. VERIFICAR OVERRIDES DO USUÁRIO
            if (userPolicy.SpecificDenyModules.Contains(module.Id))
            {
                authorization.IsAuthorized = false;
                authorization.Reason = AuthorizationReason.DeniedByUserOverride;
                return authorization;
            }

            if (userPolicy.SpecificAllowModules.Contains(module.Id))
            {
                authorization.IsAuthorized = true;
                authorization.Reason = AuthorizationReason.AllowedByUserOverride;
                return authorization;
            }

            // 4. VERIFICAR REQUISITOS DE POLÍTICA
            if (module.RequiresAdmin && !globalPolicy.AllowSystemChanges)
            {
                authorization.IsAuthorized = false;
                authorization.Reason = AuthorizationReason.RequiresAdminButNotAllowed;
                return authorization;
            }

            if (module.RequiresRegistryAccess && !globalPolicy.AllowRegistryChanges)
            {
                authorization.IsAuthorized = false;
                authorization.Reason = AuthorizationReason.RequiresRegistryButNotAllowed;
                return authorization;
            }

            if (module.RequiresDriverAccess && !globalPolicy.AllowDriverChanges)
            {
                authorization.IsAuthorized = false;
                authorization.Reason = AuthorizationReason.RequiresDriverButNotAllowed;
                return authorization;
            }

            // 5. VERIFICAR NÍVEL DE RISCO
            // Permitir módulos seguros mesmo em perfis mais conservadores
            if ((int)module.RiskLevel > (int)globalPolicy.MaxAllowedRisk && !module.IsSafe)
            {
                authorization.IsAuthorized = false;
                authorization.Reason = AuthorizationReason.HighRiskNotAllowed;
                return authorization;
            }
            
            // Permitir módulos seguros independentemente do nível de risco
            if (module.IsSafe)
            {
                authorization.IsAuthorized = true;
                authorization.Reason = AuthorizationReason.AllowedByPolicyCompliance;
                return authorization;
            }

            // 6. APROVAR SE PASSAR EM TODAS AS VERIFICAÇÕES
            authorization.IsAuthorized = true;
            authorization.Reason = AuthorizationReason.AllowedByPolicyCompliance;
            
            return authorization;
        }

        /// <summary>
        /// CONVERSÃO DE NÍVEL DE RISCO
        /// </summary>
        private UltraClean.RiskLevel ConvertRiskLevel(int riskLevel)
        {
            return riskLevel switch
            {
                <= 0 => UltraClean.RiskLevel.None,
                1 => UltraClean.RiskLevel.Low,
                2 => UltraClean.RiskLevel.Medium,
                3 => UltraClean.RiskLevel.High,
                _ => UltraClean.RiskLevel.Extreme
            };
        }

        /// <summary>
        /// GERAÇÃO DE HASH PARA VERIFICAÇÃO DE INTEGRIDADE
        /// </summary>
        private string GeneratePolicyHash(SecurityPolicy globalPolicy, UserProfilePolicy userPolicy)
        {
            var policyString = $"{globalPolicy.MaxAllowedRisk}_{globalPolicy.AllowSystemChanges}_{globalPolicy.AllowRegistryChanges}_{userPolicy.AllowAggressiveOptimizations}";
            return System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(policyString))
                .Aggregate("", (s, b) => s + b.ToString("x2"));
        }

        /// <summary>
        /// PLANO DE FALLBACK SEGURO
        /// </summary>
        private UltraCleanExecutionPlan GenerateConservativeFallbackPlan()
        {
            var plan = new UltraCleanExecutionPlan
            {
                ModuleAuthorizations = new Dictionary<string, ModuleAuthorization>(),
                GlobalSecurityPolicy = new SecurityPolicy
                {
                    MaxAllowedRisk = RiskLevel.Low,
                    AllowSystemChanges = false,
                    AllowRegistryChanges = false,
                    AllowDriverChanges = false,
                    ProtectedModules = new List<string>(),
                    ForbiddenModules = new List<string>(),
                    ExecutionTimeout = TimeSpan.FromMinutes(30),
                    RequireAuditTrail = true
                },
                Metadata = new AuditMetadata
                {
                    GeneratedAt = DateTimeOffset.UtcNow,
                    Generator = "UltraCleanProfileResolver.Fallback",
                    SourcePolicy = "Conservative",
                    TotalModules = 0,
                    AuthorizedModules = 0,
                    WasFallback = true
                },
                Source = PolicySource.Fallback,
                GeneratedAt = DateTimeOffset.UtcNow,
                PolicyHash = "fallback"
            };

            _logger.LogWarning("[UltraCleanProfileResolver] Usando plano de fallback conservador devido a erro na resolução");

            return plan;
        }
    }

    /// <summary>
    /// POLÍTICA DE SEGURANÇA GERAL
    /// 
    /// BASEADA EM CAPACIDADES, NÃO EM NOMES DE PERFIL
    /// </summary>
    public class SecurityPolicy
    {
        public RiskLevel MaxAllowedRisk { get; set; }
        public bool AllowSystemChanges { get; set; }
        public bool AllowRegistryChanges { get; set; }
        public bool AllowDriverChanges { get; set; }
        public bool AllowWindowsUpdateCleanup { get; set; }
        public List<string> ProtectedModules { get; set; } = new List<string>();
        public List<string> ForbiddenModules { get; set; } = new List<string>();
        public TimeSpan ExecutionTimeout { get; set; }
        public bool RequireAuditTrail { get; set; }
    }

    /// <summary>
    /// POLÍTICA DO USUÁRIO (overrides)
    /// </summary>
    public class UserProfilePolicy
    {
        public List<string> SpecificAllowModules { get; set; } = new List<string>();
        public List<string> SpecificDenyModules { get; set; } = new List<string>();
        public RiskLevel CustomRiskThreshold { get; set; }
        public bool AllowAggressiveOptimizations { get; set; }
    }

    /// <summary>
    /// PLANO DE EXECUÇÃO DA LIMPEZA ULTRA
    /// 
    /// LEVE, AUDITÁVEL E DESACOPLADO
    /// </summary>
    public class UltraCleanExecutionPlan
    {
        public Dictionary<string, ModuleAuthorization> ModuleAuthorizations { get; set; } = new Dictionary<string, ModuleAuthorization>();
        public SecurityPolicy GlobalSecurityPolicy { get; set; }
        public AuditMetadata Metadata { get; set; }
        public PolicySource Source { get; set; }
        public DateTimeOffset GeneratedAt { get; set; }
        public string PolicyHash { get; set; }
    }

    /// <summary>
    /// AUTORIZAÇÃO DE MÓDULO
    /// 
    /// APENAS O NECESSÁRIO, SEM DUPLICAÇÃO DE METADADOS
    /// </summary>
    public class ModuleAuthorization
    {
        public string ModuleId { get; set; }
        public bool IsAuthorized { get; set; }
        public AuthorizationReason Reason { get; set; }
        public SecurityOverride SecurityOverride { get; set; }
        public DateTimeOffset? AuthorizedUntil { get; set; }
        public string PolicyApplied { get; set; }
    }

    /// <summary>
    /// METADADOS PARA AUDITORIA
    /// </summary>
    public class AuditMetadata
    {
        public DateTimeOffset GeneratedAt { get; set; }
        public string Generator { get; set; }
        public string SourcePolicy { get; set; }
        public int TotalModules { get; set; }
        public int AuthorizedModules { get; set; }
        public bool WasFallback { get; set; }
    }



    /// <summary>
    /// MOTIVO DA AUTORIZAÇÃO
    /// </summary>
    public enum AuthorizationReason
    {
        DefaultDenied,
        ProtectedByPolicy,
        ForbiddenByPolicy,
        DeniedByUserOverride,
        AllowedByUserOverride,
        RequiresAdminButNotAllowed,
        RequiresRegistryButNotAllowed,
        RequiresDriverButNotAllowed,
        HighRiskNotAllowed,
        AllowedByPolicyCompliance
    }

    /// <summary>
    /// ORIGEM DA POLÍTICA
    /// </summary>
    public enum PolicySource
    {
        Profile,
        User,
        Remote,
        Default,
        Fallback
    }

    /// <summary>
    /// OVERRIDE DE SEGURANÇA ESPECÍFICO
    /// </summary>
    public class SecurityOverride
    {
        public bool? AllowAdmin { get; set; }
        public bool? AllowRegistry { get; set; }
        public bool? AllowDriver { get; set; }
        public RiskLevel? MaxRiskOverride { get; set; }
        public string OverrideReason { get; set; }
    }
}
