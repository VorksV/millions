using System;
using System.Collections.Generic;
using VoltrisOptimizer.Core.Constants;

namespace VoltrisOptimizer.Services
{
    /// <summary>
    /// Serviço de validação de operações críticas do sistema
    /// </summary>
    public class SystemValidationService
    {
        private readonly ILoggingService _logger;
        
        public enum RiskLevel
        {
            Safe,
            Low,
            Medium,
            High,
            Critical
        }
        
        public class ValidationResult
        {
            public bool IsValid { get; set; }
            public RiskLevel Risk { get; set; }
            public List<string> Warnings { get; set; } = new List<string>();
            public List<string> Recommendations { get; set; } = new List<string>();
            public bool RequiresUserConfirmation { get; set; }
        }
        
        public SystemValidationService(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        /// <summary>
        /// Valida mudança de TDR (Timeout Detection and Recovery)
        /// </summary>
        public ValidationResult ValidateTdrChange(int tdrLevel, int tdrDelay)
        {
            var result = new ValidationResult { IsValid = true };
            
            // TDR Level 0 = Desabilitado completamente (CRÍTICO)
            if (tdrLevel == SystemConstants.TdrSettings.LevelDisabled)
            {
                result.Risk = RiskLevel.Critical;
                result.RequiresUserConfirmation = true;
                result.Warnings.Add("⚠️ ATENÇÃO: Você está DESABILITANDO completamente o TDR (Timeout Detection and Recovery)");
                result.Warnings.Add("🔴 RISCO CRÍTICO: Se a GPU travar, o sistema pode congelar completamente");
                result.Warnings.Add("💾 PERDA DE DADOS: Você pode perder trabalho não salvo");
                result.Warnings.Add("🖥️ TELA PRETA: O display pode ficar preto sem recuperação");
                result.Recommendations.Add("✅ Recomendado: Use TDR Level 3 (recuperação com delay) em vez de 0");
                result.Recommendations.Add("💡 Alternativa: Aumente apenas o TdrDelay para 10-15 segundos");
                result.Recommendations.Add("🔄 Crie um ponto de restauração do sistema antes de aplicar");
            }
            // TDR Level 1 = Recuperação desabilitada (ALTO)
            else if (tdrLevel == 1)
            {
                result.Risk = RiskLevel.High;
                result.RequiresUserConfirmation = true;
                result.Warnings.Add("⚠️ Recuperação de GPU desabilitada - risco de travamento");
                result.Recommendations.Add("Considere usar TDR Level 3 com delay maior");
            }
            // TDR Delay muito alto (> 30 segundos)
            else if (tdrDelay > 30)
            {
                result.Risk = RiskLevel.Medium;
                result.Warnings.Add($"TDR Delay de {tdrDelay}s é muito alto - GPU pode travar por muito tempo");
                result.Recommendations.Add("Recomendado: Use delay entre 8-15 segundos");
            }
            // Configuração segura
            else
            {
                result.Risk = RiskLevel.Low;
            }
            
            return result;
        }
        
        /// <summary>
        /// Loga operação validada
        /// </summary>
        public void LogValidatedOperation(string operationName, ValidationResult validation)
        {
            _logger.LogInfo($"[VALIDAÇÃO] {operationName} - Risco: {validation.Risk}");
            
            foreach (var warning in validation.Warnings)
            {
                _logger.LogWarning($"[VALIDAÇÃO] {warning}");
            }
            
            foreach (var recommendation in validation.Recommendations)
            {
                _logger.LogInfo($"[VALIDAÇÃO] {recommendation}");
            }
        }
    }
}
