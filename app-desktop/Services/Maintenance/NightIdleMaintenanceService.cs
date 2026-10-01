using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Utils.Win32;

namespace VoltrisOptimizer.Services.Maintenance
{
    /// <summary>
    /// Resultado de uma rodada de Manutenção Noturna / Ociosa Profunda.
    /// </summary>
    public sealed class NightMaintenanceResult
    {
        public DateTime ExecutedAtUtc { get; init; } = DateTime.UtcNow;
        public long TempBytesCleaned { get; init; }
        public int FilesDeletedCount { get; init; }
        public int ProcessesCompactedCount { get; init; }
        public long RamCompactedMb { get; init; }
        public TimeSpan Duration { get; init; }
        public bool Success { get; init; }
        public string Summary { get; init; } = string.Empty;
    }

    /// <summary>
    /// Serviço de Otimização e Manutenção Silenciosa em Ociosidade / Horário Noturno.
    /// Executa limpezas profundas e compactação de memória reais (100% código C# e Win32 nativo, zero scripts).
    /// </summary>
    public sealed class NightIdleMaintenanceService : IDisposable
    {
        private static readonly Lazy<NightIdleMaintenanceService> _instance =
            new(() => new NightIdleMaintenanceService(App.LoggingService));
        public static NightIdleMaintenanceService Instance => _instance.Value;

        private readonly ILoggingService? _logger;
        private readonly object _lock = new();
        private CancellationTokenSource? _cts;
        private Task? _monitorTask;
        private volatile bool _isRunning;
        private DateTime _lastRunDate = DateTime.MinValue;

        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);

        public NightIdleMaintenanceService(ILoggingService? logger)
        {
            _logger = logger;
            _logger?.LogInfo("[NightMaintenance] Serviço de Manutenção Silenciosa Noturna instanciado.");
        }

        public void Start()
        {
            lock (_lock)
            {
                if (_isRunning) return;
                _isRunning = true;
                _cts = new CancellationTokenSource();
            }

            _logger?.LogInfo("[NightMaintenance] Monitor de ociosidade noturna iniciado.");
            _monitorTask = Task.Run(() => WatchdogLoopAsync(_cts.Token));
        }

        public void Stop()
        {
            lock (_lock)
            {
                if (!_isRunning) return;
                _isRunning = false;
                _cts?.Cancel();
            }

            _logger?.LogInfo("[NightMaintenance] Monitor de ociosidade noturna parado.");
        }

