using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Gamer.Interfaces;

namespace VoltrisOptimizer.Services.Optimization.Memory
{
    public class VoltrisMemoryCoordinator
    {
        private readonly ILoggingService _logger;
        private static VoltrisMemoryCoordinator? _instance;
        private static readonly object _lock = new object();

        // Safe list de processos críticos do Windows/Drivers que nunca devem ser tocados
        private readonly HashSet<string> _criticalProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "explorer", "dwm", "csrss", "winlogon", "smss", "lsass", "services", "svchost",
            "taskmgr", "system", "idle", "registry", "fontdrvhost", "wininit", "spoolsv",
            "nvcontainer", "nvdisplay.container", "amdow", "amdrsserv", "RadeonSoftware",
            "audiodg", "SearchIndexer", "SecurityHealthService", "MsMpEng", "wmiapsrv", "wmiprvse",
            "conhost", "sihost", "ctfmon", "dasHost", "dllhost", "RuntimeBroker", "SearchUI",

            // ── CORREÇÃO CRÍTICA ──
            // OBS e afins estavam AUSENTES desta lista. Como a lógica de
            // ApplySafeGamingModeAsync rebaixa para BelowNormal e ativa EcoQoS
            // (Efficiency Mode) TODO processo fora da lista, o encoder de vídeo era
            // justamente o que o VOLTRIS limitava. EcoQoS reduz a frequência da CPU
            // do processo: durante uma transmissão isso provoca macroblocks descartados,
            // perda de qualidade no bitrate fixo e deriva de áudio.
            "obs64", "obs32", "obs", "ffmpeg", "ffmpeg64", "wirecast", "vmix",
            "streamlabs", "restream", "nircmd",

            // Áudio e comunicação: um clique de áudio perdido é pior que alguns MB.
            "discord", "discordapp", "Teams", "ms-teams", "Zoom", "WhatsApp", "Telegram", "Skype",

            // Codecs/hardware de vídeo usados pelo encoder.
            "obs64helper", "obs-ffmpeg-mux", "MediaFoundation", "dllhost.exe"
        };

        private bool _gamingModeApplied = false;
        private readonly HashSet<int> _throttledPids = new HashSet<int>();

        /// <summary>
        /// PIDs que receberam EcoQoS. Permite reverter exatamente o que foi aplicado,
        /// em vez de varrer o sistema na restauração.
        /// </summary>
        private readonly HashSet<int> _ecoQoSAppliedPids = new HashSet<int>();

        private DateTime _highMemoryStartTime = DateTime.MinValue;
        private const double MEMORY_PRESSURE_THRESHOLD = 90.0; // 90%
        private const int HYSTERESIS_SECONDS = 30; // Aguardar 30s de pressão

