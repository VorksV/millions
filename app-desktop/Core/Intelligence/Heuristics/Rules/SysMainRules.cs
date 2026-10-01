using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Core.Intelligence.Heuristics.Abstractions;
using VoltrisOptimizer.Core.Intelligence.Heuristics.Models;

namespace VoltrisOptimizer.Core.Intelligence.Heuristics.Rules
{
    public class SysMainDiskTypeRule : IHeuristicRule
    {
        public string RuleName => "SysMain Disk Hardware Assessor";
        public string TargetOptimization => "SysMain";

        public ValueTask<RuleResult> EvaluateAsync(HeuristicsContext context, CancellationToken cancellationToken = default)
        {
            var hw = context.HardwareProfile;
            
            // Lembre-se: O Score aqui avalia "Devemos PAUSAR o SysMain?"
            if (hw?.HasSSD == false)
            {
                // Se o cara tem um HD, NUNCA pause o SysMain. Ele precisa do prefetch para não demorar 5 minutos num loading de jogo.
                return ValueTask.FromResult(new RuleResult
                {
                    RuleName = RuleName,
                    ScoreAdditive = -100, // Força a não mexer
                    ConfidenceImpact = 30, // Muita certeza dessa evidência
                    Reason = "O armazenamento principal possui um HDD Mecânico. O serviço de prefetch é vital para atenuar o gargalo crônico de leitura física. Pausar isso destruiria os tempos de carregamento.",
                    IsVeto = true, // VETO: Não importa o que as outras regras digam, proíba a desativação.
                    Priority = RulePriority.Critical,
                    Severity = RuleSeverity.Blocking,
                    Category = RuleCategory.HardwareCapability
                });
            }

            if (hw?.HasNVMe == true)
            {
                // NVMe não precisa de SuperFetch, a latência do disco já é menor que 1ms. Pausar o serviço evita I/O e CPU desperdiçados no background.
                return ValueTask.FromResult(new RuleResult
                {
                    RuleName = RuleName,
                    ScoreAdditive = +30, // Aumenta a vontade de Pausar
                    ConfidenceImpact = 10,
                    Reason = "Sistema equipado com armazenamento NVMe. O tempo de acesso aos dados é ultrarrápido por hardware, tornando o cache do SysMain em memória quase inútil, liberando o VOLTRIS para pausá-lo de forma segura.",
                    Priority = RulePriority.High,
                    Severity = RuleSeverity.Info,
                    Category = RuleCategory.HardwareCapability
                });
            }

            // SSD Sata Comum
            return ValueTask.FromResult(new RuleResult
            {
                RuleName = RuleName,
                ScoreAdditive = +10,
                ConfidenceImpact = 5,
                Reason = "SSD SATA Padrão detectado. O cache do SysMain tem um benefício mediano, mas sua pausa não causa perdas dramáticas de performance.",
                Priority = RulePriority.Normal,
                Severity = RuleSeverity.Info,
                Category = RuleCategory.HardwareCapability
            });
        }
    }

    public class SysMainRamCapacityRule : IHeuristicRule
    {
        public string RuleName => "SysMain RAM Pressure Assessor";
        public string TargetOptimization => "SysMain";

        public ValueTask<RuleResult> EvaluateAsync(HeuristicsContext context, CancellationToken cancellationToken = default)
        {
            var hw = context.HardwareProfile;
            if (hw == null) return ValueTask.FromResult(RuleResult.Neutral(RuleName));

            if (hw.TotalRAMGB <= 8)
            {
                return ValueTask.FromResult(new RuleResult
                {
                    RuleName = RuleName,
                    ScoreAdditive = +25,
                    ConfidenceImpact = 15,
                    Reason = $"Memória total escassa ({hw.TotalRAMGB}GB). O SysMain consome Standby RAM de forma agressiva. Pausá-lo protegerá a pouca memória disponível para impedir Swap de disco excessivo durante o jogo.",
                    IsCriticalEnforcement = true, // Quase força a aplicação (Pausar)
                    Priority = RulePriority.High,
                    Severity = RuleSeverity.Warning,
                    Category = RuleCategory.HardwareCapability
                });
            }

            if (hw.TotalRAMGB >= 16)
            {
                return ValueTask.FromResult(new RuleResult
                {
                    RuleName = RuleName,
                    ScoreAdditive = -15, // Puxa o freio. Não precisa pausar por RAM.
                    ConfidenceImpact = 5,
                    Reason = $"Volume de memória RAM muito confortável ({hw.TotalRAMGB}GB). O SysMain possui folga extrema para realizar cache sem asfixiar o jogo.",
                    Priority = RulePriority.Normal,
                    Severity = RuleSeverity.Info,
                    Category = RuleCategory.HardwareCapability
                });
            }

            return ValueTask.FromResult(RuleResult.Neutral(RuleName));
        }
    }

    public class SysMainTelemetryRule : IHeuristicRule
    {
        public string RuleName => "SysMain I/O Telemetry Watcher";
        public string TargetOptimization => "SysMain";

        public ValueTask<RuleResult> EvaluateAsync(HeuristicsContext context, CancellationToken cancellationToken = default)
        {
            var telemetry = context.Telemetry;
            if (telemetry == null) return ValueTask.FromResult(RuleResult.Neutral(RuleName));

            // Simulação de telemetria de CPU Load. Se o PC como um todo está esgoelado no talo:
            if (telemetry.CpuLoad > 90)
            {
                return ValueTask.FromResult(new RuleResult
                {
                    RuleName = RuleName,
                    ScoreAdditive = +10,
                    ScoreMultiplier = 1.2, // Multiplicador! Escala a dor se outras regras já pedem pra pausar
                    ConfidenceImpact = 20,
                    Reason = "O processador encontra-se em uso crítico contínuo (>90%). Qualquer serviço não-essencial do SO como o SysMain deve ser imediatamente despriorizado.",
                    Priority = RulePriority.Critical,
                    Severity = RuleSeverity.Severe,
                    Category = RuleCategory.RealTimeTelemetry
                });
            }

            return ValueTask.FromResult(RuleResult.Neutral(RuleName));
        }
    }
}
