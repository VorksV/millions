using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services.Gamer.Adaptive.Models;

namespace VoltrisOptimizer.Services.Gamer.Adaptive.Intelligence
{
    /// <summary>
    /// Motor de decisão adaptativo - determina quais otimizações aplicar baseado no hardware e contexto
    /// </summary>
    public class AdaptiveOptimizationEngine
    {
        private readonly ILoggingService _logger;
        private readonly HardwareProfiler _hardwareProfiler;
        
        public AdaptiveOptimizationEngine(ILoggingService logger, HardwareProfiler hardwareProfiler)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _hardwareProfiler = hardwareProfiler ?? throw new ArgumentNullException(nameof(hardwareProfiler));
        }
        
        /// <summary>
        /// Cria plano de execução adaptativo baseado no hardware e jogo
        /// </summary>
        public async Task<ExecutionPlan> CreateExecutionPlanAsync(GameProfile game)
        {
            _logger.LogInfo(nameof(CreateExecutionPlanAsync));
            
            var sessionId = Guid.NewGuid().ToString("N").Substring(0, 8);
            
            _logger.LogInfo("═══════════════════════════════════════════════════════════════");
            _logger.LogInfo($"[AdaptiveEngine] 🧠 CRIANDO PLANO DE EXECUÇÃO ADAPTATIVO");
            _logger.LogInfo($"[AdaptiveEngine] Session ID: {sessionId}");
            _logger.LogInfo($"[AdaptiveEngine] Game: {game.Name}");
            _logger.LogInfo("═══════════════════════════════════════════════════════════════");
            
            // 1. Profile hardware
            var hardware = await _hardwareProfiler.ProfileHardwareAsync();
            hardware.SessionId = sessionId;
            
            LogHardwareProfile(hardware);
            
            // 2. Create decisions for each optimization
            var decisions = new List<OptimizationDecision>();
            
            decisions.Add(DecideTimerResolution(hardware, game));
            decisions.Add(DecidePowerPlan(hardware, game));
            decisions.Add(DecideCpuAffinity(hardware, game));
            decisions.Add(DecideMemoryOptimization(hardware, game));
            decisions.Add(DecideGpuHags(hardware, game));
            decisions.Add(DecideGpuLowLatency(hardware, game));
            decisions.Add(DecideGpuPowerMode(hardware, game));
            decisions.Add(DecideNetworkOptimization(hardware, game));
            decisions.Add(DecideWindowsServiceOptimization(hardware, game));
            decisions.Add(DecideSchedulerSuspension(hardware, game));
            decisions.Add(DecideProcessPriority(hardware, game));
            
            // 3. Sort by priority
            decisions = decisions.OrderByDescending(d => d.Priority).ToList();
            
            // 4. Create plan
            var plan = new ExecutionPlan
            {
                SessionId = sessionId,
                Hardware = hardware,
                Game = game,
                Decisions = decisions
            };
            
            LogExecutionPlan(plan);
            
            _logger.LogInfo(nameof(CreateExecutionPlanAsync));
            return plan;
        }
        
        #region Decision Rules
        
        private OptimizationDecision DecideTimerResolution(HardwareProfile hw, GameProfile game)
        {
            var decision = new OptimizationDecision
            {
                Type = OptimizationType.TimerResolution,
                Priority = 90
            };
            
            // REGRA: SKIP em CPUs < 6 cores (aumenta CPU baseline)
            if (hw.Cpu.PhysicalCores < 6)
            {
                decision.Action = OptimizationAction.SKIP;
                decision.Reason = $"CPU com {hw.Cpu.PhysicalCores} cores: Timer 0.5ms aumenta baseline CPU em 1-3%";
                decision.Risk = RiskLevel.HIGH;
                return decision;
            }
            
            // REGRA: SKIP em notebook com bateria (reduz battery life)
            if (hw.System.IsLaptop && hw.System.IsOnBattery)
            {
                decision.Action = OptimizationAction.SKIP;
                decision.Reason = "Notebook em bateria: Timer 0.5ms reduz duração da bateria";
                decision.Risk = RiskLevel.MEDIUM;
                return decision;
            }
            
            // REGRA: CONDITIONAL para jogos competitivos (benefício real)
            if (game.Type == GameType.Competitive || game.Type == GameType.Esports)
            {
                decision.Action = OptimizationAction.APPLY;
                decision.Reason = "Jogo competitivo: Timer 0.5ms reduz input lag";
                decision.Risk = RiskLevel.LOW;
                decision.Parameters["TargetResolution"] = 0.5; // ms
                return decision;
            }
            
            // REGRA: SKIP para jogos casuais (sem benefício)
            if (game.Type == GameType.Casual || game.Type == GameType.Strategy)
            {
                decision.Action = OptimizationAction.SKIP;
                decision.Reason = "Jogo casual/strategy: Timer 0.5ms sem benefício perceptível";
                decision.Risk = RiskLevel.LOW;
                return decision;
            }
            
            // DEFAULT: APPLY para CPUs >= 6 cores
            decision.Action = OptimizationAction.APPLY;
            decision.Reason = $"CPU {hw.Cpu.PhysicalCores}c: Timer 0.5ms seguro";
            decision.Risk = RiskLevel.LOW;
            decision.Parameters["TargetResolution"] = 0.5;
            return decision;
        }
        
