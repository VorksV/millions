using System;

namespace VoltrisOptimizer.Services.License
{
    /// <summary>
    /// Tipos de licença disponíveis
    /// </summary>
    public enum LicenseType
    {
        /// <summary>
        /// Nenhuma licença
        /// </summary>
        None = -1,

        /// <summary>
        /// Período de teste gratuito - 7 dias, 1 dispositivo
        /// </summary>
        Trial = 0,
        
        /// <summary>
        /// Licença Standard - 1 dispositivo
        /// </summary>
        Standard = 1,
        
        /// <summary>
        /// Licença Pro - 3 dispositivos
        /// </summary>
        Pro = 2,
        
        /// <summary>
        /// Licença Enterprise - dispositivos ilimitados
        /// </summary>
        Enterprise = 3
    }
    
    /// <summary>
    /// Informações do plano de licença
    /// </summary>
    public class LicensePlan
    {
        public LicenseType Type { get; set; }
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public int MaxDevices { get; set; }
        public decimal? Price { get; set; }
        public string? PriceFormatted { get; set; }
        public string[] Features { get; set; } = Array.Empty<string>();
        public bool IsPopular { get; set; }
        public string Color { get; set; } = "#8B31FF";
        public string Icon { get; set; } = "M12,3L2,12H5V20H19V12H22L12,3Z";
        
        /// <summary>
        /// Planos disponíveis
        /// </summary>
        public static LicensePlan[] AvailablePlans => new[]
        {
            new LicensePlan
            {
                Type = LicenseType.Trial,
                Name = "Trial",
                Description = "Período de teste gratuito por 7 dias",
                MaxDevices = 1,
                Price = null,
                PriceFormatted = "Grátis",
                Features = new[]
                {
                    "Todas as funcionalidades básicas",
                    "1 dispositivo",
                    "7 dias de teste",
                    "Suporte por email"
                },
                IsPopular = false,
                Color = "#6B6B80",
                Icon = "M12,20A8,8 0 0,0 20,12A8,8 0 0,0 12,4A8,8 0 0,0 4,12A8,8 0 0,0 12,20M12,2A10,10 0 0,1 22,12A10,10 0 0,1 12,22C6.47,22 2,17.5 2,12A10,10 0 0,1 12,2M12.5,7V12.25L17,14.92L16.25,16.15L11,13V7H12.5Z"
            },
            new LicensePlan
            {
                Type = LicenseType.Standard,
                Name = "Standard",
                Description = "Para uso pessoal em um único computador",
                MaxDevices = 1,
                Price = 99.00m,
                PriceFormatted = "R$ 99,00",
                Features = new[]
                {
                    "Todas as funcionalidades",
                    "1 dispositivo",
                    "Atualizações por 1 ano",
                    "Suporte por email",
                    "Modo Gamer básico"
                },
                IsPopular = false,
                Color = "#31A8FF",
                Icon = "M12,17.27L18.18,21L16.54,13.97L22,9.24L14.81,8.62L12,2L9.19,8.62L2,9.24L7.45,13.97L5.82,21L12,17.27Z"
            },
            new LicensePlan
            {
                Type = LicenseType.Pro,
                Name = "Pro",
                Description = "Para entusiastas com múltiplos dispositivos",
                MaxDevices = 3,
                Price = 199.00m,
                PriceFormatted = "R$ 199,00",
                Features = new[]
                {
                    "Todas as funcionalidades",
                    "3 dispositivos",
                    "Atualizações por 1 ano",
                    "Suporte prioritário",
                    "Modo Gamer avançado",
                    "Otimizações de rede",
                    "Perfis automáticos por jogo"
                },
                IsPopular = true,
                Color = "#8B31FF",
                Icon = "M5,16L3,5L8.5,10L12,4L15.5,10L21,5L19,16H5M19,19A1,1 0 0,1 18,20H6A1,1 0 0,1 5,19V18H19V19Z"
            },
            new LicensePlan
            {
                Type = LicenseType.Enterprise,
                Name = "Enterprise",
                Description = "Para empresas e gamers profissionais",
                MaxDevices = 9999,
                Price = 499.00m,
                PriceFormatted = "R$ 499,00",
                Features = new[]
                {
                    "Todas as funcionalidades",
                    "Dispositivos ilimitados",
                    "Atualizações vitalícias",
                    "Suporte 24/7",
                    "Todas as otimizações",
                    "API de integração",
                    "Painel de administração",
                    "Implantação em lote"
                },
                IsPopular = false,
                Color = "#FF4B6B",
                Icon = "M18,16.08C17.24,16.08 16.56,16.38 16.04,16.85L8.91,12.7C8.96,12.47 9,12.24 9,12C9,11.76 8.96,11.53 8.91,11.3L15.96,7.19C16.5,7.69 17.21,8 18,8A3,3 0 0,0 21,5A3,3 0 0,0 18,2A3,3 0 0,0 15,5C15,5.24 15.04,5.47 15.09,5.7L8.04,9.81C7.5,9.31 6.79,9 6,9A3,3 0 0,0 3,12A3,3 0 0,0 6,15C6.79,15 7.5,14.69 8.04,14.19L15.16,18.34C15.11,18.55 15.08,18.77 15.08,19C15.08,20.61 16.39,21.91 18,21.91C19.61,21.91 20.92,20.61 20.92,19A2.92,2.92 0 0,0 18,16.08Z"
            }
        };
        
