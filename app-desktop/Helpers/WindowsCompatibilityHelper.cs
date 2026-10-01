using System;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services.SystemChanges;

namespace VoltrisOptimizer.Helpers
{
    /// <summary>
    /// Helper centralizado para verificações de compatibilidade Windows 10/11
    /// Usa ICapabilityGuard como fonte única de verdade
    /// </summary>
    public static class WindowsCompatibilityHelper
    {
        private static ICapabilityGuard? _capabilityGuard;
        
        /// <summary>
        /// Inicializa o helper com o CapabilityGuard
        /// </summary>
        public static void Initialize(ICapabilityGuard capabilityGuard)
        {
            _capabilityGuard = capabilityGuard ?? throw new ArgumentNullException(nameof(capabilityGuard));
        }
        
        /// <summary>
        /// Obtém o CapabilityGuard (cria instância padrão se não inicializado)
        /// </summary>
        private static ICapabilityGuard GetGuard()
        {
            if (_capabilityGuard != null)
                return _capabilityGuard;
            
            // Fallback: criar instância padrão
            _capabilityGuard = new CapabilityGuard();
            return _capabilityGuard;
        }
        
        /// <summary>
        /// Verifica se o sistema é Windows 10 ou superior
        /// </summary>
        public static bool IsWindows10OrHigher() => GetGuard().IsWindows10OrHigher();
        
        /// <summary>
        /// Verifica se o sistema é Windows 11
        /// </summary>
        public static bool IsWindows11() => GetGuard().IsWindows11();
        
        /// <summary>
        /// Verifica se o sistema é Windows 10 (não 11)
        /// </summary>
        public static bool IsWindows10() => IsWindows10OrHigher() && !IsWindows11();
        
        /// <summary>
        /// Verifica se é um laptop
        /// </summary>
        public static bool IsLaptop() => GetGuard().IsLaptop();
        
        /// <summary>
        /// Verifica se tweaks de serviços são permitidos
        /// </summary>
        public static bool AllowServiceTweaks() => GetGuard().AllowServiceTweaks();
        
        /// <summary>
        /// Verifica se tweaks de registry são permitidos
        /// </summary>
        public static bool AllowRegistryTweaks() => GetGuard().AllowRegistryTweaks();
        
        /// <summary>
        /// Obtém a versão do Windows como string
        /// </summary>
        public static string GetWindowsVersionString()
        {
            if (IsWindows11())
                return "Windows 11";
            if (IsWindows10OrHigher())
                return "Windows 10";
            return "Windows Desconhecido";
        }
    }
}

