using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.Optimization.ZeroTrustCleaning
{
    public class RiskAdaptiveEngine : IRiskAdaptiveEngine
    {
        private readonly ILoggingService _logger;

        public RiskAdaptiveEngine(ILoggingService logger)
        {
            _logger = logger;
        }

        public Task<NodeRiskProfile> EvaluateRiskAsync(MutableNode node, IntelligentProfileType currentProfile)
        {
            var profile = new NodeRiskProfile
            {
                DangerScore = 0,
                RebuildCostSeconds = 0,
                IsHardBlocked = false,
                BlockReason = ""
            };

            // BASE: Determinar custo de reconstrução inerente ao Módulo
            if (node.SourceModuleId.Contains("ShaderCache", StringComparison.OrdinalIgnoreCase) ||
                node.SourceModuleId.Contains("D3D", StringComparison.OrdinalIgnoreCase))
            {
                profile.RebuildCostSeconds = 120; // 2 minutos para recompilar pipeline gráfico no 1º load
                profile.DangerScore = 40;         // Geração de Stutter garantido se deletar
            }
            else if (node.SourceModuleId.Contains("Download", StringComparison.OrdinalIgnoreCase) ||
                     node.SourceModuleId.Contains("UpdateCache", StringComparison.OrdinalIgnoreCase))
            {
                profile.RebuildCostSeconds = 300; // Demora baixar o Update do zero na banda larga
                profile.DangerScore = 15;         // Baixo risco se a rede for boa
            }
            else if (node.SourceModuleId.Contains("AssemblyCache", StringComparison.OrdinalIgnoreCase) || 
                     node.SourceModuleId.Contains(".NET", StringComparison.OrdinalIgnoreCase))
            {
                profile.RebuildCostSeconds = 900; // O Ngen.exe leva séculos no background
                profile.DangerScore = 80;         // Muito perigoso apagar o GAC (Global Assembly Cache)
            }
            else if (string.IsNullOrWhiteSpace(node.SourceModuleId) && node.Path.StartsWith(Environment.GetEnvironmentVariable("WINDIR") ?? "", StringComparison.OrdinalIgnoreCase))
            {
                 // Desconhecido dentro do Sistem32/Windows.
                 profile.DangerScore = 99; // CRÍTICO
                 profile.RebuildCostSeconds = 3600; // Talvez precise de DISM RestoreHealth
            }

            // MODULADOR DE PERFIL: O Perfil Ativo multiplica ou isenta regras

            switch (currentProfile)
            {
                case IntelligentProfileType.WorkOffice:
                case IntelligentProfileType.EnterpriseSecure:
                    // Perfis de trabalho Odeiam paralisações e stutters de Cache miss.
                    profile.DangerScore = Math.Min(100, profile.DangerScore + 30); // Soma penalidade
                    if (profile.DangerScore > 40)
                    {
                        profile.IsHardBlocked = true;
                        profile.BlockReason = $"Perfil '{currentProfile}' veta operações com Risco > 40. Pontuação: {profile.DangerScore}. (Custo de Caching Miss Inaceitável para produtividade).";
                    }

                    // Enterprise bloqueia completamente mexer em assinados do OS (Assembly)
                    if (node.SourceModuleId.Contains("AssemblyCache", StringComparison.OrdinalIgnoreCase))
                    {
                         profile.IsHardBlocked = true;
                         profile.BlockReason = $"Perfil '{currentProfile}' protege binários GAC/NGen para evitar Alerta de EDR/Antivírus corporativo.";
                    }
                    break;

                case IntelligentProfileType.GamerCompetitive:
                case IntelligentProfileType.GamerSinglePlayer:
                    // Gamer tolera rebuild lento (loading) para ganhar fps limpo. Desconto de Risco em Shaders.
                    if (node.SourceModuleId.Contains("ShaderCache", StringComparison.OrdinalIgnoreCase))
                    {
                         profile.DangerScore = Math.Max(0, profile.DangerScore - 30); // Zera praticamente o risco pra gamer
                    }

                    if (profile.DangerScore > 75)
                    {
                         profile.IsHardBlocked = true;
                         profile.BlockReason = $"Perfil '{currentProfile}' veta Risco > 75. Pontuação: {profile.DangerScore}. Risco de corromper instalação root.";
                    }
                    break;

                default: // GeneralBalanced etc
                    if (profile.DangerScore > 60)
                    {
                         profile.IsHardBlocked = true;
                         profile.BlockReason = $"Perfil padrão veta operações de alto risco (> 60). Pontuação: {profile.DangerScore}.";
                    }
                    break;
            }

            // TELEMETRIA FINAL LOG: Porcentual que será usado no modo Simulação
             if(profile.IsHardBlocked) 
                 _logger.LogWarning($"[RiskEngine] VETADO: {profile.BlockReason} -> {node.Path}");

            return Task.FromResult(profile);
        }
    }
}
