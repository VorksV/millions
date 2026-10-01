using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.Optimization.Telemetry
{
    /// <summary>
    /// Extreme Level: Scheduler-Aware e Frame-pacing Validation Loop.
    /// Emite rollback caso a intervenção no Kernel PIORE o frame-time médio.
    /// </summary>
    public class TelemetryFeedbackLoop
    {
        private readonly ILoggingService _logger;
        private readonly DxgiFrameTimeMonitor _dxgiMonitor;
        private readonly VoltrisOptimizer.Services.Optimization.Unification.OptimizationOrchestrator _orchestrator;

        public TelemetryFeedbackLoop(ILoggingService logger, DxgiFrameTimeMonitor dxgiMonitor, VoltrisOptimizer.Services.Optimization.Unification.OptimizationOrchestrator orchestrator)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _dxgiMonitor = dxgiMonitor;
            _orchestrator = orchestrator;
        }

        public async Task<bool> ValidateGameOptimizationAsync(int gamePid, Action applyAction, Action rollbackAction)
        {
            _logger.LogInfo($"[TelemetryFeedbackLoop] Testando métricas reais de DXGI para PID {gamePid} antes da otimização.");

            // 1. Coleta amostragem nativa Windows Scheduler (Sem modificação)
            double averageFrameTimeBefore = GetAverageFrameTime(gamePid);

            if (averageFrameTimeBefore <= 0)
            {
                _logger.LogWarning($"[TelemetryFeedbackLoop] Sem frame data DXGI suficiente para baseline. Intervenção CEGA permitida.");
                applyAction();
                return true; 
            }

            // 2. Aplica Injeção do Orchestrator (Priority / Affinity)
            applyAction();
            _logger.LogInfo($"[TelemetryFeedbackLoop] Otimização injetada. Aguardando 4000ms para estabilização de Queue e Cache L3...");

            // 3. Aguarda o Windows recalcular as filhas do Scheduler e o Game engine assentar caches P-Cores
            await Task.Delay(4000);

            // 4. Captura nova performance real do renderizador (Não da CPU %!)
            double averageFrameTimeAfter = GetAverageFrameTime(gamePid);

            double diff = averageFrameTimeAfter - averageFrameTimeBefore;
            double diffPercent = (diff / averageFrameTimeBefore) * 100.0;

            if (diff > 0.5) // Se subiu mais de 0.5ms (Piorou FPS)
            {
                _logger.LogWarning($"[TelemetryFeedbackLoop] 💀 OTIMIZAÇÃO FALHOU: Frame-Time subiu de {averageFrameTimeBefore:F2}ms para {averageFrameTimeAfter:F2}ms (+{diffPercent:F1}% regressão). Iniciando AUTOROLLBACK.");
                rollbackAction();

                // Registra que para ESSE scheduler as configs padrões são melhores 
                // e "finge" sucesso ao usuário ou aprende silenciosamente.
                return false;
            }
            
            if (diff < -0.1) // Se abaixou (Melhorou input lag / fluidez)
            {
                 _logger.LogSuccess($"[TelemetryFeedbackLoop] 🚀 FRAME-PACING MELHORADO: {averageFrameTimeBefore:F2}ms -> {averageFrameTimeAfter:F2}ms ({diffPercent:F1}% mais rápido). Política MANTIDA.");
                 return true;
            }

            // Zero impact (Scheduler moderno já estava fazendo certo)
            _logger.LogInfo($"[TelemetryFeedbackLoop] 🛡️ SCHEDULER AWARE: Windows já estava balanceado (Variação {diffPercent:F1}%). Mantendo intervenção atual por segurança.");
            return true;
        }

        private double GetAverageFrameTime(int pid)
        {
            // Amostra por 1.5s
            DateTime start = DateTime.UtcNow;
            int samples = 0;
            double sum = 0;

            while ((DateTime.UtcNow - start).TotalMilliseconds < 1500)
            {
                if (_dxgiMonitor.AverageFrameTime.TryGetValue(pid, out double frameMs))
                {
                    sum += frameMs;
                    samples++;
                    Thread.Sleep(50);
                }
                else
                {
                    return -1; // Jogo não mapeou DXGI ETW ainda.
                }
            }

            return samples > 0 ? (sum / samples) : -1;
        }
    }
}
