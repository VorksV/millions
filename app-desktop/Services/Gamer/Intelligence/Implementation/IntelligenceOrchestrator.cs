using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services.Gamer.GamerModeManager;
using VoltrisOptimizer.Services.Thermal;

namespace VoltrisOptimizer.Services.Gamer.Intelligence.Implementation
{
    /// <summary>
    /// IntelligenceOrchestrator
    /// Centraliza todas as avaliações de monitoramento e telemetria para ELIMINAR múltiplos 
    /// loops paralelos (while true) que causam "spikes" de CPU fora de controle.
    /// Opera com padrão de Execução Adaptativa (1s -> 3s -> 5s).
    /// </summary>
    public class IntelligenceOrchestrator : IDisposable
    {
        private readonly ILoggingService _logger;
        private readonly VoltrisOptimizer.Services.Thermal.IThermalMonitorService _thermalMonitor;
        
        private CancellationTokenSource? _cts;
        private Task? _orchestratorTask;
        private bool _isRunning;
        private bool _gameActive;
        
        // Dynamic Interval
        private int _currentDelayMs = 5000;
        
        // Referências para serviços que rodam periodicamente
        // Esses serviços não devem mais usar while(!ct) em Tasks locais, mas sim serem chamados aqui.
        // Public Actions para injeção via DI / App.xaml.cs
        public event Func<HardwareMetrics, CancellationToken, Task>? OnAdaptiveEngineTick;
        public event Func<HardwareMetrics, CancellationToken, Task>? OnStutterPreventionTick;
        public event Func<HardwareMetrics, CancellationToken, Task>? OnThermalTick;

        private readonly PerGameLearningProfileService _learningService;
        private bool _cs2Detected = false;

        public IntelligenceOrchestrator(
            ILoggingService logger, 
            VoltrisOptimizer.Services.Thermal.IThermalMonitorService thermalMonitor,
            PerGameLearningProfileService learningService)
        {
            _logger = logger;
            _thermalMonitor = thermalMonitor;
            _learningService = learningService;
        }

