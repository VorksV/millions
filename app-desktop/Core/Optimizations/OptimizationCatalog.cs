using System;
using System.Collections.Generic;

namespace VoltrisOptimizer.Core.Optimizations
{
    public enum OptimizationCategory
    {
        A_Official,     // Documentada pela MS (Sempre permitida)
        B_Conditional,  // Documentação parcial (Verifica pré-condições)
        C_HardwareSpecific, // Específica de GPU/CPU (ADLX, NVAPI)
        D_Experimental  // Placebos / Sem evidência (Desativadas)
    }

    public class OptimizationMetadata
    {
        public string Name { get; set; } = string.Empty;
        public OptimizationCategory Category { get; set; }
        public bool RebootRequired { get; set; }
        public bool HasMicrosoftEvidence { get; set; }
        public bool SupportsRollback { get; set; }
        // ... outros campos (Riscos, Apis Utilizadas, etc.)
    }

    /// <summary>
    /// Catálogo que registra todas as otimizações e ignora automaticamente
    /// as que se enquadram na Categoria D (Placebos).
    /// </summary>
    public sealed class OptimizationCatalog
    {
        private static readonly Lazy<OptimizationCatalog> _instance = new(() => new OptimizationCatalog());
        public static OptimizationCatalog Instance => _instance.Value;

        private readonly Dictionary<string, OptimizationMetadata> _registry = new();

        private OptimizationCatalog() 
        {
            InitializeCatalog();
        }

        private void InitializeCatalog()
        {
            // Exemplo de registro
            _registry["TimerResolution"] = new OptimizationMetadata
            {
                Name = "TimerResolution",
                Category = OptimizationCategory.A_Official,
                HasMicrosoftEvidence = true,
                SupportsRollback = true
            };

            _registry["DisablePagingExecutive"] = new OptimizationMetadata
            {
                Name = "DisablePagingExecutive",
                Category = OptimizationCategory.D_Experimental,
                HasMicrosoftEvidence = false,
                SupportsRollback = true
            };
        }

        public bool IsOptimizationAllowed(string name)
        {
            if (_registry.TryGetValue(name, out var meta))
            {
                return meta.Category != OptimizationCategory.D_Experimental;
            }
            return false;
        }
    }
}
