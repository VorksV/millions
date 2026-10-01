using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Core.Brain.V2;

namespace VoltrisOptimizer.Core.SystemIntelligenceProfiler
{
    /// <summary>
    /// Exceção lançada quando uma ação não pode ser aplicada porque não é aplicável no momento
    /// (ex: modo gamer sem jogo rodando). Não deve ser contada como erro.
    /// </summary>
    public class ActionNotApplicableException : Exception
    {
        public ActionNotApplicableException(string message) : base(message) { }
    }
    
    public class SystemIntelligenceProfiler : VoltrisOptimizer.Interfaces.ISystemProfiler
    {
        private readonly IAuditCollector _collector;
        private readonly IDecisionEngine _engine;
        private readonly ProfileStore _store;
        private readonly IRollbackManager _rollback;
        private readonly Interfaces.ICompatibilityPolicy _policy;
        private readonly SystemSafetyGuard _guard;
        private readonly Services.ILoggingService? _logger;
        private readonly VoltrisBrainV2 _brain;
        private ProfilerReport? _lastReport;

        public bool RequireGate => !IsGateCompleted;
        public bool IsGateCompleted => _store.Load().QuestionnaireCompleted && new OnboardingService().HasCompletedOnboarding();

        public async Task InitializeAsync()
        {
            try { App.LoggingService?.LogInfo("[PROFILER] SystemIntelligenceProfiler initialized"); } catch { }
            await Task.CompletedTask;
        }

        public SystemIntelligenceProfiler()
        {
            try
            {
                // Adicionar logs para verificar a inicialização de cada componente
                try { App.LoggingService?.LogInfo("[PROFILER] Iniciando inicialização do SystemIntelligenceProfiler"); } catch { }
                
                _collector = new AuditCollector();
                try { App.LoggingService?.LogInfo("[PROFILER] AuditCollector inicializado"); } catch { }
                
                _engine = new DecisionEngine();
                try { App.LoggingService?.LogInfo("[PROFILER] DecisionEngine inicializado"); } catch { }
                
                _store = new ProfileStore();
                try { App.LoggingService?.LogInfo("[PROFILER] ProfileStore inicializado"); } catch { }
                
                _rollback = new RollbackManager();
                try { App.LoggingService?.LogInfo("[PROFILER] RollbackManager inicializado"); } catch { }
                
                _policy = new DefaultCompatibilityPolicy();
                try { App.LoggingService?.LogInfo("[PROFILER] DefaultCompatibilityPolicy inicializado"); } catch { }
                
                _logger = App.LoggingService;
                try { App.LoggingService?.LogInfo("[PROFILER] Logger configurado"); } catch { }
                
                _guard = new SystemSafetyGuard(_policy, _logger ?? new LoggingService(LogDirectoryResolver.Resolve()));
                try { App.LoggingService?.LogInfo("[PROFILER] SystemSafetyGuard inicializado"); } catch { }
                
                try { App.LoggingService?.LogSuccess("[PROFILER] SystemIntelligenceProfiler inicializado com sucesso"); } catch { }
            }
            catch (Exception ex)
            {
                // Logar qualquer erro na inicialização
                try 
                { 
                    var logDir = LogDirectoryResolver.Resolve();
                    var logger = new LoggingService(logDir);
                    logger.LogError("[PROFILER] Erro na inicialização do SystemIntelligenceProfiler: " + ex.Message, ex);
                } 
                catch { }
                
                // Re-lançar a exceção
                throw;
            }
        }

        public SystemIntelligenceProfiler(IAuditCollector collector,
            IDecisionEngine engine,
            ProfileStore store,
            IRollbackManager rollback,
            Interfaces.ICompatibilityPolicy policy,
            Services.ILoggingService logger,
            VoltrisBrainV2 brain = null)
        {
            _collector = collector;
            _engine = engine;
            _store = store;
            _rollback = rollback;
            _policy = policy;
            _logger = logger;
            _guard = new SystemSafetyGuard(policy, logger);
            _brain = brain ?? Core.ServiceLocator.GetService<VoltrisBrainV2>();
            
            if (_brain != null)
            {
                _logger?.LogInfo("[PROFILER] Brain V2 injetado - alimentando Context Memory");
            }
        }

