using System;

namespace VoltrisOptimizer.Services.License.Models
{
    /// <summary>
    /// Estado unificado da licença — Single Source of Truth.
    /// Usado por LicenseService, LicenseGuard e LicenseOrchestrationService.
    /// </summary>
    public class LicenseState
    {
        // ─── Tipo de licença (string para compatibilidade com LicenseService) ──
        /// <summary>"None", "Trial", "Pro", "Enterprise"</summary>
        public string LicenseType { get; set; } = "None";

        // ─── Estado básico ────────────────────────────────────────────────────
        public bool IsActive { get; set; }
        public bool IsTrialActive { get; set; }
        public string FormattedStatus { get; set; } = "Unknown";
        public string SupportLevel { get; set; } = "None";
        public string Message { get; set; } = string.Empty;
        public DateTime LastChecked { get; set; } = DateTime.UtcNow;

        // ─── Expiração ────────────────────────────────────────────────────────
        /// <summary>Data de expiração (trial ou licença Pro).</summary>
        public DateTime? ExpiresAt { get; set; }

        /// <summary>Dias restantes calculados a partir do ExpiresAt.</summary>
        public int DaysRemaining
        {
            get
            {
                if (!ExpiresAt.HasValue) return 0;
                return Math.Max(0, (int)(ExpiresAt.Value - DateTime.UtcNow).TotalDays);
            }
        }

        // ─── Propriedades estendidas (usadas pelo LicenseOrchestrationService) ─
        public string LicenseKey { get; set; } = "";
        public int MaxDevices { get; set; } = 1;
        public int DevicesInUse { get; set; }
        public bool IsOnlineMode { get; set; }
        public bool IsOfflineMode { get; set; }
        public DateTime LastValidated { get; set; } = DateTime.UtcNow;
        public string ValidationSource { get; set; } = "";
        public bool CanRenew { get; set; }
        public bool CanUpgrade { get; set; }
        
        // ─── Período de cobrança ──────────────────────────────────────────────
        /// <summary>"Mensal" ou "Anual" — exibido na UI.</summary>
        public string? BillingPeriod { get; set; } = null;

        // ─── Propriedades calculadas ──────────────────────────────────────────

        /// <summary>Verdadeiro se o trial está próximo de expirar (menos de 1h).</summary>
        public bool IsNearExpiry
        {
            get
            {
                if (!ExpiresAt.HasValue || !IsTrialActive) return false;
                return (ExpiresAt.Value - DateTime.UtcNow).TotalHours < 1;
            }
        }

        /// <summary>Verdadeiro se o cache está inconsistente.</summary>
        public bool IsInconsistent
        {
            get
            {
                if (ExpiresAt.HasValue && ExpiresAt.Value < DateTime.UtcNow && IsTrialActive)
                    return true;
                return false;
            }
        }

        /// <summary>Cor de status para UI.</summary>
        public string StatusColor => LicenseType?.ToLower() switch
        {
            "pro" or "enterprise" or "standard" => "#10B981",
            "trial" when IsTrialActive => "#F59E0B",
            "trial" => "#EF4444",
            _ => "#6B7280"
        };
    }

    /// <summary>
    /// Resultado da verificação de acesso a uma feature.
    /// </summary>

    /// <summary>
    /// Event args para mudanças de estado da licença.
    /// </summary>
    public sealed class LicenseStateChangedEventArgs : EventArgs
    {
        public LicenseState? PreviousState { get; init; }
        public LicenseState CurrentState { get; init; } = null!;
        public DateTime ChangeTimestamp { get; init; }

        public bool IsSignificantChange
        {
            get
            {
                if (PreviousState == null) return true;
                return PreviousState.IsActive != CurrentState.IsActive ||
                       PreviousState.LicenseType != CurrentState.LicenseType;
            }
        }
    }
}
