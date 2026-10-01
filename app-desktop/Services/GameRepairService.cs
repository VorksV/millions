using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services
{
    // -------------------------------------------------------------------------
    // MODELOS DE RESULTADO
    // -------------------------------------------------------------------------

    public enum GameRepairSeverity { Info, Warning, Error }

    /// <summary>
    /// Status em tempo real de um item durante scan/reparo.
    /// </summary>
    public enum GameRepairItemStatus
    {
        Unknown,     // ainda não verificado
        Checking,    // em verificação
        OK,          // instalado/configurado corretamente
        Missing,     // faltando / problema detectado
        Fixing,      // sendo corrigido agora
        Fixed,       // corrigido com sucesso
        Failed,      // falhou ao corrigir
        Skipped      // requer ação manual
    }

    public class GameRepairIssue : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        private void OnPropChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

        public string Id          { get; set; } = "";
        public string Title       { get; set; } = "";
        public string Description { get; set; } = "";
        public GameRepairSeverity Severity { get; set; } = GameRepairSeverity.Warning;
        public bool   CanAutoFix  { get; set; } = true;
        public bool   RequiresReboot { get; set; } = false;
        public string FixDescription { get; set; } = "";
        public string FixTypeLabel => CanAutoFix
            ? VoltrisOptimizer.Services.LocalizationService.Instance.GetString("GamerRepairAuto")
            : VoltrisOptimizer.Services.LocalizationService.Instance.GetString("GamerRepairManual");

        // -- Campos de estado em tempo real ------------------------------------
        private GameRepairItemStatus _status = GameRepairItemStatus.Unknown;
        public GameRepairItemStatus Status
        {
            get => _status;
            set { _status = value; OnPropChanged(nameof(Status)); OnPropChanged(nameof(StatusIcon)); OnPropChanged(nameof(StatusColor)); OnPropChanged(nameof(IsOk)); OnPropChanged(nameof(HasIssue)); }
        }

        private string _statusMessage = "";
        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropChanged(nameof(StatusMessage)); }
        }

        /// <summary>Ícone do status para exibição na UI.</summary>
        public string StatusIcon => Status switch
        {
            GameRepairItemStatus.OK      => "✓",
            GameRepairItemStatus.Missing => "!",
            GameRepairItemStatus.Fixing  => "...",
            GameRepairItemStatus.Fixed   => "✓",
            GameRepairItemStatus.Failed  => "!",
            GameRepairItemStatus.Skipped => "!",
            GameRepairItemStatus.Checking=> "...",
            _                            => "?"
        };

        /// <summary>Cor do status para exibição na UI.</summary>
        public string StatusColor => Status switch
        {
            GameRepairItemStatus.OK      => "#00CC66",
            GameRepairItemStatus.Missing => "#FF4444",
            GameRepairItemStatus.Fixing  => "#FFA500",
            GameRepairItemStatus.Fixed   => "#00CC66",
            GameRepairItemStatus.Failed  => "#FF4444",
            GameRepairItemStatus.Skipped => "#FFA500",
            GameRepairItemStatus.Checking=> "#888888",
            _                            => "#888888"
        };

        public bool IsOk     => Status == GameRepairItemStatus.OK || Status == GameRepairItemStatus.Fixed;
        public bool HasIssue => Status == GameRepairItemStatus.Missing || Status == GameRepairItemStatus.Failed;
    }

    public class GameRepairFixResult
    {
        public string IssueId  { get; set; } = "";
        public bool   Success  { get; set; }
        public string Message  { get; set; } = "";
    }

    public class GameRepairScanResult
    {
        /// <summary>Apenas os itens COM problema (compatibilidade retroativa).</summary>
        public List<GameRepairIssue> Issues   { get; set; } = new();
        /// <summary>TODOS os itens (OK + com problema) para exibir na UI em tempo real.</summary>
        public List<GameRepairIssue> AllItems { get; set; } = new();
        public bool   ScanSuccess { get; set; }
        public string ErrorMessage { get; set; } = "";
        public TimeSpan Duration  { get; set; }
        public bool   AllOk => Issues.Count == 0;
    }

    public class GameRepairResult
    {
        public int    TotalIssues   { get; set; }
        public int    FixedCount    { get; set; }
        public int    SkippedCount  { get; set; }
        public bool   RequiresReboot { get; set; }
        public List<GameRepairFixResult> Details { get; set; } = new();
    }

    // -------------------------------------------------------------------------
    // SERVIÇO PRINCIPAL
    // -------------------------------------------------------------------------

    /// <summary>
    /// Serviço inteligente de detecção e correção de erros que afetam jogos.
    /// Scan máx 15s e corrige SOMENTE o que está realmente quebrado.
    /// Suporte: VC++ 2005-2022, DirectX, OpenGL, Vulkan, .NET, Media Foundation, áudio.
    /// </summary>
    public class GameRepairService
    {
        private readonly ILoggingService _logger;

        public Action<string, string>? OnLog      { get; set; }
        public Action<int, string>?    OnProgress { get; set; }

        private static readonly string WinDir  = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        private static readonly string Sys32   = Path.Combine(WinDir, "System32");
        private static readonly string SysWow  = Path.Combine(WinDir, "SysWOW64");
        private static readonly string PsExe   = Path.Combine(WinDir, "System32", "WindowsPowerShell", "v1.0", "powershell.exe");

        // --- VC++ 2005/2008: usa msiexec diretamente para evitar janela do InstallShield -------
        // O autoextrator InstallShield dos VC++ 2005/2008 cria um processo-filho (install.exe)
        // a partir de IXP000.TMP\ com sua própria janela, que WindowStyle=Hidden não suprime.
        // Solução definitiva: baixar os MSIs diretamente e chamar msiexec /i /qn como executável principal.
        // Product codes oficiais Microsoft para detecção via msiexec:
        private static readonly (string Id, string WingetX64, string WingetX86,
            string RegistryKeyword, string Label,
            string UrlX64, string UrlX86, string InstallArgs,
            bool UseDirectMsiExec)[] VcVersions =
        {
            // VC++ 2005 - forçar msiexec direto para evitar janela do autoextrator
            ("VC_2005_MISSING",
                "Microsoft.VCRedist.2005.x64", "Microsoft.VCRedist.2005.x86",
                "Visual C++ 2005", "Visual C++ 2005",
                "https://download.microsoft.com/download/8/B/4/8B42259F-5D70-43F4-AC2E-4B208FD8D66A/vcredist_x64.EXE",
                "https://download.microsoft.com/download/8/B/4/8B42259F-5D70-43F4-AC2E-4B208FD8D66A/vcredist_x86.EXE",
                "/q:a /c:\"msiexec /i vcredist.msi /qn /norestart\"",
                false),

            // VC++ 2008 - mesmo esquema do 2005
            ("VC_2008_MISSING",
                "Microsoft.VCRedist.2008.x64", "Microsoft.VCRedist.2008.x86",
                "Visual C++ 2008", "Visual C++ 2008",
                "https://download.microsoft.com/download/5/D/8/5D8C65CB-C849-4025-8E95-C3966CAFD8AE/vcredist_x64.exe",
                "https://download.microsoft.com/download/5/D/8/5D8C65CB-C849-4025-8E95-C3966CAFD8AE/vcredist_x86.exe",
                "/q:a /c:\"msiexec /i vcredist.msi /qn /norestart\"",
                false),

            // VC++ 2010 - /quiet totalmente silencioso (sem barra de progresso)
            ("VC_2010_MISSING",
                "Microsoft.VCRedist.2010.x64", "Microsoft.VCRedist.2010.x86",
                "Visual C++ 2010", "Visual C++ 2010",
                "https://download.microsoft.com/download/1/6/5/165255E7-1014-4D0A-B094-B6A430A6BFFC/vcredist_x64.exe",
                "https://download.microsoft.com/download/1/6/5/165255E7-1014-4D0A-B094-B6A430A6BFFC/vcredist_x86.exe",
                "/quiet /norestart",
                false),

            // VC++ 2012 - /quiet totalmente silencioso
            ("VC_2012_MISSING",
                "Microsoft.VCRedist.2012.x64", "Microsoft.VCRedist.2012.x86",
                "Visual C++ 2012", "Visual C++ 2012",
                "https://download.microsoft.com/download/1/6/B/16B06F60-3B20-4FF2-B699-5E9B7962F9AE/VSU_4/vcredist_x64.exe",
                "https://download.microsoft.com/download/1/6/B/16B06F60-3B20-4FF2-B699-5E9B7962F9AE/VSU_4/vcredist_x86.exe",
                "/quiet /norestart",
                false),

            // VC++ 2013 - /quiet totalmente silencioso
            ("VC_2013_MISSING",
                "Microsoft.VCRedist.2013.x64", "Microsoft.VCRedist.2013.x86",
                "Visual C++ 2013", "Visual C++ 2013",
                "https://aka.ms/highdpimfc2013x64enu",
                "https://aka.ms/highdpimfc2013x86enu",
                "/quiet /norestart",
                false),

            // VC++ 2015-2022 - /install /quiet /norestart (já era correto)
            ("VC_2015_MISSING",
                "Microsoft.VCRedist.2015+.x64", "Microsoft.VCRedist.2015+.x86",
                "Visual C++ 2015", "Visual C++ 2015-2022",
                "https://aka.ms/vs/17/release/vc_redist.x64.exe",
                "https://aka.ms/vs/17/release/vc_redist.x86.exe",
                "/install /quiet /norestart",
                false)};

        public GameRepairService(ILoggingService logger)
        {
            _logger = logger;
        }

        // --- Helpers ---------------------------------------------------------

        private void Log(string msg, string color = "#AAAAAA")
        {
            _logger.LogInfo($"[GameRepair] {msg}");
            OnLog?.Invoke(msg, color);
        }

        private void Progress(int pct, string msg) => OnProgress?.Invoke(pct, msg);

        /// <summary>Executa PowerShell script inline de forma segura.</summary>
        private async Task<(int exitCode, string output)> RunPsAsync(
            string script, CancellationToken ct, int timeoutMs = 30_000)
        {
            var tmp = Path.GetTempFileName() + ".ps1";
            _logger.LogInfo($"[GameRepair][RunPs] Executando script PowerShell (timeout: {timeoutMs}ms)");
            _logger.LogInfo($"[GameRepair][RunPs] Script length: {script.Length} chars");
            _logger.LogInfo($"[GameRepair][RunPs] Temp file: {tmp}");
            
            try
            {
                await File.WriteAllTextAsync(tmp, script, Encoding.UTF8, ct);
                _logger.LogInfo($"[GameRepair][RunPs] Script escrito em {tmp}");
                
                var psi = new ProcessStartInfo
                {
                    FileName               = PsExe,
                    Arguments              = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{tmp}\"",
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding  = Encoding.UTF8
                };
                
                _logger.LogInfo($"[GameRepair][RunPs] Starting: {psi.FileName} {psi.Arguments}");
                
                using var proc = new Process { StartInfo = psi };
                var started = proc.Start();
                _logger.LogInfo($"[GameRepair][RunPs] Process started: PID={proc.Id}, Started={started}");
                
                var outTask = proc.StandardOutput.ReadToEndAsync();
                var errTask = proc.StandardError.ReadToEndAsync();
                
                using var cts2 = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts2.CancelAfter(timeoutMs);
                
                try 
                { 
                    await proc.WaitForExitAsync(cts2.Token); 
                    _logger.LogInfo($"[GameRepair][RunPs] Process exited: ExitCode={proc.ExitCode}");
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning($"[GameRepair][RunPs] Timeout após {timeoutMs}ms, matando processo...");
                    try { proc.Kill(entireProcessTree: true); } catch (Exception ex) { _logger.LogWarning($"[GameRepair][RunPs] Erro ao matar processo: {ex.Message}"); }
                    if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
                    var partialOutput = await outTask;
                    _logger.LogWarning($"[GameRepair][RunPs] Retornando output parcial ({partialOutput.Length} chars)");
                    return (-1, partialOutput);
                }
                
                var output = await outTask;
                var error = await errTask;
                
                if (!string.IsNullOrEmpty(error))
                {
                    _logger.LogWarning($"[GameRepair][RunPs] StdErr: {error}");
                }
                
                _logger.LogInfo($"[GameRepair][RunPs] Output length: {output.Length} chars");
                if (output.Length > 0)
                {
                    _logger.LogInfo($"[GameRepair][RunPs] Output preview: {output.Substring(0, Math.Min(200, output.Length))}");
                }
                
                return (proc.ExitCode, output);
            }
            catch (OperationCanceledException) 
            { 
                _logger.LogWarning("[GameRepair][RunPs] Cancelado");
                throw; 
            }
            catch (Exception ex) 
            { 
                _logger.LogError($"[GameRepair][RunPs] EXCEÇÃO: {ex.GetType().Name} - {ex.Message}");
                return (-99, ex.Message); 
            }
            finally 
            { 
                try 
                { 
                    if (File.Exists(tmp)) 
                    {
                        await Task.Delay(1000, ct).ConfigureAwait(false); File.Delete(tmp);
                        _logger.LogInfo($"[GameRepair][RunPs] Temp file removido: {tmp}");
                    }
                } 
                catch (Exception ex) 
                { 
                    _logger.LogWarning($"[GameRepair][RunPs] Erro ao remover temp file: {ex.Message}");
                } 
            }
        }

        /// <summary>Executa processo simples com opção de ShellExecute.</summary>
        private async Task<(int exitCode, string output)> RunAsync(
            string exe, string args, CancellationToken ct, int timeoutMs = 20_000, bool useShell = false)
        {
            _logger.LogInfo($"[GameRepair][Run] Executando: {exe} {args}");
            _logger.LogInfo($"[GameRepair][Run] Timeout: {timeoutMs}ms | UseShell: {useShell}");
            
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName               = exe,
                    Arguments              = args,
                    UseShellExecute        = useShell,
                    CreateNoWindow         = !useShell,
                    RedirectStandardOutput = false,
                    RedirectStandardError  = false,
                    WindowStyle            = useShell ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal
                };
                
                using var proc = new Process { StartInfo = psi };
                proc.Start();
                _logger.LogInfo($"[GameRepair][Run] Process started: PID={proc.Id}");
                
                using var cts2 = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts2.CancelAfter(timeoutMs);
                
                try
                {
                    await proc.WaitForExitAsync(cts2.Token);
                    _logger.LogInfo($"[GameRepair][Run] Process exited: ExitCode={proc.ExitCode}");
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning($"[GameRepair][Run] Timeout após {timeoutMs}ms");
                    try { proc.Kill(entireProcessTree: true); } catch { }
                    if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
                    return (-1, "TIMEOUT");
                }
                
                return (proc.ExitCode, $"ExitCode={proc.ExitCode}");
            }
            catch (Exception ex) 
            { 
                _logger.LogError($"[GameRepair][Run] EXCEÇÃO: {ex.GetType().Name} - {ex.Message}");
                return (-99, ex.Message); 
            }
        }

        // ---------------------------------------------------------------------
        // SCAN INTELIGENTE (máx 15 segundos)
        // ---------------------------------------------------------------------

        public async Task<GameRepairScanResult> ScanAsync(
            IProgress<(int pct, string msg)>? progress = null,
            CancellationToken ct = default)
        {
            _logger.LogInfo("[GameRepair][ScanAsync] === INICIANDO SCAN ===");
            
            var sw = Stopwatch.StartNew();
            var result = new GameRepairScanResult { ScanSuccess = true };

            var globalProgress = ProgressBridge.WrapExisting<(int, string)>("Verificando jogos...", progress);
            progress = globalProgress;

            Log(LocalizationService.Instance.GetString("GameRepairLog_SCANCorrigirErros"), "#31A8FF");
            progress?.Report((0, "Iniciando scan inteligente..."));

            try
            {
                using var scanCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                scanCts.CancelAfter(TimeSpan.FromSeconds(14));
                _logger.LogInfo("[GameRepair][ScanAsync] Timeout configurado: 14 segundos");

                // Definir os geradores de tarefas para execução controlada
                var taskGenerators = new List<(string Name, string InternalName, Func<CancellationToken, Task<List<GameRepairIssue>>> Func)>
                {
                    ("Visual C++ Redist", "VcRedist", ct => CheckVcRedistAsync(ct)),
                    ("DirectX Desktop", "DirectX", ct => CheckDirectXAsync(ct)),
                    ("D3DX Legacy DLLs", "D3DXLegacy", ct => CheckD3DXLegacyAsync(ct)),
                    ("xAudio2 Engine", "xAudio2", ct => CheckxAudio2Async(ct)),
                    ("OpenGL Drivers", "OpenGL", ct => CheckOpenGLAsync(ct)),
                    ("Vulkan Runtime", "Vulkan", ct => CheckVulkanAsync(ct)),
                    (".NET Runtimes", "DotNet", ct => CheckDotNetRuntimeAsync(ct)),
                    ("Media Foundation", "MediaFoundation", ct => CheckMediaFoundationAsync(ct)),
                    ("Crashes de GPU", "GpuCrashes", ct => CheckGpuCrashesAsync(ct)),
                    ("Serviço de Áudio", "AudioService", ct => CheckAudioServiceAsync(ct)),
                    ("Launchers (Steam/EA)", "GameLaunchers", ct => CheckGameLaunchersAsync(ct)),
                    ("Modo de Jogo", "GameMode", ct => CheckGameModeAsync(ct)),
                    ("HAGS", "HAGS", ct => CheckHAGSAsync(ct)),
                    ("Xbox Game DVR", "XboxDVR", ct => CheckXboxDVRAsync(ct)),
                    ("Pagefile/Virtual RAM", "Pagefile", ct => CheckPagefileAsync(ct))};
                
                _logger.LogInfo($"[GameRepair][ScanAsync] Total de tarefas: {taskGenerators.Count}");

                int completed = 0;
                var semaphore = new SemaphoreSlim(3); // Máximo 3 tarefas simultâneas para evitar sobrecarga (especialmente PS)
                
                var runningTasks = taskGenerators.Select(async tg =>
                {
                    try
                    {
                        await semaphore.WaitAsync(scanCts.Token);
                        
                        _logger.LogInfo($"[GameRepair][ScanAsync] Iniciando: {tg.InternalName}");
                        progress?.Report((Math.Min(90, (completed * 90) / taskGenerators.Count),
                            $"Verificando {tg.Name}... ({completed + 1}/{taskGenerators.Count})"));
                        
                        // Executar tarefa com timeout individual de segurança se for PowerShell ou operação pesada
                        var task = Task.Run(() => tg.Func(scanCts.Token), scanCts.Token);
                        var r = await task;
                        
                        Interlocked.Increment(ref completed);
                        _logger.LogInfo($"[GameRepair][ScanAsync] Concluído: {tg.InternalName} (Issues: {r.Count})");
                        
                        progress?.Report((Math.Min(90, (completed * 90) / taskGenerators.Count),
                            $"Verificando componentes... ({completed}/{taskGenerators.Count})"));
                        
                        return r;
                    }
                    catch (OperationCanceledException) 
                    { 
                        _logger.LogWarning($"[GameRepair][ScanAsync] Cancelado/Timeout: {tg.InternalName}");
                        Interlocked.Increment(ref completed); 
                        return new List<GameRepairIssue>(); 
                    }
                    catch (Exception ex) 
                    { 
                        _logger.LogError($"[GameRepair][ScanAsync] Falha em {tg.InternalName}: {ex.Message}");
                        Interlocked.Increment(ref completed); 
                        return new List<GameRepairIssue>(); 
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                var allResults = await Task.WhenAll(runningTasks);

                foreach (var issues in allResults)
                {
                    foreach (var issue in issues)
                    {
                        result.AllItems.Add(issue);
                        if (issue.Status == GameRepairItemStatus.Missing)
                            result.Issues.Add(issue);
                    }
                }

                progress?.Report((95, $"Scan concluído: {result.Issues.Count} problema(s) encontrado(s)"));

                if (result.AllOk)
                {
                    Log(LocalizationService.Instance.GetString("GameRepairLog_TudoOK"), "#00FF88");
                    _logger.LogInfo("[GameRepair][ScanAsync] Nenhum problema detectado");
                }
                else
                {
                    Log(string.Format(LocalizationService.Instance.GetString("GameRepairLog_ProblemasDetectados"), result.Issues.Count), "#FFA500");
                    _logger.LogWarning($"[GameRepair][ScanAsync] {result.Issues.Count} problema(s) detectado(s)");
                    foreach (var issue in result.Issues)
                    {
                        _logger.LogWarning($"[GameRepair][ScanAsync]   - {issue.Id}: {issue.Title}");
                    }
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning("[GameRepair][ScanAsync] Scan encerrado por timeout");
                Log(LocalizationService.Instance.GetString("GameRepairLog_ScanTimeout"), "#FFA500");
            }
            catch (OperationCanceledException) 
            { 
                _logger.LogWarning("[GameRepair][ScanAsync] Scan cancelado pelo usuário");
                throw; 
            }
            catch (Exception ex)
            {
                result.ScanSuccess = false;
                result.ErrorMessage = ex.Message;
                _logger.LogError($"[GameRepair][ScanAsync] EXCEÇÃO: {ex.GetType().Name} - {ex.Message}");
                _logger.LogError($"[GameRepair][ScanAsync] Stack trace: {ex.StackTrace}");
                Log(string.Format(LocalizationService.Instance.GetString("GameRepairLog_ErroNoScan"), ex.Message), "#FF4444");
            }

            result.Duration = sw.Elapsed;
            _logger.LogInfo($"[GameRepair][ScanAsync] Duração: {result.Duration.TotalSeconds:F2}s");
            _logger.LogInfo($"[GameRepair][ScanAsync] ScanSuccess: {result.ScanSuccess}");
            _logger.LogInfo($"[GameRepair][ScanAsync] Total issues: {result.Issues.Count}");
            
            progress?.Report((100, result.AllOk ? "Tudo OK" : $"{result.Issues.Count} problema(s) encontrado(s)"));
            return result;
        }

        // ---------------------------------------------------------------------
        // VERIFICAÇÕES INDIVIDUAIS
        // ---------------------------------------------------------------------

        private async Task<List<GameRepairIssue>> CheckVcRedistAsync(CancellationToken ct)
        {
            var items = new List<GameRepairIssue>();
            Log(LocalizationService.Instance.GetString("GameRepairLog_VerificandoVC"), "#AAAAAA");
            _logger.LogInfo("[GameRepair][CheckVcRedist] Iniciando verificação de VC++ Redistributables");

            try
            {
                foreach (var vc in VcVersions)
                {
                    ct.ThrowIfCancellationRequested();

                    _logger.LogInfo($"[GameRepair][CheckVcRedist] Verificando {vc.Label}...");
                    _logger.LogInfo($"[GameRepair][CheckVcRedist]   RegistryKeyword: {vc.RegistryKeyword}");

                    var installed = await IsVcRedistInstalledViaRegistryAsync(vc.RegistryKeyword, ct);
                    
                    _logger.LogInfo($"[GameRepair][CheckVcRedist]   Resultado: {(installed ? "INSTALADO" : "AUSENTE")}");

                    if (!installed)
                    {
                        var issue = new GameRepairIssue
                        {
                            Id           = vc.Id,
                            Title        = $"{vc.Label} não instalado",
                            Description  = $"{vc.Label} ausente. Jogos que dependem desta versão podem não iniciar.",
                            Severity     = GameRepairSeverity.Error,
                            CanAutoFix   = true,
                            FixDescription = $"Instalar {vc.Label} x64 + x86",
                            Status       = GameRepairItemStatus.Missing,
                            StatusMessage = $"{vc.Label}: não instalado"
                        };
                        items.Add(issue);
                        Log(string.Format(LocalizationService.Instance.GetString("GameRepairLog_VCFaltando"), vc.Label), "#FF4444");
                        _logger.LogWarning($"[GameRepair][CheckVcRedist] {vc.Label} NÃO encontrado no registry");
                    }
                    else
                    {
                        items.Add(new GameRepairIssue
                        {
                            Id     = vc.Id.Replace("_MISSING", "_OK"),
                            Title  = $"{vc.Label}",
                            Status = GameRepairItemStatus.OK,
                            StatusMessage = $"{vc.Label}: instalado",
                            CanAutoFix = false
                        });
                        Log(string.Format(LocalizationService.Instance.GetString("GameRepairLog_VCOK"), vc.Label), "#00FF88");
                        _logger.LogInfo($"[GameRepair][CheckVcRedist] {vc.Label} encontrado no registry");
                    }
                }
                
                _logger.LogInfo($"[GameRepair][CheckVcRedist] Verificação concluída. Issues encontradas: {items.Count(i => i.Status == GameRepairItemStatus.Missing)}");
            }
            catch (OperationCanceledException) 
            { 
                _logger.LogWarning("[GameRepair][CheckVcRedist] Verificação cancelada");
                throw; 
            }
            catch (Exception ex) 
            { 
                _logger.LogError($"[GameRepair][CheckVcRedist] EXCEÇÃO: {ex.GetType().Name} - {ex.Message}");
                _logger.LogError($"[GameRepair][CheckVcRedist] Stack trace: {ex.StackTrace}");
            }

            return items;
        }

        private async Task<bool> IsVcRedistInstalledViaRegistryAsync(string keyword, CancellationToken ct)
        {
            _logger.LogInfo($"[GameRepair][Registry] Buscando por keyword: '{keyword}'");
            
            var regPaths = new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };
            
            foreach (var regPath in regPaths)
            {
                ct.ThrowIfCancellationRequested();
                _logger.LogInfo($"[GameRepair][Registry] Verificando path: HKLM\\{regPath}");
                
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(regPath);
                    if (key == null) 
                    {
                        _logger.LogWarning($"[GameRepair][Registry] Chave não encontrada: {regPath}");
                        continue;
                    }
                    
                    var subKeyNames = key.GetSubKeyNames();
                    _logger.LogInfo($"[GameRepair][Registry] Total de subchaves: {subKeyNames.Length}");
                    
                    int subIndex = 0;
                    foreach (var subName in subKeyNames)
                    {
                        subIndex++;
                        if (subIndex % 50 == 0) ct.ThrowIfCancellationRequested();

                        try
                        {
                            using var sub = key.OpenSubKey(subName);
                            var name = sub?.GetValue("DisplayName") as string ?? "";
                            
                            if (!string.IsNullOrEmpty(name) && name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                            {
                                _logger.LogInfo($"[GameRepair][Registry] MATCH encontrado: '{name}' em {subName}");
                                return true;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"[GameRepair][Registry] Erro ao ler subchave {subName}: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex) 
                { 
                    _logger.LogError($"[GameRepair][Registry] Erro ao acessar {regPath}: {ex.Message}");
                }
            }
            
            _logger.LogInfo($"[GameRepair][Registry] Keyword '{keyword}' NÃO encontrada em nenhuma subchave");
            return false;
        }

        private async Task<List<GameRepairIssue>> CheckDirectXAsync(CancellationToken ct)
        {
            var issues = new List<GameRepairIssue>();
            Log(LocalizationService.Instance.GetString("GameRepairLog_VerificandoDX"), "#AAAAAA");
            try
            {
                var dxDlls = new[]
                {
                    ("d3d9.dll",          "DirectX 9"),
                    ("d3d11.dll",         "DirectX 11"),
                    ("d3d12.dll",         "DirectX 12"),
                    ("dxgi.dll",          "DXGI"),
                    ("d3dcompiler_47.dll", "D3D Compiler 47"),
                    ("xinput1_4.dll",      "XInput 1.4"),
                    ("xinput9_1_0.dll",    "XInput 9.1.0")};
                var missing = new List<string>();
                foreach (var (dll, label) in dxDlls)
                {
                    ct.ThrowIfCancellationRequested();
                    bool has32 = System.IO.File.Exists(System.IO.Path.Combine(Sys32, dll));
                    bool has64 = System.IO.File.Exists(System.IO.Path.Combine(SysWow, dll));
                    bool ok = dll == "d3d12.dll" ? has32 : (has32 || has64);
                    if (!ok) missing.Add(label);
                }
                bool hasDx9Redist = System.IO.File.Exists(System.IO.Path.Combine(Sys32, "d3dx9_43.dll")) ||
                                    System.IO.File.Exists(System.IO.Path.Combine(SysWow, "d3dx9_43.dll"));
                if (!hasDx9Redist) missing.Add("DirectX 9 Redistributable (d3dx9_43)");
                if (missing.Any())
                {
                    issues.Add(new GameRepairIssue
                    {
                        Id = "DX_MISSING", Title = "Componentes DirectX ausentes",
                        Description = $"DLLs faltando: {string.Join(", ", missing)}. Jogos DirectX podem não iniciar.",
                        Severity = GameRepairSeverity.Error, CanAutoFix = true,
                        FixDescription = "Executar DirectX End-User Runtime Web Installer"
                    });
                    Log($"  ! DirectX: {string.Join(", ", missing)} faltando", "#FF4444");
                }
                else { Log(LocalizationService.Instance.GetString("GameRepairLog_DXOK"), "#00FF88"); }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { _logger.LogWarning($"[GameRepair] CheckDirectX: {ex.Message}"); }
            return issues;
        }

        private async Task<List<GameRepairIssue>> CheckOpenGLAsync(CancellationToken ct)
        {
            var issues = new List<GameRepairIssue>();
            Log(LocalizationService.Instance.GetString("GameRepairLog_VerificandoGL"), "#AAAAAA");
            try
            {
                ct.ThrowIfCancellationRequested();
                bool hasOpenGL = System.IO.File.Exists(System.IO.Path.Combine(Sys32, "opengl32.dll"));
                if (!hasOpenGL)
                {
                    issues.Add(new GameRepairIssue
                    {
                        Id = "OPENGL_MISSING", Title = "OpenGL ausente (opengl32.dll)",
                        Description = "opengl32.dll não encontrado em System32. Jogos OpenGL não funcionarão.",
                        Severity = GameRepairSeverity.Error, CanAutoFix = true,
                        FixDescription = "Restaurar opengl32.dll via SFC /scannow"
                    });
                    Log(LocalizationService.Instance.GetString("GameRepairLog_GLFaltando"), "#FF4444");
                }
                else { Log(LocalizationService.Instance.GetString("GameRepairLog_GLOK"), "#00FF88"); }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { _logger.LogWarning($"[GameRepair] CheckOpenGL: {ex.Message}"); }
            return issues;
        }

        private async Task<List<GameRepairIssue>> CheckVulkanAsync(CancellationToken ct)
        {
            var issues = new List<GameRepairIssue>();
            Log(LocalizationService.Instance.GetString("GameRepairLog_VerificandoVulkan"), "#AAAAAA");
            try
            {
                ct.ThrowIfCancellationRequested();
                bool hasVulkan64 = System.IO.File.Exists(System.IO.Path.Combine(Sys32, "vulkan-1.dll"));
                bool hasVulkan32 = System.IO.File.Exists(System.IO.Path.Combine(SysWow, "vulkan-1.dll"));
                bool hasVulkanReg = false;
                try
                {
                    using var vkKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Khronos\Vulkan\Drivers");
                    hasVulkanReg = vkKey != null && vkKey.GetValueNames().Length > 0;
                }
                catch { }
                if (!hasVulkan64 && !hasVulkan32 && !hasVulkanReg)
                {
                    issues.Add(new GameRepairIssue
                    {
                        Id = "VULKAN_MISSING", Title = "Vulkan Runtime não instalado",
                        Description = "vulkan-1.dll ausente. Jogos Vulkan não funcionarão.",
                        Severity = GameRepairSeverity.Warning, CanAutoFix = true,
                        FixDescription = "Instalar Vulkan Runtime via driver de GPU (NVIDIA/AMD/Intel)"
                    });
                    Log(LocalizationService.Instance.GetString("GameRepairLog_VulkanFaltando"), "#FF4444");
                }
                else { Log(LocalizationService.Instance.GetString("GameRepairLog_VulkanOK"), "#00FF88"); }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { _logger.LogWarning($"[GameRepair] CheckVulkan: {ex.Message}"); }
            return issues;
        }

        private async Task<List<GameRepairIssue>> CheckDotNetRuntimeAsync(CancellationToken ct)
        {
            var issues = new List<GameRepairIssue>();
            Log(LocalizationService.Instance.GetString("GameRepairLog_VerificandoNET"), "#AAAAAA");
            try
            {
                ct.ThrowIfCancellationRequested();
                bool hasDotNet4 = false;
                try
                {
                    using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                        @"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full");
                    var release = key?.GetValue("Release") as int?;
                    hasDotNet4 = release.HasValue && release.Value >= 394802;
                }
                catch { }
                if (!hasDotNet4)
                {
                    issues.Add(new GameRepairIssue
                    {
                        Id = "DOTNET4_MISSING", Title = ".NET Framework 4.6.2+ não instalado",
                        Description = ".NET Framework 4.6.2 ou superior ausente.",
                        Severity = GameRepairSeverity.Error, CanAutoFix = true,
                        FixDescription = "Instalar .NET Framework 4.8 via Windows Update"
                    });
                    Log(LocalizationService.Instance.GetString("GameRepairLog_NET462Faltando"), "#FF4444");
                }
                else { Log(LocalizationService.Instance.GetString("GameRepairLog_NET4OK"), "#00FF88"); }

                ct.ThrowIfCancellationRequested();
                var dotnetRoot = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "dotnet", "shared", "Microsoft.NETCore.App");
                bool hasDotNetCore = System.IO.Directory.Exists(dotnetRoot) &&
                                     System.IO.Directory.GetDirectories(dotnetRoot).Length > 0;
                if (!hasDotNetCore)
                {
                    issues.Add(new GameRepairIssue
                    {
                        Id = "DOTNETCORE_MISSING", Title = ".NET 6/7/8 Runtime não instalado",
                        Description = ".NET Runtime moderno ausente.",
                        Severity = GameRepairSeverity.Warning, CanAutoFix = true,
                        FixDescription = "Instalar .NET 8 Desktop Runtime automaticamente"
                    });
                    Log(LocalizationService.Instance.GetString("GameRepairLog_NETModernFaltando"), "#FF4444");
                }
                else { Log(LocalizationService.Instance.GetString("GameRepairLog_NETModernOK"), "#00FF88"); }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { _logger.LogWarning($"[GameRepair] CheckDotNetRuntime: {ex.Message}"); }
            return issues;
        }

        private async Task<List<GameRepairIssue>> CheckMediaFoundationAsync(CancellationToken ct)
        {
            var issues = new List<GameRepairIssue>();
            Log(LocalizationService.Instance.GetString("GameRepairLog_VerificandoMediaFound"), "#AAAAAA");
            try
            {
                ct.ThrowIfCancellationRequested();
                var mfDlls = new[] { "mf.dll", "mfplat.dll", "mfreadwrite.dll", "mfplay.dll" };
                var missing = mfDlls.Where(dll => !System.IO.File.Exists(System.IO.Path.Combine(Sys32, dll))).ToList();
                if (missing.Any())
                {
                    issues.Add(new GameRepairIssue
                    {
                        Id = "MF_MISSING", Title = "Media Foundation ausente",
                        Description = $"DLLs faltando: {string.Join(", ", missing)}.",
                        Severity = GameRepairSeverity.Warning, CanAutoFix = true,
                        FixDescription = "Habilitar Media Foundation via DISM"
                    });
                    Log($"  ! Media Foundation: {string.Join(", ", missing)} faltando", "#FF4444");
                }
                else { Log(LocalizationService.Instance.GetString("GameRepairLog_MediaFoundOK"), "#00FF88"); }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { _logger.LogWarning($"[GameRepair] CheckMediaFoundation: {ex.Message}"); }
            return issues;
        }

        private async Task<List<GameRepairIssue>> CheckGpuCrashesAsync(CancellationToken ct)
        {
            var issues = new List<GameRepairIssue>();
            Log(LocalizationService.Instance.GetString("GameRepairLog_VerificandoGPU"), "#AAAAAA");
            try
            {
                ct.ThrowIfCancellationRequested();
                // Usando XPath Filter que é significativamente mais rápido que FilterHashtable em logs grandes
                var script = @"
$xpath = ""*[System[(EventID=4101 or EventID=4116) and TimeCreated[timediff(@SystemTime) <= 604800000]]]""
$events = Get-WinEvent -FilterXPath $xpath -LogName System -ErrorAction SilentlyContinue -MaxEvents 5
if ($events) { Write-Output ""GPU_TDR_COUNT=$($events.Count)"" } else { Write-Output 'GPU_TDR_NONE' }
";
                var (_, psOut) = await RunPsAsync(script, ct, timeoutMs: 10_000);
                if (psOut.Contains("GPU_TDR_COUNT="))
                {
                    var countStr = psOut.Split('\n').FirstOrDefault(l => l.Trim().StartsWith("GPU_TDR_COUNT="))?.Replace("GPU_TDR_COUNT=", "").Trim() ?? "0";
                    int.TryParse(countStr, out int tdrCount);
                    if (tdrCount > 0)
                    {
                        issues.Add(new GameRepairIssue
                        {
                            Id = "GPU_TDR", Title = $"GPU: {tdrCount} crash(es) de driver nos últimos 7 dias",
                            Description = "Erros TDR de GPU detectados. Jogos podem travar.",
                            Severity = GameRepairSeverity.Warning, CanAutoFix = false,
                            FixDescription = "Atualizar ou reinstalar driver de GPU"
                        });
                        Log(string.Format(LocalizationService.Instance.GetString("GameRepairLog_GPUTDRs"), tdrCount), "#FF4444");
                    }
                    else { Log(LocalizationService.Instance.GetString("GameRepairLog_GPUOK"), "#00FF88"); }
                }
                else { Log(LocalizationService.Instance.GetString("GameRepairLog_GPUOK"), "#00FF88"); }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { _logger.LogWarning($"[GameRepair] CheckGpuCrashes: {ex.Message}"); }
            return issues;
        }

        private async Task<List<GameRepairIssue>> CheckAudioServiceAsync(CancellationToken ct)
        {
            var issues = new List<GameRepairIssue>();
            Log(LocalizationService.Instance.GetString("GameRepairLog_VerificandoAudio"), "#AAAAAA");
            try
            {
                ct.ThrowIfCancellationRequested();
                var script = @"
$svc = Get-Service -Name 'AudioSrv' -ErrorAction SilentlyContinue
if ($svc) { Write-Output ""AUDIO_STATUS=$($svc.Status)"" } else { Write-Output 'AUDIO_NOT_FOUND' }
";
                var (_, psOut) = await RunPsAsync(script, ct, timeoutMs: 10_000);
                bool audioRunning = psOut.Contains("AUDIO_STATUS=Running");
                bool audioFound   = !psOut.Contains("AUDIO_NOT_FOUND");
                if (!audioFound)
                {
                    issues.Add(new GameRepairIssue { Id = "AUDIO_SVC_MISSING", Title = "Serviço de Áudio não encontrado",
                        Description = "Windows Audio Service não encontrado.", Severity = GameRepairSeverity.Error,
                        CanAutoFix = true, FixDescription = "Restaurar via SFC /scannow" });
                    Log(LocalizationService.Instance.GetString("GameRepairLog_AudioAusente"), "#FF4444");
                }
                else if (!audioRunning)
                {
                    issues.Add(new GameRepairIssue { Id = "AUDIO_SVC_STOPPED", Title = "Serviço de Áudio parado",
                        Description = "Windows Audio Service parado. Jogos não terão áudio.", Severity = GameRepairSeverity.Error,
                        CanAutoFix = true, FixDescription = "Iniciar Windows Audio Service" });
                    Log(LocalizationService.Instance.GetString("GameRepairLog_AudioParado"), "#FF4444");
                }
                else { Log(LocalizationService.Instance.GetString("GameRepairLog_AudioOK"), "#00FF88"); }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { _logger.LogWarning($"[GameRepair] CheckAudioService: {ex.Message}"); }
            return issues;
        }

        private async Task<List<GameRepairIssue>> CheckGameLaunchersAsync(CancellationToken ct)
        {
            var issues = new List<GameRepairIssue>();
            Log(LocalizationService.Instance.GetString("GameRepairLog_VerificandoLaunchers"), "#AAAAAA");
            try
            {
                ct.ThrowIfCancellationRequested();
                var launchers = new[]
                {
                    ("Steam",      @"SOFTWARE\Valve\Steam",                       "InstallPath",     "steam.exe"),
                    ("EA App",     @"SOFTWARE\Electronic Arts\EA Desktop",        "InstallLocation", "EADesktop.exe"),
                    ("Battle.net", @"SOFTWARE\Blizzard Entertainment\Battle.net", "InstallPath",     "Battle.net.exe")};
                foreach (var (name, regKey, valueName, exeName) in launchers)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(regKey) ??
                                        Microsoft.Win32.Registry.CurrentUser.OpenSubKey(regKey);
                        if (key == null) continue;
                        var installPath = key.GetValue(valueName) as string;
                        if (!string.IsNullOrEmpty(installPath))
                        {
                            var exePath = System.IO.Path.Combine(installPath, exeName);
                            if (!System.IO.File.Exists(exePath))
                            {
                                issues.Add(new GameRepairIssue
                                {
                                    Id = $"LAUNCHER_{name.Replace(" ","_").ToUpper()}_CORRUPT",
                                    Title = $"{name}: executável ausente",
                                    Description = $"{name} registrado mas executável não encontrado. Reinstale.",
                                    Severity = GameRepairSeverity.Warning, CanAutoFix = false,
                                    FixDescription = $"Reinstalar {name}"
                                });
                                Log(string.Format(LocalizationService.Instance.GetString("GameRepairLog_LauncherFaltando"), name), "#FF4444");
                                continue;
                            }
                        }
                        Log(string.Format(LocalizationService.Instance.GetString("GameRepairLog_LauncherOK"), name), "#00FF88");
                    }
                    catch { }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { _logger.LogWarning($"[GameRepair] CheckGameLaunchers: {ex.Message}"); }
            return issues;
        }

        // --- Novos checks profissionais ----------------------------------------

        private async Task<List<GameRepairIssue>> CheckD3DXLegacyAsync(CancellationToken ct)
        {
            var issues = new List<GameRepairIssue>();
            Log(LocalizationService.Instance.GetString("GameRepairLog_VerificandoD3DX"), "#AAAAAA");
            try
            {
                ct.ThrowIfCancellationRequested();
                var missing = new List<string>();

                for (int i = 24; i <= 43; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var dll = $"d3dx9_{i}.dll";
                    bool has = System.IO.File.Exists(System.IO.Path.Combine(Sys32, dll))
                            || System.IO.File.Exists(System.IO.Path.Combine(SysWow, dll));
                    if (!has) missing.Add(dll);
                }

                foreach (var dll in new[] { "d3dx10.dll", "d3dx10_43.dll" })
                {
                    ct.ThrowIfCancellationRequested();
                    bool has = System.IO.File.Exists(System.IO.Path.Combine(Sys32, dll))
                            || System.IO.File.Exists(System.IO.Path.Combine(SysWow, dll));
                    if (!has) missing.Add(dll);
                }

                {
                    var dll = "d3dx11_43.dll";
                    bool has = System.IO.File.Exists(System.IO.Path.Combine(Sys32, dll))
                            || System.IO.File.Exists(System.IO.Path.Combine(SysWow, dll));
                    if (!has) missing.Add(dll);
                }

                if (missing.Count > 0)
                {
                    issues.Add(new GameRepairIssue
                    {
                        Id = "D3DX_LEGACY_MISSING",
                        Title = $"D3DX Legacy: {missing.Count} DLL(s) ausente(s)",
                        Description = $"DLLs ausentes: {string.Join(", ", missing.Take(5))}{(missing.Count > 5 ? $" +{missing.Count - 5} mais" : "")}. Jogos antigos podem não iniciar.",
                        Severity = GameRepairSeverity.Error,
                        CanAutoFix = true,
                        FixDescription = "Instalar DirectX End-User Runtime (inclui todas as D3DX legacy)"
                    });
                    Log(string.Format(LocalizationService.Instance.GetString("GameRepairLog_D3DXFaltando"), missing.Count), "#FF4444");
                    _logger.LogWarning($"[GameRepair][CheckD3DXLegacy] {missing.Count} DLLs ausentes: {string.Join(", ", missing)}");
                }
                else
                {
                    Log(LocalizationService.Instance.GetString("GameRepairLog_D3DXOK"), "#00FF88");
                    _logger.LogInfo("[GameRepair][CheckD3DXLegacy] Todas as DLLs D3DX Legacy presentes");
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { _logger.LogWarning($"[GameRepair] CheckD3DXLegacy: {ex.Message}"); }
            return issues;
        }

        private async Task<List<GameRepairIssue>> CheckxAudio2Async(CancellationToken ct)
        {
            var issues = new List<GameRepairIssue>();
            Log(LocalizationService.Instance.GetString("GameRepairLog_VerificandoXAudio"), "#AAAAAA");
            try
            {
                ct.ThrowIfCancellationRequested();
                var missing = new List<string>();

                for (int i = 0; i <= 9; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var dll = $"xaudio2_{i}.dll";
                    bool has = System.IO.File.Exists(System.IO.Path.Combine(Sys32, dll))
                            || System.IO.File.Exists(System.IO.Path.Combine(SysWow, dll));
                    if (!has) missing.Add(dll);
                }

                var x3d = "x3daudio1_7.dll";
                if (!System.IO.File.Exists(System.IO.Path.Combine(Sys32, x3d))
                 && !System.IO.File.Exists(System.IO.Path.Combine(SysWow, x3d)))
                    missing.Add(x3d);

                var xapofx = "XAPOFX1_5.dll";
                if (!System.IO.File.Exists(System.IO.Path.Combine(Sys32, xapofx))
                 && !System.IO.File.Exists(System.IO.Path.Combine(SysWow, xapofx)))
                    missing.Add(xapofx);

                if (missing.Count > 0)
                {
                    issues.Add(new GameRepairIssue
                    {
                        Id = "XAUDIO2_MISSING",
                        Title = $"xAudio2: {missing.Count} DLL(s) ausente(s)",
                        Description = $"xAudio2 DLLs ausentes: {string.Join(", ", missing)}. Jogos podem não ter áudio.",
                        Severity = GameRepairSeverity.Error,
                        CanAutoFix = true,
                        FixDescription = "Instalar DirectX End-User Runtime (inclui xAudio2 completo)"
                    });
                    Log(string.Format(LocalizationService.Instance.GetString("GameRepairLog_XAudioFaltando"), missing.Count), "#FF4444");
                    _logger.LogWarning($"[GameRepair][CheckxAudio2] {missing.Count} DLLs ausentes: {string.Join(", ", missing)}");
                }
                else
                {
                    Log(LocalizationService.Instance.GetString("GameRepairLog_XAudioOK"), "#00FF88");
                    _logger.LogInfo("[GameRepair][CheckxAudio2] xAudio2 OK");
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { _logger.LogWarning($"[GameRepair] CheckxAudio2: {ex.Message}"); }
            return issues;
        }

        private async Task<List<GameRepairIssue>> CheckGameModeAsync(CancellationToken ct)
        {
            var issues = new List<GameRepairIssue>();
            Log(LocalizationService.Instance.GetString("GameRepairLog_VerificandoGameMode"), "#AAAAAA");
            try
            {
                ct.ThrowIfCancellationRequested();
                bool gameModeOk = false;
                try
                {
                    using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                        @"Software\Microsoft\GameBar");
                    var val = key?.GetValue("AllowAutoGameMode") as int?;
                    var val2 = key?.GetValue("AutoGameModeEnabled") as int?;
                    gameModeOk = val.GetValueOrDefault(1) != 0 && val2.GetValueOrDefault(1) != 0;
                }
                catch { gameModeOk = true; }

                if (!gameModeOk)
                {
                    issues.Add(new GameRepairIssue
                    {
                        Id = "GAMEMODE_DISABLED",
                        Title = "Windows Game Mode desativado",
                        Description = "Game Mode desativado. Ative para melhorar FPS e reduzir stuttering.",
                        Severity = GameRepairSeverity.Warning,
                        CanAutoFix = true,
                        FixDescription = "Ativar Game Mode via Registry"
                    });
                    Log(LocalizationService.Instance.GetString("GameRepairLog_GameModeDesativado"), "#FF4444");
                    _logger.LogWarning("[GameRepair][CheckGameMode] Game Mode desativado");
                }
                else
                {
                    Log(LocalizationService.Instance.GetString("GameRepairLog_GameModeOK"), "#00FF88");
                    _logger.LogInfo("[GameRepair][CheckGameMode] Game Mode ativo");
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { _logger.LogWarning($"[GameRepair] CheckGameMode: {ex.Message}"); }
            return issues;
        }

        private async Task<List<GameRepairIssue>> CheckHAGSAsync(CancellationToken ct)
        {
            var issues = new List<GameRepairIssue>();
            Log(LocalizationService.Instance.GetString("GameRepairLog_VerificandoHAGS"), "#AAAAAA");
            try
            {
                ct.ThrowIfCancellationRequested();
                bool hagsEnabled = false;
                bool hagsSupported = false;
                try
                {
                    using var gpuKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                        @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers");
                    if (gpuKey != null)
                    {
                        hagsSupported = true;
                        var hagsVal = gpuKey.GetValue("HwSchMode") as int?;
                        hagsEnabled = hagsVal.HasValue && hagsVal.Value == 2;
                    }
                }
                catch { }

                if (hagsSupported && !hagsEnabled)
                {
                    issues.Add(new GameRepairIssue
                    {
                        Id = "HAGS_DISABLED",
                        Title = "HAGS (Hardware GPU Scheduling) desativado",
                        Description = "HAGS desativado. Ativar reduz latência de GPU e melhora performance em jogos modernos.",
                        Severity = GameRepairSeverity.Warning,
                        CanAutoFix = true,
                        FixDescription = "Ativar HAGS via Registry (requer reinicialização)"
                    });
                    Log(LocalizationService.Instance.GetString("GameRepairLog_HAGSDesativado"), "#FF4444");
                    _logger.LogWarning("[GameRepair][CheckHAGS] HAGS desativado");
                }
                else
                {
                    Log(LocalizationService.Instance.GetString("GameRepairLog_HAGSOK"), "#00FF88");
                    _logger.LogInfo($"[GameRepair][CheckHAGS] HAGS OK (supported={hagsSupported}, enabled={hagsEnabled})");
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { _logger.LogWarning($"[GameRepair] CheckHAGS: {ex.Message}"); }
            return issues;
        }

        private async Task<List<GameRepairIssue>> CheckXboxDVRAsync(CancellationToken ct)
        {
            var issues = new List<GameRepairIssue>();
            Log(LocalizationService.Instance.GetString("GameRepairLog_VerificandoXboxDVR"), "#AAAAAA");
            try
            {
                ct.ThrowIfCancellationRequested();
                bool dvrActive = false;
                try
                {
                    using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                        @"System\GameConfigStore");
                    var dvrVal = key?.GetValue("GameDVR_Enabled") as int?;
                    dvrActive = dvrVal.GetValueOrDefault(1) == 1;

                    if (!dvrActive)
                    {
                        using var key2 = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                            @"SOFTWARE\Policies\Microsoft\Windows\GameDVR");
                        var captureVal = key2?.GetValue("AllowGameDVR") as int?;
                        dvrActive = captureVal == null || captureVal.Value != 0;
                    }
                }
                catch { dvrActive = false; }

                if (dvrActive)
                {
                    issues.Add(new GameRepairIssue
                    {
                        Id = "XBOX_DVR_ACTIVE",
                        Title = "Xbox Game Bar DVR ativo (impacto de FPS)",
                        Description = "Game Bar DVR ativo pode causar stuttering e reduzir FPS em até 15%.",
                        Severity = GameRepairSeverity.Warning,
                        CanAutoFix = true,
                        FixDescription = "Desativar Game Bar DVR via Registry"
                    });
                    Log(LocalizationService.Instance.GetString("GameRepairLog_XboxDVRAtivo"), "#FF4444");
                    _logger.LogWarning("[GameRepair][CheckXboxDVR] Xbox DVR ativo");
                }
                else
                {
                    Log(LocalizationService.Instance.GetString("GameRepairLog_XboxDVROK"), "#00FF88");
                    _logger.LogInfo("[GameRepair][CheckXboxDVR] Xbox DVR desativado");
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { _logger.LogWarning($"[GameRepair] CheckXboxDVR: {ex.Message}"); }
            return issues;
        }

        private async Task<List<GameRepairIssue>> CheckPagefileAsync(CancellationToken ct)
        {
            var issues = new List<GameRepairIssue>();
            Log(LocalizationService.Instance.GetString("GameRepairLog_VerificandoPagefile"), "#AAAAAA");
            try
            {
                ct.ThrowIfCancellationRequested();
                long totalRamMb = 0;
                long pagefileMb = 0;

                try
                {
                    var script = @"
$cs = Get-CimInstance -ClassName Win32_ComputerSystem
$pf = Get-CimInstance -ClassName Win32_PageFileUsage
Write-Output ""RAM_MB=$([math]::Round($cs.TotalPhysicalMemory / 1MB))""
Write-Output ""PAGEFILE_MB=$($pf | Measure-Object -Property AllocatedBaseSize -Sum | Select-Object -ExpandProperty Sum)""
";
                    var (_, psOut) = await RunPsAsync(script, ct, timeoutMs: 10_000);
                    var ramLine = psOut.Split('\n').FirstOrDefault(l => l.StartsWith("RAM_MB="));
                    var pfLine  = psOut.Split('\n').FirstOrDefault(l => l.StartsWith("PAGEFILE_MB="));
                    if (ramLine != null) long.TryParse(ramLine.Replace("RAM_MB=", "").Trim(), out totalRamMb);
                    if (pfLine  != null) long.TryParse(pfLine.Replace("PAGEFILE_MB=", "").Trim(), out pagefileMb);
                }
                catch { }

                _logger.LogInfo($"[GameRepair][CheckPagefile] RAM={totalRamMb}MB, Pagefile={pagefileMb}MB");

                long minRecommendedMb = Math.Max(totalRamMb, 4096);

                if (pagefileMb == 0)
                {
                    issues.Add(new GameRepairIssue
                    {
                        Id = "PAGEFILE_DISABLED",
                        Title = "Pagefile (memória virtual) desativado",
                        Description = "Sem pagefile, jogos que precisam de mais RAM vão travar ou crashar.",
                        Severity = GameRepairSeverity.Error,
                        CanAutoFix = true,
                        FixDescription = "Ativar pagefile gerenciado pelo Windows"
                    });
                    Log(LocalizationService.Instance.GetString("GameRepairLog_PagefileDesativado"), "#FF4444");
                    _logger.LogWarning("[GameRepair][CheckPagefile] Pagefile desativado");
                }
                else if (totalRamMb > 0 && pagefileMb < minRecommendedMb)
                {
                    issues.Add(new GameRepairIssue
                    {
                        Id = "PAGEFILE_SMALL",
                        Title = $"Pagefile pequeno ({pagefileMb}MB - recomendado {minRecommendedMb}MB)",
                        Description = $"Pagefile de {pagefileMb}MB pode ser insuficiente. Recomendado: {minRecommendedMb}MB.",
                        Severity = GameRepairSeverity.Warning,
                        CanAutoFix = true,
                        FixDescription = "Aumentar pagefile para tamanho gerenciado pelo Windows"
                    });
                    Log(string.Format(LocalizationService.Instance.GetString("GameRepairLog_PagefilePequeno"), pagefileMb, minRecommendedMb), "#FF4444");
                    _logger.LogWarning($"[GameRepair][CheckPagefile] Pagefile insuficiente: {pagefileMb}MB");
                }
                else
                {
                    Log(string.Format(LocalizationService.Instance.GetString("GameRepairLog_PagefileOK"), pagefileMb), "#00FF88");
                    _logger.LogInfo($"[GameRepair][CheckPagefile] Pagefile OK: {pagefileMb}MB");
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { _logger.LogWarning($"[GameRepair] CheckPagefile: {ex.Message}"); }
            return issues;
        }

        // -------------------------------------------------------------------------
        // REPARO

        public async Task<GameRepairResult> RepairAsync(
            List<GameRepairIssue> issues,
            IProgress<(int pct, string msg)>? progress = null,
            CancellationToken ct = default)
        {
            _logger.LogInfo($"[GameRepair][RepairAsync] Iniciando reparo. Total de issues: {issues.Count}");

            var globalProgress = ProgressBridge.WrapExisting<(int, string)>("Reparando jogos...", progress);
            progress = globalProgress;

            var result = new GameRepairResult { TotalIssues = issues.Count };
            Log(LocalizationService.Instance.GetString("GameRepairLog_REPARO"), "#31A8FF");
            progress?.Report((0, $"Iniciando reparo de {issues.Count} problema(s)..."));

            var instantIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "GAMEMODE_DISABLED", "HAGS_DISABLED", "XBOX_DVR_ACTIVE",
                  "PAGEFILE_DISABLED", "PAGEFILE_SMALL",
                  "AUDIO_SVC_STOPPED", "AUDIO_SVC_MISSING",
                  "OPENGL_MISSING" };

            var instantIssues = issues.Where(i =>  i.CanAutoFix && instantIds.Contains(i.Id)).ToList();
            var downloadIssues = issues.Where(i => i.CanAutoFix && !instantIds.Contains(i.Id)).ToList();
            var manualIssues   = issues.Where(i => !i.CanAutoFix).ToList();

            if (instantIssues.Count > 0)
            {
                progress?.Report((2, $"Aplicando {instantIssues.Count} correção(ões) instantânea(s)..."));
                _logger.LogInfo($"[GameRepair][RepairAsync] ETAPA 1: {instantIssues.Count} instantâneas em paralelo");

                var instantTasks = instantIssues.Select(async issue =>
                {
                    ct.ThrowIfCancellationRequested();
                    _logger.LogInfo($"[GameRepair][Instant] Corrigindo: {issue.Id}");
                    var fix = await FixIssueAsync(issue, null, ct).ConfigureAwait(false);
                    return (issue, fix);
                });

                var instantResults = await Task.WhenAll(instantTasks).ConfigureAwait(false);
                foreach (var (issue, fix) in instantResults)
                {
                    result.Details.Add(fix);
                    if (fix.Success) { result.FixedCount++;   Log(string.Format(LocalizationService.Instance.GetString("GameRepairLog_IssueCorrigido"), issue.Title), "#00FF88"); }
                    else             { result.SkippedCount++; Log(string.Format(LocalizationService.Instance.GetString("GameRepairLog_IssueFalhou"), issue.Title, fix.Message), "#FF4444"); }
                }
                int instantOk = instantResults.Count(r => r.fix.Success);
                progress?.Report((30, $"Correções instantâneas: {instantOk}/{instantIssues.Count} OK"));
            }

            int downloadStart = instantIssues.Count > 0 ? 30 : 0;
            int downloadRange = 95 - downloadStart;
            int downloadIdx   = 0;

            foreach (var issue in downloadIssues)
            {
                ct.ThrowIfCancellationRequested();
                downloadIdx++;

                int basePct = downloadStart + ((downloadIdx - 1) * downloadRange) / Math.Max(downloadIssues.Count, 1);
                int nextPct = downloadStart + (downloadIdx       * downloadRange) / Math.Max(downloadIssues.Count, 1);

                IProgress<(int pct, string msg)> subProgress = new Progress<(int pct, string msg)>(p =>
                {
                    int mapped = basePct + (int)((p.pct / 100.0) * (nextPct - basePct));
                    progress?.Report((Math.Min(mapped, 95), p.msg));
                });

                progress?.Report((basePct, $"[{downloadIdx}/{downloadIssues.Count}] Baixando/Instalando: {issue.Title}"));
                Log(string.Format(LocalizationService.Instance.GetString("GameRepairLog_IssueDownload"), issue.Title), "#AAAAAA");
                _logger.LogInfo($"[GameRepair][Download] [{downloadIdx}/{downloadIssues.Count}] {issue.Id}");

                var fixResult = await FixIssueAsync(issue, subProgress, ct).ConfigureAwait(false);
                result.Details.Add(fixResult);

                if (fixResult.Success)
                {
                    result.FixedCount++;
                    Log(string.Format(LocalizationService.Instance.GetString("GameRepairLog_IssueCorrigido"), issue.Title), "#00FF88");
                    progress?.Report((nextPct, $"✓ {issue.Title}: corrigido"));
                }
                else
                {
                    result.SkippedCount++;
                    Log(string.Format(LocalizationService.Instance.GetString("GameRepairLog_IssueFalhou"), issue.Title, fixResult.Message), "#FF4444");
                    _logger.LogError($"[GameRepair][Download] Falha: {issue.Id} - {fixResult.Message}");
                    progress?.Report((nextPct, $"! {issue.Title}: falhou"));
                }
            }

            foreach (var issue in manualIssues)
            {
                result.SkippedCount++;
                result.Details.Add(new GameRepairFixResult { IssueId = issue.Id, Success = false, Message = "Correção manual: " + issue.FixDescription });
                Log(string.Format(LocalizationService.Instance.GetString("GameRepairLog_IssueRequerAcao"), issue.Title), "#FFA500");
            }

            progress?.Report((100, $"Reparo concluído: {result.FixedCount}/{result.TotalIssues} corrigidos"));
            Log(string.Format(LocalizationService.Instance.GetString("GameRepairLog_ReparoConcluido"), result.FixedCount, result.TotalIssues), "#00FF88");

            _logger.LogInfo($"[GameRepair][RepairAsync] Total={result.TotalIssues} Fixed={result.FixedCount} Skipped={result.SkippedCount} RequiresReboot={result.RequiresReboot}");
            
            if (result.FixedCount > 0)
            {
                HistoryService.RecordActivity("Game Repair", 
                    $"Otimização Gamer: {result.FixedCount} componentes reparados com sucesso.", true);
            }

            return result;

        }

        private async Task<GameRepairFixResult> FixIssueAsync(GameRepairIssue issue, IProgress<(int pct, string msg)>? progress, CancellationToken ct)
        {
            var globalProgress = ProgressBridge.WrapExisting<(int, string)>("Corrigindo...", progress);
            progress = globalProgress;

            _logger.LogInfo($"[GameRepair][FixIssueAsync] Issue ID: {issue.Id}");
            _logger.LogInfo($"[GameRepair][FixIssueAsync] Dispatching para handler apropriado...");
            
            try
            {
                GameRepairFixResult result;
                
                switch (issue.Id)
                {
                    case "D3DX_LEGACY_MISSING":
                        _logger.LogInfo($"[GameRepair][FixIssueAsync] Handler: FixDirectXAsync (D3DX Legacy)");
                        result = await FixDirectXAsync(progress, ct);
                        result.IssueId = "D3DX_LEGACY_MISSING";
                        break;

                    case "XAUDIO2_MISSING":
                        _logger.LogInfo($"[GameRepair][FixIssueAsync] Handler: FixDirectXAsync (XAudio2)");
                        result = await FixDirectXAsync(progress, ct);
                        result.IssueId = "XAUDIO2_MISSING";
                        break;

                    case "GAMEMODE_DISABLED":
                        _logger.LogInfo($"[GameRepair][FixIssueAsync] Handler: FixGameModeAsync");
                        result = await FixGameModeAsync(ct);
                        break;

                    case "HAGS_DISABLED":
                        _logger.LogInfo($"[GameRepair][FixIssueAsync] Handler: FixHAGSAsync");
                        result = await FixHAGSAsync(ct);
                        break;

                    case "XBOX_DVR_ACTIVE":
                        _logger.LogInfo($"[GameRepair][FixIssueAsync] Handler: FixXboxDVRAsync");
                        result = await FixXboxDVRAsync(ct);
                        break;

                    case "PAGEFILE_DISABLED":
                    case "PAGEFILE_SMALL":
                        _logger.LogInfo($"[GameRepair][FixIssueAsync] Handler: FixPagefileAsync");
                        result = await FixPagefileAsync(issue.Id, ct);
                        break;

                    case var id when id.StartsWith("VC_") && id.EndsWith("_MISSING"):
                        _logger.LogInfo($"[GameRepair][FixIssueAsync] Handler: FixVcRedistAsync");
                        result = await FixVcRedistAsync(issue, progress, ct);
                        break;
                        
                    case "DX_MISSING":
                        _logger.LogInfo($"[GameRepair][FixIssueAsync] Handler: FixDirectXAsync");
                        result = await FixDirectXAsync(progress, ct);
                        break;
                        
                    case "OPENGL_MISSING":
                        _logger.LogInfo($"[GameRepair][FixIssueAsync] Handler: FixOpenGLAsync");
                        result = await FixOpenGLAsync(ct);
                        break;
                        
                    case "VULKAN_MISSING":
                        _logger.LogWarning($"[GameRepair][FixIssueAsync] Vulkan requer correção manual (driver GPU)");
                        result = new GameRepairFixResult { IssueId = issue.Id, Success = false, Message = "Vulkan requer reinstalação do driver de GPU" };
                        break;
                        
                    case "DOTNET4_MISSING":
                        _logger.LogInfo($"[GameRepair][FixIssueAsync] Handler: FixDotNet4Async");
                        result = await FixDotNet4Async(progress, ct);
                        break;
                        
                    case "DOTNETCORE_MISSING":
                        _logger.LogInfo($"[GameRepair][FixIssueAsync] Handler: FixDotNetCoreAsync");
                        result = await FixDotNetCoreAsync(progress, ct);
                        break;
                        
                    case "MF_MISSING":
                        _logger.LogInfo($"[GameRepair][FixIssueAsync] Handler: FixMediaFoundationAsync");
                        result = await FixMediaFoundationAsync(progress, ct);
                        break;
                        
                    case "AUDIO_SVC_STOPPED":
                    case "AUDIO_SVC_MISSING":
                        _logger.LogInfo($"[GameRepair][FixIssueAsync] Handler: FixAudioServiceAsync");
                        result = await FixAudioServiceAsync(ct);
                        break;
                        
                    default:
                        _logger.LogWarning($"[GameRepair][FixIssueAsync] Sem handler automático para: {issue.Id}");
                        result = new GameRepairFixResult { IssueId = issue.Id, Success = false, Message = "Sem correção automática: " + issue.FixDescription };
                        break;
                }
                
                _logger.LogInfo($"[GameRepair][FixIssueAsync] Resultado: Success={result.Success}, Message={result.Message}");
                return result;
            }
            catch (OperationCanceledException) 
            { 
                _logger.LogWarning($"[GameRepair][FixIssueAsync] Cancelado: {issue.Id}");
                throw; 
            }
            catch (Exception ex) 
            { 
                _logger.LogError($"[GameRepair][FixIssueAsync] EXCEÇÃO em {issue.Id}: {ex.GetType().Name} - {ex.Message}");
                _logger.LogError($"[GameRepair][FixIssueAsync] Stack trace: {ex.StackTrace}");
                return new GameRepairFixResult { IssueId = issue.Id, Success = false, Message = ex.Message }; 
            }
        }

        private async Task<GameRepairFixResult> FixVcRedistAsync(GameRepairIssue issue, IProgress<(int pct, string msg)>? progress, CancellationToken ct)
        {
            var globalProgress = ProgressBridge.WrapExisting<(int, string)>("Instalando VC++...", progress);
            progress = globalProgress;

            _logger.LogInfo($"[GameRepair][FixVcRedist] Iniciando reparo para issue: {issue.Id}");
            _logger.LogInfo($"[GameRepair][FixVcRedist] Título: {issue.Title}");
            
            var vc = VcVersions.FirstOrDefault(v => v.Id == issue.Id);
            if (string.IsNullOrEmpty(vc.Id))
            {
                _logger.LogError($"[GameRepair][FixVcRedist] Versão VC++ não mapeada para ID: {issue.Id}");
                return new GameRepairFixResult { IssueId = issue.Id, Success = false, Message = "Versão VC++ não mapeada" };
            }
            
            _logger.LogInfo($"[GameRepair][FixVcRedist] Versão encontrada: {vc.Label}");
            _logger.LogInfo($"[GameRepair][FixVcRedist] URL x64: {vc.UrlX64}");
            _logger.LogInfo($"[GameRepair][FixVcRedist] URL x86: {vc.UrlX86}");
            _logger.LogInfo($"[GameRepair][FixVcRedist] InstallArgs: {vc.InstallArgs}");
            
            Log(string.Format(LocalizationService.Instance.GetString("GameRepairLog_BaixandoEInstalando"), vc.Label), "#AAAAAA");
            
            _logger.LogInfo($"[GameRepair][FixVcRedist] Iniciando download x64...");
            progress?.Report((5,  $"Baixando {vc.Label} x64..."));
            bool x64ok = await DownloadAndInstallAsync(vc.UrlX64, vc.InstallArgs, $"{vc.Label} x64", progress, 5, 45, ct, vc.WingetX64);
            _logger.LogInfo($"[GameRepair][FixVcRedist] Resultado x64: {(x64ok ? "SUCESSO" : "FALHA")}");
            progress?.Report((50, $"{vc.Label} x64: {(x64ok ? "✓ instalado" : "✗ falhou")}"));

            _logger.LogInfo($"[GameRepair][FixVcRedist] Iniciando download x86...");
            progress?.Report((55, $"Baixando {vc.Label} x86..."));
            bool x86ok = await DownloadAndInstallAsync(vc.UrlX86, vc.InstallArgs, $"{vc.Label} x86", progress, 55, 95, ct, vc.WingetX86);
            _logger.LogInfo($"[GameRepair][FixVcRedist] Resultado x86: {(x86ok ? "SUCESSO" : "FALHA")}");
            progress?.Report((98, $"{vc.Label} x86: {(x86ok ? "✓ instalado" : "✗ falhou")}"));

            bool success = x64ok || x86ok;
            
            _logger.LogInfo($"[GameRepair][FixVcRedist] Resultado final: {(success ? "SUCESSO" : "FALHA")} (x64={x64ok}, x86={x86ok})");
            
            return new GameRepairFixResult 
            { 
                IssueId = issue.Id, 
                Success = success, 
                Message = success ? $"{vc.Label} instalado" : $"Falha ao instalar {vc.Label}" 
            };
        }

        private async Task<GameRepairFixResult> FixDirectXAsync(IProgress<(int pct, string msg)>? progress, CancellationToken ct)
        {
            var globalProgress = ProgressBridge.WrapExisting<(int, string)>("Instalando DirectX...", progress);
            progress = globalProgress;
            Log(LocalizationService.Instance.GetString("GameRepairLog_BaixandoDX"), "#AAAAAA");
            const string url = "https://download.microsoft.com/download/1/7/1/1718CCC4-6315-4D8E-9543-8E28A4E18C4C/dxwebsetup.exe";
            progress?.Report((5, "Baixando DirectX End-User Runtime (D3D9/10/11)..."));
            bool ok = await DownloadAndInstallAsync(url, "/Q", "DirectX Web Installer (D3D9/10/11)", progress, 5, 90, ct);
            progress?.Report((100, ok ? "✓ DirectX instalado (D3D9/10/11)" : "✗ Falha ao instalar DirectX"));
            return new GameRepairFixResult { IssueId = "DX_MISSING", Success = ok, Message = ok ? "DirectX instalado" : "Falha ao instalar DirectX" };
        }

        private async Task<GameRepairFixResult> FixOpenGLAsync(CancellationToken ct)
        {
            Log(LocalizationService.Instance.GetString("GameRepairLog_SFC"), "#AAAAAA");
            var sfc = System.IO.File.Exists(System.IO.Path.Combine(Sys32, "sfc.exe")) ? System.IO.Path.Combine(Sys32, "sfc.exe") : "sfc.exe";
            var (code, _) = await RunAsync(sfc, "/scannow", ct, timeoutMs: 300_000);
            return new GameRepairFixResult { IssueId = "OPENGL_MISSING", Success = code == 0, Message = code == 0 ? "SFC concluido" : $"SFC retornou {code}" };
        }

        private async Task<GameRepairFixResult> FixDotNet4Async(IProgress<(int pct, string msg)>? progress, CancellationToken ct)
        {
            var globalProgress = ProgressBridge.WrapExisting<(int, string)>("Instalando .NET Framework...", progress);
            progress = globalProgress;
            Log(LocalizationService.Instance.GetString("GameRepairLog_InstalandoNET4"), "#AAAAAA");
            var dism = System.IO.File.Exists(System.IO.Path.Combine(Sys32, "dism.exe")) ? System.IO.Path.Combine(Sys32, "dism.exe") : "dism.exe";
            progress?.Report((5, "Habilitando .NET Framework 3.5 via DISM..."));
            var (dismCode, _) = await RunAsync(dism, "/Online /Enable-Feature /FeatureName:NetFx3 /All /NoRestart /Quiet", ct, timeoutMs: 180_000);
            if (dismCode == 0 || dismCode == 3010)
            {
                progress?.Report((50, "Baixando .NET Framework 4.8..."));
            }

            const string url48 = "https://go.microsoft.com/fwlink/?LinkId=2085155";
            bool ok = await DownloadAndInstallAsync(url48, "/q /norestart", ".NET Framework 4.8", progress, 50, 95, ct);
            progress?.Report((100, ok ? "✓ .NET Framework 4.8 instalado" : "✗ Falha ao instalar .NET Framework 4.8"));
            return new GameRepairFixResult { IssueId = "DOTNET4_MISSING", Success = ok || dismCode == 0 || dismCode == 3010, Message = ok ? ".NET Framework 4.8 instalado" : $"DISM={dismCode}" };
        }

        private async Task<GameRepairFixResult> FixDotNetCoreAsync(IProgress<(int pct, string msg)>? progress, CancellationToken ct)
        {
            var globalProgress = ProgressBridge.WrapExisting<(int, string)>("Instalando .NET Runtime...", progress);
            progress = globalProgress;
            Log(LocalizationService.Instance.GetString("GameRepairLog_InstalandoNET8"), "#AAAAAA");
            const string urlDesktop64 = "https://download.visualstudio.microsoft.com/download/pr/dotnet-runtime-8.0-windows-x64.exe";
            progress?.Report((5, "Baixando .NET 8 Desktop Runtime x64..."));
            bool ok64 = await DownloadAndInstallAsync(urlDesktop64, "/install /quiet /norestart", ".NET 8 Desktop Runtime x64", progress, 5, 55, ct, "Microsoft.DotNet.DesktopRuntime.8");
            progress?.Report((55, ok64 ? "✓ .NET 8 x64 instalado" : "✗ Falha .NET 8 x64"));

            const string urlDesktop86 = "https://download.visualstudio.microsoft.com/download/pr/dotnet-runtime-8.0-windows-x86.exe";
            progress?.Report((60, "Baixando .NET 8 Desktop Runtime x86..."));
            bool ok86 = await DownloadAndInstallAsync(urlDesktop86, "/install /quiet /norestart", ".NET 8 Desktop Runtime x86", progress, 60, 95, ct, "Microsoft.DotNet.DesktopRuntime.8");
            progress?.Report((100, ok64 || ok86 ? "✓ .NET 8 Runtime instalado" : "✗ Falha ao instalar .NET 8"));
            return new GameRepairFixResult { IssueId = "DOTNETCORE_MISSING", Success = ok64 || ok86, Message = ok64 || ok86 ? ".NET 8 Runtime instalado" : "Falha ao instalar .NET 8" };
        }

        private async Task<GameRepairFixResult> FixMediaFoundationAsync(IProgress<(int pct, string msg)>? progress, CancellationToken ct)
        {
            var globalProgress = ProgressBridge.WrapExisting<(int, string)>("Habilitando Media Foundation...", progress);
            progress = globalProgress;
            Log(LocalizationService.Instance.GetString("GameRepairLog_InstalandoMediaFound"), "#AAAAAA");
            progress?.Report((5, "Habilitando Media Foundation via DISM..."));
            var dism = System.IO.File.Exists(System.IO.Path.Combine(Sys32, "dism.exe")) ? System.IO.Path.Combine(Sys32, "dism.exe") : "dism.exe";
            var (code, _) = await RunAsync(dism, "/Online /Enable-Feature /FeatureName:MediaPlayback /All /NoRestart /Quiet", ct, timeoutMs: 120_000);
            bool ok = code == 0 || code == 3010;
            progress?.Report((100, ok ? "✓ Media Foundation habilitado" : $"✗ DISM retornou {code}"));
            return new GameRepairFixResult { IssueId = "MF_MISSING", Success = ok, Message = ok ? "Media Foundation habilitado" : $"DISM retornou {code}" };
        }

        private async Task<GameRepairFixResult> FixAudioServiceAsync(CancellationToken ct)
        {
            Log("  -> Iniciando servico de áudio...", "#AAAAAA");
            var script = @"
Set-Service -Name 'AudioSrv' -StartupType Automatic -ErrorAction SilentlyContinue
Set-Service -Name 'AudioEndpointBuilder' -StartupType Automatic -ErrorAction SilentlyContinue
Start-Service -Name 'AudioEndpointBuilder' -ErrorAction SilentlyContinue
Start-Service -Name 'AudioSrv' -ErrorAction SilentlyContinue
Write-Output ""AUDIO_STATUS=$((Get-Service -Name 'AudioSrv').Status)""
";
            var (_, psOut) = await RunPsAsync(script, ct, timeoutMs: 30_000);
            bool ok = psOut.Contains("AUDIO_STATUS=Running");
            return new GameRepairFixResult { IssueId = "AUDIO_SVC_STOPPED", Success = ok, Message = ok ? "Servico de áudio iniciado" : "Falha ao iniciar servico de áudio" };
        }

        private async Task<GameRepairFixResult> FixGameModeAsync(CancellationToken ct)
        {
            Log(LocalizationService.Instance.GetString("GameRepairLog_InstalandoGameMode"), "#AAAAAA");
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                    @"Software\Microsoft\GameBar", writable: true);
                key.SetValue("AllowAutoGameMode",   1, Microsoft.Win32.RegistryValueKind.DWord);
                key.SetValue("AutoGameModeEnabled", 1, Microsoft.Win32.RegistryValueKind.DWord);
                _logger.LogInfo("[GameRepair][FixGameMode] Game Mode ativado via Registry");
                return new GameRepairFixResult { IssueId = "GAMEMODE_DISABLED", Success = true, Message = "Game Mode ativado" };
            }
            catch (Exception ex)
            {
                _logger.LogError($"[GameRepair][FixGameMode] Erro: {ex.Message}");
                return new GameRepairFixResult { IssueId = "GAMEMODE_DISABLED", Success = false, Message = ex.Message };
            }
        }

        private async Task<GameRepairFixResult> FixHAGSAsync(CancellationToken ct)
        {
            Log(LocalizationService.Instance.GetString("GameRepairLog_InstalandoHAGS"), "#AAAAAA");
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(
                    @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", writable: true);
                key.SetValue("HwSchMode", 2, Microsoft.Win32.RegistryValueKind.DWord);
                _logger.LogInfo("[GameRepair][FixHAGS] HAGS ativado via Registry (requer reboot)");
                return new GameRepairFixResult { IssueId = "HAGS_DISABLED", Success = true, Message = "HAGS ativado (reinicialize o PC para aplicar)" };
            }
            catch (Exception ex)
            {
                _logger.LogError($"[GameRepair][FixHAGS] Erro: {ex.Message}");
                return new GameRepairFixResult { IssueId = "HAGS_DISABLED", Success = false, Message = ex.Message };
            }
        }

        private async Task<GameRepairFixResult> FixXboxDVRAsync(CancellationToken ct)
        {
            Log(LocalizationService.Instance.GetString("GameRepairLog_InstalandoXboxDVR"), "#AAAAAA");
            try
            {
                using var key1 = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                    @"System\GameConfigStore", writable: true);
                key1.SetValue("GameDVR_Enabled", 0, Microsoft.Win32.RegistryValueKind.DWord);

                using var key2 = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(
                    @"SOFTWARE\Policies\Microsoft\Windows\GameDVR", writable: true);
                key2.SetValue("AllowGameDVR", 0, Microsoft.Win32.RegistryValueKind.DWord);

                _logger.LogInfo("[GameRepair][FixXboxDVR] Xbox DVR desativado via Registry");
                return new GameRepairFixResult { IssueId = "XBOX_DVR_ACTIVE", Success = true, Message = "Xbox Game Bar DVR desativado" };
            }
            catch (Exception ex)
            {
                _logger.LogError($"[GameRepair][FixXboxDVR] Erro: {ex.Message}");
                return new GameRepairFixResult { IssueId = "XBOX_DVR_ACTIVE", Success = false, Message = ex.Message };
            }
        }

        private async Task<GameRepairFixResult> FixPagefileAsync(string issueId, CancellationToken ct)
        {
            Log(LocalizationService.Instance.GetString("GameRepairLog_InstalandoPagefile"), "#AAAAAA");
            
            var script = @"
$cs  = Get-CimInstance -ClassName Win32_ComputerSystem
$pf  = Get-CimInstance -ClassName Win32_PageFileSetting -ErrorAction SilentlyContinue
$ramMb = [math]::Round($cs.TotalPhysicalMemory / 1MB)

$sysDrive = $env:SystemDrive
$disk = Get-PSDrive -Name ($sysDrive.TrimEnd(':')) -ErrorAction SilentlyContinue
$freeSpaceMb = if ($disk) { [math]::Round($disk.Free / 1MB) } else { 0 }

Write-Output ""RAM_MB=$ramMb""
Write-Output ""FREE_MB=$freeSpaceMb""
Write-Output ""SYS_DRIVE=$sysDrive""
";
            try
            {
                var (_, psOut) = await RunPsAsync(script, ct, timeoutMs: 15_000);
                
                long ramMb = 0;
                long freeMb = 0;
                string sysDrive = "C:";
                
                foreach (var line in psOut.Split('\n'))
                {
                    if (line.StartsWith("RAM_MB="))     long.TryParse(line.Replace("RAM_MB=", "").Trim(), out ramMb);
                    if (line.StartsWith("FREE_MB="))    long.TryParse(line.Replace("FREE_MB=", "").Trim(), out freeMb);
                    if (line.StartsWith("SYS_DRIVE="))  sysDrive = line.Replace("SYS_DRIVE=", "").Trim();
                }
                
                _logger.LogInfo($"[GameRepair][FixPagefile] RAM={ramMb}MB, EspaçoLivre={freeMb}MB, Drive={sysDrive}");

                long idealMb   = (long)(ramMb * 1.5);
                long maxMb     = Math.Min((long)(ramMb * 4), 16 * 1024);
                long diskCapMb = (long)(freeMb * 0.15);
                long targetMb  = Math.Max(1024, Math.Min(idealMb, diskCapMb));
                targetMb = Math.Min(targetMb, maxMb);
                
                _logger.LogInfo($"[GameRepair][FixPagefile] Ideal={idealMb}MB, DiskCap={diskCapMb}MB, Target={targetMb}MB");

                if (freeMb < 2048 && issueId == "PAGEFILE_SMALL")
                {
                    _logger.LogWarning($"[GameRepair][FixPagefile] Espaço insuficiente ({freeMb}MB livres) - pagefile mantido");
                    return new GameRepairFixResult
                    {
                        IssueId = issueId, Success = false,
                        Message = $"Espaço insuficiente no disco ({freeMb}MB livres). Libere espaço e tente novamente."
                    };
                }

                // Define pagefile com tamanho fixo calculado (inicial = target, máx = target)
                // Desativa gerenciamento automático para ter controle preciso do tamanho
                var setScript = $@"
$cs = Get-CimInstance -ClassName Win32_ComputerSystem
$cs.AutomaticManagedPagefile = $false
$cs.Put() | Out-Null
$existing = Get-CimInstance -ClassName Win32_PageFileSetting -ErrorAction SilentlyContinue
if ($existing) {{
    $existing | Remove-CimInstance -ErrorAction SilentlyContinue
}}
$pfs = New-CimInstance -ClassName Win32_PageFileSetting -Property @{{Name='{sysDrive}\\pagefile.sys'; InitialSize={targetMb}; MaximumSize={targetMb}}}
Write-Output 'PAGEFILE_SET=OK'
";
                var (_, setOut) = await RunPsAsync(setScript, ct, timeoutMs: 30_000);
                bool ok = setOut.Contains("PAGEFILE_SET=OK");
                
                _logger.LogInfo($"[GameRepair][FixPagefile] Resultado: {(ok ? $"OK – {targetMb}MB definido" : setOut)}");
                
                string msg = ok
                    ? $"Pagefile configurado: {targetMb}MB em {sysDrive} (RAM={ramMb}MB, livre={freeMb}MB) – reinicialize para aplicar"
                    : $"Falha ao configurar pagefile";
                    
                return new GameRepairFixResult { IssueId = issueId, Success = ok, Message = msg };
            }
            catch (Exception ex)
            {
                _logger.LogError($"[GameRepair][FixPagefile] Erro: {ex.Message}");
                return new GameRepairFixResult { IssueId = issueId, Success = false, Message = ex.Message };
            }
        }

        // pctStart e pctEnd definem o intervalo de progresso a ser reportado para esta operação
        private async Task<bool> DownloadAndInstallAsync(string url, string installArgs, string label,
            IProgress<(int pct, string msg)>? progress, int pctStart, int pctEnd, CancellationToken ct, string? wingetId = null)
        {
            var globalProgress = ProgressBridge.WrapExisting<(int, string)>("Baixando...", progress);
            progress = globalProgress;
            var tmpExe = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"voltris_fix_{Guid.NewGuid():N}.exe");
            int downloadEnd = pctStart + (int)((pctEnd - pctStart) * 0.6); // 60% do range = download
            int installEnd  = pctEnd; // 40% restante = instalação

            _logger.LogInfo($"[GameRepair][Download] Iniciando download de {label}");
            _logger.LogInfo($"[GameRepair][Download] URL: {url}");
            
            try
            {
                Log(string.Format(LocalizationService.Instance.GetString("GameRepairLog_BaixandoItem"), label), "#888888");
                using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(5) };
                http.DefaultRequestHeaders.Add("User-Agent", "VoltrisOptimizer/1.0");
                
                using var response = await http.GetAsync(url, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();
                
                using (var fs = new System.IO.FileStream(tmpExe, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None))
                {
                    await response.Content.CopyToAsync(fs, ct);
                    await fs.FlushAsync(ct);
                }

                var fileInfo = new System.IO.FileInfo(tmpExe);
                if (fileInfo.Length < 1024)
                {
                    _logger.LogError($"[GameRepair][Download] ERRO: Arquivo muito pequeno ({fileInfo.Length} bytes)");
                    return false;
                }

                progress?.Report((downloadEnd + 5, $"Instalando {label}..."));
                Log(string.Format(LocalizationService.Instance.GetString("GameRepairLog_InstalandoItem"), label), "#888888");

                // Instala com useShell: true para evitar bloqueios e garantir execução correta de wrappers
                var (code, output) = await RunAsync(tmpExe, installArgs, ct, timeoutMs: 120_000, useShell: true);
                
                _logger.LogInfo($"[GameRepair][Install] {label} exit code: {code}");

                // Caso retorne 1612 (Source Absent), tentamos via WINGET como fallback profissional
                if (code == 1612 && !string.IsNullOrEmpty(wingetId))
                {
                    _logger.LogWarning($"[GameRepair][Install] {label} falhou com 1612. Tentando fallback via WINGET: {wingetId}");
                    Log(LocalizationService.Instance.GetString("GameRepairLog_Falha1612"), "#FFA500");
                    progress?.Report((downloadEnd + 10, $"Usando Winget para {label}..."));
                    
                    var wingetArgs = $"install --id {wingetId} --silent --accept-package-agreements --accept-source-agreements";
                    var (wCode, wOut) = await RunAsync("winget.exe", wingetArgs, ct, timeoutMs: 300_000, useShell: true);
                    
                    _logger.LogInfo($"[GameRepair][Winget] {wingetId} exit code: {wCode}");
                    if (wCode == 0 || wCode == 3010) return true;
                    
                    // Se falhou mesmo assim, registramos no log mas procedemos com o erro original
                }
                
                bool success = code == 0 || code == 3010 || code == 1638;
                
                if (success)
                {
                    _logger.LogInfo($"[GameRepair][Install] SUCESSO: {label} (exit code {code})");
                    progress?.Report((installEnd, $"✓ {label} instalado (code {code})"));
                }
                else
                {
                    _logger.LogError($"[GameRepair][Install] FALHA: {label} retornou exit code {code}");
                    progress?.Report((installEnd, $"✗ {label} falhou (code {code})"));
                }
                
                return success;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) 
            { 
                _logger.LogError($"[GameRepair][Download] EXCEÇÃO em {label}: {ex.Message}");
                return false; 
            }
            finally 
            { 
                try { if (System.IO.File.Exists(tmpExe)) System.IO.File.Delete(tmpExe); } catch { } 
            }
        }
    }
}