        private OptimizationDecision DecideCpuAffinity(HardwareProfile hw, GameProfile game)
        {
            var decision = new OptimizationDecision
            {
                Type = OptimizationType.CpuAffinity,
                Priority = 30 // LOW priority
            };
            
            // REGRA: NUNCA aplicar em CPUs híbridas (Intel 12th gen+)
            if (hw.Cpu.IsHybrid)
            {
                decision.Action = OptimizationAction.SKIP;
                decision.Reason = "CPU híbrida (Intel 12gen+): Thread Director gerencia melhor que affinity manual";
                decision.Risk = RiskLevel.HIGH;
                return decision;
            }
            
            // REGRA: NUNCA aplicar em AMD Ryzen modernas (NUMA aware)
            if (hw.Cpu.Vendor.Contains("AMD", StringComparison.OrdinalIgnoreCase) && hw.Cpu.PhysicalCores >= 8)
            {
                decision.Action = OptimizationAction.SKIP;
                decision.Reason = "AMD Ryzen 8+cores: Affinity manual quebra NUMA e CCX optimization";
                decision.Risk = RiskLevel.HIGH;
                return decision;
            }
            
            // DEFAULT: SKIP (affinity manual é legacy)
            decision.Action = OptimizationAction.SKIP;
            decision.Reason = "CPU Affinity manual é legado e geralmente prejudica performance";
            decision.Risk = RiskLevel.HIGH;
            return decision;
        }
        
        private OptimizationDecision DecidePowerPlan(HardwareProfile hw, GameProfile game)
        {
            var decision = new OptimizationDecision
            {
                Type = OptimizationType.PowerPlan,
                Priority = 95
            };
            
            // REGRA: SKIP em notebook com bateria
            if (hw.System.IsLaptop && hw.System.IsOnBattery)
            {
                decision.Action = OptimizationAction.SKIP;
                decision.Reason = "Notebook em bateria: manter plano de energia atual";
                decision.Risk = RiskLevel.LOW;
                return decision;
            }
            
            // REGRA: APPLY Ultimate Performance em desktops high-end
            if (!hw.System.IsLaptop && hw.OverallTier >= HardwareTier.HIGH)
            {
                decision.Action = OptimizationAction.APPLY;
                decision.Reason = "Desktop high-end: Ultimate Performance seguro";
                decision.Risk = RiskLevel.LOW;
                decision.Parameters["TargetPlan"] = "Ultimate Performance";
                return decision;
            }
            
            // DEFAULT: APPLY High Performance
            decision.Action = OptimizationAction.APPLY;
            decision.Reason = "Hardware adequado: High Performance recomendado";
            decision.Risk = RiskLevel.LOW;
            decision.Parameters["TargetPlan"] = "High Performance";
            return decision;
        }
        
        private OptimizationDecision DecideMemoryOptimization(HardwareProfile hw, GameProfile game)
        {
            var decision = new OptimizationDecision
            {
                Type = OptimizationType.MemoryOptimization,
                Priority = 50
            };
            
            // REGRA: SKIP se RAM suficiente (>50% disponível)
            if (hw.Ram.UsagePercent < 50)
            {
                decision.Action = OptimizationAction.SKIP;
                decision.Reason = $"RAM suficiente ({hw.Ram.AvailableGb:F1}GB livre): limpeza desnecessária";
                decision.Risk = RiskLevel.LOW;
                return decision;
            }
            
            // DEFAULT: APPLY limpeza moderada
            decision.Action = OptimizationAction.APPLY;
            decision.Reason = $"RAM em uso ({hw.Ram.UsagePercent:F0}%): limpeza moderada";
            decision.Risk = RiskLevel.LOW;
            decision.Parameters["AggressivenessLevel"] = "Moderate";
            return decision;
        }
        
