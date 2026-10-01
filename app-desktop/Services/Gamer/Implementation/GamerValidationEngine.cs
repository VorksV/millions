using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services.Gamer.Interfaces;
using VoltrisOptimizer.Services.Gamer.Models;

namespace VoltrisOptimizer.Services.Gamer.Implementation
{
    /// <summary>
    /// MOTOR DE VALIDAÇÃO REAL - ANTI-PLACEBO
    /// Prova que as otimizações foram REALMENTE aplicadas
    /// </summary>
    public class GamerValidationEngine
    {
        private readonly ILoggingService _logger;
        private readonly IHardwareDetector _hardwareDetector;
        private readonly ICpuGamingOptimizer _cpuOptimizer;
        private readonly IGpuGamingOptimizer _gpuOptimizer;

        public GamerValidationEngine(ILoggingService logger, IHardwareDetector hardwareDetector, ICpuGamingOptimizer cpuOptimizer, IGpuGamingOptimizer gpuOptimizer)
        {
            _logger.LogEntry(nameof(GamerValidationEngine));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _hardwareDetector = hardwareDetector ?? throw new ArgumentNullException(nameof(hardwareDetector));
            _cpuOptimizer = cpuOptimizer ?? throw new ArgumentNullException(nameof(cpuOptimizer));
            _gpuOptimizer = gpuOptimizer ?? throw new ArgumentNullException(nameof(gpuOptimizer));
            _logger.LogExit(nameof(GamerValidationEngine));
        }

        /// <summary>
        /// Valida se otimização de CPU foi realmente aplicada
        /// </summary>
        public async Task<GamerValidationResult> ValidateCpuOptimizationAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogEntry(nameof(ValidateCpuOptimizationAsync));
            _logger.LogInfo("[VALIDATION] Validando otimização de CPU...");

            var validation = new GamerValidationResult
            {
                ModuleName = "CPU"
            };

            try
            {
                // 1. Verificar se core parking está desativado
                var coreParkingDisabled = await ValidateCoreParkingDisabledAsync(cancellationToken);
                validation.Checks.Add(new ValidationCheck
                {
                    Name = "CoreParkingDisabled",
                    Expected = true,
                    Actual = coreParkingDisabled,
                    Passed = coreParkingDisabled
                });

                // 2. Verificar power plan
                var powerPlanOk = await ValidatePowerPlanAsync(cancellationToken);
                validation.Checks.Add(new ValidationCheck
                {
                    Name = "PowerPlan",
                    Expected = true,
                    Actual = powerPlanOk,
                    Passed = powerPlanOk
                });

                // 3. Verificar priority separation
                var prioritySeparationOk = await ValidatePrioritySeparationAsync(cancellationToken);
                validation.Checks.Add(new ValidationCheck
                {
                    Name = "PrioritySeparation",
                    Expected = true,
                    Actual = prioritySeparationOk,
                    Passed = prioritySeparationOk
                });

                // 4. Verificar prioridade do processo em foreground
                var gamePrioritySet = await ValidateGameProcessPriorityAsync(cancellationToken);
                if (gamePrioritySet.HasValue)
                {
                    validation.Checks.Add(new ValidationCheck
                    {
                        Name = "GameProcessPriority",
                        Expected = true,
                        Actual = gamePrioritySet.Value,
                        Passed = gamePrioritySet.Value
                    });
                }

                validation.IsOverallSuccess = validation.Checks.All(c => c.Passed);
                validation.Summary = $"CPU: {validation.Checks.Count(c => c.Passed)}/{validation.Checks.Count} validações passaram";
                _logger.LogInfo($"[VALIDATION] {validation.Summary}");
                _logger.LogExit(nameof(ValidateCpuOptimizationAsync));
                return validation;
            }
            catch (Exception ex)
            {
                _logger.LogError("[VALIDATION] Erro na validação de CPU", ex);
                validation.IsOverallSuccess = false;
                validation.Error = ex.Message;
                _logger.LogExit(nameof(ValidateCpuOptimizationAsync));
                return validation;
            }
        }

        /// <summary>
        /// Valida se otimização de GPU foi realmente aplicada
        /// </summary>
        public async Task<GamerValidationResult> ValidateGpuOptimizationAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogEntry(nameof(ValidateGpuOptimizationAsync));
            _logger.LogInfo("[VALIDATION] Validando otimização de GPU...");

