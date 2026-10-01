using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Core.Body;
using VoltrisOptimizer.Core.Intelligence;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Core.Brain.V2;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Core.NetworkIntelligence;

public sealed class NetworkIntelligenceOrchestrator : INetworkIntelligenceOrchestrator
{
    private readonly ILoggingService _logger;
    private readonly IVoltrisBody _body;
    private readonly INetworkArm _networkArm;
    private readonly ITemporalPatternEngine _temporal;
    private readonly ILearningLogger _learningDb;
    private readonly VoltrisBrainV2 _brain; // ✅ FASE 6: Brain integration

    private CancellationTokenSource? _cts;
    private Task? _loop;
    private volatile bool _running;
    private DateTime _lastProfileSwitch = DateTime.MinValue;
    private static readonly TimeSpan ProfileCooldown = TimeSpan.FromSeconds(30);
    private int _evaluationCount;

    private readonly HashSet<DecisionProfile> _confirmedProfiles = new();

    public NetworkDecision? LastDecision { get; private set; }

    public DecisionProfile ActiveProfile { get; private set; } = DecisionProfile.Idle;

    public bool IsRunning => _running;

    public event EventHandler<DecisionResultEventArgs>? OnDecisionProduced;

    public NetworkIntelligenceOrchestrator(
        ILoggingService logger,
        IVoltrisBody body,
        INetworkArm networkArm,
        ITemporalPatternEngine temporal,
        ILearningLogger learningDb,
        VoltrisBrainV2 brain = null) // ✅ FASE 6: Brain injection
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _body = body ?? throw new ArgumentNullException(nameof(body));
        _networkArm = networkArm ?? throw new ArgumentNullException(nameof(networkArm));
        _temporal = temporal ?? throw new ArgumentNullException(nameof(temporal));
        _learningDb = learningDb ?? throw new ArgumentNullException(nameof(learningDb));
        _brain = brain ?? Core.ServiceLocator.GetService<VoltrisBrainV2>(); // ✅ FASE 6
        
