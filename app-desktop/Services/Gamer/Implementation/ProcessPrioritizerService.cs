using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using VoltrisOptimizer.Core.Constants;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services.Gamer.Interfaces;
using GamerModels = VoltrisOptimizer.Services.Gamer.Models;
using VoltrisOptimizer.Utils;

namespace VoltrisOptimizer.Services.Gamer.Implementation
{
    /// <summary>
    /// Implementação do gerenciador de prioridade de processos
    /// </summary>
    public class ProcessPrioritizerService : IProcessPrioritizer
    {
        private readonly ILoggingService _logger;
        private readonly Dictionary<int, ProcessPriorityClass> _originalPriorities = new();
        private readonly object _lock = new();
        private readonly bool _isGamingMode;
        private readonly bool _isThermalConstrained;
        private readonly bool _isOnBattery;
        
        // 🔥 CORREÇÃO: Adicionar Instance estático
        private static ProcessPrioritizerService? _instance;
        public static ProcessPrioritizerService Instance => _instance ??= new ProcessPrioritizerService();

        /// <summary>
        /// Processos de Streaming e Gravação que devem ser protegidos e priorizados
        /// </summary>
        private static readonly string[] StreamingSoftware = new[]
        {
            "obs64", "obs32", "obs",            // OBS Studio
            "Streamlabs OBS", "Streamlabs Desktop", // Streamlabs
            "TikTok Live Studio",               // TikTok Studio
            "TwitchStudio",                     // Twitch Studio
            "XSplit.Core",                      // XSplit
            "vMix64",                           // vMix
            "NVIDIA Share", "NVIDIA Broadcast", // NVIDIA Tools
            "Discord", "DiscordPTB", "DiscordCanary" // Discord (spared from killing, prioritized if streaming)
        };

        /// <summary>
        /// Processos que podem ser fechados durante jogos
        /// </summary>
        private static readonly string[] ClosableProcesses = new[]
        {
            "OneDrive", "Dropbox", "GoogleDriveSync",
            "iCloudServices", "CCleaner", "MalwareBytes", "AdobeUpdateService"
        };

        /// <summary>
        /// Processos a ter prioridade reduzida
        /// </summary>
        private static readonly string[] LowerPriorityProcesses = new[]
        {
            "SearchIndexer", "WSearch", "SysMain", "DiagTrack", "WMIProviderHost",
            "MsMpEng", "Windows Defender", "backgroundTaskHost", "SettingSyncHost"
        };

        public bool IsStreamerModeActive { get; private set; }

        public ProcessPrioritizerService(ILoggingService logger = null, bool isGamingMode = false, bool isThermalConstrained = false, bool isOnBattery = false)
        {
            _logger = logger ?? VoltrisOptimizer.App.LoggingService;
            _isGamingMode = isGamingMode;
            _isThermalConstrained = isThermalConstrained;
            _isOnBattery = isOnBattery;
        }

        public void DetectStreamingActivity()
        {
            try 
            {
                IsStreamerModeActive = Process.GetProcesses().Any(p => 
                    StreamingSoftware.Any(s => p.ProcessName.Contains(s, StringComparison.OrdinalIgnoreCase)));

                if (IsStreamerModeActive)
                {
                    _logger.LogInfo("[Prioritizer] MODO STREAMER DETECTADO 🎥 - Ajustando estratégia de prioridade");
                }
            }
            catch (Exception ex) { _logger.LogDebug($"[Prioritizer] DetectStreamingActivity erro: {ex.Message}"); }
        }