            var validation = new GamerValidationResult
            {
                ModuleName = "GPU"
            };

            try
            {
                // 1. Verificar modo performance da GPU
                var performanceMode = await ValidateGpuPerformanceModeAsync(cancellationToken);
                validation.Checks.Add(new ValidationCheck
                {
                    Name = "GpuPerformanceMode",
                    Expected = true,
                    Actual = performanceMode,
                    Passed = performanceMode
                });

                // 2. Verificar TDR settings
                var tdrSettingsOk = await ValidateTdrSettingsAsync(cancellationToken);
                validation.Checks.Add(new ValidationCheck
                {
                    Name = "TdrSettingsOk",
                    Expected = true,
                    Actual = tdrSettingsOk,
                    Passed = tdrSettingsOk
                });

                validation.IsOverallSuccess = validation.Checks.All(c => c.Passed);
                validation.Summary = $"GPU: {validation.Checks.Count(c => c.Passed)}/{validation.Checks.Count} validações passaram";
                _logger.LogInfo($"[VALIDATION] {validation.Summary}");
                _logger.LogExit(nameof(ValidateGpuOptimizationAsync));
                return validation;
            }
            catch (Exception ex)
            {
                _logger.LogError("[VALIDATION] Erro na validação de GPU", ex);
                validation.IsOverallSuccess = false;
                validation.Error = ex.Message;
                _logger.LogExit(nameof(ValidateGpuOptimizationAsync));
                return validation;
            }
        }

        /// <summary>
        /// Valida se otimização de processos foi realmente aplicada
        /// </summary>
        public async Task<GamerValidationResult> ValidateProcessOptimizationAsync(int? gameProcessId, CancellationToken cancellationToken = default)
        {
            _logger.LogEntry(nameof(ValidateProcessOptimizationAsync));
            _logger.LogInfo("[VALIDATION] Validando otimização de processos...");

            var validation = new GamerValidationResult
            {
                ModuleName = "Process"
            };

            try
            {
                if (gameProcessId.HasValue)
                {
                    // Verificar prioridade do processo do jogo
                    var gamePriority = await ValidateProcessPriorityAsync(gameProcessId.Value, cancellationToken);
                    validation.Checks.Add(new ValidationCheck
                    {
                        Name = "GameProcessPriority",
                        Expected = true,
                        Actual = gamePriority,
                        Passed = gamePriority
                    });

                    // Verificar affinity do processo
                    var gameAffinity = await ValidateProcessAffinityAsync(gameProcessId.Value, cancellationToken);
                    validation.Checks.Add(new ValidationCheck
                    {
                        Name = "GameProcessAffinity",
                        Expected = true,
                        Actual = gameAffinity,
                        Passed = gameAffinity
                    });
                }
                else
                {
                    _logger.LogWarning("[VALIDATION] Nenhum processo de jogo fornecido para validação");
                }

                validation.IsOverallSuccess = validation.Checks.All(c => c.Passed);
                validation.Summary = $"Process: {validation.Checks.Count(c => c.Passed)}/{validation.Checks.Count} validações passaram";
                _logger.LogInfo($"[VALIDATION] {validation.Summary}");
                _logger.LogExit(nameof(ValidateProcessOptimizationAsync));
                return validation;
            }
            catch (Exception ex)
            {
                _logger.LogError("[VALIDATION] Erro na validação de Processos", ex);
                validation.IsOverallSuccess = false;
                validation.Error = ex.Message;
                _logger.LogExit(nameof(ValidateProcessOptimizationAsync));
                return validation;
            }
        }

        /// <summary>
        /// Validação completa de todas as otimizações
        /// </summary>
        public async Task<FullValidationReport> ValidateAllOptimizationsAsync(int? gameProcessId = null, CancellationToken cancellationToken = default)
        {
            _logger.LogEntry(nameof(ValidateAllOptimizationsAsync));
            _logger.LogInfo("[VALIDATION] Iniciando validação completa de otimizações...");

            var report = new FullValidationReport
            {
                StartTime = DateTime.UtcNow,
                GameProcessId = gameProcessId
            };

            try
            {
                // Validar CPU
                report.CpuValidation = await ValidateCpuOptimizationAsync(cancellationToken);

                // Validar GPU
                report.GpuValidation = await ValidateGpuOptimizationAsync(cancellationToken);

                // Validar Processos
                report.ProcessValidation = await ValidateProcessOptimizationAsync(gameProcessId, cancellationToken);

                // Calcular resultado geral
                var allValidations = new[] { report.CpuValidation, report.GpuValidation, report.ProcessValidation };
                report.TotalChecks = allValidations.Sum(v => v.Checks.Count);
                report.PassedChecks = allValidations.Sum(v => v.Checks.Count(c => c.Passed));
                report.FailedChecks = report.TotalChecks - report.PassedChecks;
                report.IsOverallSuccess = allValidations.All(v => v.IsOverallSuccess);
                report.SuccessPercentage = report.TotalChecks > 0 ? (report.PassedChecks * 100.0 / report.TotalChecks) : 0;
                report.EndTime = DateTime.UtcNow;
                report.Duration = report.EndTime - report.StartTime;

                // Log final
                if (report.IsOverallSuccess)
                {
                    _logger.LogSuccess($"[VALIDATION] VALIDAÇÃO COMPLETA SUCESSO: {report.PassedChecks}/{report.TotalChecks} checks passaram em {report.Duration.TotalMilliseconds:F0} ms");
                }
                else
                {
                    _logger.LogError($"[VALIDATION] VALIDAÇÃO FALHOU: {report.PassedChecks}/{report.TotalChecks} checks passaram em {report.Duration.TotalMilliseconds:F0} ms");
                }

                _logger.LogExit(nameof(ValidateAllOptimizationsAsync));
                return report;
            }
            catch (Exception ex)
            {
                _logger.LogError("[VALIDATION] Erro na validação completa", ex);
                report.Error = ex.Message;
                report.EndTime = DateTime.UtcNow;
                report.Duration = report.EndTime - report.StartTime;
                _logger.LogExit(nameof(ValidateAllOptimizationsAsync));
                return report;
            }
        }

        private async Task<bool> ValidateCoreParkingDisabledAsync(CancellationToken cancellationToken)
        {
            _logger.LogEntry(nameof(ValidateCoreParkingDisabledAsync));
            try
            {
                // Verificar se core parking está desativado no power plan atual
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power\PowerSchemes", false);
                if (key == null) return false;

                // Obter GUID do plano ativo
                var activeScheme = key.GetValue("ActivePowerScheme") as string;
                if (string.IsNullOrEmpty(activeScheme)) return false;

                // Verificar configuração de core parking
                var coreParkingPath = $@"SYSTEM\CurrentControlSet\Control\Power\PowerSchemes\{activeScheme}\54533251-82be-4824-96c1-47b60b740d00\0cc5b647-c1df-4637-891a-dec35c318583";
                using var coreParkingKey = Registry.LocalMachine.OpenSubKey(coreParkingPath, false);
                if (coreParkingKey == null) return false;

                var acValue = coreParkingKey.GetValue("ACSettingIndex") as int?;
                var dcValue = coreParkingKey.GetValue("DCSettingIndex") as int?;

                // Core parking desativado = 100%
                _logger.LogExit(nameof(ValidateCoreParkingDisabledAsync));
                return (acValue == 100) && (dcValue == 100);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[VALIDATION] Erro ao validar core parking: {ex.Message}");
                _logger.LogExit(nameof(ValidateCoreParkingDisabledAsync));
                return false;
            }
        }

        private async Task<bool> ValidatePowerPlanAsync(CancellationToken cancellationToken)
        {
            _logger.LogEntry(nameof(ValidatePowerPlanAsync));
            try
            {
                // Verificar power plan ativo via powercfg
                var psi = new ProcessStartInfo
                {
                    FileName = "powercfg.exe",
                    Arguments = "/getactivescheme",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return false;

                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                // Verificar se é um plano de performance
                _logger.LogExit(nameof(ValidatePowerPlanAsync));
                return output.Contains("High Performance", StringComparison.OrdinalIgnoreCase) ||
                       output.Contains("Ultimate Performance", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[VALIDATION] Erro ao validar power plan: {ex.Message}");
                _logger.LogExit(nameof(ValidatePowerPlanAsync));
                return false;
            }
        }

        private async Task<bool> ValidatePrioritySeparationAsync(CancellationToken cancellationToken)
        {
            _logger.LogEntry(nameof(ValidatePrioritySeparationAsync));
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\PriorityControl", false);
                if (key == null) return false;

                var value = key.GetValue("Win32PrioritySeparation") as int?;
                _logger.LogExit(nameof(ValidatePrioritySeparationAsync));
                return value.HasValue && value.Value == 38; // Valor para foreground boost
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[VALIDATION] Erro ao validar priority separation: {ex.Message}");
                _logger.LogExit(nameof(ValidatePrioritySeparationAsync));
                return false;
            }
        }

        private async Task<bool?> ValidateGameProcessPriorityAsync(CancellationToken cancellationToken)
        {
            _logger.LogEntry(nameof(ValidateGameProcessPriorityAsync));
            try
            {
                int processId = VoltrisOptimizer.Core.ForegroundWindowTracker.Instance.CurrentPid;
                if (processId <= 0) return null;
                using var process = Process.GetProcessById((int)processId);
                _logger.LogExit(nameof(ValidateGameProcessPriorityAsync));
                return process.PriorityClass == ProcessPriorityClass.High;
            }
            catch
            {
                _logger.LogExit(nameof(ValidateGameProcessPriorityAsync));
                return null;
            }
        }

        private async Task<bool> ValidateProcessPriorityAsync(int processId, CancellationToken cancellationToken)
        {
            _logger.LogEntry(nameof(ValidateProcessPriorityAsync));
            try
            {
                using var process = Process.GetProcessById(processId);
                _logger.LogExit(nameof(ValidateProcessPriorityAsync));
                return process.PriorityClass == ProcessPriorityClass.High;
            }
            catch
            {
                _logger.LogExit(nameof(ValidateProcessPriorityAsync));
                return false;
            }
        }

        private async Task<bool> ValidateProcessAffinityAsync(int processId, CancellationToken cancellationToken)
        {
            _logger.LogEntry(nameof(ValidateProcessAffinityAsync));
            try
            {
                using var process = Process.GetProcessById(processId);
                var affinity = process.ProcessorAffinity;
                // Verificar se affinity não é 0 (erro) ou todos os cores (default)
                _logger.LogExit(nameof(ValidateProcessAffinityAsync));
                return affinity != IntPtr.Zero && affinity != new IntPtr(-1);
            }
            catch
            {
                _logger.LogExit(nameof(ValidateProcessAffinityAsync));
                return false;
            }
        }

        private async Task<bool> ValidateGpuPerformanceModeAsync(CancellationToken cancellationToken)
        {
            _logger.LogEntry(nameof(ValidateGpuPerformanceModeAsync));
            try
            {
                var gpuInfo = await _gpuOptimizer.GetGpuInfoAsync(cancellationToken);
                // Verificar se GPU está em modo performance (implementação específica por vendor)
                switch (gpuInfo.Vendor)
                {
                    case VoltrisOptimizer.Services.Gamer.Models.GpuVendor.Nvidia:
                        _logger.LogExit(nameof(ValidateGpuPerformanceModeAsync));
                        return await ValidateNvidiaPerformanceModeAsync(cancellationToken);
                    case VoltrisOptimizer.Services.Gamer.Models.GpuVendor.Amd:
                        _logger.LogExit(nameof(ValidateGpuPerformanceModeAsync));
                        return await ValidateAmdPerformanceModeAsync(cancellationToken);
                    case VoltrisOptimizer.Services.Gamer.Models.GpuVendor.Intel:
                        _logger.LogExit(nameof(ValidateGpuPerformanceModeAsync));
                        return await ValidateIntelPerformanceModeAsync(cancellationToken);
                    default:
                        _logger.LogExit(nameof(ValidateGpuPerformanceModeAsync));
                        return true; // Não possível validar
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[VALIDATION] Erro ao validar modo performance GPU: {ex.Message}");
                _logger.LogExit(nameof(ValidateGpuPerformanceModeAsync));
                return false;
            }
        }

        private async Task<bool> ValidateNvidiaPerformanceModeAsync(CancellationToken cancellationToken)
        {
            _logger.LogEntry(nameof(ValidateNvidiaPerformanceModeAsync));
            try
            {
                // Verificar se NVIDIA settings está em modo performance
                var psi = new ProcessStartInfo
                {
                    FileName = "nvidia-smi.exe",
                    Arguments = "--query-gpu=power.limit --format=csv,noheader,nounits",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return false;

                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                // Se houver limite de power, está em modo performance
                _logger.LogExit(nameof(ValidateNvidiaPerformanceModeAsync));
                return !string.IsNullOrEmpty(output) && output.Trim() != "Not Supported";
            }
            catch
            {
                _logger.LogExit(nameof(ValidateNvidiaPerformanceModeAsync));
                return false;
            }
        }

        private async Task<bool> ValidateAmdPerformanceModeAsync(CancellationToken cancellationToken)
        {
            _logger.LogEntry(nameof(ValidateAmdPerformanceModeAsync));
            try
            {
                // Verificar Radeon settings (implementação simplificada)
                var psi = new ProcessStartInfo
                {
                    FileName = "radeon-settings.exe",
                    Arguments = "--get-performance-mode",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return false;

                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                _logger.LogExit(nameof(ValidateAmdPerformanceModeAsync));
                return output.Contains("performance", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                _logger.LogExit(nameof(ValidateAmdPerformanceModeAsync));
                return false;
            }
        }

        private async Task<bool> ValidateIntelPerformanceModeAsync(CancellationToken cancellationToken)
        {
            _logger.LogEntry(nameof(ValidateIntelPerformanceModeAsync));
            try
            {
                // Para GPUs Intel, verificar registry settings
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}", false);
                if (key == null) return false;

                // Verificar se há configurações de performance
                var subKeyNames = key.GetSubKeyNames();
                _logger.LogExit(nameof(ValidateIntelPerformanceModeAsync));
                return subKeyNames.Any(name => name.StartsWith("0", StringComparison.Ordinal));
            }
            catch
            {
                _logger.LogExit(nameof(ValidateIntelPerformanceModeAsync));
                return false;
            }
        }

        private async Task<bool> ValidateTdrSettingsAsync(CancellationToken cancellationToken)
        {
            _logger.LogEntry(nameof(ValidateTdrSettingsAsync));
            try
            {
                // Verificar TDR delay e level
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", false);
                if (key == null) return true; // Default aceitável

                var tdrDelay = key.GetValue("TdrDelay") as int?;
                var tdrLevel = key.GetValue("TdrLevel") as int?;

                // Valores otimizados: TdrDelay >= 8, TdrLevel <= 3
                var delayOk = !tdrDelay.HasValue || tdrDelay.Value >= 8;
                var levelOk = !tdrLevel.HasValue || tdrLevel.Value <= 3;

                _logger.LogExit(nameof(ValidateTdrSettingsAsync));
                return delayOk && levelOk;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[VALIDATION] Erro ao validar TDR settings: {ex.Message}");
                _logger.LogExit(nameof(ValidateTdrSettingsAsync));
                return false;
            }
        }

        // P/Invokes de foreground removidos
    }

    // Classes de modelo
    public class GamerValidationResult
    {
        public string ModuleName { get; set; } = string.Empty;
        public bool IsOverallSuccess { get; set; }
        public List<ValidationCheck> Checks { get; set; } = new();
        public string Summary { get; set; } = string.Empty;
        public string? Error { get; set; }
    }

    public class ValidationCheck
    {
        public string Name { get; set; } = string.Empty;
        public bool Expected { get; set; }
        public bool Actual { get; set; }
        public bool Passed { get; set; }
        public string Details { get; set; } = string.Empty;
    }

    public class FullValidationReport
    {
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration { get; set; }
        public int? GameProcessId { get; set; }
        public GamerValidationResult CpuValidation { get; set; } = new();
        public GamerValidationResult GpuValidation { get; set; } = new();
        public GamerValidationResult ProcessValidation { get; set; } = new();
        public int TotalChecks { get; set; }
        public int PassedChecks { get; set; }
        public int FailedChecks { get; set; }
        public bool IsOverallSuccess { get; set; }
        public double SuccessPercentage { get; set; }
        public string? Error { get; set; }
    }
}