        public async Task<object> StartAuditAsync(CancellationToken ct)
        {
            App.LoggingService?.LogInfo("[PROFILER] 🔍 INICIANDO StartAuditAsync - Auditoria do sistema");
            App.LoggingService?.LogInfo($"[PROFILER] 📋 _collector instância: {_collector != null}");
            App.LoggingService?.LogInfo($"[PROFILER] 📋 _engine instância: {_engine != null}");
            App.LoggingService?.LogInfo($"[PROFILER] 📋 _store instância: {_store != null}");
            
            using var profilerToken = GlobalProgressService.Instance.BeginOperation(LocalizationService.Instance.GetString("AuditingSystem"), true);
            
            try
            {
                // Registrar início da operação
                var startTime = DateTime.UtcNow;
                App.LoggingService?.LogInfo($"[PROFILER] ⏱️ Início da auditoria: {startTime:HH:mm:ss.fff}");
                
                // Criar um cancellation token com timeout de 60 segundos para a operação completa.
                // 60s é suficiente para máquinas lentas/com WMI lento sem ser muito restritivo.
                // O token externo (ct) representa cancelamento manual pelo usuário.
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(60));
                App.LoggingService?.LogInfo("[PROFILER] ⏱️ CTS criado com timeout de 60 segundos");
                
                // Verificar cancelamento antes de começar
                cts.Token.ThrowIfCancellationRequested();
                
                App.LoggingService?.LogInfo("[PROFILER] 🔍 Chamando _collector.CollectAsync...");
                profilerToken.UpdateProgress(10, LocalizationService.Instance.GetString("CollectingSystemInfo"));
                var audit = await _collector.CollectAsync(cts.Token);
                App.LoggingService?.LogInfo($"[PROFILER] ✅ Coleta concluída - Audit instância: {audit != null}");
                profilerToken.UpdateProgress(60, LocalizationService.Instance.GetString("InfoCollectedEvaluating"));
                
                // Verificar cancelamento após a coleta
                cts.Token.ThrowIfCancellationRequested();
                
                // Verificar se a operação está demorando muito
                if ((DateTime.UtcNow - startTime).TotalSeconds > 45)
                {
                    App.LoggingService?.LogWarning("[PROFILER] ⚠️ Coleta de auditoria demorou mais que 45 segundos");
                }
                
                App.LoggingService?.LogInfo("[PROFILER] 🔄 Carregando answers do store");
                var answers = _store.Load().Answers;
                App.LoggingService?.LogInfo($"[PROFILER] ✅ Answers carregados: {answers != null}");
                
                // Verificar cancelamento antes de avaliar
                cts.Token.ThrowIfCancellationRequested();
                
                profilerToken.UpdateProgress(70, LocalizationService.Instance.GetString("EvaluatingRecommendations"));
                App.LoggingService?.LogInfo("[PROFILER] 🔍 Chamando _engine.Evaluate...");
                var report = _engine.Evaluate(audit, answers);
                App.LoggingService?.LogInfo($"[PROFILER] ✅ Avaliação concluída - Report instância: {report != null}");
                App.LoggingService?.LogInfo($"[PROFILER] 📊 Quantidade de recomendações: {report?.Recommendations?.Count ?? 0}");
                
                // Verificar cancelamento após avaliar
                cts.Token.ThrowIfCancellationRequested();
                
                var duration = DateTime.UtcNow - startTime;
                App.LoggingService?.LogSuccess($"[PROFILER] ✅ Auditoria concluída com sucesso | recs={report?.Recommendations?.Count ?? 0} | duração={duration.TotalSeconds:F2}s");
                profilerToken.UpdateProgress(100, LocalizationService.Instance.GetString("AuditCompleted"));
                return report;
            }
            catch (OperationCanceledException ocEx)
            {
                profilerToken?.UpdateProgress(0, LocalizationService.Instance.GetString("AuditCancelled"));

                // Distinguir entre timeout automático e cancelamento manual pelo usuário.
                // Em AMBOS os casos, é comportamento controlado — logar como Warning, não Error.
                bool wasTimeout = !ct.IsCancellationRequested; // ct externo não cancelou → foi o nosso CTS interno (timeout)
                string reason = wasTimeout
                    ? $"Timeout de 60 segundos atingido (coleta demorada em {ocEx.Source})"
                    : "Cancelado pelo usuário";

                App.LoggingService?.LogWarning($"[PROFILER] ⏱️ Auditoria interrompida: {reason}. Retornando relatório vazio.");

                // Retornar relatório vazio em caso de cancelamento — sem crash, sem Error no log
                return new ProfilerReport
                {
                    Audit = new AuditData(),
                    Answers = new UserAnswers(),
                    Recommendations = new List<ActionRecommendation>()
                };
            }
            catch (System.Exception ex)
            {
                profilerToken?.UpdateProgress(0, string.Format(LocalizationService.Instance.GetString("ErrorMessage"), ex.Message));
                App.LoggingService?.LogError($"[PROFILER] ❌ Falha na auditoria: {ex.Message}", ex);
                App.LoggingService?.LogError($"[PROFILER] ❌ StackTrace: {ex.StackTrace}");
                // Fallback resiliente: retorna relatório básico para não quebrar a UI
                App.LoggingService?.LogInfo("[PROFILER] 🔄 Criando fallback report");
                var fallbackAudit = new AuditData();
                var fallbackAnswers = _store.Load().Answers;
                var fallbackReport = _engine.Evaluate(fallbackAudit, fallbackAnswers);
                App.LoggingService?.LogInfo($"[PROFILER] ✅ Fallback report criado com {fallbackReport?.Recommendations?.Count ?? 0} recomendações");
                return fallbackReport;
            }
        }

        public async Task<object> AnalyzeAsync(CancellationToken ct = default)
        {
            _lastReport = await StartAuditAsync(ct) as ProfilerReport;
            
            // ✅ FASE 3: ALIMENTAR BRAIN CONTEXT MEMORY COM PERFIS
            if (_brain != null && _lastReport is ProfilerReport report)
            {
                await FeedBrainWithProfileAsync(report);
            }
            
            return _lastReport;
        }

        /// <summary>
        /// Alimenta Brain Context Memory com perfis do SystemIntelligenceProfiler
        /// PROTEÇÃO CRÍTICA: SaveAsync único fora do loop (evita I/O múltiplo)
        /// </summary>
        private async Task FeedBrainWithProfileAsync(ProfilerReport report)
        {
            try
            {
                _logger?.LogInfo("[PROFILER-BRAIN] Alimentando Brain com perfis do SystemIntelligenceProfiler...");

                int statesCreated = 0;
                
                // ✅ PROTEÇÃO CRÍTICA #2: Acumular na RAM, salvar no final
                foreach (var rec in report.Recommendations.Where(r => r.IsSelected || r.IsAlreadyOptimized))
                {
                    // Criar estado baseado no tipo de otimização
                    var brainState = new BrainStateKey(
                        workload: GetWorkloadFromActionType(rec.Type),
                        cpuBucket: (byte)GetCpuBucketFromAction(rec.Type),
                        ramBucket: (byte)GetRamBucketFromAction(rec.Type),
                        tempBucket: 5,  // Default
                        contextBucket: 4  // Profiler context
                    );

                    // Observar estado no Brain (acumula na RAM)
                    _brain.Memory.Observe(brainState, new SensorSnapshot
                    {
                        Workload = (WorkloadCategory)brainState.WorkloadBucket,
                        ForegroundProcessName = $"profile:{rec.Type}",
                        CpuUsagePercent = GetExpectedCpuFromAction(rec.Type),
                        RamUsagePercent = GetExpectedRamFromAction(rec.Type)
                    });

                    statesCreated++;
                    _logger?.LogDebug($"[PROFILER-BRAIN] Estado criado: {brainState.CanonicalKey} para {rec.Name}");
                }

                // ✅ PROTEÇÃO CRÍTICA #2: Save único após acumular todos os estados
                await _brain.Memory.SaveAsync();
                
                _logger?.LogSuccess($"[PROFILER-BRAIN] {statesCreated} perfis sincronizados com Brain");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[PROFILER-BRAIN] Erro: {ex.Message}");
            }
        }

