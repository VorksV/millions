using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.Uninstaller
{
    /// <summary>
    /// Install Trace Engine - Sistema avançado de rastreamento e remoção completa de aplicativos
    /// Equivalente ao Revo Uninstaller, Geek Uninstaller e Total Uninstall
    /// </summary>
    public class InstallTraceEngine
    {
        private readonly ILoggingService _logger;
        private readonly ApplicationProfiler _profiler;
        private readonly ResidualTracer _tracer;
        private readonly CleanupEngine _cleanupEngine;
        private readonly SilentUninstallEngine _silentEngine;

        public InstallTraceEngine(ILoggingService logger)
        {
            _logger = logger;
            _profiler = new ApplicationProfiler(logger);
            _tracer = new ResidualTracer(logger);
            _cleanupEngine = new CleanupEngine(logger);
            _silentEngine = new SilentUninstallEngine(logger);
        }

        /// <summary>
        /// FASE 1-5: Desinstalação completa com rastreamento profundo
        /// </summary>
        public async Task<UninstallResult> PerformAdvancedUninstall(AppInfo app, bool forceRemoval = false)
        {
            var result = new UninstallResult { AppName = app.Name };

            try
            {
                _logger.LogInfo($"[Install Trace Engine] ═══════════════════════════════════════");
                _logger.LogInfo($"[Install Trace Engine] Iniciando desinstalação avançada: {app.Name}");
                _logger.LogInfo($"[Install Trace Engine] ═══════════════════════════════════════");

                // FASE 1: IDENTIFICAÇÃO E CRIAÇÃO DO PERFIL
                _logger.LogInfo($"[FASE 1] Criando perfil do aplicativo...");
                var profile = await _profiler.CreateApplicationProfile(app);
                result.Profile = profile;

                _logger.LogInfo($"[FASE 1] Perfil criado:");
                _logger.LogInfo($"  - Executáveis principais: {profile.MainExecutables.Count}");
                _logger.LogInfo($"  - Processos ativos: {profile.RunningProcesses.Count}");
                _logger.LogInfo($"  - Serviços relacionados: {profile.RelatedServices.Count}");

                // FASE 2: EXECUÇÃO DO DESINSTALADOR PADRÃO (se existir)
                bool uninstallerExecuted = false;
                if (!forceRemoval && profile.HasUninstaller)
                {
                    _logger.LogInfo($"[FASE 2] Executando desinstalação silenciosa automática...");
                    var silentResult = await _silentEngine.PerformSilentUninstall(profile);
                    uninstallerExecuted = silentResult.Success;
                    
                    if (uninstallerExecuted)
                    {
                        _logger.LogSuccess($"[FASE 2] Desinstalação silenciosa executada com sucesso!");
                        // Aguardar um pouco para o sistema processar as mudanças
                        await Task.Delay(2000);
                    }
                    else
                    {
                        _logger.LogWarning($"[FASE 2] Desinstalação silenciosa falhou: {silentResult.ErrorMessage}");
                        _logger.LogInfo($"[FASE 2] Continuando com remoção forçada...");
                    }
                }
                else if (forceRemoval)
                {
                    _logger.LogInfo($"[FASE 2] Modo de remoção forçada ativado. Pulando desinstalador padrão.");
                }
                else
                {
                    _logger.LogInfo($"[FASE 2] Nenhum desinstalador encontrado. Prosseguindo com remoção manual.");
                }

                // FASE 3: SCAN PROFUNDO DE RESÍDUOS
                _logger.LogInfo($"[FASE 3] Iniciando escaneamento profundo de resíduos...");
                var residues = await _tracer.TraceAllResidues(profile);
                result.Residues = residues;

                _logger.LogInfo($"[FASE 3] Resíduos encontrados:");
                _logger.LogInfo($"  - Pastas: {residues.Folders.Count}");
                _logger.LogInfo($"  - Arquivos: {residues.Files.Count}");
                _logger.LogInfo($"  - Chaves de Registro: {residues.RegistryKeys.Count}");
                _logger.LogInfo($"  - Serviços: {residues.Services.Count}");
                _logger.LogInfo($"  - Tarefas Agendadas: {residues.ScheduledTasks.Count}");
                _logger.LogInfo($"  - Entradas de Inicialização: {residues.StartupEntries.Count}");

                // FASE 4: LIMPEZA AUTOMÁTICA DE RESÍDUOS (SEM CONFIRMAÇÃO)
                if (residues.HasResidues)
                {
                    _logger.LogInfo($"[FASE 4] Iniciando limpeza automática de resíduos...");
                    _logger.LogInfo($"[FASE 4] Total de resíduos: {residues.TotalCount}");
                    
                    await PerformDeepCleanup(residues);
                    result.CleanupPerformed = true;
                    _logger.LogSuccess($"[FASE 4] Limpeza profunda concluída automaticamente.");
                }
                else
                {
                    _logger.LogSuccess($"[FASE 4] Nenhum resíduo encontrado. Sistema limpo!");
                    result.CleanupPerformed = true;
                }

                result.Success = true;
                _logger.LogSuccess($"[Install Trace Engine] Desinstalação avançada concluída com sucesso!");
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[Install Trace Engine] Erro durante desinstalação avançada: {ex.Message}", ex);
                result.Success = false;
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        private async Task PerformDeepCleanup(ApplicationResidues residues)
        {
            // 1. Finalizar processos
            if (residues.RunningProcesses.Any())
            {
                _logger.LogInfo($"Finalizando {residues.RunningProcesses.Count} processos...");
                foreach (var procName in residues.RunningProcesses)
                {
                    try
                    {
                        var procs = Process.GetProcessesByName(procName);
                        foreach (var proc in procs)
                        {
                            proc.Kill();
                            _logger.LogInfo($"  ✓ Processo finalizado: {procName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"  ✗ Falha ao finalizar {procName}: {ex.Message}");
                    }
                }
            }

            // 2. Remover serviços
            if (residues.Services.Any())
            {
                _logger.LogInfo($"Removendo {residues.Services.Count} serviços...");
                _cleanupEngine.RemoveServices(residues.Services);
            }

            // 3. Remover tarefas agendadas
            if (residues.ScheduledTasks.Any())
            {
                _logger.LogInfo($"Removendo {residues.ScheduledTasks.Count} tarefas agendadas...");
                foreach (var task in residues.ScheduledTasks)
                {
                    try
                    {
                        // Sanitizar nome da tarefa para prevenir injeção de argumento
                        var safeTaskName = task
                            .Replace("\"", "")
                            .Replace("&", "")
                            .Replace("|", "")
                            .Replace(">", "")
                            .Replace("<", "")
                            .Replace("^", "");
                        
                        using var proc = Process.Start(new ProcessStartInfo
                        {
                            FileName = "schtasks.exe",
                            Arguments = $"/Delete /TN \"{safeTaskName}\" /F",
                            CreateNoWindow = true,
                            UseShellExecute = false
                        });
                        proc?.WaitForExit(15000); // Timeout de 15s para não travar
                        _logger.LogInfo($"  ✓ Tarefa removida: {task}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"  ✗ Falha ao remover tarefa {task}: {ex.Message}");
                    }
                }
            }

            // 4. Remover chaves de registro
            if (residues.RegistryKeys.Any())
            {
                _logger.LogInfo($"Removendo {residues.RegistryKeys.Count} chaves de registro...");
                _cleanupEngine.DeleteRegistryKeys(residues.RegistryKeys);
            }

            // 5. Remover arquivos individuais
            if (residues.Files.Any())
            {
                _logger.LogInfo($"Removendo {residues.Files.Count} arquivos...");
                foreach (var file in residues.Files)
                {
                    try
                    {
                        if (File.Exists(file))
                        {
                            File.Delete(file);
                            _logger.LogInfo($"  ✓ Arquivo removido: {file}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"  ✗ Falha ao remover {file}: {ex.Message}");
                    }
                }
            }

            // 6. Remover pastas (por último)
            if (residues.Folders.Any())
            {
                _logger.LogInfo($"Removendo {residues.Folders.Count} pastas...");
                _cleanupEngine.DeleteFolders(residues.Folders);
            }

            await Task.CompletedTask;
        }
    }
}