        /// <summary>
        /// Obtém limite de dispositivos por tipo
        /// </summary>
        public static int GetMaxDevices(LicenseType type)
        {
            return type switch
            {
                LicenseType.Trial => 1,
                LicenseType.Standard => 1,
                LicenseType.Pro => 3,
                LicenseType.Enterprise => 9999,
                _ => 1
            };
        }
        
        /// <summary>
        /// Obtém limite de dispositivos por nome do tipo
        /// </summary>
        public static int GetMaxDevices(string typeName)
        {
            if (Enum.TryParse<LicenseType>(typeName, true, out var type))
                return GetMaxDevices(type);
            return 1;
        }
        
        /// <summary>
        /// Converte string para LicenseType
        /// </summary>
        public static LicenseType ParseType(string typeName)
        {
            if (Enum.TryParse<LicenseType>(typeName, true, out var type))
                return type;
            return LicenseType.Trial;
        }
    }
    
    /// <summary>
    /// Estado atual da licença (LEGACY - substituído por LicenseOrchestrationService)
    /// </summary>
    public class LegacyLicenseState
    {
        public bool IsActivated { get; set; }
        public LicenseType Type { get; set; } = LicenseType.Trial;
        public string? LicenseKey { get; set; }
        public string? DeviceId { get; set; }
        public int MaxDevices { get; set; } = 1;
        public int DevicesInUse { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public DateTime? ActivatedAt { get; set; }
        public int TrialDaysRemaining { get; set; }
        public bool IsTrialExpired { get; set; }
        public bool IsOfflineMode { get; set; }
        public string? ErrorMessage { get; set; }
        
        /// <summary>
        /// Verifica se está em modo Trial
        /// </summary>
        public bool IsTrial => Type == LicenseType.Trial && !IsActivated;
        
        /// <summary>
        /// Verifica se é Pro ou superior
        /// </summary>
        public bool IsPro => Type >= LicenseType.Pro && IsActivated;
        
        /// <summary>
        /// Verifica se há slots disponíveis
        /// </summary>
        public bool HasAvailableSlots => DevicesInUse < MaxDevices;
        
        /// <summary>
        /// Slots restantes
        /// </summary>
        public int RemainingSlots => Math.Max(0, MaxDevices - DevicesInUse);
        
        /// <summary>
        /// Nome formatado do tipo
        /// </summary>
        public string TypeDisplayName => Type switch
        {
            LicenseType.Trial => "Trial (7 dias)",
            LicenseType.Standard => "Standard",
            LicenseType.Pro => "Pro",
            LicenseType.Enterprise => "Enterprise",
            _ => "Desconhecido"
        };
    }
    
    /// <summary>
    /// Resultado de uma operação de ativação
    /// </summary>
    public class ActivationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public LicenseType Type { get; set; } = LicenseType.Trial;
        public int MaxDevices { get; set; } = 1;
        public int DevicesInUse { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public string? ErrorCode { get; set; }
        
        public static ActivationResult Failed(string message, string? errorCode = null)
        {
            return new ActivationResult
            {
                Success = false,
                Message = message,
                ErrorCode = errorCode
            };
        }
        
        public static ActivationResult Succeeded(LicenseType type, int maxDevices, int devicesInUse, DateTime? expiresAt = null)
        {
            return new ActivationResult
            {
                Success = true,
                Message = "Licença ativada com sucesso!",
                Type = type,
                MaxDevices = maxDevices,
                DevicesInUse = devicesInUse,
                ExpiresAt = expiresAt
            };
        }
    }
}

