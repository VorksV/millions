using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services.Gamer.Interfaces;
using VoltrisOptimizer.Services.Gamer.Models;
using GamerModels = VoltrisOptimizer.Services.Gamer.Models;

namespace VoltrisOptimizer.Services.Gamer.Implementation.AdaptiveGovernor
{
    /// <summary>
    /// Camada de ação - aplica ações seguras e reversíveis
    /// NUNCA aplica ações irreversíveis ou perigosas
    /// </summary>
    internal class ActionLayer
    {
        private readonly ILoggingService _logger;
        private readonly IProcessPrioritizer _processPrioritizer;
        private readonly IMemoryGamingOptimizer _memoryOptimizer;
        private readonly Process? _gameProcess;
        
        // Windows API para afinidade
        [DllImport("kernel32.dll")]
        private static extern bool SetProcessAffinityMask(IntPtr hProcess, IntPtr dwProcessAffinityMask);
        
        [DllImport("kernel32.dll")]
        private static extern bool GetProcessAffinityMask(IntPtr hProcess, out IntPtr lpProcessAffinityMask, out IntPtr lpSystemAffinityMask);
        
        public ActionLayer(
            ILoggingService logger,
            IProcessPrioritizer processPrioritizer,
            IMemoryGamingOptimizer memoryOptimizer,
            Process? gameProcess)
        {
            _logger.LogEntry(nameof(ActionLayer), ("logger", logger != null), ("processPrioritizer", processPrioritizer != null), ("memoryOptimizer", memoryOptimizer != null));
            _logger.LogDebug($"ActionLayer gameProcess={gameProcess?.ProcessName}");
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _processPrioritizer = processPrioritizer ?? throw new ArgumentNullException(nameof(processPrioritizer));
            _memoryOptimizer = memoryOptimizer ?? throw new ArgumentNullException(nameof(memoryOptimizer));
            _gameProcess = gameProcess;
            _logger.LogExit(nameof(ActionLayer));
        }
        
        /// <summary>
        /// Executa uma ação adaptativa de forma segura
        /// </summary>
        public async Task<ActionResult> ExecuteActionAsync(AdaptiveAction action, CancellationToken cancellationToken = default)
        {
            _logger.LogEntry(nameof(ExecuteActionAsync), ("action.Type", action.Type), ("action.Reason", action.Reason));
            var stopwatch = Stopwatch.StartNew();
            var result = new ActionResult
            {
                Action = action,
                Success = false,
                Timestamp = DateTime.UtcNow
            };
            
            try
            {
                switch (action.Type)
                {
                    case ActionType.IncreaseGamePriority:
                        result = await IncreaseGamePriorityAsync(cancellationToken);
                        break;
                    
                    case ActionType.ReduceBackgroundPriorities:
                        result = await ReduceBackgroundPrioritiesAsync(cancellationToken);
                        break;
                    
                    case ActionType.CleanStandbyList:
                        result = await CleanStandbyListAsync(cancellationToken);
                        break;
                    
                    case ActionType.AdjustAffinity:
                        result = await AdjustAffinityAsync(cancellationToken);
                        break;
                    
                    default:
                        result.ErrorMessage = "Tipo de ação desconhecido";
                        _logger.LogExit(nameof(ExecuteActionAsync), result.Success, stopwatch.ElapsedMilliseconds);
                        return result;
                }
                
                result.Success = true;
                _logger.LogInfo($"[ActionLayer] ✓ Ação executada: {action.Type} - {action.Reason}");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                _logger.LogError($"[ActionLayer] ✗ Erro ao executar ação {action.Type}: {ex.Message}", ex);
            }
            
            _logger.LogExit(nameof(ExecuteActionAsync), result.Success, stopwatch.ElapsedMilliseconds);
            return result;
        }
        