        public int CloseUnnecessaryProcesses()
        {
            // Se estiver streamando, ser MUITO conservador ao fechar processos
            DetectStreamingActivity();
            if (IsStreamerModeActive)
            {
                _logger.LogInfo("[Prioritizer] Modo Streamer ativo: Pulando fechamento automático de processos para estabilidade");
                return 0;
            }

            int closedCount = 0;

            foreach (var processName in ClosableProcesses)
            {
                try
                {
                    var processes = Process.GetProcessesByName(processName);
                    
                    foreach (var process in processes)
                    {
                        try
                        {
                            if (IsProtectedProcess(process.ProcessName)) continue;

                            // Tentar fechar graciosamente primeiro
                            process.CloseMainWindow();
                            
                            if (!process.WaitForExit(2000))
                            {
                                process.Kill();
                            }

                            closedCount++;
                            _logger.LogInfo($"[Prioritizer] Processo fechado: {processName}");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"[Prioritizer] Falha ao fechar {processName}: {ex.Message}");
                        }
                        finally
                        {
                            process.Dispose();
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[Prioritizer] Erro ao processar {processName}: {ex.Message}");
                }
            }

            if (closedCount > 0)
            {
                _logger.LogSuccess($"[Prioritizer] {closedCount} processo(s) fechado(s)");
            }

            return closedCount;
        }

        public bool SetPriority(int processId, GamerModels.ProcessPriorityLevel priority)
        {
            try
            {
                using var process = SafeProcess.TryGet(processId);

                _logger.LogDebug($"[Prioritizer] TENTANDO setar PID {processId} ({process.ProcessName}) para {priority}. Prioridade atual: {process.PriorityClass}");

                // CORREÇÃO: Bloquear apenas se estiver em restrições térmicas ou bateria
                // Modo Gamer não é mais requisito obrigatório para priorização de jogos
                if (_isThermalConstrained || _isOnBattery)
                {
                    _logger.LogInfo("[Prioritizer] Priority: Pulando - Sistema em restrição térmica ou bateria");
                    return false;
                }

                // CORREÇÃO: Nunca usar Realtime, sempre validar
                if (priority == GamerModels.ProcessPriorityLevel.RealTime)
                {
                    _logger.LogWarning("[Prioritizer] RealTime priority bloqueado por segurança");
                    priority = GamerModels.ProcessPriorityLevel.High;
                }

                // ENTERPRISE LOGIC: Adaptação para PCs Fracos (< 4 Cores)
                // Evitar travamento total do sistema
                if (Environment.ProcessorCount <= 4 && priority == GamerModels.ProcessPriorityLevel.High)
                {
                    priority = GamerModels.ProcessPriorityLevel.AboveNormal;
                    _logger.LogWarning("[Prioritizer] CPU Dual/Quad-Core detectada: Rebaixando High -> AboveNormal para segurança");
                }

                DetectStreamingActivity();

                var targetPriority = priority switch
                {
                    GamerModels.ProcessPriorityLevel.Idle => ProcessPriorityClass.Idle,
                    GamerModels.ProcessPriorityLevel.BelowNormal => ProcessPriorityClass.BelowNormal,
                    GamerModels.ProcessPriorityLevel.Normal => ProcessPriorityClass.Normal,
                    GamerModels.ProcessPriorityLevel.AboveNormal => ProcessPriorityClass.AboveNormal,
                    GamerModels.ProcessPriorityLevel.High => ProcessPriorityClass.High,
                    GamerModels.ProcessPriorityLevel.RealTime => ProcessPriorityClass.High,
                    _ => ProcessPriorityClass.Normal
                };

                _logger.LogDebug($"[Prioritizer] Aplicando ProcessPriorityClass.{targetPriority} no PID {processId}");
                
                lock (_lock)
                {
                    // Salvar prioridade original se não existe
                    if (!_originalPriorities.ContainsKey(processId))
                    {
                        _originalPriorities[processId] = process.PriorityClass;
                        _logger.LogDebug($"[Prioritizer] Prioridade original salva: {process.PriorityClass}");
                    }
                }

                process.PriorityClass = targetPriority;
                
                _logger.LogDebug($"[Prioritizer] Prioridade após aplicação: {process.PriorityClass}");
                _logger.LogInfo($"[Prioritizer] ✅ Processo {processId} ({process.ProcessName}) definido para {priority} (target={targetPriority})");

                // STREAMER MODE: Se detectado streaming, elevar OBS/TikTok também para evitar dropped frames
                if (IsStreamerModeActive)
                {
                    PrioritizeStreamingSoftware();
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[Prioritizer] ❌ ERRO ao definir prioridade do PID {processId}: {ex.Message}", ex);
                return false;
            }
        }

        private void PrioritizeStreamingSoftware()
        {
            foreach (var streamApp in StreamingSoftware)
            {
                try 
                {
                    var procs = Process.GetProcessesByName(streamApp); // Simple match first
                    if (procs.Length == 0) // Try partial match logic if needed, but Process.GetProcesses() is heavy
                    {
                         procs = Process.GetProcesses().Where(p => p.ProcessName.Contains(streamApp, StringComparison.OrdinalIgnoreCase)).ToArray();
                    }

                    foreach (var proc in procs)
                    {
                         try 
                         {
                             // Backup original
                             lock (_lock)
                             {
                                if (!_originalPriorities.ContainsKey(proc.Id))
                                    _originalPriorities[proc.Id] = proc.PriorityClass;
                             }

                             // Set Above Normal (High pode brigar com o jogo, Above Normal ganha do sistema)
                             if (proc.PriorityClass < ProcessPriorityClass.AboveNormal)
                             {
                                 proc.PriorityClass = ProcessPriorityClass.AboveNormal;
                                 _logger.LogInfo($"[StreamerMode] 🎬 Priorizando {proc.ProcessName} (AboveNormal)");
                             }
                         }
                         catch (Exception ex) { _logger.LogDebug($"[StreamerMode] Erro ao priorizar processo: {ex.Message}"); }
                         finally { proc.Dispose(); }
                    }
                }
                catch (Exception ex) { _logger.LogDebug($"[Prioritizer] Erro ao proteger streaming: {ex.Message}"); }
            }
        }

        public int LowerBackgroundProcessesPriority()
        {
            // Em modo streamer, cuidado extra com processos de background que podem ser encoders
            DetectStreamingActivity();

            int loweredCount = 0;

            foreach (var processName in LowerPriorityProcesses)
            {
                try
                {
                    var processes = Process.GetProcessesByName(processName);
                    
                    foreach (var process in processes)
                    {
                        try
                        {
                            lock (_lock)
                            {
                                if (!_originalPriorities.ContainsKey(process.Id))
                                {
                                    _originalPriorities[process.Id] = process.PriorityClass;
                                }
                            }

                            process.PriorityClass = ProcessPriorityClass.BelowNormal;
                            loweredCount++;
                        }
                        catch
                        {
                            // Alguns processos do sistema não permitem alteração
                        }
                        finally
                        {
                            process.Dispose();
                        }
                    }
                }
                catch
                {
                    // Ignorar erros ao enumerar processos
                }
            }

            if (loweredCount > 0)
            {
                _logger.LogInfo($"[Prioritizer] {loweredCount} processo(s) com prioridade reduzida");
            }

            return loweredCount;
        }

        public void RestoreAllPriorities()
        {
            int restoredCount = 0;

            lock (_lock)
            {
                foreach (var kvp in _originalPriorities.ToList())
                {
                    try
                    {
                        using var process = SafeProcess.TryGet(kvp.Key);
                        process.PriorityClass = kvp.Value;
                        restoredCount++;
                    }
                    catch
                    {
                        // Processo pode não existir mais
                    }
                }

                _originalPriorities.Clear();
            }

            if (restoredCount > 0)
            {
                _logger.LogInfo($"[Prioritizer] {restoredCount} prioridade(s) restaurada(s)");
            }
        }

        public bool IsProtectedProcess(string processName)
        {
            if (string.IsNullOrEmpty(processName)) return true;

            var lowerName = processName.ToLowerInvariant();

            // Processos do sistema
            if (SystemConstants.ProtectedProcesses.System.Any(p => 
                lowerName.Equals(p, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            // Voltris
            if (SystemConstants.ProtectedProcesses.Voltris.Any(p => 
                lowerName.Equals(p, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            // Launchers de jogos
            if (SystemConstants.ProtectedProcesses.Launchers.Any(p => 
                lowerName.Equals(p, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
            
            // Softwares de Streaming (Proteção Extra)
            if (StreamingSoftware.Any(s => lowerName.Contains(s.ToLowerInvariant())))
            {
                return true;
            }

            return false;
        }
    }
}

