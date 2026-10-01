using VoltrisOptimizer.Core.SystemIntelligenceProfiler;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Core.SystemIntelligenceProfiler
{
    public class DefaultCompatibilityPolicy : ICompatibilityPolicy
    {
        public bool IsAllowed(ActionRecommendation recommendation, AuditData audit)
        {
            if (recommendation == null) return false;
            if (audit == null) return false;

            // Trava 1: nunca aplicar nada fora de "Safe" por este caminho.
            // "Safe" deixa de ser um carimbo: abaixo ele é confrontado com o
            // estado real da máquina.
            if (recommendation.Category != RecommendationCategory.Safe) return false;

            // Trava 2: o tipo da ação é conferido, não só o rótulo.
            // Estas duas são as mais perigosas do catálogo e NUNCA são
            // automáticas, mesmo que alguém as marque como "Safe":
            //  - General_Optimize grava TdrLevel=0 (desliga o TDR: um travamento
            //    de GPU deixa a tela morta até reinício físico);
            //  - Memory_Optimize grava DisablePagingExecutive=1 (kernel e
            //    pagefile sem paginação: trava em máquina com pouca RAM).
            switch (recommendation.Type)
            {
                case ActionType.General_Optimize:
                case ActionType.Memory_Optimize:
                case ActionType.Process_Optimize:
                    return false;
            }

            // Trava 3: estado térmico crítico bloqueia qualquer alteração.
            if (audit.ThermalStatus == ThermalTier.Critical) return false;

            // Trava 4: HAGS só em GPU dedicada que o suporta de fato.
            if (recommendation.Type == ActionType.Advanced_EnableHags)
            {
                if (audit.Gpu.IsIntegrated) return false;
                if (!audit.Gpu.HagsSupported) return false;
            }

            // Trava 5: remover efeitos visuais só faz sentido em hardware
            // realmente limitado — em máquina boa é só perda de qualidade.
            if (recommendation.Type == ActionType.Visual_Optimize)
            {
                if (audit.Ram.TotalMb >= 8192 && audit.Cpu.LogicalCores > 2) return false;
            }

            // Trava 6: reiniciar serviços derruba a fila de impressão.
            if (recommendation.Type == ActionType.Advanced_RestartCriticalServices) return false;

            // Trava 7: reset de stack de rede é decided por quem chama, porque
            // depende de saber se há VPN/sessão remota/tráfego no momento.
            if (recommendation.Type == ActionType.Network_ResetStack) return false;

            return true;
        }
    }
}