        private async Task<ActionResult> IncreaseGamePriorityAsync(CancellationToken cancellationToken)
        {
            _logger.LogEntry(nameof(IncreaseGamePriorityAsync));
            var stopwatch = Stopwatch.StartNew();
            var result = new ActionResult
            {
                Action = new AdaptiveAction { Type = ActionType.IncreaseGamePriority },
                Timestamp = DateTime.UtcNow
            };
            
            if (_gameProcess == null || _gameProcess.HasExited)
            {
                result.ErrorMessage = "Processo do jogo não disponível";
                _logger.LogExit(nameof(IncreaseGamePriorityAsync), result.Success, stopwatch.ElapsedMilliseconds);
                return result;
            }
            
            return await Task.Run(() =>
            {
                try
                {
                    // Verificar prioridade atual
                    var currentPriority = _gameProcess.PriorityClass;
                    
                    // Se já está em High ou RealTime, não fazer nada
                    if (currentPriority == ProcessPriorityClass.High || 
                        currentPriority == ProcessPriorityClass.RealTime)
                    {
                        result.Success = true;
                        result.Message = $"Prioridade já está em {currentPriority}";
                        _logger.LogDebug($"[ActionLayer] {result.Message}");
                        return result;
                    }
                    
                    // Aumentar para High (nunca RealTime - muito perigoso)
                    _processPrioritizer.SetPriority(
                        _gameProcess.Id, 
                        GamerModels.ProcessPriorityLevel.High);
                    
                    result.Success = true;
                    result.Message = $"Prioridade aumentada de {currentPriority} para High";
                    result.RollbackData = new RollbackData
                    {
                        Type = RollbackType.ProcessPriority,
                        ProcessId = _gameProcess.Id,
                        OriginalPriority = currentPriority
                    };
                    
                    return result;
                }
                catch (Exception ex)
                {
                    result.ErrorMessage = ex.Message;
                    return result;
                }
            }, cancellationToken);
        }
        
        private async Task<ActionResult> ReduceBackgroundPrioritiesAsync(CancellationToken cancellationToken)
        {
            _logger.LogEntry(nameof(ReduceBackgroundPrioritiesAsync));
            var stopwatch = Stopwatch.StartNew();
            var result = new ActionResult
            {
                Action = new AdaptiveAction { Type = ActionType.ReduceBackgroundPriorities },
                Timestamp = DateTime.UtcNow
            };
            
            try
            {
                // Usar método existente do ProcessPrioritizer
                var count = _processPrioritizer.LowerBackgroundProcessesPriority();
                
                result.Success = true;
                result.Message = $"{count} processos em background tiveram prioridade reduzida";
                _logger.LogInfo($"[ActionLayer] {result.Message}");
                
                // Nota: Rollback é gerenciado pelo ProcessPrioritizer
                _logger.LogExit(nameof(ReduceBackgroundPrioritiesAsync), result.Success, stopwatch.ElapsedMilliseconds);
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                _logger.LogError($"[ActionLayer] Erro ao reduzir prioridades: {ex.Message}", ex);
                _logger.LogExit(nameof(ReduceBackgroundPrioritiesAsync), result.Success, stopwatch.ElapsedMilliseconds);
                return result;
            }
        }
        
        private async Task<ActionResult> CleanStandbyListAsync(CancellationToken cancellationToken)
        {
            _logger.LogEntry(nameof(CleanStandbyListAsync));
            var stopwatch = Stopwatch.StartNew();
            var result = new ActionResult
            {
                Action = new AdaptiveAction { Type = ActionType.CleanStandbyList },
                Timestamp = DateTime.UtcNow
            };
            
            try
            {
                // Limpar standby list (ação temporária, não precisa rollback)
                _memoryOptimizer.CleanStandbyList();
                
                result.Success = true;
                result.Message = LocalizationService.Instance.GetString("StandbyListCleanedMsg");
                _logger.LogInfo($"[ActionLayer] {result.Message}");
                
                // Não precisa rollback - efeito é temporário
                _logger.LogExit(nameof(CleanStandbyListAsync), result.Success, stopwatch.ElapsedMilliseconds);
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                _logger.LogError($"[ActionLayer] Erro ao limpar standby list: {ex.Message}", ex);
                _logger.LogExit(nameof(CleanStandbyListAsync), result.Success, stopwatch.ElapsedMilliseconds);
                return result;
            }
        }
        