        private async Task WatchdogLoopAsync(CancellationToken ct)
        {
            // Delay inicial de 30 segundos
            await Task.Delay(30000, ct).ConfigureAwait(false);

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    // Checa se estamos na janela noturna (00:00 às 06:00) ou se o sistema está em ociosidade profunda (> 15 minutos sem input)
                    DateTime now = DateTime.Now;
                    bool isNightWindow = now.Hour >= 0 && now.Hour < 6;
                    double idleMinutes = GetSystemIdleMinutes();

                    // Se já executou hoje no mesmo dia, não repete
                    bool alreadyRanToday = _lastRunDate.Date == now.Date;

                    if (!alreadyRanToday && ((isNightWindow && idleMinutes >= 5.0) || idleMinutes >= 20.0))
                    {
                        _logger?.LogInfo($"[NightMaintenance] Condições atendidas: NightWindow={isNightWindow}, IdleMinutes={idleMinutes:F1}. Iniciando manutenção profunda...");
                        _lastRunDate = now;
                        await ExecuteDeepMaintenanceAsync(ct).ConfigureAwait(false);
                    }

                    // Checa a cada 5 minutos
                    await Task.Delay(TimeSpan.FromMinutes(5), ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger?.LogError($"[NightMaintenance] Erro no loop de manutenção: {ex.Message}", ex);
                    await Task.Delay(TimeSpan.FromMinutes(10), ct).ConfigureAwait(false);
                }
            }
        }

        private static double GetSystemIdleMinutes()
        {
            try
            {
                var lii = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO)) };
                if (GetLastInputInfo(ref lii))
                {
                    uint idleMs = (uint)Environment.TickCount - lii.dwTime;
                    return idleMs / 60000.0;
                }
            }
            catch { }
            return 0;
        }

        /// <summary>
        /// Executa o pipeline completo de manutenção profunda (100% C# nativo).
        /// </summary>
        public async Task<NightMaintenanceResult> ExecuteDeepMaintenanceAsync(CancellationToken ct = default)
        {
            var sw = Stopwatch.StartNew();
            _logger?.LogInfo("[NightMaintenance] ================= INICIANDO MANUTENÇÃO PROFUNDA =================");

            long totalBytesCleaned = 0;
            int totalFilesDeleted = 0;
            int compactedProcs = 0;

            // 1. Limpeza de Diretórios Temporários Reais (segura, apenas arquivos > 24h)
            try
            {
                var tempDirs = new List<string>
                {
                    Path.GetTempPath(),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "INetCache")
                };

                foreach (var dir in tempDirs)
                {
                    if (ct.IsCancellationRequested) break;
                    if (!Directory.Exists(dir)) continue;

                    _logger?.LogInfo($"[NightMaintenance] Varrer diretório temporário: {dir}");
                    var (cleaned, files) = CleanDirectorySafe(dir, TimeSpan.FromHours(24), ct);
                    totalBytesCleaned += cleaned;
                    totalFilesDeleted += files;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[NightMaintenance] Falha ao limpar diretórios temporários: {ex.Message}");
            }

            // 2. Compactação de Working Set de Processos Ociosos
            try
            {
                compactedProcs = CompactIdleProcessesWorkingSets();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[NightMaintenance] Falha na compactação de processos: {ex.Message}");
            }

            // 3. Limpeza de Logs Antigos (> 7 dias) do AppData Voltris
            try
            {
                string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Voltris", "Logs");
                if (Directory.Exists(logDir))
                {
                    var (cleanedLogs, filesLogs) = CleanDirectorySafe(logDir, TimeSpan.FromDays(7), ct);
                    totalBytesCleaned += cleanedLogs;
                    totalFilesDeleted += filesLogs;
                }
            }
            catch { }

            sw.Stop();

            double mbCleaned = totalBytesCleaned / (1024.0 * 1024.0);
            string summary = $"Manutenção concluída em {sw.ElapsedMilliseconds}ms: {mbCleaned:F1} MB liberados ({totalFilesDeleted} arquivos) e {compactedProcs} processos compactados.";
            _logger?.LogSuccess($"[NightMaintenance] ✅ {summary}");

            // Notifica de forma discreta o usuário sobre a manutenção noturna executada
            if (totalFilesDeleted > 0 || compactedProcs > 0)
            {
                NotificationManager.Show(
                    "🌙 Manutenção Silenciosa Concluída",
                    $"O Voltris liberou {mbCleaned:F1} MB e otimizou {compactedProcs} processos em segundo plano enquanto o PC estava ocioso.",
                    NotificationType.Success);
            }

            return new NightMaintenanceResult
            {
                ExecutedAtUtc = DateTime.UtcNow,
                TempBytesCleaned = totalBytesCleaned,
                FilesDeletedCount = totalFilesDeleted,
                ProcessesCompactedCount = compactedProcs,
                Duration = sw.Elapsed,
                Success = true,
                Summary = summary
            };
        }

        private (long bytes, int files) CleanDirectorySafe(string directoryPath, TimeSpan olderThan, CancellationToken ct)
        {
            long bytesFreed = 0;
            int filesDeleted = 0;

            try
            {
                var dirInfo = new DirectoryInfo(directoryPath);
                DateTime threshold = DateTime.UtcNow - olderThan;

                foreach (var file in dirInfo.EnumerateFiles("*", SearchOption.TopDirectoryOnly))
                {
                    if (ct.IsCancellationRequested) break;
                    try
                    {
                        if (file.LastWriteTimeUtc < threshold && file.LastAccessTimeUtc < threshold)
                        {
                            long len = file.Length;
                            file.Delete();
                            bytesFreed += len;
                            filesDeleted++;
                        }
                    }
                    catch
                    {
                        // Arquivo em uso ou bloqueado por outro processo: ignora com segurança
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogTrace($"[NightMaintenance] Erro ao enumerar {directoryPath}: {ex.Message}");
            }

            return (bytesFreed, filesDeleted);
        }

        private int CompactIdleProcessesWorkingSets()
        {
            int compacted = 0;
            try
            {
                var procs = Process.GetProcesses();
                foreach (var p in procs)
                {
                    try
                    {
                        if (p.WorkingSet64 > 50 * 1024 * 1024)
                        {
                            IntPtr h = ProcessNativeMethods.OpenProcess(
                                ProcessNativeMethods.PROCESS_SET_INFORMATION | ProcessNativeMethods.PROCESS_QUERY_LIMITED_INFORMATION,
                                false,
                                p.Id);

                            if (h != IntPtr.Zero)
                            {
                                try
                                {
                                    if (EmptyWorkingSet(h))
                                        compacted++;
                                }
                                finally
                                {
                                    ProcessNativeMethods.CloseHandle(h);
                                }
                            }
                        }
                    }
                    catch { }
                    finally
                    {
                        p.Dispose();
                    }
                }
            }
            catch { }
            return compacted;
        }

        public void Dispose()
        {
            Stop();
            _cts?.Dispose();
        }
    }
}