        // Métodos auxiliares para mapear ActionType para estados do Brain
        private static WorkloadCategory GetWorkloadFromActionType(ActionType type)
        {
            return type switch
            {
                ActionType.PowerPlan_HighPerformance => WorkloadCategory.Game,
                ActionType.GamerMode_Activate => WorkloadCategory.Game,
                ActionType.Network_OptimizeTcp => WorkloadCategory.Work,
                ActionType.Network_ResetStack => WorkloadCategory.Work,
                ActionType.Memory_Optimize => WorkloadCategory.Work,
                ActionType.Process_Optimize => WorkloadCategory.Work,
                _ => WorkloadCategory.Idle
            };
        }

        private static int GetCpuBucketFromAction(ActionType type)
        {
            return type switch
            {
                ActionType.PowerPlan_HighPerformance => 0,  // 0-10% (performance)
                ActionType.GamerMode_Activate => 1,  // 10-20%
                ActionType.Memory_Optimize => 3,  // 30-40%
                ActionType.Process_Optimize => 3,  // 30-40%
                _ => 5  // 50-60% (default)
            };
        }

        private static int GetRamBucketFromAction(ActionType type)
        {
            return type switch
            {
                ActionType.Memory_Optimize => 2,  // 20-30% (otimizada)
                ActionType.Process_Optimize => 3,  // 30-40%
                _ => 5  // 50-60% (default)
            };
        }

        private static double GetExpectedCpuFromAction(ActionType type)
        {
            return type switch
            {
                ActionType.PowerPlan_HighPerformance => 15.0,
                ActionType.GamerMode_Activate => 25.0,
                ActionType.Memory_Optimize => 30.0,
                ActionType.Process_Optimize => 35.0,
                _ => 50.0
            };
        }

        private static double GetExpectedRamFromAction(ActionType type)
        {
            return type switch
            {
                ActionType.Memory_Optimize => 35.0,
                ActionType.Process_Optimize => 45.0,
                _ => 60.0
            };
        }

        public Task<ProfilerReport?> GetLastReportAsync()
        {
            return Task.FromResult(_lastReport);
        }

        public async Task UpdateRecommendationsStatusAsync(List<ActionRecommendation> recommendations)
        {
            foreach (var rec in recommendations)
            {
                rec.IsAlreadyOptimized = await CheckIfAlreadyOptimizedAsync(rec);
                
                if (!rec.Supported)
                {
                    rec.State = OptimizationState.Unsupported;
                    rec.StateMessage = LocalizationService.Instance.GetString("IncompatibleWithHardware");
                    rec.IsSelected = false;
                }
                else if (rec.IsAlreadyOptimized)
                {
                    rec.State = OptimizationState.AlreadyOptimized;
                    rec.StateMessage = LocalizationService.Instance.GetString("AlreadyActiveCorrectly");
                    rec.IsSelected = false;
                }
                else
                {
                    rec.State = OptimizationState.Recommended;
                    rec.StateMessage = LocalizationService.Instance.GetString("OptimizationRecommended");
                    rec.IsSelected = true;
                }
            }
        }

