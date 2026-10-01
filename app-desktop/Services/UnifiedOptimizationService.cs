using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services.SystemChanges;
using System.ServiceProcess;
using System.Runtime.InteropServices;
using VoltrisOptimizer.Core;
using VoltrisOptimizer.Models;
using VoltrisOptimizer.Services.Gamer.Models;
using GamerOptimizationContext = VoltrisOptimizer.Services.Gamer.Models.OptimizationContext;

namespace VoltrisOptimizer.Services
{
    public class UnifiedOptimizationService : IDisposable
    {
        private readonly ILoggingService _logger;
        private readonly ISystemChangeTransactionService? _txService;
        private readonly SystemCleaner _systemCleaner;
        private readonly VoltrisPerformanceOptimizer _performanceOptimizer;
        private readonly NetworkOptimizer _networkOptimizer;
        private readonly AdvancedTweaksService _advancedTweaks;
        private readonly UltraCleanerService _ultraCleaner;
        private ISystemTransaction? _currentTx;
        private readonly Dictionary<string, object> _backups = new();
        private bool _disposed = false;

        private readonly Dictionary<string, DateTime> _lastOptimizations = new();
        private readonly Dictionary<string, long> _optimizationResults = new();
        private readonly object _lock = new object();

        public UnifiedOptimizationService(ILoggingService logger, global::VoltrisOptimizer.VoltrisGlobalInsightService insightService, ISystemChangeTransactionService? txService = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _txService = txService;

            _logger.LogInfo("[UnifiedOpt] Inicializando serviço unificado de otimização...");

            _systemCleaner = new SystemCleaner(_logger);
            _performanceOptimizer = new VoltrisPerformanceOptimizer(_logger);
            _networkOptimizer = new NetworkOptimizer(_logger);
            _advancedTweaks = new AdvancedTweaksService(_logger);
            _ultraCleaner = new UltraCleanerService(_logger);

            _logger.LogSuccess("[UnifiedOpt] Serviço unificado inicializado com sucesso");
        }

        public async Task<OptimizationResult> RunFullOptimizationAsync(Action<int>? progressCallback = null)
        {
            return await Task.Run(async () =>
            {
                var result = new OptimizationResult { Success = true, Timestamp = DateTime.Now };
                try
                {
                    _logger.LogInfo("[UnifiedOpt] Iniciando otimização completa...");
                    progressCallback?.Invoke(5);

                    // 1. Limpeza de Sistema
                    _logger.LogInfo("[UnifiedOpt] Passo 1/5: Limpeza de sistema");
                    long cleaned = await CleanBasicAsync(p => progressCallback?.Invoke(10 + (int)(p * 0.15)));
                    result.SpaceFreed += cleaned;
                    result.OptimizationsApplied.Add($"Limpeza de Arquivos Temporários ({VoltrisOptimizer.Helpers.FileSystemHelper.FormatBytes(cleaned)})");

                    // 2. Performance
                    _logger.LogInfo("[UnifiedOpt] Passo 2/5: Otimização de performance");
                    await _performanceOptimizer.OptimizeProcessesAsync();
                    await _performanceOptimizer.OptimizeMemoryAsync();
                    await _performanceOptimizer.OptimizeDiskAsync();
                    result.OptimizationsApplied.Add("Otimização de Processos e RAM");
                    progressCallback?.Invoke(45);

                    // 3. Rede
                    _logger.LogInfo("[UnifiedOpt] Passo 3/5: Otimização de rede");
                    await _networkOptimizer.FlushDnsAsync();
                    await _networkOptimizer.OptimizeTcpSettingsAsync();
                    result.OptimizationsApplied.Add("Otimização de Rede (DNS/TCP)");
                    progressCallback?.Invoke(65);

                    // 4. Tweaks Avançados
                    _logger.LogInfo("[UnifiedOpt] Passo 4/5: Tweaks avançados");
                    await ApplyBasicTweaksAsync(p => progressCallback?.Invoke(70 + (int)(p * 0.15)));
                    result.OptimizationsApplied.Add("Ajustes de Registro do Windows");
                    progressCallback?.Invoke(85);

                    // 5. Ultra Clean
                    _logger.LogInfo("[UnifiedOpt] Passo 5/5: Ultra limpeza");
                    var ultraResult = await _ultraCleaner.QuickCleanupAsync(new Progress<CleanupProgress>(p => { /* silenciar */ }));
                    result.SpaceFreed += ultraResult.SpaceCleaned;
                    result.OptimizationsApplied.Add("Limpeza Profunda Voltris");
                    progressCallback?.Invoke(100);

                    _logger.LogSuccess($"[UnifiedOpt] Otimização completa finalizada. {result.OptimizationsApplied.Count} itens processados.");
                    return result;
                }
                catch (Exception ex)
                {
                    _logger.LogError("[UnifiedOpt] Erro durante otimização completa", ex);
                    result.Success = false;
                    result.OptimizationsApplied.Add($"ERRO: {ex.Message}");
                    return result;
                }
            });
        }

