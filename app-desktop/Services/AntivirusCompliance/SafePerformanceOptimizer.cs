using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Core;
using VoltrisOptimizer.Services.Optimization.Onboarding;
using PerformanceOptimizer = VoltrisOptimizer.Services.VoltrisPerformanceOptimizer;
using SysIODriveInfo = System.IO.DriveInfo;

namespace VoltrisOptimizer.Services.AntivirusCompliance
{
    /// <summary>
    /// PerformanceOptimizer compatível com antivírus
    /// Versão segura que não causa falso positivo para Windows Defender
    /// </summary>
    public class SafePerformanceOptimizer : IPerformanceOptimizer
    {
        private readonly ILoggingService _logger;
        private readonly SafeOperationWhitelist _whitelist;
        
        public SafePerformanceOptimizer(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _whitelist = new SafeOperationWhitelist(logger);
        }

        public async Task<OperationResult> OptimizeRAMAsync(Action<int>? progressCallback = null)
        {
            try
            {
                _logger.LogInfo("[SAFE_PERFORMANCE] Iniciando otimização de RAM segura...");
                progressCallback?.Invoke(10);

                // Abordagem segura: apenas limpar nossa própria aplicação
                var processesToOptimize = new List<Process>();
                var currentProcess = Process.GetCurrentProcess();
                processesToOptimize.Add(currentProcess);

                // Adicionar apenas processos conhecidos e seguros
                var safeProcessNames = new[] { "chrome", "firefox", "msedge", "code", "notepad++" };
                
                foreach (var procName in safeProcessNames)
                {
                    try
                    {
                        var processes = Process.GetProcessesByName(procName);
                        processesToOptimize.AddRange(processes);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"[SAFE_PERFORMANCE] Erro ao obter processos {procName}: {ex.Message}");
                    }
                }

                int successCount = 0;
                int total = processesToOptimize.Count;

                for (int i = 0; i < total; i++)
                {
                    var process = processesToOptimize[i];
                    try
                    {
                        // Validar com whitelist antes de otimizar
                        if (_whitelist.IsSafeMemoryOperation("EmptyWorkingSet_Optimization", process.ProcessName))
                        {
                            // Apenas reduzir working set (sem EmptyWorkingSet direto)
                            process.MinWorkingSet = (int)(process.WorkingSet64 / 2);
                            successCount++;
                            
                            _logger.LogInfo($"[SAFE_PERFORMANCE] WorkingSet reduzido para {process.ProcessName}");
                        }
                        else
                        {
                            _logger.LogWarning($"[SAFE_PERFORMANCE] Processo não otimizável: {process.ProcessName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"[SAFE_PERFORMANCE] Erro ao otimizar {process.ProcessName}: {ex.Message}");
                    }
                    
                    progressCallback?.Invoke(10 + (int)((i / (float)total) * 80));
                }

                // Limpar recursos
                foreach (var process in processesToOptimize)
                {
                    try { process.Dispose(); } catch { }
                }

                progressCallback?.Invoke(90);
                
                // Forçar GC apenas na nossa aplicação

                _logger.LogSuccess($"[SAFE_PERFORMANCE] RAM otimizada com segurança: {successCount} processos");
                progressCallback?.Invoke(100);
                
                return OperationResult.CreateSuccess($"RAM otimizada com segurança: {successCount} processos", true);
            }
            catch (Exception ex)
            {
                _logger.LogError("[SAFE_PERFORMANCE] Erro na otimização de RAM", ex);
                return OperationResult.CreateFailure("Erro na otimização de RAM segura", ex.Message);
            }
        }

        public async Task<OperationResult> OptimizeServicesAsync(Action<int>? progressCallback = null)
        {
            try
            {
                _logger.LogInfo("[SAFE_PERFORMANCE] Iniciando otimização de serviços segura...");
                progressCallback?.Invoke(10);

                // P1: SysMain (Superfetch) APENAS em SSD/NVMe
                var safeServices = new List<(string Name, string Description, string StartupType)>();

                bool isSystemDriveSSD = IsSystemDriveSSD();
                _logger.LogInfo($"[SAFE_PERFORMANCE] Unidade de sistema SSD: {isSystemDriveSSD}");

                if (isSystemDriveSSD)
                {
                    safeServices.Add(("SysMain", "Superfetch (desativado em SSD para evitar leaks de RAM)", "disabled"));
                    _logger.LogInfo("[SAFE_PERFORMANCE] SSD detectado — SysMain será DESATIVADO (evita leaks 50GB+ RAM)");
                }
                else
                {
                    safeServices.Add(("SysMain", "Superfetch (mantido em HDD para performance)", "demand"));
                    _logger.LogInfo("[SAFE_PERFORMANCE] HDD detectado — SysMain mantido em DEMAND");
                }

                // WSearch (Windows Search) — opcional, só em SSD com RAM >= 8GB
                var cache = Core.SystemMetricsCache.Instance;
                double totalRamGb = 0;
                try
                {
                    var hw = new HardwareProfileAnalyzer(_logger);
                    var hwProfile = await hw.AnalyzeAsync();
                    totalRamGb = hwProfile.TotalRamGb;
                }
                catch { }

                if (isSystemDriveSSD && totalRamGb >= 8)
                {
                    safeServices.Add(("WSearch", "Windows Search (indexação desativada em SSD+RAM>=8GB)", "disabled"));
                }
                else
                {
                    safeServices.Add(("WSearch", "Windows Search (indexação)", "demand"));
                }

                var results = new List<OperationResult>();
                int current = 0;

                foreach (var (Name, Description, StartupType) in safeServices)
                {
                    try
                    {
                        _logger.LogInfo($"[SAFE_PERFORMANCE] Validando serviço {Name}...");
                        
                        // Validar com whitelist antes de modificar
                        var validation = await _whitelist.ValidateCommandAsync("sc.exe", $"config {Name} start= {StartupType}");
                        
                        if (!validation.IsSafe)
                        {
                            _logger.LogWarning($"[SAFE_PERFORMANCE] Serviço não aprovado: {validation.Reason}");
                            results.Add(OperationResult.CreateFailure($"Serviço não seguro: {Name}", validation.Reason));
                            continue;
                        }

                        // Executar comando validado
                        var processInfo = new ProcessStartInfo
                        {
                            FileName = "sc.exe",
                            Arguments = $"config {Name} start= {StartupType}",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        };

                        using (var process = Process.Start(processInfo))
                        {
                            if (process == null)
                            {
                                results.Add(OperationResult.CreateFailure($"Failed to start sc.exe for {Name}", "Process.Start returned null"));
                                continue;
                            }

                            var output = process.StandardOutput.ReadToEnd();
                            var error = process.StandardError.ReadToEnd();
                            process.WaitForExit();

                            if (process.ExitCode == 0)
                            {
                                _logger.LogSuccess($"[SAFE_PERFORMANCE] Serviço {Name} configurado para {StartupType}");
                                results.Add(OperationResult.CreateSuccess($"Serviço {Name} otimizado", true));
                            }
                            else
                            {
                                _logger.LogWarning($"[SAFE_PERFORMANCE] Falha ao configurar {Name}: {error}");
                                results.Add(OperationResult.CreateFailure($"sc.exe failed for {Name}", error));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"[SAFE_PERFORMANCE] Erro ao configurar serviço {Name}", ex);
                        results.Add(OperationResult.CreateFailure($"Exception configuring {Name}", ex.Message));
                    }
                    
                    current++;
                    progressCallback?.Invoke(10 + (current * 40));
                }

                var combinedResult = OperationResult.Combine(results.ToArray());
                
                if (combinedResult.Success)
                {
                    _logger.LogSuccess("[SAFE_PERFORMANCE] Serviços otimizados com segurança");
                }
                else
                {
                    _logger.LogWarning("[SAFE_PERFORMANCE] Falhas parciais na otimização de serviços");
                }
                
                progressCallback?.Invoke(100);
                return combinedResult;
            }
            catch (Exception ex)
            {
                _logger.LogError("[SAFE_PERFORMANCE] Erro geral na otimização de serviços", ex);
                return OperationResult.CreateFailure("Erro geral na otimização de serviços segura", ex.Message);
            }
        }
        /// <summary>
        /// [FIX:UNICO-DONO-DE-ENERGIA] "Alto desempenho seguro" delega ao Perfil.
        ///
        /// O método original validava `/setactive` na whitelist e rodava
        /// `powercfg /setactive {HighPerformance}`, depois validava que o plano
        /// tinha sido ativado. Tudo certo do ponto de vista do Windows, e
        /// exatamente o oposto do ponto de vista da energia do aplicativo: ele
        /// desligava o Perfil Inteligente sem que o Perfil soubesse.
        ///
        /// A validação pós-ativação tornava isso pior, e não melhor: ela
        /// confirmava que o High Performance ESTAVA ativo — ou seja, confirmava
        /// que o dono da energia tinha sido destituído. Um check que valida o
        /// defeito.
        ///
        /// O nome e a intenção do método continuam. Quem executa mudou.
        /// </summary>
        public async Task<OperationResult> SetHighPerformancePlanAsync(Action<int>? progressCallback = null)
        {
            try
            {
                _logger.LogInfo("[SAFE_PERFORMANCE] Alto desempenho: delegando ao Perfil Inteligente...");
                progressCallback?.Invoke(10);

                bool ok = VoltrisOptimizer.Services.Power.ProfilePowerAuthority.RequestProfileApply(
                    "SafePerformanceOptimizer.SetHighPerformance",
                    "usuario pediu alto desempenho seguro",
                    _logger);

                progressCallback?.Invoke(100);

                if (!ok)
                {
                    return OperationResult.CreateFailure(
                        "Falha", "O Perfil Inteligente nao reaplicou (sem admin?)");
                }

                _logger.LogSuccess("[SAFE_PERFORMANCE] Perfil Inteligente reaplicado.");
                return OperationResult.CreateSuccess("Perfil Inteligente reaplicado", true);
            }
            catch (Exception ex)
            {
                var failureResult = OperationResult.CreateFailure("Exception during power plan activation", ex.Message);
                _logger.LogError("[SAFE_PERFORMANCE] Erro ao pedir reaplicacao do perfil", ex);
                progressCallback?.Invoke(100);
                return failureResult;
            }
        }

        /// <summary>
        /// [FIX:UNICO-DONO-DE-ENERGIA] "Balanceado seguro" delega ao Perfil.
        ///
        /// Este é o caso mais incoerente de todo o serviço. O Perfil Inteligente
        /// é dono do plano "Voltris - Equilibrado" — é o plano que ele cria e
        /// mantém. Este método rodava `powercfg /setactive {381b4222…}`, o
        /// Balanced NATIVO do Windows, achando que estava "voltando ao
        /// equilibrado".
        ///
        /// O efeito real era desligar o dono do estado equilibrado e deixar o
        /// Windows no plano dele, que e' exatamente o sintoma que o usuario
        /// reportou: o plano do Voltris alternando com o plano do Windows, e a
        /// maquina indo e voltando de desempenho.
        /// </summary>
        public async Task<OperationResult> SetBalancedPlanAsync(Action<int>? progressCallback = null)
        {
            try
            {
                _logger.LogInfo("[SAFE_PERFORMANCE] Modo equilibrado: delegando ao Perfil Inteligente...");
                progressCallback?.Invoke(10);

                bool ok = VoltrisOptimizer.Services.Power.ProfilePowerAuthority.RequestProfileApply(
                    "SafePerformanceOptimizer.SetBalanced",
                    "usuario pediu modo equilibrado seguro",
                    _logger);

                progressCallback?.Invoke(100);

                if (!ok)
                {
                    return OperationResult.CreateFailure(
                        "Falha", "O Perfil Inteligente nao reaplicou (sem admin?)");
                }

                _logger.LogSuccess("[SAFE_PERFORMANCE] Perfil Inteligente reaplicado.");
                return OperationResult.CreateSuccess("Perfil Inteligente reaplicado", true);
            }
            catch (Exception ex)
            {
                var failureResult = OperationResult.CreateFailure("Exception during balanced plan activation", ex.Message);
                _logger.LogError("[SAFE_PERFORMANCE] Erro ao pedir reaplicacao do perfil", ex);
                progressCallback?.Invoke(100);
                return failureResult;
            }
        }

        public async Task<OperationResult> OptimizeStartupAsync(Action<int>? progressCallback = null)
        {
            try
            {
                _logger.LogInfo("[SAFE_PERFORMANCE] Otimizando inicialização segura...");
                progressCallback?.Invoke(10);

                string keyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize";
                string valueName = "StartupDelayInMSec";
                int expectedValue = 0;

                progressCallback?.Invoke(30);

                using (var key = Registry.CurrentUser.CreateSubKey(keyPath, true))
                {
                    if (key == null)
                    {
                        var failureResult = OperationResult.CreateFailure("Failed to open registry key", $"CreateSubKey returned null for path: {keyPath}");
                        _logger.LogError($"[SAFE_PERFORMANCE] {failureResult.GetFullMessage()}");
                        progressCallback?.Invoke(100);
                        return failureResult;
                    }

                    // Validar operação segura
                    if (!_whitelist.IsSafeMemoryOperation("Registry_Optimization", "Explorer"))
                    {
                        var failureResult = OperationResult.CreateFailure("Registry operation blocked by safety", "Explorer registry modification not approved");
                        _logger.LogWarning($"[SAFE_PERFORMANCE] {failureResult.GetFullMessage()}");
                        progressCallback?.Invoke(100);
                        return failureResult;
                    }

                    key.SetValue(valueName, expectedValue, RegistryValueKind.DWord);
                }

                progressCallback?.Invoke(60);

                // Validação pós-aplicação
                _logger.LogInfo("[SAFE_PERFORMANCE] Validando valor no registro...");
                var validationResult = Core.Validation.OptimizationValidators.ValidateRegistryValue(
                    Registry.CurrentUser,
                    keyPath,
                    valueName,
                    expectedValue,
                    RegistryValueKind.DWord);

                if (!validationResult.Success)
                {
                    _logger.LogError($"[SAFE_PERFORMANCE] VALIDATION FAILED: {validationResult.GetFullMessage()}");
                    progressCallback?.Invoke(100);
                    return validationResult;
                }

                _logger.LogSuccess($"[SAFE_PERFORMANCE] StartupDelay reduzido para 0ms e validado com segurança");
                progressCallback?.Invoke(100);
                return validationResult;
            }
            catch (UnauthorizedAccessException ex)
            {
                var failureResult = OperationResult.CreateFailure("Access denied to registry", $"Insufficient permissions: {ex.Message}");
                _logger.LogError($"[SAFE_PERFORMANCE] {failureResult.GetFullMessage()}", ex);
                progressCallback?.Invoke(100);
                return failureResult;
            }
            catch (Exception ex)
            {
                var failureResult = OperationResult.CreateFailure("Exception during startup optimization", ex.Message);
                _logger.LogError("[SAFE_PERFORMANCE] Erro na otimização de inicialização", ex);
                progressCallback?.Invoke(100);
                return failureResult;
            }
        }

        // Métodos auxiliares privados
        private void RecordHistory(string action, string description, bool success)
        {
            try
            {
                var entry = new OptimizationHistory
                {
                    ActionType = "Performance",
                    Description = description,
                    Timestamp = DateTime.Now,
                    Success = success,
                    Details = new Dictionary<string, object> { { "Action", action }, { "SafeMode", true } }
                };
                HistoryService.Instance.AddHistoryEntry(entry);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Falha ao gravar histórico: {ex.Message}");
            }
        }

        // Métodos estáticos de compatibilidade
        public static bool HasHighPerformancePlan()
        {
            // return PerformanceOptimizer.HasHighPerformancePlan();
            return false; // TODO: Implementar HasHighPerformancePlan
        }

        // P1: Detecção simples de SSD na unidade de sistema
        private static bool IsSystemDriveSSD()
        {
            try
            {
                string systemDrive = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
                var drive = new SysIODriveInfo(systemDrive);
                if (!drive.IsReady) return false;

                long sizeGb = drive.TotalSize / (1024 * 1024 * 1024);

                // Heurística conservadora baseada apenas no tamanho:
                // - Drives <= 512GB em qualquer sistema = provável SSD
                // - Drives > 2TB = provável HDD mecânico
                // - Entre 512GB-2TB = assume SSD por segurança (SysMain disabled é seguro)
                if (sizeGb <= 512) return true;
                if (sizeGb > 2048) return false;
                return true;
            }
            catch { return false; }
        }
    }
}