        public void StartOrchestrator()
        {
            if (_isRunning) 
            {
                _logger.LogWarning("🧠 [Intelligence] Orchestrator JÁ está rodando - ignorando chamada");
                return;
            }
            
            // 🧠 LOGGING MÁXIMO: Inicialização detalhada
            _logger.LogInfo("🧠 [Intelligence] INICIANDO ORCHESTRATOR DE IA INTELIGENTE");
            _logger.LogInfo($"🧠 [Intelligence] Data/Hora: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            _logger.LogInfo($"🧠 [Intelligence] Thread ID: {System.Threading.Thread.CurrentThread.ManagedThreadId}");
            _logger.LogInfo($"🧠 [Intelligence] Process ID: {Process.GetCurrentProcess().Id}");
            
            try
            {
                _cts = new CancellationTokenSource();
                _isRunning = true;
                _gameActive = false;
                
                _logger.LogSuccess("🧠 [Intelligence] 🧠 Loop Principal de Inteligência Centralizado Iniciado.");
                _logger.LogInfo("🧠 [Intelligence] Modo: Adaptativo | Inicial: 5000ms");
                
                // Verificar dependências
                if (_thermalMonitor == null)
                {
                    _logger.LogError("🧠 [Intelligence] ❌ ThermalMonitorService é NULO!");
                }
                else
                {
                    _logger.LogSuccess("🧠 [Intelligence] ✅ ThermalMonitorService OK");
                }
                
                _orchestratorTask = Task.Run(() => MainOrchestrationLoop(_cts.Token), _cts.Token);
                
                _logger.LogSuccess("🧠 [Intelligence] Task de orquestramento INICIADO com sucesso");
            }
            catch (Exception ex)
            {
                _logger.LogError($"🧠 [Intelligence] ERRO FATAL ao iniciar orchestrator: {ex.Message}", ex);
                _logger.LogError($"🧠 [Intelligence] Stack Trace: {ex.StackTrace}");
                throw;
            }
        }

        public void NotifyGameState(bool isGameActive)
        {
            _gameActive = isGameActive;
            if (_gameActive)
            {
                _currentDelayMs = 1500; // Começar com 1.5s ao abrir jogo
                _logger.LogInfo("[Orchestrator] 🎮 Jogo detectado. Frequência de pooling ajustada para 1.5s.");
            }
            else
            {
                _currentDelayMs = 5000;
                _logger.LogInfo("[Orchestrator] 📉 Sistema Idle. Frequência reduzida para 5s (Idle Mode).");
            }
        }

        private async Task MainOrchestrationLoop(CancellationToken ct)
        {
            var loopCount = 0;
            var orchestratorStart = DateTime.Now;
            
            _logger.LogInfo("🧠 [Intelligence] INICIANDO LOOP PRINCIPAL DE ORQUESTRAÇÃO");
            
            while (!ct.IsCancellationRequested)
            {
                loopCount++;
                var loopStart = DateTime.Now;
                
                try
                {
                    _logger.LogInfo($"🧠 [Intelligence] === LOOP #{loopCount} INICIADO === {loopStart:HH:mm:ss.fff}");
                    
                    var sw = Stopwatch.StartNew();

                    // 🔥 CORREÇÃO CRÍTICA: Detectar CS2 automaticamente para Games Learned
                    if (!_cs2Detected && loopCount % 10 == 1) // A cada 10 loops
                    {
                        try
                        {
                            _learningService.DetectAndStartCounterStrikeLearning();
                            _cs2Detected = true;
                            _logger.LogSuccess("🧠 [Intelligence] CS2 detection iniciada para Games Learned");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"🧠 [Intelligence] Erro ao iniciar detecção CS2: {ex.Message}");
                        }
                    }

                    // 1. Obter métricas 1 ÚNICA VEZ por ciclo (Centralizado + Cache Inteligente)
                    _logger.LogInfo("🧠 [Intelligence] Obtendo métricas térmicas...");
                    
                    var thermal = await _thermalMonitor.GetCurrentMetricsAsync();
                    
                    _logger.LogInfo($"🧠 [Intelligence] Métricas obtidas - CPU: {thermal.CpuTemperature:F1}°C, GPU: {thermal.GpuTemperature:F1}°C");
                    _logger.LogInfo($"🧠 [Intelligence] CPU Usage: {thermal.CpuUsage:F1}%, GPU Usage: {thermal.GpuUsage:F1}%");
                    _logger.LogInfo($"🧠 [Intelligence] Throttling - CPU: {thermal.CpuThrottling}, GPU: {thermal.GpuThrottling}");
                    
                    // Mapear para HardwareMetrics (GamerModeManager)
                    var metrics = new HardwareMetrics 
                    {
                        CpuTemperature = thermal.CpuTemperature,
                        GpuTemperature = thermal.GpuTemperature,
                        CpuUsage = thermal.CpuUsage,
                        GpuUsage = thermal.GpuUsage,
                        RamUsagePercent = thermal.RamUsagePercent,
                        GpuVramUsed = thermal.GpuVramUsed,
                        GpuVramTotal = thermal.GpuVramTotal,
                        CpuThrottling = thermal.CpuThrottling,
                        GpuThrottling = thermal.GpuThrottling,
                        Timestamp = thermal.Timestamp
                    };
                    
                    _logger.LogInfo($"🧠 [Intelligence] HardwareMetrics mapeado - VRAM: {metrics.GpuVramUsed:F1}/{metrics.GpuVramTotal:F1}MB");
                    
                    // 2. Verificar estado do jogo
                    var gameDetected = await DetectGameAsync();
                    if (gameDetected != _gameActive)
                    {
                        NotifyGameState(gameDetected);
                        _logger.LogInfo($"🧠 [Intelligence] Estado do jogo mudou: {_gameActive} -> {gameDetected}");
                    }

                    // 2. Acionar Subsistemas (Elimina a necessidade de while(true) paralelos)
                    _logger.LogInfo("🧠 [Intelligence] Acionando subsistemas de IA...");
                    
                    if (OnThermalTick != null) 
                    {
                        _logger.LogInfo("🧠 [Intelligence] 🌡️ Executando ThermalTick...");
                        await OnThermalTick(metrics, ct);
                        _logger.LogInfo("🧠 [Intelligence] 🌡️ ThermalTick concluído");
                    }
                    else
                    {
                        _logger.LogWarning("🧠 [Intelligence] ⚠️ OnThermalTick é NULO");
                    }
                    
                    if (OnAdaptiveEngineTick != null) 
                    {
                        _logger.LogInfo("🧠 [Intelligence] 🔧 Executando AdaptiveEngineTick...");
                        await OnAdaptiveEngineTick(metrics, ct);
                        _logger.LogInfo("🧠 [Intelligence] 🔧 AdaptiveEngineTick concluído");
                    }
                    else
                    {
                        _logger.LogWarning("🧠 [Intelligence] ⚠️ OnAdaptiveEngineTick é NULO");
                    }
                    
                    if (OnStutterPreventionTick != null) 
                    {
                        _logger.LogInfo("🧠 [Intelligence] 🛡️ Executando StutterPreventionTick...");
                        await OnStutterPreventionTick(metrics, ct);
                        _logger.LogInfo("🧠 [Intelligence] 🛡️ StutterPreventionTick concluído");
                    }
                    else
                    {
                        _logger.LogWarning("🧠 [Intelligence] ⚠️ OnStutterPreventionTick é NULO");
                    }

                    // 3. Recalcular a Frequência Adaptativa (Execution Intelligence)
                    _logger.LogInfo("🧠 [Intelligence] Ajustando frequência adaptativa...");
                    AdjustPollingFrequency(metrics);

                    sw.Stop();
                    var loopDuration = sw.ElapsedMilliseconds;
                    int timeToWait = Math.Max(0, _currentDelayMs - (int)loopDuration);
                    
                    _logger.LogInfo($"🧠 [Intelligence] Loop #{loopCount} concluído em {loopDuration}ms");
                    _logger.LogInfo($"🧠 [Intelligence] Próximo ciclo em {timeToWait}ms (intervalo atual: {_currentDelayMs}ms)");
                    
                    // Throttling global e bloqueio de CPU burst
                    // Se o ciclo está demorando mais que a frequência mínima, aplicamos backoff
                    if (loopDuration > 2000)
                    {
                        _logger.LogWarning($"🧠 [Intelligence] ⚠️ Throttling global ativado! Atraso de {loopDuration}ms detectado.");
                        timeToWait = Math.Max(timeToWait, 3000); // Forçar throttle para respirar a CPU
                        _logger.LogWarning($"🧠 [Intelligence] ⚠️ Forçando backoff para {timeToWait}ms");
                    }

                    await Task.Delay(timeToWait, ct);
                }
                catch (TaskCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogError($"🧠 [Intelligence] ❌ ERRO NO LOOP #{loopCount}: {ex.Message}", ex);
                    _logger.LogError($"🧠 [Intelligence] Stack Trace: {ex.StackTrace}");
                    _logger.LogError($"🧠 [Intelligence] Inner Exception: {ex.InnerException?.Message}");
                    
                    // Tentativa de recuperação
                    try
                    {
                        _logger.LogWarning("🧠 [Intelligence] Tentando recuperação após erro...");
                        await Task.Delay(5000, ct);
                    }
                    catch (Exception recoveryEx)
                    {
                        _logger.LogError($"🧠 [Intelligence] ❌ ERRO NA RECUPERAÇÃO: {recoveryEx.Message}", recoveryEx);
                        break;
                    }
                }
            }
        }