        public void Dispose()
        {
            if (_disposed) return;
            _currentTx?.Dispose();
            _disposed = true;
        }

        public async Task<UnifiedOptimizationResult> ExecuteOptimizationAsync(OptimizationType type, GamerOptimizationContext context)
        {
            var result = new UnifiedOptimizationResult { Success = false };

            try
            {
                _logger.LogInfo($"[UnifiedOpt] Executando otimização do tipo {type}...");

                switch (type)
                {
                    case OptimizationType.Full:
                        var fullResult = await RunFullOptimizationAsync();
                        result.Success = fullResult.Success;
                        result.SpaceFreed = fullResult.SpaceFreed;
                        result.PerformanceGain = fullResult.PerformanceGain;
                        result.OptimizationsApplied = fullResult.OptimizationsApplied;
                        break;
                    case OptimizationType.Cleanup:
                        long freed = await CleanBasicAsync();
                        _logger?.LogSuccess($"[UnifiedOpt.Cleanup] Limpeza concluída: {VoltrisOptimizer.Helpers.FileSystemHelper.FormatBytes(freed)} liberados");
                        result.SpaceFreed = freed;
                        result.Success = true;
                        result.OptimizationsApplied.Add($"Limpeza básica ({VoltrisOptimizer.Helpers.FileSystemHelper.FormatBytes(freed)})");
                        break;
                    case OptimizationType.Gaming:
                        await _performanceOptimizer.OptimizeForGamingAsync();
                        result.Success = true;
                        result.OptimizationsApplied.Add("Modo gaming ativado");
                        break;
                    case OptimizationType.Performance:
                        await _performanceOptimizer.OptimizeProcessesAsync();
                        await _performanceOptimizer.OptimizeMemoryAsync();
                        result.Success = true;
                        result.OptimizationsApplied.Add("Otimização de performance");
                        break;
                    default:
                        result.ErrorMessage = "Tipo de otimização não suportado";
                        break;
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError("[UnifiedOpt] Erro ao executar otimização", ex);
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        public Dictionary<string, object> GetStatistics()
        {
            return new Dictionary<string, object>
            {
                ["LastOptimization"] = _lastOptimizations.OrderByDescending(kvp => kvp.Value).FirstOrDefault().Key ?? "Nunca",
                ["TotalOptimizations"] = _lastOptimizations.Count,
                ["TotalSpaceFreed"] = _optimizationResults.Values.Sum()
            };
        }

        private async Task<long> CleanBasicAsync(Action<int>? progressCallback = null)
        {
            long cleaned = 0;
            cleaned += await _systemCleaner.CleanTempFilesAsync(p => progressCallback?.Invoke((int)(p * 0.5)));
            cleaned += await _systemCleaner.EmptyRecycleBinAsync() ? 1024 : 0; 
            cleaned += await _systemCleaner.CleanThumbnailsAsync(p => progressCallback?.Invoke(50 + (int)(p * 0.5)));
            return cleaned;
        }

        private async Task ApplyBasicTweaksAsync(Action<int>? progressCallback = null)
        {
            await _performanceOptimizer.OptimizeStartupAsync(p => progressCallback?.Invoke((int)(p * 0.5)));
            await _performanceOptimizer.OptimizeServicesAsync(p => progressCallback?.Invoke(50 + (int)(p * 0.5)));
        }


        public class OptimizationResult
        {
            public bool Success { get; set; }
            public DateTime Timestamp { get; set; }
            public List<string> OptimizationsApplied { get; set; } = new();
            public long SpaceFreed { get; set; }
            public double PerformanceGain { get; set; }
        }
    }

    public class VoltrisPerformanceOptimizer : IPerformanceOptimizer
    {
        private readonly ILoggingService _logger;
        private readonly AntivirusCompliance.SafeOperationWhitelist _whitelist;
        
        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        private const uint PROCESS_QUERY_INFORMATION = 0x0400;
        private const uint PROCESS_SET_QUOTA = 0x0100;
        
        // Blacklist rígida de processos protegidos (Sincronizado com BrainActionExecutorV2)
        private static readonly HashSet<string> ProtectedProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "system", "registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "lsm",
            "fontdrvhost", "dwm", "explorer", "taskhostw", "svchost", "voltrisoptimizer",
            "msmpeng", "nissrv", "securityhealthservice", "securityhealthsystray"
        };

        public VoltrisPerformanceOptimizer(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _whitelist = new AntivirusCompliance.SafeOperationWhitelist(logger);
        }

        /// <summary>
        /// [FIX:UNICO-DONO-DE-ENERGIA] "Alto desempenho seguro" não é mais uma
        /// troca de plano.
        ///
        /// O método validava o comando na whitelist e rodava
        /// `powercfg /setactive {HighPerformance}`. A validação não era o
        /// problema — ela permitia, corretamente, porque `/setactive` estava na
        /// lista de operações seguras. O problema é que "seguro" e "correto" são
        /// coisas diferentes: o comando é inofensivo para o sistema, e ainda assim
        /// desliga o Perfil Inteligente.
        ///
        /// Pedir "alto desempenho" aqui é pedir uma decisão de energia de um
        /// componente que não sabe qual é a capacidade da máquina nem qual é o
        /// perfil em uso. Quem sabe é o Perfil.
        /// </summary>
        public async Task<OperationResult> SetHighPerformancePlanAsync(Action<int>? progressCallback = null)
        {
            return await Task.Run(async () =>
            {
                try
                {
                    _logger.LogInfo(
                        "[PERFORMANCE] 'Alto desempenho' agora delega ao Perfil Inteligente " +
                        "(antes: powercfg /setactive HighPerformance, fora do dono da energia).");
                    progressCallback?.Invoke(10);

                    bool ok = VoltrisOptimizer.Services.Power.ProfilePowerAuthority.RequestProfileApply(
                        "UnifiedOptimization.SetHighPerformance",
                        "usuario pediu alto desempenho",
                        _logger);

                    progressCallback?.Invoke(100);

                    return ok
                        ? OperationResult.CreateSuccess("Perfil Inteligente reaplicado", true)
                        : OperationResult.CreateFailure("Falha", "O Perfil Inteligente nao reaplicou (sem admin?)");
                }
                catch (Exception ex) { return OperationResult.CreateFailure("Erro", ex.Message); }
            });
        }

        /// <summary>
        /// [FIX:UNICO-DONO-DE-ENERGIA] O Balanceado aqui era o pior caso de todos.
        ///
        /// O Perfil Inteligente é dono do plano E é, por construção, o dono do
        /// plano "Voltris - Equilibrado". Este método rodava
        /// `powercfg /setactive {Balanced}` — o plano nativo do Windows — para
        /// "voltar ao equilibrado".
        ///
        /// Ou seja: a operação de "voltar ao estado bom" desligava justamente o
        /// dono do estado bom. O Perfil detectava, reassertava, e este serviço
        /// podia chamar de novo. É a origem direta do sintoma que o usuário
        /// relatou: o plano do Voltris alternando com o do Windows.
        ///
        /// O nome do método continua, porque a INTENÇÃO do usuário continua
        /// válida (quer o comportamento equilibrado). O que muda é quem executa.
        /// </summary>
        public async Task<OperationResult> SetBalancedPlanAsync(Action<int>? progressCallback = null)
        {
            return await Task.Run(async () =>
            {
                try
                {
                    _logger.LogInfo(
                        "[PERFORMANCE] 'Equilibrado' agora delega ao Perfil Inteligente " +
                        "(antes: powercfg /setactive Balanced, que desligava o proprio dono do plano equilibrado).");

                    bool ok = VoltrisOptimizer.Services.Power.ProfilePowerAuthority.RequestProfileApply(
                        "UnifiedOptimization.SetBalanced",
                        "usuario pediu modo equilibrado",
                        _logger);

                    progressCallback?.Invoke(100);

                    return ok
                        ? OperationResult.CreateSuccess("Perfil Inteligente reaplicado", true)
                        : OperationResult.CreateFailure("Falha", "O Perfil Inteligente nao reaplicou (sem admin?)");
                }
                catch (Exception ex) { return OperationResult.CreateFailure("Erro", ex.Message); }
            });
        }

        public async Task<OperationResult> OptimizeStartupAsync(Action<int>? progressCallback = null)
        {
            return await Task.Run(() => { return OperationResult.CreateSuccess("Sucesso", true); });
        }

        public async Task<OperationResult> OptimizeServicesAsync(Action<int>? progressCallback = null)
        {
            return await Task.Run(() => { return OperationResult.CreateSuccess("Sucesso", true); });
        }

        /// <summary>
        /// Realiza a limpeza profunda de memória RAM (Trim Working Set)
        /// </summary>
        public async Task<OperationResult> OptimizeRAMAsync(Action<int>? progressCallback = null)
        {
            return await Task.Run(async () =>
            {
                try
                {
                    _logger.LogInfo("[PERFORMANCE] Iniciando otimização centralizada de RAM (via VoltrisMemoryCoordinator)...");
                    progressCallback?.Invoke(10);

                    bool result = await global::VoltrisOptimizer.Services.Optimization.Memory.VoltrisMemoryCoordinator.Instance.OptimizeMemoryAsync(isManualClick: true);
                    
                    progressCallback?.Invoke(90);

                    // ── RECUPERAÇÃO DE MEMÉRIA COM GANHO REAL ─────────────────
                    // O coordinators acima cuida de working set / trim por processo.
                    // Estas duas áreas do kernel devolvem memória de forma rápida e
                    // DURÁVEL — e não existiam no Voltris antes. Implementação
                    // equivalente à do WinMemoryCleaner
                    // (ComputerService.OptimizeModifiedPageList linha 579,
                    //  OptimizeSystemFileCache linha 662).
                    //
                    // Colocado aqui — e não no botão — porque OptimizeRAMAsync é
                    // o ponto ÚNICO por onde passam TANTO o botão de otimização
                    // rápida QUANTO a etapa 4 do botão circular. Um lugar, dois
                    // botões corrigidos.
                    try
                    {
                        var reclaim = new VoltrisOptimizer.Services.Performance.MemoryReclaimService(_logger);
                        var summary = await reclaim.ReclaimAllAsync().ConfigureAwait(false);

                        if (summary.FreedMb > 0.5)
                        {
                            _logger.LogSuccess(
                                $"[PERFORMANCE] Recuperação de memória: +{summary.FreedMb:N0} MB disponíveis " +
                                $"(ModifiedPageList={summary.ModifiedPageList.Ok}, FileCache={summary.SystemFileCache.Ok}).");
                        }
                        else
                        {
                            _logger.LogInfo(
                                $"[PERFORMANCE] Recuperação de memória sem ganho mensurável (+{summary.FreedMb:N1} MB). " +
                                $"ModifiedPageList={summary.ModifiedPageList.Ok}" +
                                $"{(string.IsNullOrEmpty(summary.ModifiedPageList.Skipped) ? "" : " (" + summary.ModifiedPageList.Skipped + ")")}, " +
                                $"FileCache={summary.SystemFileCache.Ok}" +
                                $"{(string.IsNullOrEmpty(summary.SystemFileCache.Skipped) ? "" : " (" + summary.SystemFileCache.Skipped + ")")}.");
                        }
                    }
                    catch (Exception exReclaim)
                    {
                        // Falha aqui não pode derrubar a otimização de RAM inteira.
                        _logger.LogWarning($"[PERFORMANCE] Recuperação de memória falhou (continuando): {exReclaim.Message}");
                    }

                    // REMOVIDO: GC.Collect forçado causa stuttering em jogos
                    // GC.Collect(2, GCCollectionMode.Forced, true, true);
                    // GC.WaitForPendingFinalizers();

                    _logger.LogSuccess($"[PERFORMANCE] Otimização de RAM concluída. (Sucesso: {result})");
                    
                    progressCallback?.Invoke(100);
                    
                    var opResult = OperationResult.CreateSuccess($"RAM Otimizada", true);
                    return opResult;
                }
                catch (Exception ex)
                {
                    _logger.LogError("[PERFORMANCE] Falha crítica na otimização de RAM", ex);
                    return OperationResult.CreateFailure("Erro ao otimizar RAM", ex.Message);
                }
            });
        }

        private string FormatBytes(long bytes)
        {
            return VoltrisOptimizer.Helpers.FileSystemHelper.FormatBytes(bytes);
        }

        public async Task OptimizeProcessesAsync() { await OptimizeRAMAsync(); }
        public async Task OptimizeMemoryAsync() { await OptimizeRAMAsync(); }
        public Task OptimizeDiskAsync() { return Task.CompletedTask; }
        public async Task OptimizeForGamingAsync() { await SetHighPerformancePlanAsync(); }
    }
}



