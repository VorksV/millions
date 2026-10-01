using System;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Gamer.Models;
using VoltrisOptimizer.Interfaces;
using HardwareCapabilities = VoltrisOptimizer.Interfaces.HardwareCapabilities;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services.Gamer
{
    /// <summary>
    /// Resolve plano de execução do Modo Gamer baseado no Perfil Inteligente e Hardware
    /// Enterprise-grade: Políticas declarativas, segurança, adaptação ao hardware
    /// </summary>
    public class GamerProfileResolver
    {
        private readonly SettingsService _settingsService;
        private readonly ILoggingService _logger;

        public GamerProfileResolver(SettingsService settingsService, ILoggingService logger)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogEntry(nameof(GamerProfileResolver));
            _logger.LogExit(nameof(GamerProfileResolver));
        }

        /// <summary>
        /// Resolve plano de execução baseado no Perfil Inteligente e capacidades de hardware
        /// </summary>
        public GamerExecutionPlan ResolveExecutionPlan(HardwareCapabilities hardware)
        {
            _logger.LogEntry(nameof(ResolveExecutionPlan));
            try
            {
                var profile = _settingsService.Settings.IntelligentProfile;
                _logger.LogInfo($"[GamerProfileResolver] Resolvendo plano para perfil: {profile}");
                // Detectar se é laptop através do ISystemInfoService
                bool isLaptop = false;
                try
                {
                    var systemInfoService = App.Services?.GetService(typeof(ISystemInfoService)) as ISystemInfoService;
                    isLaptop = systemInfoService?.IsLaptop() ?? false;
                }
                catch (Exception exLaptop) { _logger?.LogWarning($"[GamerProfileResolver] Erro ao detectar laptop: {exLaptop.Message}"); }
                
                _logger.LogInfo($"[GamerProfileResolver] Hardware: {(isLaptop ? "Laptop" : "Desktop")}, CPU: {hardware.Cpu.CoreCount} cores, RAM: {hardware.Ram.TotalGB}GB");

                var policy = ExtractPolicyFromProfile(profile, hardware);
                var plan = GeneratePlanFromPolicy(policy, profile, hardware);

                _logger.LogSuccess($"[GamerProfileResolver] Plano gerado: Ativação={plan.AllowActivation}, Otimizações={plan.AllowedOptimizations.Count}");

                _logger.LogExit(nameof(ResolveExecutionPlan));
                return plan;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[GamerProfileResolver] Erro ao resolver plano: {ex.Message}", ex);
                
                // Fallback seguro: plano conservador
                _logger.LogExit(nameof(ResolveExecutionPlan));
                return CreateFallbackPlan();
            }
        }

        /// <summary>
        /// Extrai política de Modo Gamer do perfil inteligente e hardware
        /// </summary>
        private GamerPolicy ExtractPolicyFromProfile(
            IntelligentProfileType profile,
            HardwareCapabilities hardware)
        {
            _logger.LogEntry(nameof(ExtractPolicyFromProfile));
            // REGRA CRÍTICA: Laptops SEMPRE recebem política conservadora (evitar superaquecimento)
            // Detectar se é laptop através do ISystemInfoService
            bool isLaptop = false;
            try
            {
                var systemInfoService = App.Services?.GetService(typeof(ISystemInfoService)) as ISystemInfoService;
                isLaptop = systemInfoService?.IsLaptop() ?? false;
            }
            catch (Exception exLaptop2) { _logger?.LogWarning($"[GamerProfileResolver] Erro ao detectar laptop na política: {exLaptop2.Message}"); }
            
            if (isLaptop)
            {
                _logger.LogWarning("[GamerProfileResolver] Laptop detectado: Aplicando política conservadora para evitar superaquecimento");
                var laptopPolicy = new GamerPolicy
                {
                    AllowActivation = true,
                    AllowUltimatePerformance = false, // Evitar superaquecimento
                    AllowProcessClosing = false, // Não fechar processos em laptop
                    AllowTimerResolution = true, // Seguro
                    AllowMemoryOptimization = false, // Conservador
                    AllowNetworkOptimization = true, // Seguro
                    MaxCpuPriority = System.Diagnostics.ProcessPriorityClass.AboveNormal, // Não High
                    RequireTemperatureCheck = true, // OBRIGATÓRIO
                    MaxAllowedTempCelsius = 80, // Limite de segurança
                    Reason = LocalizationService.Instance.GetString("GamerPolicyLaptop")
                };
                _logger.LogExit(nameof(ExtractPolicyFromProfile));
                return laptopPolicy;
            }

            // Políticas por perfil (Desktop)
            var result = profile switch
            {
                // Perfis Gamer: Máxima performance
                IntelligentProfileType.GamerCompetitive => new GamerPolicy
                {
                    AllowActivation = true,
                    AllowUltimatePerformance = true,
                    AllowProcessClosing = true,
                    AllowTimerResolution = true,
                    AllowMemoryOptimization = true,
                    AllowNetworkOptimization = true,
                    MaxCpuPriority = System.Diagnostics.ProcessPriorityClass.High,
                    RequireTemperatureCheck = false, // Usuário gamer assume risco
                    MaxAllowedTempCelsius = 90,
                    Reason = LocalizationService.Instance.GetString("GamerPolicyGamerCompetitive")
                },

                IntelligentProfileType.GamerSinglePlayer => new GamerPolicy
                {
                    AllowActivation = true,
                    AllowUltimatePerformance = true,
                    AllowProcessClosing = true,
                    AllowTimerResolution = true,
                    AllowMemoryOptimization = true,
                    AllowNetworkOptimization = true,
                    MaxCpuPriority = System.Diagnostics.ProcessPriorityClass.High,
                    RequireTemperatureCheck = false,
                    MaxAllowedTempCelsius = 90,
                    Reason = LocalizationService.Instance.GetString("GamerPolicyGamerSinglePlayer")
                },

                IntelligentProfileType.GamerSimulation => new GamerPolicy
                {
                    AllowActivation = true,
                    AllowUltimatePerformance = true,
                    AllowProcessClosing = true,
                    AllowTimerResolution = true,
                    AllowMemoryOptimization = true,
                    AllowNetworkOptimization = true,
                    MaxCpuPriority = System.Diagnostics.ProcessPriorityClass.High,
                    RequireTemperatureCheck = false,
                    MaxAllowedTempCelsius = 90,
                    Reason = LocalizationService.Instance.GetString("GamerPolicyGamerSimulation")
                },

                IntelligentProfileType.GamerMMO => new GamerPolicy
                {
                    AllowActivation = true,
                    AllowUltimatePerformance = true,
                    AllowProcessClosing = true,
                    AllowTimerResolution = true,
                    AllowMemoryOptimization = true,
                    AllowNetworkOptimization = true,
                    MaxCpuPriority = System.Diagnostics.ProcessPriorityClass.High,
                    RequireTemperatureCheck = false,
                    MaxAllowedTempCelsius = 90,
                    Reason = LocalizationService.Instance.GetString("GamerPolicyGamerMMO")
                },

                IntelligentProfileType.GamerStrategy => new GamerPolicy
                {
                    AllowActivation = true,
                    AllowUltimatePerformance = true,
                    AllowProcessClosing = true,
                    AllowTimerResolution = true,
                    AllowMemoryOptimization = true,
                    AllowNetworkOptimization = true,
                    MaxCpuPriority = System.Diagnostics.ProcessPriorityClass.High,
                    RequireTemperatureCheck = false,
                    MaxAllowedTempCelsius = 90,
                    Reason = LocalizationService.Instance.GetString("GamerPolicyGamerStrategy")
                },

                // Perfil Work: Conservador
                IntelligentProfileType.WorkOffice => new GamerPolicy
                {
                    AllowActivation = true,
                    AllowUltimatePerformance = false, // Não mudar plano de energia
                    AllowProcessClosing = false, // Não fechar processos de trabalho
                    AllowTimerResolution = true, // Seguro
                    AllowMemoryOptimization = false, // Conservador
                    AllowNetworkOptimization = true, // Seguro
                    MaxCpuPriority = System.Diagnostics.ProcessPriorityClass.AboveNormal,
                    RequireTemperatureCheck = true,
                    MaxAllowedTempCelsius = 75,
                    Reason = LocalizationService.Instance.GetString("GamerPolicyWorkOffice")
                },

                // Perfil Enterprise: BLOQUEADO
                IntelligentProfileType.EnterpriseSecure => new GamerPolicy
                {
                    AllowActivation = false, // BLOQUEAR COMPLETAMENTE
                    AllowUltimatePerformance = false,
                    AllowProcessClosing = false,
                    AllowTimerResolution = false,
                    AllowMemoryOptimization = false,
                    AllowNetworkOptimization = false,
                    MaxCpuPriority = System.Diagnostics.ProcessPriorityClass.Normal,
                    RequireTemperatureCheck = true,
                    MaxAllowedTempCelsius = 70,
                    Reason = LocalizationService.Instance.GetString("GamerPolicyEnterpriseSecure")
                },

                // Perfil Creative: Balanceado
                IntelligentProfileType.CreativeVideoEditing => new GamerPolicy
                {
                    AllowActivation = true,
                    AllowUltimatePerformance = true, // Permitir para renderização
                    AllowProcessClosing = false, // Não fechar processos criativos
                    AllowTimerResolution = true,
                    AllowMemoryOptimization = false, // Conservador (RAM importante)
                    AllowNetworkOptimization = true,
                    MaxCpuPriority = System.Diagnostics.ProcessPriorityClass.AboveNormal,
                    RequireTemperatureCheck = true,
                    MaxAllowedTempCelsius = 85,
                    Reason = LocalizationService.Instance.GetString("GamerPolicyCreative")
                },

                // Perfil Developer: Balanceado
                IntelligentProfileType.DeveloperProgramming => new GamerPolicy
                {
                    AllowActivation = true,
                    AllowUltimatePerformance = false, // Não mudar plano de energia
                    AllowProcessClosing = false, // Não fechar IDEs/compiladores
                    AllowTimerResolution = true,
                    AllowMemoryOptimization = false, // Conservador
                    AllowNetworkOptimization = true,
                    MaxCpuPriority = System.Diagnostics.ProcessPriorityClass.AboveNormal,
                    RequireTemperatureCheck = true,
                    MaxAllowedTempCelsius = 80,
                    Reason = LocalizationService.Instance.GetString("GamerPolicyDeveloper")
                },

                // Perfil General: Balanceado padrão
                IntelligentProfileType.GeneralBalanced => new GamerPolicy
                {
                    AllowActivation = true,
                    AllowUltimatePerformance = true,
                    AllowProcessClosing = true,
                    AllowTimerResolution = true,
                    AllowMemoryOptimization = true,
                    AllowNetworkOptimization = true,
                    MaxCpuPriority = System.Diagnostics.ProcessPriorityClass.High,
                    RequireTemperatureCheck = true,
                    MaxAllowedTempCelsius = 85,
                    Reason = LocalizationService.Instance.GetString("GamerPolicyGeneralBalanced")
                },

                _ => new GamerPolicy
                {
                    AllowActivation = true,
                    AllowUltimatePerformance = false,
                    AllowProcessClosing = false,
                    AllowTimerResolution = true,
                    AllowMemoryOptimization = false,
                    AllowNetworkOptimization = true,
                    MaxCpuPriority = System.Diagnostics.ProcessPriorityClass.AboveNormal,
                    RequireTemperatureCheck = true,
                    MaxAllowedTempCelsius = 80,
                    Reason = LocalizationService.Instance.GetString("GamerPolicyUnknown")
                }
            };
            _logger.LogExit(nameof(ExtractPolicyFromProfile));
            return result;
        }

        /// <summary>
        /// Gera plano de execução baseado na política
        /// </summary>
        private GamerExecutionPlan GeneratePlanFromPolicy(
            GamerPolicy policy,
            IntelligentProfileType profile,
            HardwareCapabilities hardware)
        {
            _logger.LogEntry(nameof(GeneratePlanFromPolicy));
            // Detectar se é laptop através do ISystemInfoService
            bool isLaptop = false;
            try
            {
                var systemInfoService = App.Services?.GetService(typeof(ISystemInfoService)) as ISystemInfoService;
                isLaptop = systemInfoService?.IsLaptop() ?? false;
            }
            catch (Exception ex) { _logger?.LogWarning($"[GamerProfileResolver] Erro ao detectar laptop em GeneratePlan: {ex.Message}"); }
            
            var plan = new GamerExecutionPlan
            {
                AllowActivation = policy.AllowActivation,
                BlockReason = policy.AllowActivation ? null : policy.Reason,
                SourceProfile = profile,
                IsLaptop = isLaptop,
                RequireTemperatureCheck = policy.RequireTemperatureCheck,
                MaxAllowedTempCelsius = policy.MaxAllowedTempCelsius,
                GeneratedAt = DateTimeOffset.UtcNow
            };

            // Adicionar otimizações permitidas
            if (policy.AllowTimerResolution)
                plan.AllowedOptimizations.Add("TimerResolution");

            if (policy.AllowUltimatePerformance)
                plan.AllowedOptimizations.Add("UltimatePerformance");

            if (policy.AllowProcessClosing)
                plan.AllowedOptimizations.Add("CloseBackgroundApps");

            if (policy.AllowMemoryOptimization)
                plan.AllowedOptimizations.Add("MemoryOptimization");

            if (policy.AllowNetworkOptimization)
                plan.AllowedOptimizations.Add("NetworkOptimization");

            plan.AllowedOptimizations.Add("GpuOptimization"); // Sempre permitido
            plan.AllowedOptimizations.Add("CpuOptimization"); // Sempre permitido

            plan.MaxCpuPriority = policy.MaxCpuPriority;

            _logger.LogInfo($"[GamerProfileResolver] Otimizações permitidas: {string.Join(", ", plan.AllowedOptimizations)}");
            _logger.LogInfo($"[GamerProfileResolver] Prioridade máxima: {plan.MaxCpuPriority}");

            _logger.LogExit(nameof(GeneratePlanFromPolicy));
            return plan;
        }

        /// <summary>
        /// Cria plano de fallback conservador em caso de erro
        /// </summary>
        private GamerExecutionPlan CreateFallbackPlan()
        {
            _logger.LogEntry(nameof(CreateFallbackPlan));
            _logger.LogWarning("[GamerProfileResolver] Usando plano de fallback conservador");

            var fallback = new GamerExecutionPlan
            {
                AllowActivation = true,
                BlockReason = null,
                SourceProfile = IntelligentProfileType.GeneralBalanced,
                IsLaptop = false,
                RequireTemperatureCheck = true,
                MaxAllowedTempCelsius = 80,
                MaxCpuPriority = System.Diagnostics.ProcessPriorityClass.AboveNormal,
                AllowedOptimizations = new System.Collections.Generic.List<string>
                {
                    "TimerResolution",
                    "NetworkOptimization",
                    "GpuOptimization",
                    "CpuOptimization"
                },
                GeneratedAt = DateTimeOffset.UtcNow
            };
            _logger.LogExit(nameof(CreateFallbackPlan));
            return fallback;
        }
    }

    #region Models

    /// <summary>
    /// Política de Modo Gamer extraída do perfil inteligente
    /// </summary>
    public class GamerPolicy
    {
        public bool AllowActivation { get; set; } = true;
        public bool AllowUltimatePerformance { get; set; }
        public bool AllowProcessClosing { get; set; }
        public bool AllowTimerResolution { get; set; }
        public bool AllowMemoryOptimization { get; set; }
        public bool AllowNetworkOptimization { get; set; }
        public System.Diagnostics.ProcessPriorityClass MaxCpuPriority { get; set; }
        public bool RequireTemperatureCheck { get; set; }
        public double MaxAllowedTempCelsius { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    /// <summary>
    /// Plano de execução do Modo Gamer
    /// </summary>
    public class GamerExecutionPlan
    {
        public bool AllowActivation { get; set; }
        public string? BlockReason { get; set; }
        public IntelligentProfileType SourceProfile { get; set; }
        public bool IsLaptop { get; set; }
        public bool RequireTemperatureCheck { get; set; }
        public double MaxAllowedTempCelsius { get; set; }
        public System.Diagnostics.ProcessPriorityClass MaxCpuPriority { get; set; }
        public System.Collections.Generic.List<string> AllowedOptimizations { get; set; } = new();
        public DateTimeOffset GeneratedAt { get; set; }

        public bool IsOptimizationAllowed(string optimizationName)
        {
            return AllowedOptimizations.Contains(optimizationName);
        }
    }

    #endregion
}