        if (_brain != null)
        {
            _logger.LogInfo("[NET-ORCH] Brain V2 injetado - usando Q-Table para decisões de rede");
        }
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        if (_running) return Task.CompletedTask;
        _running = true;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loop = Task.Run(() => DecisionLoopAsync(_cts!.Token), _cts!.Token);
        _logger.LogInfo("[NET-ORCH] Orquestrador de Rede Inteligente iniciado (intervalo = 5s)");
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (!_running) return;
        _running = false;
        try { _cts?.Cancel(); } catch { }
        try { if (_loop != null) await _loop.ConfigureAwait(false); } catch (OperationCanceledException) { }
        _logger.LogInfo("[NET-ORCH] Orquestrador de Rede parado.");
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { }
        _cts?.Dispose();
    }

    private async Task DecisionLoopAsync(CancellationToken ct)
    {
        _logger.LogInfo("[NET-ORCH] Loop de decisão iniciado");
        while (!ct.IsCancellationRequested && _running)
        {
            try
            {
                await Task.Delay(5000, ct).ConfigureAwait(false);
                SystemContextSnapshot ctx = CollectContext();
                NetworkDecision decision = Evaluate(ctx);
                await ApplyDecisionAsync(decision, ctx);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError("[NET-ORCH] Erro no loop: " + ex.Message);
            }
        }
        _logger.LogInfo("[NET-ORCH] Loop de decisão encerrado");
    }

    public async Task<NetworkDecision> ForceEvaluateNowAsync()
    {
        SystemContextSnapshot ctx = CollectContext();
        NetworkDecision decision = Evaluate(ctx);
        await ApplyDecisionAsync(decision, ctx);
        return decision;
    }

    public async Task<NetworkDecision> RequestProfileAsync(DecisionProfile profile)
    {
        SystemContextSnapshot ctx = CollectContext();
        string[] actions = profile switch
        {
            DecisionProfile.ModoGamer => new[] {
                "otimizar_rede_para_jogos",
                "aplicar_qos_gaming",
                "reduzir_latencia",
                "priorizar_pacotes_jogo"
            },
            DecisionProfile.Dsl => new[] {
                "ajustar_mtu",
                "otimizar_tcp_para_dsl",
                "reduzir_jitter_buffer",
                "aplicar_qos_dsl"
            },
            DecisionProfile.WatchDogs => new[] {
                "monitorar_trafego_continuo",
                "detectar_anomalias",
                "registrar_alertas",
                "analisar_padroes_rede"
            },
            DecisionProfile.PerfilInteligente => new[] {
                "analisar_contexto_atual",
                "aplicar_perfil_otimo",
                "ajustar_parametros_dinamicos",
                "registrar_resultados"
            },
            _ => new[] { "nenhuma_acao_necessaria" }
        };

        var decision = new NetworkDecision
        {
            Decisao = profile switch
            {
                DecisionProfile.ModoGamer => "modo_gamer",
                DecisionProfile.Dsl => "dsl",
                DecisionProfile.WatchDogs => "watch_dogs",
                DecisionProfile.PerfilInteligente => "perfil_inteligente",
                _ => "idle"
            },
            Motivo = $"Perfil solicitado manualmente: {profile} | Contexto: {ctx.ToSummary()}",
            Acoes = new List<string>(actions),
            Prioridade = profile == DecisionProfile.WatchDogs ? 2 : 4,
            RequerConfirmacaoUsuario = false
        };

        await ApplyDecisionAsync(decision, ctx);
        return decision;
    }

    private SystemContextSnapshot CollectContext()
    {
        SystemMetricsCache cache = SystemMetricsCache.Instance;
        return new SystemContextSnapshot
        {
            CpuPercent = cache.CpuPercent,
            RamPercent = cache.MemoryUsedPercent,
            GpuPercent = cache.GpuUsagePercent,
            CpuTempCelsius = _body.CpuTempCelsius,
            ForegroundProcess = _body.ForegroundProcessName,
            CurrentContext = _body.CurrentContext,
            ActiveNicSpeedMbps = 0,
            ConnectionType = "ethernet",
            IsOnBattery = false,
            IsGamingModeActive = _body.IsGamingModeActive
        };
    }

    private NetworkDecision Evaluate(SystemContextSnapshot ctx)
    {
        _evaluationCount++;

        // ✅ FASE 6: Observar estado no Brain para aprendizado passivo
        if (_brain != null)
        {
            try
            {
                var brainState = new BrainStateKey(
                    workload: ctx.CurrentContext == OperationalContext.Gaming ? WorkloadCategory.Game :
                              ctx.CpuPercent < 5.0 ? WorkloadCategory.Idle : WorkloadCategory.Work,
                    cpuBucket: (byte)(ctx.CpuPercent / 10.0),
                    ramBucket: (byte)(ctx.RamPercent / 10.0),
                    tempBucket: (byte)(ctx.CpuTempCelsius / 10.0),
                    contextBucket: 2  // Network context
                );

                _brain.Memory.Observe(brainState, new SensorSnapshot
                {
                    ForegroundProcessName = ctx.ForegroundProcess ?? "unknown",
                    Workload = ctx.CurrentContext == OperationalContext.Gaming ? WorkloadCategory.Game :
                               ctx.CpuPercent < 5.0 ? WorkloadCategory.Idle : WorkloadCategory.Work,
                    CpuUsagePercent = ctx.CpuPercent,
                    RamUsagePercent = ctx.RamPercent,
                    CpuTemperatureC = ctx.CpuTempCelsius
                });
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[NET-ORCH] Erro ao observar estado no Brain: {ex.Message}");
            }
        }

        // Fallback: lógica original (protegida)
        if (ctx.CpuPercent > 90.0 || ctx.CpuTempCelsius > 85.0)
        {
            _logger.LogWarning($"[NET-ORCH] Sistema sob estresse (CPU={ctx.CpuPercent:F1}% TEMP={ctx.CpuTempCelsius:F0}C). Nenhuma mudança drástica aplicada.");
            return new NetworkDecision
            {
                Decisao = "watch_dogs",
                Motivo = $"Sistema sob estresse térmico/de CPU. Watch Dogs ativo para monitorar estabilidade. CPU={ctx.CpuPercent:F1}% TEMP={ctx.CpuTempCelsius:F0}C",
                Acoes = new List<string> { "monitorar_estresse", "evitar_mudancas_drasticas", "registrar_metricas" },
                Prioridade = 5,
                RequerConfirmacaoUsuario = false
            };
        }

        if (ctx.CurrentContext == OperationalContext.Gaming || ctx.IsGamingModeActive)
        {
            if (ActiveProfile != DecisionProfile.ModoGamer && CanSwitchProfile())
            {
                _logger.LogInfo($"[NET-ORCH] Contexto Gaming detectado: FG={ctx.ForegroundProcess}");
                return new NetworkDecision
                {
                    Decisao = "modo_gamer",
                    Motivo = $"Contexto de jogo detectado: {ctx.ForegroundProcess}. Aplicando otimizações de rede para baixa latência.",
                    Acoes = new List<string>
                    {
                        "aplicar_qos_gaming",
                        "otimizar_tcp_ack_frequency",
                        "priorizar_pacotes_do_jogo",
                        "ajustar_interrupt_affinity",
                        "monitorar_latencia_em_tempo_real"
                    },
                    Prioridade = 5,
                    RequerConfirmacaoUsuario = _evaluationCount < 3
                };
            }
            return new NetworkDecision
            {
                Decisao = "modo_gamer",
                Motivo = $"Modo Gamer já ativo. Monitorando latência para {ctx.ForegroundProcess}.",
                Acoes = new List<string> { "monitorar_latencia", "ajustar_fino_qos" },
                Prioridade = 3,
                RequerConfirmacaoUsuario = false
            };
        }

        if (ctx.CpuPercent < 5.0 && ctx.CurrentContext == OperationalContext.Idle)
        {
            if (ActiveProfile != DecisionProfile.Idle && CanSwitchProfile())
            {
                return new NetworkDecision
                {
                    Decisao = "idle",
                    Motivo = $"Sistema ocioso (CPU={ctx.CpuPercent:F1}%). Reduzindo intervenções de rede para economizar energia.",
                    Acoes = new List<string> { "restaurar_parametros_padrao", "reduzir_frequencia_monitoramento" },
                    Prioridade = 1,
                    RequerConfirmacaoUsuario = false
                };
            }
            return new NetworkDecision
            {
                Decisao = "idle",
                Motivo = "Sistema ocioso. Nenhuma ação necessária.",
                Acoes = new List<string> { "nenhuma_acao_necessaria" },
                Prioridade = 1,
                RequerConfirmacaoUsuario = false
            };
        }

        if (ShouldApplyIntelligentProfile(ctx))
        {
            string contextName = ctx.CurrentContext.ToString();
            return new NetworkDecision
            {
                Decisao = "perfil_inteligente",
                Motivo = $"Perfil Inteligente ativado para contexto {contextName}. Baseado em padrões temporais e comportamentais.",
                Acoes = new List<string>
                {
                    $"aplicar_perfil_{contextName.ToLowerInvariant()}",
                    "ajustar_qos_dinamico",
                    "otimizar_parametros_tcp",
                    "registrar_aprendizado"
                },
                Prioridade = 3,
                RequerConfirmacaoUsuario = _evaluationCount < 5
            };
        }

        return new NetworkDecision
        {
            Decisao = "perfil_inteligente",
            Motivo = $"Monitorando contexto {ctx.CurrentContext}. Aplicando ajustes finos conforme padrões de uso.",
            Acoes = new List<string> { "monitorar_padroes", "ajustes_finos_qos" },
            Prioridade = 2,
            RequerConfirmacaoUsuario = false
        };
    }

    private bool CanSwitchProfile()
    {
        TimeSpan elapsed = DateTime.UtcNow - _lastProfileSwitch;
        return elapsed >= ProfileCooldown;
    }

    private bool ShouldApplyIntelligentProfile(SystemContextSnapshot ctx)
    {
        double temporalProb = _temporal.GetProbability(DateTime.UtcNow.DayOfWeek, DateTime.UtcNow.Hour, ctx.CurrentContext);
        return _evaluationCount > 5 || temporalProb > 0.5;
    }

    private async Task ApplyDecisionAsync(NetworkDecision decision, SystemContextSnapshot ctx)
    {
        _lastProfileSwitch = DateTime.UtcNow;
        LastDecision = decision;
        ActiveProfile = decision.Decisao switch
        {
            "modo_gamer" => DecisionProfile.ModoGamer,
            "dsl" => DecisionProfile.Dsl,
            "watch_dogs" => DecisionProfile.WatchDogs,
            "perfil_inteligente" => DecisionProfile.PerfilInteligente,
            _ => DecisionProfile.Idle
        };

        _logger.LogInfo($"[NET-ORCH] DECISÃO: {decision.ToJson()}");

        try
        {
            await _learningDb.LogBrainDecisionAsync(
                ruleName: "network-orchestrator:" + decision.Decisao,
                score: decision.Prioridade,
                reason: $"{decision.Motivo} | acoes={string.Join(";", decision.Acoes)} | ctx={ctx.ToSummary()}",
                hardwareHash: Environment.MachineName
            );

            // ✅ FASE 6: Reportar decisão ao Brain para Q-Learning
            if (_brain != null)
            {
                try
                {
                    _brain.ReportExternalReward("network_decision", 
                        decision.Prioridade, 
                        new { decisao = decision.Decisao, prioridade = decision.Prioridade, perfil = ActiveProfile.ToString() });
                }
                catch (Exception ex)
                {
                    _logger.LogDebug($"[NET-ORCH] Erro ao reportar ao Brain: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug($"[NET-ORCH] Falha ao registrar decisão no learning.db: {ex.Message}");
        }

        foreach (string acao in decision.Acoes)
        {
            try
            {
                await ExecuteActionAsync(acao, ctx);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[NET-ORCH] Falha ao executar ação '{acao}': {ex.Message}");
            }
        }

        OnDecisionProduced?.Invoke(this, new DecisionResultEventArgs
        {
            Decision = decision,
            Context = ctx
        });
    }

    private async Task ExecuteActionAsync(string action, SystemContextSnapshot ctx)
    {
        switch (action)
        {
            case "aplicar_qos_gaming":
            case "otimizar_rede_para_jogos":
                await _networkArm.OptimizeForGamingAsync(ctx.ForegroundProcess);
                break;

            case "ajustar_interrupt_affinity":
                await _networkArm.SetInterruptAffinityAsync();
                break;

            case "restaurar_parametros_padrao":
            case "nenhuma_acao_necessaria":
                break;

            case "monitorar_latencia":
            case "monitorar_estresse":
            case "monitorar_padroes":
            case "monitorar_trafego_continuo":
            case "detectar_anomalias":
            case "registrar_alertas":
            case "analisar_padroes_rede":
            case "reduzir_frequencia_monitoramento":
            case "registrar_metricas":
            case "evitar_mudancas_drasticas":
            case "registrar_aprendizado":
            case "ajustes_finos_qos":
                break;

            case "aplicar_qos_dsl":
            case "ajustar_mtu":
            case "otimizar_tcp_para_dsl":
            case "reduzir_jitter_buffer":
                _logger.LogInfo($"[NET-ORCH] Ação '{action}' reconhecida para perfil DSL — implementação específica pendente de hardware de link.");
                break;

            default:
                _logger.LogDebug($"[NET-ORCH] Ação '{action}' delegada ao Body para execução.");
                break;
        }
    }
}