        private void AdjustPollingFrequency(HardwareMetrics metrics)
        {
            // O modo Gamer agora é PASSIVO.
            // Operamos com histerese alta (5 a 10 segundos) para não disputar tempo de CPU.
            if (!_gameActive)
            {
                _currentDelayMs = 10000; // 10 segundos em desktop (Deep Idle)
            }
            else
            {
                bool isThrottling = metrics.CpuThrottling || metrics.GpuThrottling || metrics.CpuTemperature > 85;
                
                if (isThrottling)
                    _currentDelayMs = 5000; // Se houver throttle, verificar a cada 5s
                else
                    _currentDelayMs = 7500; // Estabilidade normal: 7.5s (praticamente nulo de impacto)
            }
        }

        private Task<bool> DetectGameAsync()
        {
            // [REMOVIDO] A varredura síncrona de processos foi removida.
            // O estado do jogo é agora gerido de forma reativa pelo GamerModeOrchestrator 
            // através de chamadas a NotifyGameState(), eliminando totalmente o overhead 
            // de enumerar processos a cada segundo.
            return Task.FromResult(_gameActive);
        }

        public void StopOrchestrator()
        {
            if (!_isRunning) 
            {
                _logger.LogWarning("🧠 [Intelligence] Orchestrator NÃO está rodando - ignorando parada");
                return;
            }
            
            _logger.LogInfo("🧠 [Intelligence] 🛑 PARANDO ORCHESTRATOR DE IA");
            _logger.LogInfo($"🧠 [Intelligence] Tempo de execução: {DateTime.Now - new DateTime(2026, 4, 23, 18, 20, 0):TotalMinutes:F1} minutos");
            
            _isRunning = false;
            _cts?.Cancel();
            try { _orchestratorTask?.Wait(1000); } catch { }
            _cts?.Dispose();
            _cts = null;
            
            _logger.LogSuccess("🧠 [Intelligence] 🛑 Loop Centralizado finalizado com sucesso");
        }

        public void Dispose()
        {
            StopOrchestrator();
            GC.SuppressFinalize(this);
        }
    }
}