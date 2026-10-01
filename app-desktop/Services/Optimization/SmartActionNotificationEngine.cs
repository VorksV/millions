using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Core;
using VoltrisOptimizer.Core.Constants;
using VoltrisOptimizer.Utils.Win32;
using VoltrisOptimizer.Services.Power;

namespace VoltrisOptimizer.Services.Optimization
{
    /// <summary>
    /// Motor Proativo de Ações Inteligentes e Notificações de Impacto Real.
    /// Monitora o estado do PC e aplica correções automáticas em tempo real com comprovação métrica.
    /// </summary>
    public sealed class SmartActionNotificationEngine : IDisposable
    {
        private static readonly Lazy<SmartActionNotificationEngine> _instance =
            new(() => new SmartActionNotificationEngine(App.LoggingService));
        public static SmartActionNotificationEngine Instance => _instance.Value;

        private readonly ILoggingService? _logger;
        private readonly object _lock = new();
        private CancellationTokenSource? _cts;
        private Task? _workerTask;
        private volatile bool _isRunning;

        // Cooldowns independentes para evitar fadiga de notificações
        private DateTime _lastRamNotificationTime = DateTime.MinValue;
        private DateTime _lastThermalNotificationTime = DateTime.MinValue;
        private DateTime _lastFocusNotificationTime = DateTime.MinValue;

    private static readonly TimeSpan RamNotificationCooldown = TimeSpan.FromMinutes(4);
    private static readonly TimeSpan ThermalNotificationCooldown = TimeSpan.FromMinutes(8);
    private static readonly TimeSpan FocusNotificationCooldown = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Limite seguro de temperatura da CPU, em °C. Alinhado com o limiar
    /// "crítico" do ThermalIndicatorConverter e dos cartões de temperatura do
    /// Dashboard, para que a notificação, os cartões e o código de alívio
    /// usem o mesmo número. Acima daqui o processador passa a reduzir a
    /// frequência por segurança própria.
    /// </summary>
    private const double TemperaturaMaximaCpuSegura = 95.0;

        // Contadores de sustentação (evita disparos por picos de 100ms)
        private int _consecutiveHighRamSamples = 0;
        private int _consecutiveHighThermalSamples = 0;

        // Histórico acumulado de ganhos reais da sessão
        public long TotalRamFreedMbSession { get; private set; } = 0;
        public int TotalSmartActionsExecuted { get; private set; } = 0;

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);