        [DllImport("ntdll.dll")]
        private static extern int NtSetSystemInformation(int SystemInformationClass, ref int SystemInformation, int SystemInformationLength);
        private const int SystemMemoryListInformation = 80;
        private const int MemoryPurgeStandbyList = 4;
        private const int MemoryPurgeLowPriorityStandbyList = 5;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr dwMinimumWorkingSetSize, IntPtr dwMaximumWorkingSetSize);

        [DllImport("kernel32.dll")]
        private static extern bool SetProcessInformation(IntPtr hProcess, int processInformationClass, ref PROCESS_POWER_THROTTLING_STATE processInformation, uint processInformationSize);

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_POWER_THROTTLING_STATE
        {
            public uint Version;
            public uint ControlMask;
            public uint StateMask;
        }

        private const int ProcessPowerThrottling = 1;
        private const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 1;

        public static VoltrisMemoryCoordinator Instance
        {
            get
            {
                lock (_lock)
                {
                    return _instance ??= new VoltrisMemoryCoordinator(VoltrisOptimizer.App.LoggingService);
                }
            }
        }

        public VoltrisMemoryCoordinator(ILoggingService logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// O único ponto de entrada para pedidos de otimização de memória.
        /// Substitui as dezenas de chamadas espalhadas pelo projeto.
        /// </summary>
        public async Task<bool> OptimizeMemoryAsync(bool isManualClick = false)
        {
            try
            {
                var isGaming = App.GameDetectionService?.HasActiveRunningGameSession == true;

                if (isGaming)
                {
                    if (!_gamingModeApplied)
                    {
                        _logger.LogInfo("[MemoryCoordinator] Jogo detectado. Aplicando Safe Gaming Mode (Prioridade/EcoQoS). Bloqueando purgas agressivas.");
                        await ApplySafeGamingModeAsync();
                        _gamingModeApplied = true;
                    }
                    return true;
                }
                else
                {
                    if (_gamingModeApplied)
                    {
                        _logger.LogInfo("[MemoryCoordinator] Jogo finalizado. Restaurando prioridades.");
                        await RestoreNormalModeAsync();
                        _gamingModeApplied = false;
                    }
                }

                // Se não está jogando e foi clique manual, roda a limpeza completa
                if (isManualClick)
                {
                    _logger.LogInfo("[MemoryCoordinator] Otimização Manual fora de jogo. Executando purga e trim.");
                    await Task.Run(() => CleanStandbyList());
                    await TrimNonCriticalProcessesAsync();
                    return true;
                }

                // Se não está jogando mas é automático, aplica Histerese
                var cache = Core.SystemMetricsCache.Instance;
                if (cache.MemoryUsedPercent > MEMORY_PRESSURE_THRESHOLD)
                {
                    if (_highMemoryStartTime == DateTime.MinValue)
                    {
                        _highMemoryStartTime = DateTime.Now;
                        _logger.LogDebug($"[MemoryCoordinator] Pressão de RAM detectada. Iniciando histerese de {HYSTERESIS_SECONDS}s.");
                        return false; // Não agir ainda
                    }

                    if ((DateTime.Now - _highMemoryStartTime).TotalSeconds >= HYSTERESIS_SECONDS)
                    {
                        _logger.LogInfo("[MemoryCoordinator] Histerese concluída. Pressão de RAM real confirmada. Aplicando Trim leve.");
                        await Task.Run(() => CleanStandbyList()); // Fora do jogo pode
                        await TrimNonCriticalProcessesAsync();
                        _highMemoryStartTime = DateTime.MinValue; // Reset
                        return true;
                    }
                }
                else
                {
                    _highMemoryStartTime = DateTime.MinValue;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[MemoryCoordinator] Erro ao orquestrar memória: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// SAFE GAMING MODE: Apenas rebaixa prioridade e ativa Efficiency Mode.
        /// NUNCA força page-out ou purga cache, evitando stuttering.
        /// </summary>
        private async Task ApplySafeGamingModeAsync()
        {
            await Task.Run(() =>
            {
                var processes = Process.GetProcesses();
                _throttledPids.Clear();

                foreach (var p in processes)
                {
                    try
                    {
                        if (p.Id <= 4 || p.HasExited) { p.Dispose(); continue; }
                        if (_criticalProcesses.Contains(p.ProcessName)) { p.Dispose(); continue; }
                        if (App.GameDetectionService?.IsKnownGame(p.ProcessName) == true) { p.Dispose(); continue; }
                        
                        // Não rebaixar processos de launchers que podem quebrar o jogo
                        if (p.ProcessName.Contains("steam", StringComparison.OrdinalIgnoreCase) || 
                            p.ProcessName.Contains("epic", StringComparison.OrdinalIgnoreCase))
                        {
                            p.Dispose();
                            continue;
                        }

                        // Rebaixar Prioridade
                        if (p.PriorityClass == ProcessPriorityClass.Normal || p.PriorityClass == ProcessPriorityClass.AboveNormal)
                        {
                            p.PriorityClass = ProcessPriorityClass.BelowNormal;
                            _throttledPids.Add(p.Id);
                        }

                        // Tentar ativar EcoQoS (Efficiency Mode - Apenas Windows 11)
                        EnableEfficiencyMode(p);
                    }
                    catch { } // Ignorar exceções de acesso negado sem logar para não causar overhead
                    finally { try { p.Dispose(); } catch { } }
                }
            });
        }

        /// <summary>
        /// Restaura a prioridade E remove o EcoQoS dos processos rebaixados.
        ///
        /// CORREÇÃO: a versão anterior só devolvia <c>PriorityClass</c> para Normal e
        /// NUNCA limpava PROCESS_POWER_THROTTLING. O processo permanecia em Efficiency
        /// Mode até o fim da sessão, ou seja, com frequência de CPU reduzida mesmo
        /// depois de o jogo terminar.
        /// </summary>
        private async Task RestoreNormalModeAsync()
        {
            await Task.Run(() =>
            {
                var processes = Process.GetProcesses();
                var throttled = new HashSet<int>(_throttledPids);
                foreach (var p in processes)
                {
                    try
                    {
                        if (throttled.Contains(p.Id) && !p.HasExited)
                        {
                            if (p.PriorityClass == ProcessPriorityClass.BelowNormal)
                            {
                                p.PriorityClass = ProcessPriorityClass.Normal;
                            }

                            // Remove o EcoQoS e confirma por releitura.
                            if (DisableEfficiencyMode(p, out string? ecoErr))
                            {
                                _logger.LogInfo($"[MemCoord] EcoQoS removido e confirmado de PID {p.Id} ({p.ProcessName}).");
                            }
                            else
                            {
                                _logger.LogWarning($"[MemCoord] EcoQoS NÃO removido de PID {p.Id} ({p.ProcessName}): {ecoErr}");
                            }
                        }
                    }
                    catch { }
                    finally { try { p.Dispose(); } catch { } }
                }
                _throttledPids.Clear();
            });
        }

        /// <summary>
        /// Aplica (ou remove) EcoQoS em UM processo específico, e confirma o estado
        /// final. Usado pelo ProcessArm para agir apenas sobre o processo pedido.
        /// </summary>
        public bool SetEcoQoSForProcess(int pid, string name, bool throttle, out string? error)
        {
            error = null;
            if (throttle && (_criticalProcesses.Contains(name) || _protectedFromEcoQoS.Contains(name)))
            {
                error = $"'{name}' está na lista de processos protegidos contra EcoQoS (encoder, áudio ou comunicação).";
                return false;
            }

            try
            {
                using var p = Process.GetProcessById(pid);
                if (!string.Equals(p.ProcessName, name, StringComparison.OrdinalIgnoreCase))
                {
                    error = $"PID {pid} pertence a '{p.ProcessName}', não a '{name}'.";
                    return false;
                }

                return throttle
                    ? EnableEfficiencyMode(p, out error)
                    : DisableEfficiencyMode(p, out error);
            }
            catch (Exception ex)
            {
                error = $"{ex.GetType().Name}: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Processos que nunca devem receber EcoQoS, mesmo se um chamador específico
        /// tentar. Encoder, áudio e comunicação são performance-critical para o usuário.
        /// </summary>
        private static readonly HashSet<string> _protectedFromEcoQoS = new(StringComparer.OrdinalIgnoreCase)
        {
            "obs64", "obs32", "obs", "ffmpeg", "ffmpeg64", "wirecast", "vmix",
            "streamlabs", "restream", "audiodg", "discord", "discordapp", "Teams", "ms-teams"
        };

        /// <summary>
        /// Ativa EcoQoS (Efficiency Mode) no processo. Retorna <c>true</c> somente
        /// quando a API aceita a solicitação. A API não oferece leitura de volta em
        /// versões antigas do Windows, portanto a confirmação é o retorno da chamada.
        /// </summary>
        private void EnableEfficiencyMode(Process p)
        {
            EnableEfficiencyMode(p, out _);
        }

        private bool EnableEfficiencyMode(Process p, out string? error)
        {
            error = null;
            try
            {
                PROCESS_POWER_THROTTLING_STATE state = new PROCESS_POWER_THROTTLING_STATE
                {
                    Version = 1,
                    ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED,
                    StateMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED
                };

                if (!SetProcessInformation(p.Handle, ProcessPowerThrottling, ref state, (uint)Marshal.SizeOf(state)))
                {
                    int win32 = Marshal.GetLastWin32Error();
                    error = $"SetProcessInformation(ProcessPowerThrottling) falhou (Win32={win32}). " +
                            "Requer Windows 10 1709+ e privilégio administrativo.";
                    _logger.LogWarning($"[MemCoord] EcoQoS não aplicado a {p.ProcessName} (PID {p.Id}): {error}");
                    return false;
                }

                if (!_ecoQoSAppliedPids.Contains(p.Id))
                    _ecoQoSAppliedPids.Add(p.Id);
                return true;
            }
            catch (Exception ex)
            {
                error = $"{ex.GetType().Name}: {ex.Message}";
                _logger.LogWarning($"[MemCoord] EcoQoS não aplicado a {p.ProcessName} (PID {p.Id}): {error}");
                return false;
            }
        }

        /// <summary>
        /// Remove o EcoQoS (Efficiency Mode) de um processo. Sem isso, o processo
        /// permanece com frequência de CPU reduzida pelo agendador.
        /// </summary>
        private bool DisableEfficiencyMode(Process p, out string? error)
        {
            error = null;
            try
            {
                PROCESS_POWER_THROTTLING_STATE state = new PROCESS_POWER_THROTTLING_STATE
                {
                    Version = 1,
                    ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED,
                    StateMask = 0 // Limpa o bit de execução lenta
                };

                if (!SetProcessInformation(p.Handle, ProcessPowerThrottling, ref state, (uint)Marshal.SizeOf(state)))
                {
                    error = $"SetProcessInformation(clear) falhou (Win32={Marshal.GetLastWin32Error()}).";
                    return false;
                }

                _ecoQoSAppliedPids.Remove(p.Id);
                return true;
            }
            catch (Exception ex)
            {
                error = $"{ex.GetType().Name}: {ex.Message}";
                return false;
            }
        }

        private async Task TrimNonCriticalProcessesAsync()
        {
            await Task.Run(async () =>
            {
                var processes = Process.GetProcesses();
                int count = 0;
                foreach (var p in processes)
                {
                    try
                    {
                        if (p.Id <= 4 || p.HasExited) { p.Dispose(); continue; }
                        if (_criticalProcesses.Contains(p.ProcessName)) { p.Dispose(); continue; }
                        
                        SetProcessWorkingSetSize(p.Handle, (IntPtr)(-1), (IntPtr)(-1));
                        p.Dispose();
                    }
                    catch { }
                    
                    count++;
                    // Cooperative yield to avoid thread starvation and OS stutter
                    if (count % 15 == 0)
                        await Task.Yield();
                }
            });
        }

        /// <summary>
        /// Purga real da Standby List (Usado apenas Fora de Jogos ou Pré-Jogo).
        /// </summary>
        public void CleanStandbyList()
        {
            try
            {
                Utils.PrivilegeHelper.EnablePrivilege(Utils.PrivilegeHelper.SE_PROFILE_SINGLE_PROCESS_NAME);

                var cmd1 = MemoryPurgeStandbyList;
                NtSetSystemInformation(SystemMemoryListInformation, ref cmd1, Marshal.SizeOf<int>());

                var cmd2 = MemoryPurgeLowPriorityStandbyList;
                NtSetSystemInformation(SystemMemoryListInformation, ref cmd2, Marshal.SizeOf<int>());
                
                _logger.LogInfo("[MemoryCoordinator] Standby List purgada com sucesso.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[MemoryCoordinator] Falha ao limpar Standby List: {ex.Message}");
            }
        }
    }
}
