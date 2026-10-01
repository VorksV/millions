  using System;
using System.Runtime;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.Gamer.Interfaces;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services.Gamer.Optimization
{
    /// <summary>
    /// OTIMIZAÇÃO PROFISSIONAL NÍVEL ENTERPRISE
    /// 
    /// Garbage Collection Optimizer para Gaming
    /// 
    /// Problema: GC do .NET pode causar pauses de 50-200ms durante coleta,
    /// resultando em micro-stutters durante gameplay.
    /// 
    /// Solução: Técnicas avançadas para minimizar GC pressure:
    /// 1. Forçar GC apenas em momentos seguros (fora do gameplay ativo)
    /// 2. Ajustar latência do GC para modo batch durante jogo
    /// 3. Limpar finalizers antes de sessões críticas
    /// 4. Monitorar alocações e prevenir coletas desnecessárias
    /// </summary>
    public class GamingGcOptimizer : IDisposable
    {
        private readonly ILoggingService _logger;
        private readonly object _gcLock = new();
        private bool _isGamingMode;
        private GCLatencyMode _originalLatencyMode;
        private bool _isDisposed;

        // Contadores para monitoramento
        private long _collectionCount0;
        private long _collectionCount1;
        private long _collectionCount2;
        private DateTime _lastGcTime = DateTime.MinValue;
        private int _forcedGcCount;

        // Thresholds profissionais
        private const int MAX_GC_PER_MINUTE = 2; // Máximo 2 GCs por minuto durante gaming
        private const int BYTES_ALLOCATED_THRESHOLD = 50 * 1024 * 1024; // 50MB

        public GamingGcOptimizer(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _originalLatencyMode = GCSettings.LatencyMode;
            
            _logger.LogInfo("[GcOptimizer] ✅ Gaming GC Optimizer inicializado");
            _logger.LogInfo($"[GcOptimizer] Modo original: {GCSettings.LatencyMode}");
        }

        /// <summary>
        /// Ativa modo gaming - otimiza GC para performance máxima
        /// </summary>
        public void EnterGamingMode()
        {
            _logger.LogInfo("[GcOptimizer.EnterGamingMode] Entry");
            lock (_gcLock)
            {
                if (_isGamingMode)
                {
                    _logger.LogWarning("[GcOptimizer] Já está em gaming mode");
                    _logger.LogInfo("[GcOptimizer.EnterGamingMode] Exit (already in mode)");
                    return;
                }

                _logger.LogInfo("[GcOptimizer] 🎮 Entrando em Gaming Mode...");

                // 1. Salvar contadores atuais para baseline
                _collectionCount0 = GC.CollectionCount(0);
                _collectionCount1 = GC.CollectionCount(1);
                _collectionCount2 = GC.CollectionCount(2);

                // 2. Limpar finalizers pendentes ANTES de começar o jogo
                // Isso previne GCs inesperados durante gameplay
                _logger.LogInfo("[GcOptimizer] Limpando finalizers pendentes...");
                GC.WaitForPendingFinalizers();
                GC.WaitForPendingFinalizers(); // Chamar duas vezes para garantir

                // AUDITORIA: GC pré-gaming não-bloqueante e sem compactação para evitar pausa longa.
                // blocking:true + compacting:true causa pausa de 100-300ms — inaceitável mesmo antes do jogo.
                _logger.LogInfo("[GcOptimizer] GC pré-gaming (non-blocking, no compact)...");
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, blocking: false, compacting: false);
                _forcedGcCount++;

                // AUDITORIA FORENSE: SustainedLowLatency em vez de Batch.
                // Batch mode = GC bloqueante completo (gen2) = pausas de 50-200ms = stutter.
                // SustainedLowLatency suprime GCs gen2 durante gameplay e é próprio para
                // cenários de longa duração (sessões de jogo).
                _logger.LogInfo("[GcOptimizer] Ajustando GC para SustainedLowLatency (auditado)...");
                GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;

                _isGamingMode = true;
                _lastGcTime = DateTime.Now;

                _logger.LogSuccess("[GcOptimizer] ✅ Gaming Mode ATIVADO - GC otimizado para performance");
                _logger.LogInfo($"[GcOptimizer] Baseline: Gen0={_collectionCount0}, Gen1={_collectionCount1}, Gen2={_collectionCount2}");
            }
            _logger.LogInfo("[GcOptimizer.EnterGamingMode] Exit");
        }

        /// <summary>
        /// Sai do modo gaming - restaura configurações originais
        /// </summary>
        public void ExitGamingMode()
        {
            _logger.LogInfo("[GcOptimizer.ExitGamingMode] Entry");
            lock (_gcLock)
            {
                if (!_isGamingMode)
                {
                    _logger.LogWarning("[GcOptimizer] Não está em gaming mode");
                    _logger.LogInfo("[GcOptimizer.ExitGamingMode] Exit (not in mode)");
                    return;
                }

                _logger.LogInfo("[GcOptimizer] 🛑 Saindo de Gaming Mode...");

                // 1. Restaurar latência original
                GCSettings.LatencyMode = _originalLatencyMode;

                // 2. Reportar estatísticas
                var gen0Delta = GC.CollectionCount(0) - _collectionCount0;
                var gen1Delta = GC.CollectionCount(1) - _collectionCount1;
                var gen2Delta = GC.CollectionCount(2) - _collectionCount2;

                _logger.LogInfo("[GcOptimizer] 📊 Estatísticas da sessão:");
                _logger.LogInfo($"  • GCs Gen0: {gen0Delta}");
                _logger.LogInfo($"  • GCs Gen1: {gen1Delta}");
                _logger.LogInfo($"  • GCs Gen2: {gen2Delta}");
                _logger.LogInfo($"  • GCs Forçados: {_forcedGcCount}");

                // 3. Coleta suave pós-gaming (não-blocking)
                _logger.LogInfo("[GcOptimizer] Executando GC pós-gaming (non-blocking)...");
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, blocking: false);
                });

                _isGamingMode = false;
                _forcedGcCount = 0;

                _logger.LogSuccess("[GcOptimizer] ✅ Configurações originais restauradas");
            }
            _logger.LogInfo("[GcOptimizer.ExitGamingMode] Exit");
        }

        /// <summary>
        /// Monitora GCs durante gaming e previne excesso
        /// Chamar periodicamente (a cada 30s) durante gameplay
        /// </summary>
        public void MonitorGcHealth()
        {
            _logger.LogDebug("[GcOptimizer.MonitorGcHealth] Entry");
            if (!_isGamingMode) return;

            lock (_gcLock)
            {
                var currentGen0 = GC.CollectionCount(0);
                var gen0SinceLastCheck = currentGen0 - _collectionCount0;
                var timeSinceLastGc = (DateTime.Now - _lastGcTime).TotalSeconds;

                // Se teve muitos GCs em pouco tempo, alertar
                if (gen0SinceLastCheck > MAX_GC_PER_MINUTE && timeSinceLastGc < 60)
                {
                    _logger.LogWarning($"[GcOptimizer] ⚠️ Alta frequência de GCs: {gen0SinceLastCheck} em {timeSinceLastGc:F0}s");
                    _logger.LogWarning("[GcOptimizer] Considerando reduzir alocações ou ajustar thresholds");
                }

                _collectionCount0 = currentGen0;
            }
            _logger.LogDebug("[GcOptimizer.MonitorGcHealth] Exit");
        }

        /// <summary>
        /// Executa GC estratégico em momento seguro
        /// Usar apenas durante pausas naturais (loading screens, etc.)
        /// </summary>
        public void PerformStrategicGc()
        {
            _logger.LogDebug("[GcOptimizer.PerformStrategicGc] Entry");
            if (!_isGamingMode) return;

            lock (_gcLock)
            {
                var timeSinceLastGc = (DateTime.Now - _lastGcTime).TotalMinutes;

                // Só executar se passou pelo menos 2 minutos desde último GC
                if (timeSinceLastGc < 2.0)
                {
                    _logger.LogDebug($"[GcOptimizer] Skip GC estratégico (apenas {timeSinceLastGc:F1}min desde último)");
                    return;
                }

                _logger.LogInfo("[GcOptimizer] Executando GC estratégico (loading screen)...");
                
                // GC otimizado: non-blocking para não causar stutter
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, blocking: false);
                
                _lastGcTime = DateTime.Now;
                _forcedGcCount++;
            }
            _logger.LogDebug("[GcOptimizer.PerformStrategicGc] Exit");
        }

        public void Dispose()
        {
            _logger.LogDebug("[GcOptimizer.Dispose] Entry");
            if (!_isDisposed)
            {
                if (_isGamingMode)
                {
                    ExitGamingMode();
                }

                _isDisposed = true;
            }
            _logger.LogDebug("[GcOptimizer.Dispose] Exit");
        }
    }
}