        // Lista de processos críticos do sistema que nunca devem ser tocados
        private static readonly HashSet<string> ExcludedSystemProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "System", "Idle", "Registry", "smss", "csrss", "wininit", "services",
            "lsass", "svchost", "fontdrvhost", "dwm", "explorer", "sihost",
            "taskhostw", "RuntimeBroker", "SearchHost", "StartMenuExperienceHost",
            "ShellExperienceHost", "ctfmon", "SecurityHealthSystray", "SecurityHealthService",
            "VoltrisOptimizer", "devenv", "vshost"
        };

        public SmartActionNotificationEngine(ILoggingService? logger)
        {
            _logger = logger;
            _logger?.LogInfo("[SmartActionEngine] Inicializado. Pronto para otimizações autônomas com comprovação.");
        }

        public void Start()
        {
            lock (_lock)
            {
                if (_isRunning) return;
                _isRunning = true;
                _cts = new CancellationTokenSource();
            }

            _logger?.LogInfo("[SmartActionEngine] Loop autônomo iniciado (vigilância de hardware 100% nativa).");
            _workerTask = Task.Run(() => WatchdogLoopAsync(_cts.Token));
        }

        public void Stop()
        {
            lock (_lock)
            {
                if (!_isRunning) return;
                _isRunning = false;
                _cts?.Cancel();
            }

            _logger?.LogInfo("[SmartActionEngine] Parado com sucesso.");
        }

        private async Task WatchdogLoopAsync(CancellationToken ct)
        {
            // Aguarda 10s pós-boot para deixar a inicialização do Windows assentar
            await Task.Delay(10000, ct).ConfigureAwait(false);

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    EvaluateSmartTriggers();
                    await Task.Delay(4000, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger?.LogError($"[SmartActionEngine] Erro no ciclo de monitoramento inteligente: {ex.Message}", ex);
                    await Task.Delay(6000, ct).ConfigureAwait(false);
                }
            }
        }

        private void EvaluateSmartTriggers()
        {
            var memStatus = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)) };
            if (!GlobalMemoryStatusEx(ref memStatus)) return;

            double ramLoad = memStatus.dwMemoryLoad;
            double availMb = memStatus.ullAvailPhys / (1024.0 * 1024.0);
            double cpuTemp = SystemMetricsCache.Instance.CpuTemperature;

            // ── GATILHO 1: Pressão Crítica de Memória RAM (> 80% ou < 1.5 GB livres) ──
            if (ramLoad >= 80.0 || availMb < 1500.0)
            {
                _consecutiveHighRamSamples++;
                _logger?.LogTrace($"[SmartActionEngine] Pressão de RAM detectada ({ramLoad:F0}%, {availMb:F0}MB livre, amostra {_consecutiveHighRamSamples}/2)");

                // Dispara após 2 amostras consecutivas (~8 segundos de sustentação)
                if (_consecutiveHighRamSamples >= 2)
                {
                    _consecutiveHighRamSamples = 0;
                    ExecuteIntelligentRamTrim(ramLoad, availMb);
                }
            }
            else
            {
                _consecutiveHighRamSamples = Math.Max(0, _consecutiveHighRamSamples - 1);
            }

            // ── GATILHO 2: Temperatura Elevada Sustentada (> 82°C) ──
            if (cpuTemp >= 82.0)
            {
                _consecutiveHighThermalSamples++;
                _logger?.LogTrace($"[SmartActionEngine] Temperatura alta detectada ({cpuTemp:F0}°C, amostra {_consecutiveHighThermalSamples}/2)");

                if (_consecutiveHighThermalSamples >= 2)
                {
                    _consecutiveHighThermalSamples = 0;
                    ExecuteThermalReliefAction(cpuTemp);
                }
            }
            else
            {
                _consecutiveHighThermalSamples = 0;
            }
        }

        /// <summary>
        /// Executa Trim real de Working Set de processos de fundo usando Psapi EmptyWorkingSet.
        /// Mede a memória antes e depois e emite notificação Toast ao usuário se o ganho for significativo.
        /// </summary>
        private void ExecuteIntelligentRamTrim(double ramLoadBefore, double availMbBefore)
        {
            var sw = Stopwatch.StartNew();
            _logger?.LogInfo($"[SmartActionEngine] [RAM-BOOST] Iniciando trim inteligente de RAM (Antes: {ramLoadBefore:F0}% usado, {availMbBefore:F0}MB livre)...");

            int foregroundPid = ForegroundWindowTracker.Instance.CurrentPid;
            int trimmedProcessesCount = 0;

            try
            {
                var processes = Process.GetProcesses();
                foreach (var proc in processes)
                {
                    try
                    {
                        // Não tocar no processo em primeiro plano nem em processos críticos do Windows
                        if (proc.Id == foregroundPid || ExcludedSystemProcesses.Contains(proc.ProcessName))
                            continue;

                        // Só otimizar processos em background com Working Set > 40MB
                        long wsBytes = proc.WorkingSet64;
                        if (wsBytes < 40 * 1024 * 1024)
                            continue;

                        IntPtr hProcess = ProcessNativeMethods.OpenProcess(
                            ProcessNativeMethods.PROCESS_SET_INFORMATION | ProcessNativeMethods.PROCESS_QUERY_LIMITED_INFORMATION,
                            false,
                            proc.Id);

                        if (hProcess != IntPtr.Zero)
                        {
                            try
                            {
                                if (EmptyWorkingSet(hProcess))
                                {
                                    trimmedProcessesCount++;
                                }
                            }
                            finally
                            {
                                ProcessNativeMethods.CloseHandle(hProcess);
                            }
                        }
                    }
                    catch
                    {
                        // Ignorar processos com acesso negado pelo OS
                    }
                    finally
                    {
                        proc.Dispose();
                    }
                }

                // Medição pós-trim com API física do Windows
                var memAfter = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)) };
                GlobalMemoryStatusEx(ref memAfter);

                double availMbAfter = memAfter.ullAvailPhys / (1024.0 * 1024.0);
                double ramFreedMb = Math.Max(0, availMbAfter - availMbBefore);

                sw.Stop();
                _logger?.LogSuccess($"[SmartActionEngine] [RAM-BOOST] Trim concluído em {sw.ElapsedMilliseconds}ms. {trimmedProcessesCount} processos otimizados. RAM Liberada: +{ramFreedMb:F0} MB.");

                if (ramFreedMb >= 100.0)
                {
                    TotalRamFreedMbSession += (long)ramFreedMb;
                    TotalSmartActionsExecuted++;

                    // Notificar o usuário se respeitar o cooldown
                    if ((DateTime.UtcNow - _lastRamNotificationTime) > RamNotificationCooldown)
                    {
                        _lastRamNotificationTime = DateTime.UtcNow;
                        string title = LocalizationService.Instance.GetString("SmartActionRamBoostTitle");
                        string msg = string.Format(LocalizationService.Instance.GetString("SmartActionRamBoostDesc"), ramFreedMb);
                        NotificationManager.Show(title, msg, NotificationType.Success);
                        _logger?.LogInfo($"[SmartActionEngine] Notificação de RAM enviada ao usuário: '{msg}'");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[SmartActionEngine] Exceção durante RAM Trim: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Executa mitigação térmica inteligente quando a CPU atinge temperaturas perigosas.
        /// </summary>
        private void ExecuteThermalReliefAction(double currentTemp)
        {
            _logger?.LogWarning($"[SmartActionEngine] [THERMAL-RELIEF] Ativando protocolo de alívio térmico para CPU em {currentTemp:F0}°C...");

            try
            {
                // [FIX:UNICO-DONO-DE-ENERGIA] Antes isto pedia troca de plano com
                // prioridade Emergency (0), que ficava ACIMA do Perfil Inteligente
                // (3): uma alçada térmica tirava o plano gerenciado do ar. O
                // Perfil é o único dono — e o alivio termico e exatamente o que o
                // Perfil já considera, pelo EPP e pelo cooling policy. Pedir de
                // novo seria escolher um plano por cima do dono.
                bool planoAplicado = ProfilePowerAuthority.RequestProfileApply(
                    "SmartActionThermalGuard", "alivio termico", _logger);
                if (planoAplicado)
                {
                    _logger?.LogSuccess("[SmartActionEngine] [THERMAL-RELIEF] Perfil Inteligente reaplicado para resfriamento.");
                }

                TotalSmartActionsExecuted++;

                if ((DateTime.UtcNow - _lastThermalNotificationTime) > ThermalNotificationCooldown)
                {
                    _lastThermalNotificationTime = DateTime.UtcNow;

                    // ── BUGS CORRIGIDOS NESTA NOTIFICAÇÃO ──
                    //
                    // 1) NÚMERO SEM FORMATAÇÃO. A mensagem usava
                    //      string.Format("...para {0}°", currentTemp)
                    //    com currentTemp sendo um double. O ToString() padrão
                    //    imprime todos os dígitos significativos, e o usuário viu
                    //    literalmente: "Temperatura reduzida para 85,6006305268735°".
                    //    Agora o valor é arredondado para inteiro e formatado com a
                    //    cultura ativa, então sai "86 °C" em PT/ES e "86 °C" em EN.
                    //
                    // 2) MENSAGEM FALSA. Dizia que a temperatura "foi reduzida",
                    //    mas a ação apenas trocou o plano de energia: a CPU
                    //    CONTINUA quente. Um número que não representa a reality
                    //    do sistema é pior que nenhum número.
                    //    A mensagem agora diz o que a medição é, o limite e o que
                    //    foi feito — sem afirmar que o problema já acabou.
                    //
                    // 3) SEM ÍCONE DE TEMPERATURA. Usava o triângulo genérico de
                    //    aviso. Agora usa o mesmo termômetro, com a mesma cor por
                    //    severidade, dos cartões de CPU/GPU do Dashboard.
                    string tempFormatada = currentTemp.ToString("F0", CultureInfo.CurrentCulture);
                    string limiteSeguro = TemperaturaMaximaCpuSegura.ToString("F0", CultureInfo.CurrentCulture);

                    bool estimada = SystemMetricsCache.Instance.IsCpuTemperatureEstimated;
                    string chave = estimada
                        ? "SmartActionThermalDescEstimated"
                        : "SmartActionThermalDesc";

                    string title = LocalizationService.Instance.GetString("SmartActionThermalTitle");
                    string msg = string.Format(
                        CultureInfo.CurrentCulture,
                        LocalizationService.Instance.GetString(chave),
                        tempFormatada,
                        limiteSeguro);

                    // Mesmo glifo e mesma cor dos cartões de temperatura do Dashboard.
                    var (glyph, accent) = VoltrisOptimizer.UI.Converters.ThermalGlyphResolver.Resolve(currentTemp);

                    VoltrisOptimizer.Services.GlobalNotificationService.Show(
                        title, msg, NotificationType.Warning, glyph, accent,
                        critical: false, contextualFilled: true);

                    _logger?.LogInfo(
                        $"[SmartActionEngine] Notificação Térmica enviada ao usuário: '{msg}' " +
                        $"(medida estimada={estimada}, plano aplicado={planoAplicado}, " +
                        $"cor do ícone={accent})");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[SmartActionEngine] Exceção durante alívio térmico: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Notifica o usuário de foco prioritário quando um aplicativo pesado ganha foco.
        /// </summary>
        public void NotifyFocusBoost(string appName, int pid)
        {
            if (string.IsNullOrWhiteSpace(appName)) return;

            if ((DateTime.UtcNow - _lastFocusNotificationTime) > FocusNotificationCooldown)
            {
                _lastFocusNotificationTime = DateTime.UtcNow;
                string title = LocalizationService.Instance.GetString("SmartActionFocusTitle");
                string msg = string.Format(LocalizationService.Instance.GetString("SmartActionFocusDesc"), appName, pid);
                NotificationManager.Show(title, msg, NotificationType.Info);
                _logger?.LogInfo($"[SmartActionEngine] Notificação de Foco enviada para {appName}");
            }
        }

        public void Dispose()
        {
            Stop();
            _cts?.Dispose();
        }
    }
}
