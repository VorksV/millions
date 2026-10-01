using System;
using VoltrisOptimizer.Services.License;
using VoltrisOptimizer.Services.License.Models;

namespace VoltrisOptimizer.Services
{
    /// <summary>
    /// LICENSE EXPIRED EXCEPTION - SaaS Level Enforcement
    /// Exceção profissional para controle de acesso baseado em licença
    /// Usada para implementar Guard Clauses em serviços de forma enterprise-grade
    /// </summary>
    public class LicenseExpiredException : Exception
    {
        #region Properties

        /// <summary>
        /// Tipo de licença necessária
        /// </summary>
        public LicenseType RequiredLicenseType { get; set; }

        /// <summary>
        /// Feature que foi bloqueada
        /// </summary>
        public string FeatureName { get; set; }

        /// <summary>
        /// Estado atual da licença
        /// </summary>
        public dynamic? CurrentLicenseState { get; set; }

        /// <summary>
        /// Se o usuário pode fazer upgrade
        /// </summary>
        public bool CanUpgrade { get; set; }

        /// <summary>
        /// Se o usuário pode renovar
        /// </summary>
        public bool CanRenew { get; set; }

        #endregion

        #region Constructors

        /// <summary>
        /// Constructor para feature específica
        /// </summary>
        public LicenseExpiredException(string featureName, dynamic currentState, LicenseType requiredLicense = LicenseType.Pro)
            : base(FormatMessage(featureName, (object)currentState))
        {
            FeatureName = featureName ?? throw new ArgumentNullException(nameof(featureName));
            CurrentLicenseState = currentState ?? throw new ArgumentNullException(nameof(currentState));
            RequiredLicenseType = requiredLicense;
            CanUpgrade = true; // Default values
            CanRenew = true;
        }

        /// <summary>
        /// Constructor genérico
        /// </summary>
        public LicenseExpiredException(string message)
            : base(message)
        {
            FeatureName = "Unknown";
            RequiredLicenseType = LicenseType.Pro;
            CanUpgrade = false;
            CanRenew = false;
        }

        /// <summary>
        /// Constructor com inner exception
        /// </summary>
        public LicenseExpiredException(string message, Exception innerException)
            : base(message, innerException)
        {
            FeatureName = "Unknown";
            RequiredLicenseType = LicenseType.Pro;
            CanUpgrade = false;
            CanRenew = false;
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Formata mensagem de erro baseada no estado da licença
        /// </summary>
        private static string FormatMessage(string featureName, dynamic currentState)
        {
            // Propriedades confirmadas existentes no LicenseState
            var isActive = currentState.IsActive;
            var isTrialActive = currentState.IsTrialActive;
            var licenseType = currentState.LicenseType;
            
            if (!isActive)
            {
                return $"Nenhuma licença ativa encontrada. A funcionalidade '{featureName}' requer uma licença ativa.";
            }
            else if (licenseType == LicenseType.Trial && !isTrialActive)
            {
                return $"Trial expirado. A funcionalidade '{featureName}' não está disponível.";
            }
            else if (licenseType == LicenseType.Trial)
            {
                return $"Funcionalidade '{featureName}' não disponível no trial. Faça upgrade para licença Pro.";
            }
            else
            {
                return $"Acesso negado à funcionalidade '{featureName}'. Verifique sua licença.";
            }
        }

        /// <summary>
        /// Cria exceção para feature específica de forma factory
        /// </summary>
        public static LicenseExpiredException ForFeature(string featureName, dynamic currentState)
        {
            var requiredLicense = GetRequiredLicenseForFeature(featureName);
            return new LicenseExpiredException(featureName, currentState, requiredLicense);
        }

        /// <summary>
        /// Determina o tipo de licença necessário para uma feature
        /// </summary>
        private static LicenseType GetRequiredLicenseForFeature(string featureName)
        {
            return featureName.ToUpperInvariant() switch
            {
                "CLOUDSYNC" => LicenseType.Enterprise,
                "PREMIUMSUPPORT" => LicenseType.Enterprise,
                "ADVANCEDTOOLS" => LicenseType.Pro,
                "GAMERMODE" => LicenseType.Pro,
                "OPTIMIZATION" => LicenseType.Pro,
                "REALTIMEMONITORING" => LicenseType.Pro,
                _ => LicenseType.Pro
            };
        }

        #endregion

        #region Exception Factory Methods

        /// <summary>
        /// Verifica licença e lança exceção se necessário (Guard Clause Pattern)
        /// </summary>
        public static void EnsureLicenseValid(string featureName, dynamic currentState)
        {
            var requiredLicense = GetRequiredLicenseForFeature(featureName);
            
            if (!IsFeatureAvailable(featureName, currentState, requiredLicense))
            {
                throw ForFeature(featureName, currentState);
            }
        }

        /// <summary>
        /// Verifica se uma feature está disponível
        /// </summary>
        private static bool IsFeatureAvailable(string featureName, dynamic currentState, LicenseType requiredLicense)
        {
            // Se não tem licença ativa
            if (!currentState.IsActive)
                return false;

            // Se trial expirou
            if (currentState.LicenseType == LicenseType.Trial && !currentState.IsTrialActive)
                return false;

            // Verificar nível de licença
            return GetLicenseLevel(currentState.LicenseType) >= GetLicenseLevel(requiredLicense);
        }

        /// <summary>
        /// Obtém nível numérico da licença para comparação
        /// </summary>
        private static int GetLicenseLevel(LicenseType licenseType)
        {
            return licenseType switch
            {
                LicenseType.Trial => 1,
                LicenseType.Pro => 2,
                LicenseType.Enterprise => 3,
                _ => 0
            };
        }

        #endregion
    }

    /// <summary>
    /// Feature not available exception (diferente de expired)
    /// </summary>
    public class FeatureNotAvailableException : LicenseExpiredException
    {
        public FeatureNotAvailableException(string featureName, dynamic currentState)
            : base($"Feature '{featureName}' não está disponível em sua licença atual ({currentState.LicenseType}).")
        {
            FeatureName = featureName;
            CurrentLicenseState = currentState;
            RequiredLicenseType = LicenseType.Pro;
        }
    }

    /// <summary>
    /// License validation exception para erros de sistema
    /// </summary>
    public class LicenseValidationException : Exception
    {
        public string ValidationError { get; }
        public LicenseState? CurrentState { get; }

        public LicenseValidationException(string validationError, LicenseState? currentState = null)
            : base($"Erro de validação de licença: {validationError}")
        {
            ValidationError = validationError;
            CurrentState = currentState;
        }

        public LicenseValidationException(string validationError, Exception innerException, LicenseState? currentState = null)
            : base($"Erro de validação de licença: {validationError}", innerException)
        {
            ValidationError = validationError;
            CurrentState = currentState;
        }
    }
}