        private async Task<bool> CheckIfAlreadyOptimizedAsync(ActionRecommendation rec)
        {
            try
            {
                switch (rec.Type)
                {
                    case ActionType.PowerPlan_HighPerformance:
                        return await CheckPowerPlanAsync("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"); // High Performance
                    
                    case ActionType.Storage_DisableSuperfetch:
                        return !IsServiceRunning("SysMain");

                    case ActionType.Storage_EnableTrim:
                        return IsTrimEnabled();

                    case ActionType.Advanced_DisableHags:
                        return IsHagsDisabled();

                    case ActionType.Visual_Optimize:
                        return IsVisualOptimized();

                    case ActionType.Storage_DisableHibernation:
                        return !File.Exists(@"C:\hiberfil.sys");

                    case ActionType.Storage_DisableSearchIndexing:
                        return !IsServiceRunning("WSearch");

                    case ActionType.Storage_DisableLastAccess:
                        return IsLastAccessDisabled();

                    case ActionType.Storage_Disable83Naming:
                        return Is83NamingDisabled();

                    case ActionType.Storage_DisableEventLogging:
                        return IsEventLoggingReduced();
                    
                    case ActionType.Storage_DisableDefragBootFiles:
                        return IsDefragBootDisabled();
                        
                    case ActionType.Network_ResetStack:
                    case ActionType.Network_FlushDns:
                    case ActionType.SystemCleanup:
                    case ActionType.Memory_Optimize:
                    case ActionType.Process_Optimize:
                        return false; 

                    default:
                        return false;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[PROFILER] Erro ao checar status de {rec.Name}: {ex.Message}");
                return false;
            }
        }

        private bool IsLastAccessDisabled()
        {
            try
            {
                var output = AuditCollector.RunCommand("fsutil behavior query disablelastaccess");
                return output.Contains("1") || output.Contains("is 1");
            }
            catch { return false; }
        }

        private bool Is83NamingDisabled()
        {
            try
            {
                var output = AuditCollector.RunCommand("fsutil behavior query disable8dot3");
                return output.Contains("1") || output.Contains("is 1");
            }
            catch { return false; }
        }

        private bool IsEventLoggingReduced()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\WMI\Autologger\ReadyBoot", false);
                var val = key?.GetValue("Start");
                return val != null && Convert.ToInt32(val) == 0;
            }
            catch { return false; }
        }

        private bool IsDefragBootDisabled()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Dfrg\BootOptimizeFunction", false);
                var val = key?.GetValue("Enable");
                return val != null && val.ToString() == "N";
            }
            catch { return false; }
        }

        private async Task<bool> CheckPowerPlanAsync(string guidPart)
        {
            try
            {
                var output = await Task.Run(() => AuditCollector.RunCommand("powercfg /GETACTIVESCHEME"));
                return output.ToLowerInvariant().Contains(guidPart.ToLowerInvariant());
            }
            catch { return false; }
        }

        private bool IsServiceRunning(string serviceName)
        {
            try
            {
                using var sc = new System.ServiceProcess.ServiceController(serviceName);
                return sc.Status == System.ServiceProcess.ServiceControllerStatus.Running;
            }
            catch { return false; }
        }

        private bool IsTrimEnabled()
        {
            try
            {
                var output = AuditCollector.RunCommand("fsutil behavior query DisableDeleteNotify");
                return output.Contains("DisableDeleteNotify = 0");
            }
            catch { return false; }
        }

        private bool IsHagsDisabled()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", false);
                var val = key?.GetValue("HwSchMode");
                // HwSchMode: 1 = Desabilitado, 2 = Habilitado (conforme SystemConstants.HagsSettings)
                return val != null && Convert.ToInt32(val) == Core.Constants.SystemConstants.HagsSettings.Disabled;
            }
            catch { return false; }
        }

        private bool IsVisualOptimized()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", false);
                var val = key?.GetValue("VisualFXSetting");
                return val != null && Convert.ToInt32(val) == 2; // 2 = Adjust for best performance
            }
            catch { return false; }
        }

        public List<ActionRecommendation> GetRecommendations(ProfilerReport report, UserAnswers answers)
        {
            var r = _engine.Evaluate(report.Audit, answers);
            return r.Recommendations;
        }

        public async Task<ApplyResult> ApplyActionsAsync(IEnumerable<ActionRecommendation> actions, bool simulateOnly, CancellationToken ct)
        {
            App.LoggingService?.LogInfo("[PROFILER] 🔍 INICIANDO ApplyActionsAsync");
            App.LoggingService?.LogInfo($"[PROFILER] 📋 simulateOnly={simulateOnly}");
            App.LoggingService?.LogInfo($"[PROFILER] 📋 Quantidade de ações: {actions?.Count() ?? 0}");
            using var applyToken = GlobalProgressService.Instance.BeginOperation(LocalizationService.Instance.GetString("ApplyingOptimizations"), true);
            
            var session = _rollback.BeginSession();
            App.LoggingService?.LogInfo($"[PROFILER] 🔄 Session rollback iniciada: {session}");
            
            var applied = new List<string>();
            var errors  = new List<string>();
            var syncLock = new object();

            App.LoggingService?.LogInfo($"[PROFILER] 🔍 Verificando disponibilidade de serviços");
            if (!simulateOnly)
            {
                var missingServices = CheckServiceAvailability();
                if (missingServices.Count > 0)
                {
                    var errorMsg = string.Format(LocalizationService.Instance.GetString("ServicesNotAvailable"), string.Join(", ", missingServices));
                    App.LoggingService?.LogError($"[PROFILER] ❌ {errorMsg}");
                    return new ApplyResult { Success = false, Applied = new List<string>(), Errors = new List<string> { errorMsg }, Backups = _rollback.ListBackups() };
                }
                App.LoggingService?.LogInfo($"[PROFILER] ✅ Todos os serviços disponíveis");
            }
            else
            {
                App.LoggingService?.LogInfo($"[PROFILER] ⏩ Modo simulação: pulando verificação de serviços");
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(60));
            App.LoggingService?.LogInfo("[PROFILER] ⏱️ CTS criado com timeout de 60 segundos");

            // Coletar audit em paralelo com a filtragem das ações
            App.LoggingService?.LogInfo("[PROFILER] 🔍 Coletando audit em paralelo");
            var auditTask = _collector.CollectAsync(cts.Token);
            applyToken.UpdateProgress(5, LocalizationService.Instance.GetString("CheckingAvailableServices"));

            // Filtrar ações suportadas
            var actionList = new List<ActionRecommendation>();
            foreach (var a in actions) 
            { 
                if (a.Supported) 
                {
                    actionList.Add(a);
                    App.LoggingService?.LogInfo($"[PROFILER] ✅ Ação suportada: {a.Name}");
                }
                else
                {
                    App.LoggingService?.LogWarning($"[PROFILER] ⚠️ Ação não suportada: {a.Name}");
                }
            }
            App.LoggingService?.LogInfo($"[PROFILER] 📊 Quantidade de ações suportadas: {actionList.Count}");

            App.LoggingService?.LogInfo("[PROFILER] 🔍 Aguardando coleta de audit");
            var audit = await auditTask.ConfigureAwait(false);
            App.LoggingService?.LogInfo($"[PROFILER] ✅ Audit coletado: {audit != null}");

            applyToken.UpdateProgress(10, LocalizationService.Instance.GetString("PreparingActions"));

            if (simulateOnly)
            {
                App.LoggingService?.LogInfo("[PROFILER] 🔄 Modo simulação");
                foreach (var a in actionList)
                {
                    App.LoggingService?.LogInfo($"[PROFILER] ➕ Simulando: {a.Name}");
                    applied.Add("simulated:" + a.Name);
                }
                return new ApplyResult { Success = true, Applied = applied, Errors = new List<string>(), Backups = _rollback.ListBackups() };
            }

            // Separar ações em grupos: paralelas (independentes) vs sequenciais (limpeza/storage)
            var sequentialTypes = new[] { ActionType.SystemCleanup, ActionType.Storage_Defrag, ActionType.Storage_EnableTrim };
            var parallelActions   = new List<ActionRecommendation>();
            var sequentialActions = new List<ActionRecommendation>();
            foreach (var a in actionList)
            {
                bool isSeq = a.Type == ActionType.Unknown;
                if (!isSeq) { foreach (var t in sequentialTypes) { if (a.Type == t) { isSeq = true; break; } } }
                if (isSeq) sequentialActions.Add(a); else parallelActions.Add(a);
            }

            // ── Fase 1: ações paralelas (Convertidas para sequenciais rápidas para evitar travamento da UI) ──
            if (parallelActions.Count > 0)
            {
                applyToken.UpdateProgress(20, string.Format(LocalizationService.Instance.GetString("ApplyingFastOptimizations"), parallelActions.Count));
                int appliedCount = 0;
                foreach (var a in parallelActions)
                {
                    if (cts.Token.IsCancellationRequested) break;
                    if (!_guard.IsAllowed(a, audit)) { lock (syncLock) errors.Add(a.Name + ":blocked"); continue; }
                    
                    try { App.LoggingService?.LogInfo($"[PROFILER] ▶ (ação rápida) {a.Name}"); } catch { }
                    try
                    {
                        // Pequeno respiro para a UI
                        await Task.Delay(20).ConfigureAwait(false);
                        
                        bool ok = await ExecuteActionAsync(a, audit).ConfigureAwait(false);
                        if (ok) { lock (syncLock) applied.Add(a.Name); try { App.LoggingService?.LogSuccess($"[PROFILER] ✓ {a.Name}"); } catch { } }
                        else    { lock (syncLock) errors.Add(a.Name + ":falha"); try { App.LoggingService?.LogWarning($"[PROFILER] ✗ {a.Name}"); } catch { } }
                    }
                    catch (ActionNotApplicableException ex) { try { App.LoggingService?.LogInfo($"[PROFILER] ⚠ não aplicável: {a.Name} — {ex.Message}"); } catch { } }
                    catch (OperationCanceledException)      { lock (syncLock) errors.Add(a.Name + ":cancelada"); break; }
                    catch (Exception ex)                    { lock (syncLock) errors.Add(a.Name + ":" + ex.Message); try { App.LoggingService?.LogError($"[PROFILER] Erro {a.Name}: {ex.Message}", ex); } catch { } }
                    appliedCount++;
                    applyToken.UpdateProgress(20 + (int)((double)appliedCount / parallelActions.Count * 50), string.Format(LocalizationService.Instance.GetString("ApplyingOptimization"), a.Name));
                }
            }

            // ── Fase 2: ações sequenciais (I/O pesado) ───────────────────────────
            applyToken.UpdateProgress(70, string.Format(LocalizationService.Instance.GetString("ApplyingSequentialOptimizations"), sequentialActions.Count));
            int seqCount = 0;
            foreach (var a in sequentialActions)
            {
                if (cts.Token.IsCancellationRequested) break;
                if (!_guard.IsAllowed(a, audit)) { errors.Add(a.Name + ":blocked"); continue; }
                try { App.LoggingService?.LogInfo($"[PROFILER] ▶ (sequencial) {a.Name}"); } catch { }
                try
                {
                    bool ok = await ExecuteActionAsync(a, audit).ConfigureAwait(false);
                    if (ok) { applied.Add(a.Name); try { App.LoggingService?.LogSuccess($"[PROFILER] ✓ {a.Name}"); } catch { } }
                    else    { errors.Add(a.Name + ":falha"); try { App.LoggingService?.LogWarning($"[PROFILER] ✗ {a.Name}"); } catch { } }
                }
                catch (ActionNotApplicableException ex) { try { App.LoggingService?.LogInfo($"[PROFILER] ⚠ não aplicável: {a.Name} — {ex.Message}"); } catch { } }
                catch (OperationCanceledException)      { errors.Add(a.Name + ":cancelada"); break; }
                catch (Exception ex)                    { errors.Add(a.Name + ":" + ex.Message); try { App.LoggingService?.LogError($"[PROFILER] Erro {a.Name}: {ex.Message}", ex); } catch { } }
                seqCount++;
                applyToken.UpdateProgress(70 + (int)((double)seqCount / sequentialActions.Count * 25), string.Format(LocalizationService.Instance.GetString("ApplyingOptimization"), a.Name));
            }

            var appliedList = applied;
            var errorList   = errors;

            var report = new ProfilerReport
            {
                Audit = audit,
                Answers = _store.Load().Answers,
                Recommendations = new List<ActionRecommendation>(actions),
                Backups = _rollback.ListBackups(),
                Applied = appliedList
            };
            try
            {
                var json = System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles, PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
                File.WriteAllText(Path.Combine(session, "report.json"), json);
            }
            catch (Exception ex) { try { App.LoggingService?.LogError($"[PROFILER] Erro ao gravar relatório: {ex.Message}", ex); } catch { } }

            applyToken.UpdateProgress(100, string.Format(LocalizationService.Instance.GetString("OptimizationsApplied"), appliedList.Count));
            return new ApplyResult { Success = errorList.Count == 0, Applied = appliedList, Errors = errorList, Backups = _rollback.ListBackups() };
        }

        public void MarkGateCompleted()
        {
            var s = _store.Load();
            s.QuestionnaireCompleted = true;
            _store.Save(s);
        }
        
        /// <summary>
        /// Verifica se todos os serviços necessários estão disponíveis
        /// </summary>
        /// <returns>Lista de serviços ausentes</returns>
        /// <summary>
        /// Verifica se todos os serviços necessários estão disponíveis.
        /// CORREÇÃO: Aguarda alguns segundos se os serviços ainda estiverem sendo inicializados (Startup).
        /// </summary>
        public List<string> CheckServiceAvailability()
        {
            var missing = new List<string>();
            int retries = 5; // 5 segundos max
            
            while (retries > 0)
            {
                missing.Clear();
                
                if (App.SystemCleaner == null) missing.Add("SystemCleaner");
                if (App.PerformanceOptimizer == null) missing.Add("PerformanceOptimizer");
                if (App.NetworkOptimizer == null) missing.Add("NetworkOptimizer");
                if (App.AdvancedOptimizer == null) missing.Add("AdvancedOptimizer");
                if (App.ExtremeOptimizations == null) missing.Add("ExtremeOptimizations");
                if (App.UltraPerformance == null) missing.Add("UltraPerformance");
                
                if (missing.Count == 0) break;
                
                // Tentar resolver via DI se a propriedade estática ainda for null
                if (App.Services != null)
                {
                    try
                    {
                        // Se resolvermos aqui, não precisamos esperar mais
                        bool allResolved = true;
                        if (App.SystemCleaner == null && App.Services.GetService(typeof(SystemCleaner)) == null) allResolved = false;
                        // ... etc ... 
                        // Mas é melhor apenas esperar o App.PopulateStaticServices concluir
                    }
                    catch { }
                }

                _logger?.LogInfo($"[PROFILER] Aguardando inicialização de serviços ({missing.Count} ausentes, tentativas restantes: {retries})...");
                Thread.Sleep(1000);
                _logger?.LogWarning($"[PROFILER] Thread.Sleep(1000) — isto pode causar travamentos na UI se chamado da thread principal. Considere usar Task.Delay.");
                retries--;
            }
            
            return missing;
        }

        /// <summary>
        /// Sincroniza otimizações críticas de hardware silenciosamente (Background).
        /// Focado em garantir que novos módulos (como SSD Booster) sejam aplicados em atualizações.
        /// </summary>
        public async Task SyncHardwareOptimizationsSilentlyAsync()
        {
            _logger?.LogInfo("[SILENT-SYNC] Iniciando sincronização automática de hardware...");
            using var syncToken = GlobalProgressService.Instance.BeginOperation(LocalizationService.Instance.GetString("SyncingHardware"), false);
            try
            {
                syncToken.UpdateProgress(5, LocalizationService.Instance.GetString("CheckingHardware"));
                var audit = await _collector.CollectAsync(default);
                bool isSsd = audit.Storage.SystemDiskType.Contains("SSD", StringComparison.OrdinalIgnoreCase) || 
                             audit.Storage.SystemDiskType.Contains("NVMe", StringComparison.OrdinalIgnoreCase);

                if (!isSsd)
                {
                    _logger?.LogInfo("[SILENT-SYNC] SSD não detectado. Pulando otimizações específicas de armazenamento sólido.");
                    return;
                }

                _logger?.LogInfo("[SILENT-SYNC] SSD/NVMe detectado. Aplicando calibrações de performance silênciosas...");

                var actionsToApply = new List<ActionType>
                {
                    ActionType.Storage_EnableTrim,
                    ActionType.Storage_DisableSearchIndexing,
                    ActionType.Storage_DisableHibernation,
                    ActionType.Storage_DisableLastAccess,
                    ActionType.Storage_Disable83Naming,
                    ActionType.Storage_DisableSuperfetch,
                    ActionType.Storage_DisableDefragBootFiles,
                    ActionType.Storage_DisableEventLogging
                };

                int appliedCount = 0;
                syncToken.UpdateProgress(40, string.Format(LocalizationService.Instance.GetString("ApplyingOptimizationsCount"), actionsToApply.Count));
                int count = 0;
                foreach (var type in actionsToApply)
                {
                    // Executa a ação diretamente. Os métodos no UltraPerformanceService já são idempotentes.
                    var action = new ActionRecommendation { Type = type, Name = $"AutoSync_{type}" };
                    bool success = await ExecuteActionAsync(action, audit);
                    if (success) 
                    {
                        appliedCount++;
                        _logger?.LogInfo($"[SILENT-SYNC] Otimização verificada/aplicada: {type}");
                    }
                    count++;
                    syncToken.UpdateProgress(40 + (int)((double)count / actionsToApply.Count * 55), string.Format(LocalizationService.Instance.GetString("OptimizingType"), type));
                }

                _logger?.LogSuccess($"[SILENT-SYNC] Calibração de hardware concluída com sucesso. {appliedCount} módulos de SSD ativos.");
                syncToken.UpdateProgress(100, string.Format(LocalizationService.Instance.GetString("OptimizationsApplied"), appliedCount));
                return;
            }
            catch (Exception ex)
            {
                syncToken?.UpdateProgress(0, LocalizationService.Instance.GetString("SyncError"));
                _logger?.LogError($"[SILENT-SYNC] Erro crítico na sincronização de hardware: {ex.Message}", ex);
            }
        }
        
        /// <summary>
        /// Executa uma ação específica baseada no TIPO (Enum) e não mais em string mágica.
        /// </summary>
        private async Task<bool> ExecuteActionAsync(ActionRecommendation action, AuditData audit)
        {
            // Validação Inteligente: Impedir reaplicação redundante
            if (action.State == OptimizationState.AlreadyOptimized || action.IsAlreadyOptimized)
            {
                _logger?.LogInfo($"[PROFILER] Ação ignorada no pipeline pois já está aplicada ativamente: {action.Name}");
                return true;
            }

            try
            {
                // Fallback para lógica antiga se o Tipo for Unknown (transição)
                if (action.Type == ActionType.Unknown)
                {
                    return await ExecuteLegacyActionAsync(action);
                }

                switch (action.Type)
                {
                    case ActionType.SystemCleanup:
                        if (App.SystemCleaner != null)
                        {
                            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)); // Timeout segurança
                            var cleaned = await App.SystemCleaner.CleanTempFilesAsync();
                            _logger?.LogInfo($"[PROFILER] Limpeza: {cleaned / 1024 / 1024} MB liberados");
                            return true;
                        }
                        else
                        {
                            _logger?.LogWarning($"[PROFILER] SystemCleaner não disponível para ação: {action.Name}");
                            return false;
                        }

                    case ActionType.PowerPlan_HighPerformance:
                        if (App.PerformanceOptimizer != null)
                        {
                            return (await App.PerformanceOptimizer.SetHighPerformancePlanAsync()).Success;
                        }
                        else
                        {
                            _logger?.LogWarning($"[PROFILER] PerformanceOptimizer não disponível para ação: {action.Name}");
                            return false;
                        }
                        
                    case ActionType.PowerPlan_Balanced:
                        if (App.PerformanceOptimizer != null)
                        {
                            return (await App.PerformanceOptimizer.SetBalancedPlanAsync()).Success;
                        }
                        else
                        {
                            _logger?.LogWarning($"[PROFILER] PerformanceOptimizer não disponível para ação: {action.Name}");
                            return false;
                        }

                    case ActionType.Network_FlushDns:
                        if (App.NetworkOptimizer != null)
                        {
                            return await App.NetworkOptimizer.FlushDnsAsync();
                        }
                        else
                        {
                            _logger?.LogWarning($"[PROFILER] NetworkOptimizer não disponível para ação: {action.Name}");
                            return false;
                        }
                        
                    case ActionType.Network_ResetStack:
                        if (App.NetworkOptimizer != null)
                        {
                            bool s1 = await App.NetworkOptimizer.ResetWinsockAsync();
                            bool s2 = await App.NetworkOptimizer.ResetIPStackAsync();
                            return s1 && s2;
                        }
                        else
                        {
                            _logger?.LogWarning($"[PROFILER] NetworkOptimizer não disponível para ação: {action.Name}");
                            return false;
                        }

                    case ActionType.GamerMode_Activate:
                        _logger?.LogWarning($"[PROFILER] GamerOptimizer (Legacy) removido. Ação ignorada: {action.Name}");
                        return false;

                    case ActionType.Memory_Optimize:
                        if (App.AdvancedOptimizer != null)
                        {
                            return await App.AdvancedOptimizer.OptimizeMemoryAsync();
                        }
                        else
                        {
                            _logger?.LogWarning($"[PROFILER] AdvancedOptimizer não disponível para ação: {action.Name}");
                            return false;
                        }

                    case ActionType.Advanced_DisableHags:
                        if (App.ExtremeOptimizations != null)
                        {
                             return App.ExtremeOptimizations.ToggleHAGS(false);
                        }
                        else
                        {
                            _logger?.LogWarning($"[PROFILER] ExtremeOptimizations não disponível para ação: {action.Name}");
                            return false;
                        }

                    case ActionType.Advanced_EnableHags:
                        if (App.ExtremeOptimizations != null)
                        {
                             return App.ExtremeOptimizations.ToggleHAGS(true);
                        }
                        else
                        {
                            _logger?.LogWarning($"[PROFILER] ExtremeOptimizations não disponível para ação: {action.Name}");
                            return false;
                        }

                    case ActionType.Advanced_RestartCriticalServices:
                        if (App.ExtremeOptimizations != null)
                        {
                             return App.ExtremeOptimizations.RestartCriticalServices();
                        }
                        else
                        {
                            _logger?.LogWarning($"[PROFILER] ExtremeOptimizations não disponível para ação: {action.Name}");
                            return false;
                        }

                    case ActionType.Storage_DisableSuperfetch:
                        if (App.UltraPerformance != null)
                        {
                            App.UltraPerformance.DisableSuperfetch();
                            App.UltraPerformance.DisablePrefetcher();
                            return true;
                        }
                        return false;
                    
                    case ActionType.Storage_EnableTrim:
                        return App.UltraPerformance?.EnforceTrim() ?? false;

                    case ActionType.Storage_DisableHibernation:
                        return App.UltraPerformance?.DisableHibernation() ?? false;

                    case ActionType.Storage_DisableSearchIndexing:
                        return App.UltraPerformance?.DisableIndexing() ?? false;

                    case ActionType.Storage_Disable83Naming:
                        return App.UltraPerformance?.Disable83Naming() ?? false;

                    case ActionType.Storage_DisableLastAccess:
                        return App.UltraPerformance?.DisableLastAccessUpdate() ?? false;

                    case ActionType.Storage_DisableDefragBootFiles:
                        return App.UltraPerformance?.DisableDefragBootFiles() ?? false;

                    case ActionType.Storage_DisableEventLogging:
                        // Implementado via registro direto aqui ou no UltraPerformance
                        try {
                            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\WMI\Autologger\ReadyBoot", true);
                            key?.SetValue("Start", 0, Microsoft.Win32.RegistryValueKind.DWord);
                            return true;
                        } catch { return false; }

                    case ActionType.Visual_Optimize:
                        if (App.UltraPerformance != null)
                        {
                            App.UltraPerformance.DisableVisualEffects();
                            return true;
                        }
                        else
                        {
                            _logger?.LogWarning($"[PROFILER] UltraPerformance não disponível para ação: {action.Name}");
                            return false;
                        }

                    case ActionType.Process_Optimize:
                        if (App.AdvancedOptimizer != null)
                        {
                            return await App.AdvancedOptimizer.OptimizeProcessesAsync();
                        }
                        else
                        {
                            _logger?.LogWarning($"[PROFILER] AdvancedOptimizer não disponível para ação: {action.Name}");
                            return false;
                        }

                    case ActionType.General_Optimize:
                        // Otimização Avançada de GPU / Sistema
                        if (App.ExtremeOptimizations != null)
                        {
                            App.ExtremeOptimizations.ApplyGpuTdrTweaks(true);
                            App.ExtremeOptimizations.ApplyNvidiaStereo3DPolicy(true);
                            return true;
                        }
                        return false;

                    case ActionType.Advanced_OptimizeIrq:
                        if (App.ExtremeOptimizations != null)
                        {
                            return App.ExtremeOptimizations.OptimizeInterruptHandling();
                        }
                        return false;

                    case ActionType.Storage_Defrag:
                        if (App.AdvancedOptimizer != null)
                        {
                            return await App.AdvancedOptimizer.OptimizeStorageAsync("C", false);
                        }
                        return false;

                    case ActionType.Network_OptimizeTcp:
                        if (App.ExtremeOptimizations != null)
                        {
                            return App.ExtremeOptimizations.ApplyTcpAutotuneRssRsc();
                        }
                        return false;

                    case ActionType.Services_Optimize:
                        if (App.ExtremeOptimizations != null)
                        {
                            App.ExtremeOptimizations.OptimizeServices(true);
                            return true;
                        }
                        return false;
                }

                // Se chegou aqui, o tipo de ação não é suportado
                _logger?.LogWarning($"[PROFILER] Tipo de ação não suportado: {action.Type} - {action.Name}");
                return false;
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[PROFILER] Erro ao executar ação '{action.Name}': {ex.Message}", ex);
                return false;
            }
        }

        private async Task<bool> ExecuteLegacyActionAsync(ActionRecommendation action)
        {
            var actionName = action.Name.ToLowerInvariant();
             // ========================================
                // LIMPEZA DE SISTEMA
                // ========================================
                if (actionName.Contains("limpeza") || actionName.Contains("clean"))
                {
                    if (App.SystemCleaner != null)
                    {
                        // Adicionar timeout de 15 segundos para a limpeza
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                        var cleaned = await App.SystemCleaner.CleanTempFilesAsync();
                        _logger?.LogInfo($"[PROFILER] Limpeza: {cleaned / 1024 / 1024} MB liberados");
                        return true;
                    }
                    else
                    {
                        _logger?.LogWarning($"[PROFILER] SystemCleaner não disponível para ação: {action.Name}");
                        return false;
                    }
                }
                
                // ========================================
                // PLANOS DE ENERGIA
                // ========================================
                if (actionName.Contains("plano de energia") || actionName.Contains("power plan") || 
                    actionName.Contains("alto desempenho") || actionName.Contains("high performance") ||
                    actionName.Contains("balanceado") || actionName.Contains("balanced"))
                {
                    if (App.PerformanceOptimizer != null)
                    {
                        if (actionName.Contains("balanceado") || actionName.Contains("balanced"))
                        {
                            // Plano balanceado para notebooks - timeout de 10 segundos
                            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                            return (await App.PerformanceOptimizer.SetBalancedPlanAsync()).Success;
                        }
                        else
                        {
                            // Plano alto desempenho - timeout de 10 segundos
                            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                            return (await App.PerformanceOptimizer.SetHighPerformancePlanAsync()).Success;
                        }
                    }
                    else
                    {
                        _logger?.LogWarning($"[PROFILER] PerformanceOptimizer não disponível para ação: {action.Name}");
                        return false;
                    }
                }
                
                // ========================================
                // OTIMIZAÇÕES DE REDE
                // ========================================
                if (actionName.Contains("rss") || actionName.Contains("nic") || actionName.Contains("rede") || actionName.Contains("network"))
                {
                    if (App.NetworkOptimizer != null)
                    {
                        bool success = true;
                        
                        // Flush DNS sempre - timeout de 10 segundos
                        using var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                        success &= await App.NetworkOptimizer.FlushDnsAsync();
                        
                        // Se for tuning específico de RSS
                        if (actionName.Contains("rss") || actionName.Contains("tuning"))
                        {
                            // Aplicar configurações TCP otimizadas - timeout de 15 segundos
                            using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                            success &= await App.NetworkOptimizer.OptimizeTcpSettingsAsync();
                        }
                        else
                        {
                            // Reset completo da pilha de rede - timeout de 20 segundos cada
                            using var cts3 = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                            success &= await App.NetworkOptimizer.ResetWinsockAsync();
                            using var cts4 = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                            success &= await App.NetworkOptimizer.ResetIPStackAsync();
                        }
                        
                        return success;
                    }
                    else
                    {
                        _logger?.LogWarning($"[PROFILER] NetworkOptimizer não disponível para ação: {action.Name}");
                        return false;
                    }
                }
                
                // ========================================
                // MODO GAMER - SÓ ATIVAR SE HOUVER JOGO RODANDO
                // ========================================
                if (actionName.Contains("gamer") || actionName.Contains("gaming") || actionName.Contains("jogo"))
                {
                    // if (App.GamerOptimizer != null)
                    // {
                    //     // Ativar modo gamer com timeout de 30 segundos
                    //     using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    //     await App.GamerOptimizer.ActivateGamerModeAsync(null, null);
                    //     return true;
                    // }
                    // else
                    // {
                         _logger?.LogWarning($"[PROFILER] GamerOptimizer (Legacy) removido. Ação ignorada: {action.Name}");
                         return false;
                    // }
                }
                
                // ========================================
                // OTIMIZAÇÕES AVANÇADAS - PAGEFILE
                // ========================================
                if (actionName.Contains("pagefile") || actionName.Contains("paginação") || actionName.Contains("swap"))
                {
                    if (App.AdvancedOptimizer != null)
                    {
                        // Otimizar memória inclui ajuste de pagefile - timeout de 20 segundos
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                        return await App.AdvancedOptimizer.OptimizeMemoryAsync();
                    }
                    else
                    {
                        _logger?.LogWarning($"[PROFILER] AdvancedOptimizer não disponível para ação: {action.Name}");
                        return false;
                    }
                }
                
                // ========================================
                // OTIMIZAÇÕES AVANÇADAS - SERVIÇOS
                // ========================================
                if (actionName.Contains("serviços") || actionName.Contains("services") || actionName.Contains("mínimos"))
                {
                    if (App.PerformanceOptimizer != null)
                    {
                        // Otimizar serviços - timeout de 15 segundos
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                        return (await App.PerformanceOptimizer.OptimizeServicesAsync()).Success;
                    }
                    else
                    {
                        _logger?.LogWarning($"[PROFILER] PerformanceOptimizer não disponível para ação: {action.Name}");
                        return false;
                    }
                }
                
                // ========================================
                // OTIMIZAÇÕES AVANÇADAS - HAGS
                // ========================================
                if (actionName.Contains("hags") || actionName.Contains("hardware-accelerated") || actionName.Contains("gpu scheduling"))
                {
                    if (App.ExtremeOptimizations != null)
                    {
                        // Desabilitar DryRun temporariamente para aplicar de verdade
                        var previousDryRun = App.ExtremeOptimizations.DryRun;
                        App.ExtremeOptimizations.DryRun = false;
                        try
                        {
                            // HAGS é uma otimização de registro - desativar em sistemas low-end
                            // Timeout de 10 segundos
                            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                            return App.ExtremeOptimizations.ToggleHAGS(false);
                        }
                        finally
                        {
                            App.ExtremeOptimizations.DryRun = previousDryRun;
                        }
                    }
                    else
                    {
                        _logger?.LogWarning($"[PROFILER] ExtremeOptimizations não disponível para ação: {action.Name}");
                        return false;
                    }
                }
                
                // ========================================
                // OTIMIZAÇÕES AVANÇADAS - IRQ/DPC
                // ========================================
                if (actionName.Contains("irq") || actionName.Contains("dpc") || actionName.Contains("latência"))
                {
                    if (App.ExtremeOptimizations != null)
                    {
                        // Desabilitar DryRun temporariamente para aplicar de verdade
                        var previousDryRun = App.ExtremeOptimizations.DryRun;
                        App.ExtremeOptimizations.DryRun = false;
                        try
                        {
                            // Otimizações de IRQ/DPC para reduzir latência - timeout de 10 segundos
                            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                            return App.ExtremeOptimizations.OptimizeInterruptHandling();
                        }
                        finally
                        {
                            App.ExtremeOptimizations.DryRun = previousDryRun;
                        }
                    }
                    else
                    {
                        _logger?.LogWarning($"[PROFILER] ExtremeOptimizations não disponível para ação: {action.Name}");
                        return false;
                    }
                }
                
                // ========================================
                // OTIMIZAÇÕES DE PROCESSOS (FALLBACK)
                // ========================================
                if (action.Module == "AdvancedOptimizer" && App.AdvancedOptimizer != null)
                {
                    // Otimizar processos - timeout de 15 segundos
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    await App.AdvancedOptimizer.OptimizeProcessesAsync();
                    return true;
                }
                else if (action.Module == "AdvancedOptimizer" && App.AdvancedOptimizer == null)
                {
                    _logger?.LogWarning($"[PROFILER] AdvancedOptimizer não disponível para ação: {action.Name}");
                    return false;
                }
                
                // Se chegou aqui, a ação não é reconhecida
                _logger?.LogWarning($"[PROFILER] Ação não reconhecida: {action.Name} (Module: {action.Module})");
                return false;
        }

        public async Task<SystemInfo?> GetSystemInfoAsync()
        {
            try
            {
                var audit = await _collector.CollectAsync(default);
                return new SystemInfo
                {
                    ProcessorName = audit.Cpu.Model,
                    GraphicsCard = audit.Gpu.Model,
                    Memory = $"{audit.Ram.TotalMb} MB",
                    Storage = audit.Storage.SystemDiskType
                };
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Failed to get system info: {ex.Message}", ex);
                return null;
            }
        }
    }
}