        private OptimizationDecision DecideGpuHags(HardwareProfile hw, GameProfile game)
        {
            var decision = new OptimizationDecision
            {
                Type = OptimizationType.GpuHags,
                Priority = 70
            };
            
            // REGRA: SKIP se GPU não suporta
            if (!hw.Gpu.IsDiscrete)
            {
                decision.Action = OptimizationAction.SKIP;
                decision.Reason = "GPU integrada: HAGS não recomendado";
                decision.Risk = RiskLevel.LOW;
                return decision;
            }
            
            // DEFAULT: APPLY em GPUs dedicadas modernas
            decision.Action = OptimizationAction.APPLY;
            decision.Reason = "GPU dedicada: HAGS pode reduzir latência";
            decision.Risk = RiskLevel.LOW;
            return decision;
        }
        
        private OptimizationDecision DecideGpuLowLatency(HardwareProfile hw, GameProfile game)
        {
            var decision = new OptimizationDecision
            {
                Type = OptimizationType.GpuLowLatency,
                Priority = 75
            };
            
            // REGRA: SKIP se não é GPU dedicada
            if (!hw.Gpu.IsDiscrete)
            {
                decision.Action = OptimizationAction.SKIP;
                decision.Reason = "GPU integrada: low-latency mode não aplicável";
                decision.Risk = RiskLevel.LOW;
                return decision;
            }
            
            // DEFAULT: APPLY para jogos competitivos
            if (game.Type == GameType.Competitive || game.Type == GameType.Esports)
            {
                decision.Action = OptimizationAction.APPLY;
                decision.Reason = "Jogo competitivo: low-latency mode recomendado";
                decision.Risk = RiskLevel.LOW;
                return decision;
            }
            
            decision.Action = OptimizationAction.SKIP;
            decision.Reason = "Jogo casual: low-latency mode desnecessário";
            decision.Risk = RiskLevel.LOW;
            return decision;
        }
        
        private OptimizationDecision DecideGpuPowerMode(HardwareProfile hw, GameProfile game)
        {
            var decision = new OptimizationDecision
            {
                Type = OptimizationType.GpuPowerMode,
                Priority = 85
            };
            
            // REGRA: SKIP em notebooks com bateria
            if (hw.System.IsLaptop && hw.System.IsOnBattery)
            {
                decision.Action = OptimizationAction.SKIP;
                decision.Reason = "Notebook bateria: manter modo de energia balanceado";
                decision.Risk = RiskLevel.LOW;
                return decision;
            }
            
            // DEFAULT: APPLY max performance
            decision.Action = OptimizationAction.APPLY;
            decision.Reason = "Desktop/notebook plugged: max performance seguro";
            decision.Risk = RiskLevel.LOW;
            decision.Parameters["PowerMode"] = "MaxPerformance";
            return decision;
        }
        
        private OptimizationDecision DecideNetworkOptimization(HardwareProfile hw, GameProfile game)
        {
            var decision = new OptimizationDecision
            {
                Type = OptimizationType.NetworkOptimization,
                Priority = 60
            };
            
            // REGRA: SKIP se jogo não requer internet
            if (!game.RequiresInternet)
            {
                decision.Action = OptimizationAction.SKIP;
                decision.Reason = "Jogo offline: otimizações de rede desnecessárias";
                decision.Risk = RiskLevel.LOW;
                return decision;
            }
            
            // DEFAULT: APPLY tweaks seletivos
            decision.Action = OptimizationAction.APPLY;
            decision.Reason = "Jogo online: aplicar tweaks seletivos de rede";
            decision.Risk = RiskLevel.LOW;
            decision.Parameters["TweakLevel"] = "Selective";
            return decision;
        }
        
