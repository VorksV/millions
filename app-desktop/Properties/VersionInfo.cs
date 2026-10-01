using System.Reflection;
using System.Runtime.InteropServices;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Properties
{
    public static class VersionInfo
    {
        public const string Version = "1.0.2.5";
        public const string BuildDate = "2025-03-30";
        public const string Company = "Voltris Corporation";
        public const string Product = "Voltris Optimizer";
        public const string Copyright = "Copyright © 2025 Voltris Corporation";
        public const string Description = "Sistema Profissional de Otimização para Windows 10/11";
        public const string Website = "https://www.voltris.com.br";
        public const string Support = "suporte@voltris.com.br";
        public const string Privacy = "https://www.voltris.com.br/lgpd";
        public const string Terms = "https://www.voltris.com.br/termos-uso";
        
        // Metadados de segurança
        public const string SecurityLevel = "Professional";
        public const string Compliance = "Windows-Ready";
        public const string DigitalSignature = "Pending";
        
        // LicenseType dinâmico - usa nome real do backend
        public static string LicenseType 
        { 
            get 
            {
                try
                {
                    if (Services.License.LicenseTokenStore.IsProActive)
                    {
                        return Services.License.LicenseTokenStore.LicenseDisplayName ?? "Licenciado";
                    }
                    return "Grátis";
                }
                catch
                {
                    return "Commercial";
                }
            }
        }
    }
}

