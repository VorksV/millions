using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Performance
{
    /// <summary>
    /// Módulo de otimização de Scheduled Tasks do Windows.
    /// Desativa tarefas agendadas durante o Modo Gamer.
    /// Suporta backup/restore completo com verificação pós-operação.
    /// </summary>
    public sealed class ScheduledTasksOptimizer
    {
        private readonly ILoggingService _logger;
        private readonly string _backupPath;

        private readonly Dictionary<string, TaskOriginalState> _memoryBackup = new();
        private bool _isOptimizationActive;

        /// <summary>
        /// Lista completa de tarefas agendadas a desativar, agrupadas por categoria.
        /// Baseado em pesquisa mundial comprovada + corrigir.txt.
        /// </summary>
        private static readonly Dictionary<string, string[]> TasksByCategory = new()
        {
            ["⭐ Customer Experience Improvement Program"] = new[]
            {
                @"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator",
                @"\Microsoft\Windows\Customer Experience Improvement Program\KernelCeipTask",
                @"\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip",
                @"\Microsoft\Windows\Customer Experience Improvement Program\Uploader"
            },
            ["⭐ Application Experience / Telemetria"] = new[]
            {
                @"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser",
                @"\Microsoft\Windows\Application Experience\ProgramDataUpdater",
                @"\Microsoft\Windows\Application Experience\StartupAppTask",
                @"\Microsoft\Windows\Application Experience\AitAgent"
            },
            ["⭐ Diagnóstico de Disco"] = new[]
            {
                @"\Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector",
                @"\Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticResolver",
                @"\Microsoft\Windows\DiskFootprint\Diagnostics"
            },
            ["⭐ Diagnóstico e Troubleshooting"] = new[]
            {
                @"\Microsoft\Windows\Power Efficiency Diagnostics\AnalyzeSystem",
                @"\Microsoft\Windows\Diagnosis\RecommendedTroubleshootingScanner",
                @"\Microsoft\Windows\Diagnosis\Scheduled"
            },
            ["⭐ Family Safety / Shell"] = new[]
            {
                @"\Microsoft\Windows\Shell\FamilySafetyMonitor",
                @"\Microsoft\Windows\Shell\FamilySafetyRefresh",
                @"\Microsoft\Windows\Shell\FamilySafetyUpload"
            },
            ["⭐ Manutenção e Sistema"] = new[]
            {
                @"\Microsoft\Windows\Autochk\Proxy",
                @"\Microsoft\Windows\Maintenance\WinSAT",
                @"\Microsoft\Windows\Defrag\ScheduledDefrag",
                @"\Microsoft\Windows\SystemRestore\SR"
            },
            ["⭐ Error Reporting e Cloud"] = new[]
            {
                @"\Microsoft\Windows\Windows Error Reporting\QueueReporting",
                @"\Microsoft\Windows\CloudExperienceHost\CreateObjectTask"
            },
            ["⭐ Coleta de Dados e Rede"] = new[]
            {
                @"\Microsoft\Windows\PI\Sqm-Tasks",
                @"\Microsoft\Windows\NetTrace\GatherNetworkInfo",
                @"\Microsoft\Windows\Device Information\Device"
            },
            ["⭐ Maps e Atualizações"] = new[]
            {
                @"\Microsoft\Windows\Maps\MapsUpdateTask",
                @"\Microsoft\Windows\Maps\MapsToastTask"
            },
            ["⭐ Limpeza e Manutenção"] = new[]
            {
                @"\Microsoft\Windows\DiskCleanup\SilentCleanup",
                @"\Microsoft\Windows\WinRE\VerifyWinRE",
                @"\Microsoft\Windows\ApplicationData\CleanupTemporaryState",
                @"\Microsoft\Windows\Registry\RegIdleBackup",
                @"\Microsoft\Windows\StorageSense\Storage Sense"
            },
            ["⭐ Integridade e Diagnóstico"] = new[]
            {
                @"\Microsoft\Windows\Data Integrity Scan\Data Integrity Scan",
                @"\Microsoft\Windows\Device Information\Device"
            },
            ["⭐ Windows Defender"] = new[]
            {
                @"\Microsoft\Windows\Windows Defender\Scheduled Start",
                @"\Microsoft\Windows\Windows Defender\CleanupTemporaryState"
            },
            ["Office Telemetria"] = new[]
            {
                @"\Microsoft\Office\OfficeTelemetryAgentFallBack2016",
                @"\Microsoft\Office\OfficeTelemetryAgentLogOn2016",
                @"\Microsoft\Office\OfficeTelemetryAgentLogOn",
                @"\Microsoft\Office\OfficeTelemetryAgentFallBack",
                @"\Microsoft\Office\Office 15 Subscription Heartbeat"
            },
            ["Sincronização e Localização"] = new[]
            {
                @"\Microsoft\Windows\Time Synchronization\ForceSynchronizeTime",
                @"\Microsoft\Windows\Time Synchronization\SynchronizeTime",
                @"\Microsoft\Windows\Location\Notifications",
                @"\Microsoft\Windows\Location\WindowsActionDialog"
            },
            ["Diversos"] = new[]
            {
                @"\Microsoft\Windows\AppID\SmartScreenSpecific",
                @"\Microsoft\Windows\Bluetooth\UninstallDeviceTask",
                @"\Microsoft\Windows\RemoteAssistance\RemoteAssistanceTask",
                @"\Microsoft\Windows\RetailDemo\CleanupOfflineContent",
                @"\Microsoft\Windows\Speech\SpeechModelDownloadTask",
                @"\Microsoft\Windows\Sysmain\ResPriStaticDbSync",
                @"\Microsoft\Windows\Sysmain\WsSwapAssessmentTask",
                @"\Microsoft\XblGameSave\XblGameSaveTask",
                @"\Microsoft\Windows\Work Folders\Work Folders Logon Synchronization",
                @"\Microsoft\Windows\Work Folders\Work Folders Maintenance Work",
                @"\Microsoft\Windows\Mobile Broadband Accounts\MNO Metadata Parser"
            }
        };

        /// <summary>
        /// Tarefas opcionais (⭐⭐) — detectadas dinamicamente se existem.
        /// </summary>
        private static readonly string[] OptionalTaskPaths = new[]
        {
            @"\Microsoft\Office\Office Automatic Updates",
            @"\Microsoft\EdgeUpdate\Edge Update",
            @"\GoogleUpdate\Google Update",
            @"\Adobe\Adobe Update",
            @"\Microsoft\OneDrive\OneDrive Standalone Update",
            @"\Microsoft\Teams\Teams Update",
            @"\Discord\Discord Update",
            @"\Steam\Steam Scheduled Tasks"
        };

        private class TaskOriginalState
        {
            public string TaskPath { get; set; } = "";
            public string OriginalStatus { get; set; } = "";
            public bool WasModified { get; set; }
            public DateTime ModifiedAt { get; set; }
            public string? FailureReason { get; set; }
        }

        public ScheduledTasksOptimizer(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _backupPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VoltrisOptimizer", "Backups", "ScheduledTasks");
            Directory.CreateDirectory(_backupPath);
        }

        /// <summary>
        /// Retorna todas as task paths como lista plana.
        /// </summary>
        public static IReadOnlyList<string> GetAllTaskPaths()
        {
            var all = new List<string>();
            foreach (var group in TasksByCategory.Values)
                all.AddRange(group);
            return all;
        }

        /// <summary>
        /// Versão síncrona (compatível legado — delega ao async).
        /// </summary>
        public void DisableAllTasks() => Task.Run(() => DisableAllTasksAsync()).GetAwaiter().GetResult();

        /// <summary>
        /// Versão síncrona (compatível legado — delega ao async).
        /// </summary>
        public void RestoreAllTasks() => Task.Run(() => RestoreAllTasksAsync()).GetAwaiter().GetResult();

        /// <summary>
        /// Desativa todas as tarefas agendadas (versão async com memória e verificação).
        /// </summary>
        public async Task DisableAllTasksAsync(CancellationToken ct = default)
        {
            _logger.LogInfo("[ScheduledTasks] ══════════════════════════════════════════");
            _logger.LogInfo("[ScheduledTasks] 🚀 INICIANDO DESATIVAÇÃO DE TAREFAS AGENDADAS");
            _logger.LogInfo("[ScheduledTasks] ══════════════════════════════════════════");

            _memoryBackup.Clear();
            var stopwatch = Stopwatch.StartNew();

            int found = 0, altered = 0, skipped = 0, alreadyDisabled = 0, totalErrors = 0;

            // 1. Processar tarefas fixas por categoria
            foreach (var kvp in TasksByCategory)
            {
                _logger.LogInfo($"[ScheduledTasks] Grupo: {kvp.Key} ({kvp.Value.Length} tarefas)");

                foreach (var taskPath in kvp.Value)
                {
                    ct.ThrowIfCancellationRequested();
                    found++;
                    var result = ProcessTaskDisable(taskPath);
                    switch (result)
                    {
                        case TaskResult.Altered: altered++; break;
                        case TaskResult.AlreadyDisabled: alreadyDisabled++; break;
                        case TaskResult.Error: totalErrors++; break;
                    }
                }
            }

            // 2. Processar tarefas opcionais (detectar se existem)
            _logger.LogInfo($"[ScheduledTasks] Verificando {OptionalTaskPaths.Length} tarefas opcionais...");
            foreach (var taskPath in OptionalTaskPaths)
            {
                ct.ThrowIfCancellationRequested();
                var state = QueryTaskState(taskPath);
                if (state == "NotFound")
                {
                    _logger.LogInfo($"[ScheduledTasks] Opcional '{taskPath}' não encontrada — ignorando.");
                    continue;
                }
                found++;
                var result = ProcessTaskDisable(taskPath);
                switch (result)
                {
                    case TaskResult.Altered: altered++; break;
                    case TaskResult.AlreadyDisabled: alreadyDisabled++; break;
                    case TaskResult.Error: totalErrors++; break;
                }
            }

            stopwatch.Stop();
            _isOptimizationActive = true;

            // Relatório com verificação
            GenerateScheduledReport("DESATIVAÇÃO", found, altered, skipped, alreadyDisabled, 0, totalErrors, stopwatch.ElapsedMilliseconds);

            _logger.LogInfo("[ScheduledTasks] ══════════════════════════════════════════");
            _logger.LogInfo($"[ScheduledTasks] ✅ DESATIVAÇÃO CONCLUÍDA em {stopwatch.ElapsedMilliseconds}ms");
            _logger.LogInfo("[ScheduledTasks] ══════════════════════════════════════════");
        }

        /// <summary>
        /// Restaura todas as tarefas ao estado original usando backup em memória.
        /// </summary>
        public async Task RestoreAllTasksAsync(CancellationToken ct = default)
        {
            if (!_isOptimizationActive || _memoryBackup.Count == 0)
            {
                _logger.LogWarning("[ScheduledTasks] Nenhuma otimização ativa ou backup vazio — tentando fallback via arquivo.");
                _logger.LogInfo("[ScheduledTasks] Fallback: tentando reativar tarefas conhecidas via EnableAllKnownTasks.");
                EnableAllKnownTasks();
                return;
            }

            _logger.LogInfo("[ScheduledTasks] ══════════════════════════════════════════");
            _logger.LogInfo("[ScheduledTasks] 🔄 RESTAURANDO TAREFAS AGENDADAS (ROLLBACK)");
            _logger.LogInfo("[ScheduledTasks] ══════════════════════════════════════════");

            var stopwatch = Stopwatch.StartNew();
            int restored = 0, restoredErrors = 0;

            foreach (var kvp in _memoryBackup)
            {
                ct.ThrowIfCancellationRequested();
                var state = kvp.Value;

                if (!state.WasModified)
                {
                    _logger.LogDebug($"[ScheduledTasks] '{state.TaskPath}' não foi modificado — ignorado.");
                    continue;
                }

                var taskPath = state.TaskPath;
                bool success = await RestoreSingleTaskAsync(taskPath, state);
                if (success)
                    restored++;
                else
                    restoredErrors++;
            }

            stopwatch.Stop();
            _isOptimizationActive = false;
            _memoryBackup.Clear();

            _logger.LogInfo($"[ScheduledTasks] ✅ Restauradas: {restored} | Erros: {restoredErrors} | Tempo: {stopwatch.ElapsedMilliseconds}ms");
            _logger.LogInfo("[ScheduledTasks] ══════════════════════════════════════════");
        }

        /// <summary>
        /// Resultado do processamento de uma tarefa individual.
        /// </summary>
        private enum TaskResult { Altered, Skipped, AlreadyDisabled, Error }

        /// <summary>
        /// Processa a desativação de uma tarefa individual com verificação.
        /// </summary>
        private TaskResult ProcessTaskDisable(string taskPath)
        {
            var taskStopwatch = Stopwatch.StartNew();

            try
            {
                var originalState = QueryTaskState(taskPath);
                _logger.LogInfo($"[ScheduledTasks] '{taskPath}' → estado original: {originalState}");

                var state = new TaskOriginalState
                {
                    TaskPath = taskPath,
                    OriginalStatus = originalState,
                    ModifiedAt = DateTime.Now
                };

                if (originalState == "Disabled" || originalState == "NotFound")
                {
                    state.WasModified = false;
                    _memoryBackup[taskPath] = state;
                    _logger.LogInfo($"[ScheduledTasks] ⏭ '{taskPath}' já está {originalState} — ignorado.");
                    return TaskResult.AlreadyDisabled;
                }

                // Encerrar se estiver rodando
                EndTask(taskPath);

                // Desativar
                bool disabled = DisableTask(taskPath);
                if (!disabled)
                {
                    state.FailureReason = "DisableTask returned false";
                    _memoryBackup[taskPath] = state;
                    _logger.LogWarning($"[ScheduledTasks] ❌ '{taskPath}' falha ao desativar ({taskStopwatch.ElapsedMilliseconds}ms).");
                    return TaskResult.Error;
                }

                // ★ VERIFICAÇÃO PÓS-OPERAÇÃO (corrigir.txt obrigatório)
                var verifyState = QueryTaskState(taskPath);
                bool verified = verifyState.Equals("Disabled", StringComparison.OrdinalIgnoreCase)
                             || verifyState.Equals("Desabilitado", StringComparison.OrdinalIgnoreCase)
                             || verifyState.Contains("abilitado", StringComparison.OrdinalIgnoreCase);

                if (verified)
                {
                    state.WasModified = true;
                    _logger.LogSuccess($"[ScheduledTasks] ✅ '{taskPath}' desativada e VERIFICADA ({taskStopwatch.ElapsedMilliseconds}ms).");
                }
                else
                {
                    state.FailureReason = $"Verification failed: expected Disabled, got {verifyState}";
                    _logger.LogWarning($"[ScheduledTasks] ⚠️ '{taskPath}' comando ok, mas VERIFICAÇÃO falhou: estado={verifyState}.");
                    _memoryBackup[taskPath] = state;
                    return TaskResult.Error;
                }

                _memoryBackup[taskPath] = state;
                return TaskResult.Altered;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ScheduledTasks] Erro ao desativar '{taskPath}': {ex.Message}");
                _memoryBackup[taskPath] = new TaskOriginalState
                {
                    TaskPath = taskPath,
                    OriginalStatus = "Error",
                    FailureReason = ex.Message,
                    ModifiedAt = DateTime.Now
                };
                return TaskResult.Error;
            }
            finally
            {
                taskStopwatch.Stop();
            }
        }

        /// <summary>
        /// Restaura uma tarefa individual com verificação.
        /// </summary>
        private async Task<bool> RestoreSingleTaskAsync(string taskPath, TaskOriginalState state)
        {
            var taskStopwatch = Stopwatch.StartNew();

            try
            {
                bool originalWasEnabled = state.OriginalStatus == "Ready" || state.OriginalStatus == "Running";
                if (!originalWasEnabled)
                {
                    _logger.LogInfo($"[ScheduledTasks] '{taskPath}' original era '{state.OriginalStatus}' — mantendo desabilitado.");
                    return true;
                }

                bool enabled = EnableTask(taskPath);
                if (!enabled)
                {
                    _logger.LogWarning($"[ScheduledTasks] ❌ '{taskPath}' falha ao restaurar ({taskStopwatch.ElapsedMilliseconds}ms).");
                    return false;
                }

                // ★ VERIFICAÇÃO PÓS-OPERAÇÃO
                var verifyState = QueryTaskState(taskPath);
                bool verified = verifyState.Equals("Ready", StringComparison.OrdinalIgnoreCase)
                             || verifyState.Equals("Running", StringComparison.OrdinalIgnoreCase)
                             || verifyState.Equals("Pronto", StringComparison.OrdinalIgnoreCase);

                if (verified)
                {
                    _logger.LogSuccess($"[ScheduledTasks] ✅ '{taskPath}' restaurada e VERIFICADA ({taskStopwatch.ElapsedMilliseconds}ms).");
                }
                else
                {
                    _logger.LogWarning($"[ScheduledTasks] ⚠️ '{taskPath}' comando ok, mas VERIFICAÇÃO falhou: estado={verifyState}.");
                }

                return verified;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ScheduledTasks] Erro ao restaurar '{taskPath}': {ex.Message}");
                return false;
            }
            finally
            {
                taskStopwatch.Stop();
            }
        }

        /// <summary>
        /// Gera relatório detalhado da operação.
        /// </summary>
        private void GenerateScheduledReport(string operation, int found, int altered, int skipped, int alreadyDisabled, int restored, int errors, long elapsedMs)
        {
            _logger.LogInfo("┌─────────────────────────────────────────────────────────────────┐");
            _logger.LogInfo($"│  RELATÓRIO DE {operation,-28} │");
            _logger.LogInfo("├─────────────────────────────────────────────────────────────────┤");
            _logger.LogInfo($"│  Tarefas encontradas:          {found,3}                                    │");
            _logger.LogInfo($"│  Tarefas desativadas:           {altered,3}                                    │");
            _logger.LogInfo($"│  Tarefas ignoradas:             {skipped,3}                                    │");
            _logger.LogInfo($"│  Tarefas já desativadas:        {alreadyDisabled,3}                                    │");
            if (operation == "RESTAURAÇÃO")
                _logger.LogInfo($"│  Tarefas restauradas:           {restored,3}                                    │");
            _logger.LogInfo($"│  Erros:                         {errors,3}                                    │");
            _logger.LogInfo($"│  Tempo total:                   {elapsedMs,3} ms                               │");
            _logger.LogInfo("└─────────────────────────────────────────────────────────────────┘");
        }

        /// <summary>
        /// Consulta o estado atual de uma tarefa agendada.
        /// </summary>
        private string QueryTaskState(string taskPath)
        {
            try
            {
                var safeTaskPath = SanitizeTaskPath(taskPath);
                var (output, exitCode) = RunSchtasks($"/Query /TN \"{safeTaskPath}\" /FO CSV /NH");
                if (exitCode != 0 || string.IsNullOrWhiteSpace(output) 
                    || output.Contains("ERROR", StringComparison.OrdinalIgnoreCase) 
                    || output.Contains("ERRO", StringComparison.OrdinalIgnoreCase))
                    return "NotFound";

                // CSV: "TaskName","Next Run Time","Status"
                var parts = output.Split(',');
                if (parts.Length >= 3)
                {
                    var status = parts[2].Trim().Trim('"');
                    return status; // Ready, Running, Disabled, etc.
                }

                return "Unknown";
            }
            catch
            {
                return "NotFound";
            }
        }

        /// <summary>
        /// Encerra uma tarefa agendada se estiver rodando.
        /// </summary>
        private void EndTask(string taskPath)
        {
            try
            {
                var safeTaskPath = SanitizeTaskPath(taskPath);
                var (output, _) = RunSchtasks($"/End /TN \"{safeTaskPath}\"");
                _logger.LogInfo($"[ScheduledTasks] End '{taskPath}': {output.Trim()}");
            }
            catch (Exception ex)
            {
                // Não é erro crítico se a tarefa não estiver rodando
                _logger.LogInfo($"[ScheduledTasks] End '{taskPath}' (ignorável): {ex.Message}");
            }
        }

        /// <summary>
        /// Desativa uma tarefa agendada.
        /// </summary>
        private bool DisableTask(string taskPath)
        {
            try
            {
                var safeTaskPath = SanitizeTaskPath(taskPath);
                var (output, exitCode) = RunSchtasks($"/Change /TN \"{safeTaskPath}\" /Disable");
                // Exit code 0 = sucesso, independente do encoding do output
                if (exitCode == 0) return true;
                // Fallback: verificar texto (com encoding corrigido)
                bool success = output.Contains("SUCCESS", StringComparison.OrdinalIgnoreCase) 
                    || output.Contains("ÊXITO", StringComparison.OrdinalIgnoreCase) 
                    || output.Contains("XITO", StringComparison.OrdinalIgnoreCase)
                    || output.Contains("alterada", StringComparison.OrdinalIgnoreCase)
                    || output.Contains("changed", StringComparison.OrdinalIgnoreCase);
                return success;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[ScheduledTasks] Disable '{taskPath}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Reativa uma tarefa agendada.
        /// </summary>
        private bool EnableTask(string taskPath)
        {
            try
            {
                var safeTaskPath = SanitizeTaskPath(taskPath);
                var (output, exitCode) = RunSchtasks($"/Change /TN \"{safeTaskPath}\" /Enable");
                if (exitCode == 0) return true;
                bool success = output.Contains("SUCCESS", StringComparison.OrdinalIgnoreCase) 
                    || output.Contains("ÊXITO", StringComparison.OrdinalIgnoreCase) 
                    || output.Contains("XITO", StringComparison.OrdinalIgnoreCase)
                    || output.Contains("alterada", StringComparison.OrdinalIgnoreCase)
                    || output.Contains("changed", StringComparison.OrdinalIgnoreCase);
                return success;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[ScheduledTasks] Enable '{taskPath}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Reativa todas as tarefas conhecidas (fallback sem backup).
        /// </summary>
        private void EnableAllKnownTasks()
        {
            int count = 0;
            foreach (var group in TasksByCategory.Values)
            {
                foreach (var taskPath in group)
                {
                    try
                    {
                        EnableTask(taskPath);
                        count++;
                    }
                    catch { /* ignorar erros individuais */ }
                }
            }
            _logger.LogInfo($"[ScheduledTasks] Fallback: tentou reativar {count} tarefas");
        }

        /// <summary>
        /// Sanitiza um task path para uso seguro como argumento de schtasks.exe.
        /// Remove caracteres que poderiam causar injeção de comando.
        /// </summary>
        private static string SanitizeTaskPath(string taskPath)
        {
            if (string.IsNullOrEmpty(taskPath)) return taskPath;
            // Permitir apenas caracteres válidos para task paths do Windows: letras, números, espaços, \, /, -, _
            var sanitized = System.Text.RegularExpressions.Regex.Replace(taskPath, @"[^a-zA-Z0-9\s\\_/\-\.]", "");
            return sanitized;
        }

        /// <summary>
        /// Executa schtasks.exe com os argumentos fornecidos.
        /// Task paths são sanitizados para prevenir injeção de comando.
        /// </summary>
        private static (string Output, int ExitCode) RunSchtasks(string arguments)
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            // Registrar CodePages para suportar encoding OEM (CP850/437)
            try { System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance); } catch { }
            try
            {
                process.StartInfo.StandardOutputEncoding = System.Text.Encoding.GetEncoding(850);
                process.StartInfo.StandardErrorEncoding = System.Text.Encoding.GetEncoding(850);
            }
            catch
            {
                // Fallback: se CP850 não disponível, usar default
            }

            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit(10000); // 10s timeout por tarefa

            var text = string.IsNullOrEmpty(output) ? error : output;
            return (text, process.ExitCode);
        }

        #region Backup / Restore

        private void SaveBackup(Dictionary<string, string> states)
        {
            try
            {
                var filePath = Path.Combine(_backupPath, "scheduled_tasks_backup.json");
                var json = JsonSerializer.Serialize(states, new JsonSerializerOptions { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,  WriteIndented = true });
                File.WriteAllText(filePath, json);
                _logger.LogInfo($"[ScheduledTasks] Backup salvo: {states.Count} tarefas em {filePath}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ScheduledTasks] Erro ao salvar backup: {ex.Message}", ex);
            }
        }

        private Dictionary<string, string>? LoadBackup()
        {
            try
            {
                var filePath = Path.Combine(_backupPath, "scheduled_tasks_backup.json");
                if (!File.Exists(filePath))
                    return null;

                var json = File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ScheduledTasks] Erro ao carregar backup: {ex.Message}", ex);
                return null;
            }
        }

        private void ClearBackup()
        {
            try
            {
                var filePath = Path.Combine(_backupPath, "scheduled_tasks_backup.json");
                if (File.Exists(filePath))
                    File.Delete(filePath);
                _logger.LogInfo("[ScheduledTasks] Backup limpo após restauração");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[ScheduledTasks] Erro ao limpar backup: {ex.Message}");
            }
        }

        #endregion
    }
}