        private async Task<ActionResult> AdjustAffinityAsync(CancellationToken cancellationToken)
        {
            _logger.LogEntry(nameof(AdjustAffinityAsync));
            var stopwatch = Stopwatch.StartNew();
            var result = new ActionResult
            {
                Action = new AdaptiveAction { Type = ActionType.AdjustAffinity },
                Timestamp = DateTime.UtcNow
            };
            
            if (_gameProcess == null || _gameProcess.HasExited)
            {
                result.ErrorMessage = "Processo do jogo não disponível";
                _logger.LogExit(nameof(AdjustAffinityAsync), result.Success, stopwatch.ElapsedMilliseconds);
                return result;
            }
            
            try
            {
                var coreCount = Environment.ProcessorCount;
                
                // SEGURANÇA: Só ajustar afinidade em CPUs com 8+ cores
                // Em CPUs pequenas, deixar Windows gerenciar
                if (coreCount < 8)
                {
                    result.Success = false;
                    result.ErrorMessage = $"Afinidade não ajustada: CPU tem apenas {coreCount} cores (mínimo: 8)";
                    _logger.LogWarning($"[ActionLayer] {result.ErrorMessage}");
                    _logger.LogExit(nameof(AdjustAffinityAsync), result.Success, stopwatch.ElapsedMilliseconds);
                    return result;
                }
                
                // Obter afinidade atual
                if (!GetProcessAffinityMask(_gameProcess.Handle, out var currentAffinity, out var systemAffinity))
                {
                    result.ErrorMessage = "Não foi possível obter afinidade atual";
                    _logger.LogWarning($"[ActionLayer] {result.ErrorMessage}");
                    _logger.LogExit(nameof(AdjustAffinityAsync), result.Success, stopwatch.ElapsedMilliseconds);
                    return result;
                }
                
                // Reservar apenas core 0 para sistema, usar todos os outros para o jogo
                IntPtr newAffinity = (IntPtr)(((1L << coreCount) - 1) & ~1L);

                // [FIX:PRIORIDADE-UNICA] AFINIDADE FIXA RECUSADA
                // ============================================
                // Este código fixava o jogo em núcleos e reservava UM core para o
                // Windows. Três problemas, e o terceiro é o que mata:
                //
                // 1. A REGRA 7 do self-test do Perfil proíbe fixar núcleo do
                //    jogo, e a matriz documenta `PinGameCores = false` com o
                //    motivo: a Intel desaconselha afinidade dura em CPU
                //    híbrida, porque um thread preso pode acabar disputando
                //    núcleo com um processo de alta prioridade e passar fome.
                //
                // 2. Em CPU híbrida o NÚMERO do core não significa nada. O core
                //    "1" pode ser um E-core. A expressão acima assume uma
                //    topologia de núcleos equivalentes, e essa suposição quebra
                //    justamente nas máquinas mais novas.
                //
                // 3. Um core não sustenta o Windows. Kernel, drivers, áudio,
                //    rede, explorer e as dezenas de serviços de fundo dividem
                //    esse único core. Quando o jogo dispara uma rajada, o SO
                //    para de responder, e o sintoma aparece como "engasgo" —
                //    exatamente o que a REGRA 7 existe para evitar.
                //
                // A chamada é recusada pelo portão, e a recusa é registrada.
                // Não é removida: se um dia existir medição que prove o
                // contrário, o caminho está aqui e documentado.
                if (VoltrisOptimizer.Services.Power.GamerProcessAuthority.IsBlocked(
                        "ActionLayer.AdjustAffinity", ProcessPriorityClass.Normal, isAffinityPin: true, out string gateReason))
                {
                    result.Success = false;
                    result.ErrorMessage = gateReason;
                    _logger.LogWarning($"[ActionLayer] {gateReason}");
                    _logger.LogExit(nameof(AdjustAffinityAsync), result.Success, stopwatch.ElapsedMilliseconds);
                    return result;
                }

                if (SetProcessAffinityMask(_gameProcess.Handle, newAffinity))
                {
                    result.Success = true;
                    result.Message = $"Afinidade ajustada: cores 1-{coreCount - 1} (core 0 reservado)";
                    result.RollbackData = new RollbackData
                    {
                        Type = RollbackType.ProcessAffinity,
                        ProcessId = _gameProcess.Id,
                        OriginalAffinity = currentAffinity
                    };
                    _logger.LogInfo($"[ActionLayer] {result.Message}");
                }
                else
                {
                    result.ErrorMessage = "Não foi possível definir afinidade";
                    _logger.LogWarning($"[ActionLayer] {result.ErrorMessage}");
                }
                
                _logger.LogExit(nameof(AdjustAffinityAsync), result.Success, stopwatch.ElapsedMilliseconds);
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                _logger.LogError($"[ActionLayer] Erro ao ajustar afinidade: {ex.Message}", ex);
                _logger.LogExit(nameof(AdjustAffinityAsync), result.Success, stopwatch.ElapsedMilliseconds);
                return result;
            }
        }
    }
    
    /// <summary>
    /// Resultado da execução de uma ação
    /// </summary>
    internal class ActionResult
    {
        public AdaptiveAction Action { get; set; } = null!;
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public RollbackData? RollbackData { get; set; }
    }
    
    /// <summary>
    /// Dados para rollback
    /// </summary>
    internal class RollbackData
    {
        public RollbackType Type { get; set; }
        public int ProcessId { get; set; }
        public ProcessPriorityClass? OriginalPriority { get; set; }
        public IntPtr? OriginalAffinity { get; set; }
    }
    
    /// <summary>
    /// Tipos de rollback
    /// </summary>
    internal enum RollbackType
    {
        ProcessPriority,
        ProcessAffinity
    }
}