        private OptimizationDecision DecideWindowsServiceOptimization(HardwareProfile hw, GameProfile game)
        {
            var decision = new OptimizationDecision
            {
                Type = OptimizationType.WindowsServiceOptimization,
                Priority = 40
            };
            
            // REGRA: SKIP em hardware high-end
            if (hw.OverallTier >= HardwareTier.HIGH)
            {
                decision.Action = OptimizationAction.SKIP;
                decision.Reason = "Hardware high-end: serviços não impactam";
                decision.Risk = RiskLevel.LOW;
                return decision;
            }
            
            // DEFAULT: APPLY serviços SAFE
            decision.Action = OptimizationAction.APPLY;
            decision.Reason = "Hardware limitado: desativar serviços pesados";
            decision.Risk = RiskLevel.LOW;
            decision.Parameters["ServiceList"] = new[] { "WSearch", "BITS", "Fax" };
            return decision;
        }
        
        private OptimizationDecision DecideSchedulerSuspension(HardwareProfile hw, GameProfile game)
        {
            var decision = new OptimizationDecision
            {
                Type = OptimizationType.SchedulerSuspension,
                Priority = 35
            };
            
            // REGRA: APPLY apenas em hardware entry
            if (hw.Cpu.PhysicalCores <= 4)
            {
                decision.Action = OptimizationAction.APPLY;
                decision.Reason = "CPU limitada: pausar tarefas agendadas";
                decision.Risk = RiskLevel.LOW;
                return decision;
            }
            
            // DEFAULT: SKIP
            decision.Action = OptimizationAction.SKIP;
            decision.Reason = "CPU adequada: tarefas agendadas não impactam";
            decision.Risk = RiskLevel.LOW;
            return decision;
        }
        
        private OptimizationDecision DecideProcessPriority(HardwareProfile hw, GameProfile game)
        {
            var decision = new OptimizationDecision
            {
                Type = OptimizationType.ProcessPriority,
                Priority = 80
            };
            
            // REGRA: SEMPRE aplicar High priority
            decision.Action = OptimizationAction.APPLY;
            decision.Reason = "Process priority High melhora frame time consistency";
            decision.Risk = RiskLevel.LOW;
            decision.Parameters["Priority"] = "High";
            return decision;
        }
        
        #endregion
        
        #region Logging
        
        private void LogHardwareProfile(HardwareProfile hw)
        {
            _logger.LogInfo("─────────────────────────────────────────────────────────────");
            _logger.LogInfo("[AdaptiveEngine] 🖥️ HARDWARE PROFILE");
            _logger.LogInfo("─────────────────────────────────────────────────────────────");
            _logger.LogInfo($"[CPU] {hw.Cpu.Name} ({hw.Cpu.Tier})");
            _logger.LogInfo($"      {hw.Cpu.PhysicalCores}c/{hw.Cpu.LogicalProcessors}t | {hw.Cpu.MaxClockMhz:F0}MHz | Hybrid: {hw.Cpu.IsHybrid}");
            _logger.LogInfo($"[GPU] {hw.Gpu.Name} ({hw.Gpu.Tier})");
            _logger.LogInfo($"      VRAM: {hw.Gpu.VideoMemoryBytes / (1024*1024*1024):F1}GB | Discrete: {hw.Gpu.IsDiscrete}");
            _logger.LogInfo($"[RAM] {hw.Ram.TotalGb:F1}GB total, {hw.Ram.AvailableGb:F1}GB free ({100-hw.Ram.UsagePercent:F0}%)");
            _logger.LogInfo($"[SYS] {hw.System.Type} | {hw.System.WindowsVersion} | Tier: {hw.OverallTier}");
            _logger.LogInfo("─────────────────────────────────────────────────────────────");
        }
        
        private void LogExecutionPlan(ExecutionPlan plan)
        {
            _logger.LogInfo("─────────────────────────────────────────────────────────────");
            _logger.LogSuccess($"[AdaptiveEngine] 📋 PLANO: {plan.ApplyCount} APPLY | {plan.ConditionalCount} CONDITIONAL | {plan.SkipCount} SKIP");
            _logger.LogInfo("─────────────────────────────────────────────────────────────");
            
            foreach (var decision in plan.Decisions.Take(5)) // Log only top 5
            {
                var symbol = decision.Action == OptimizationAction.APPLY ? "✓" : 
                           decision.Action == OptimizationAction.SKIP ? "✗" : "?";
                _logger.LogInfo($"[{symbol}] {decision.Type}: {decision.Reason}");
            }
            
            if (plan.Decisions.Count > 5)
            {
                _logger.LogInfo($"... and {plan.Decisions.Count - 5} more decisions");
            }
            
            _logger.LogInfo("─────────────────────────────────────────────────────────────");
        }
        
        #endregion
    }
}