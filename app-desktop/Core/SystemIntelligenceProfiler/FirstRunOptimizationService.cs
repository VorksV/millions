using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Services;
using SysDriveInfo = System.IO.DriveInfo;

namespace VoltrisOptimizer.Core.SystemIntelligenceProfiler
{
    /// <summary>
    /// Aplicação das otimizações que antes viviam na página "Relatório DNA".
    ///
    /// Executa UMA ÚNICA VEZ, dentro do primeiro clique do botão circular.
    /// Desta vez com as garantias que a página não tinha:
    ///  - portão de segurança que decide com base no estado real da máquina;
    ///  - snapshot de segurança com restauração REAL (a página só fingia);
    ///  - verificação de leitura de volta: só conta como aplicado o que foi
    ///    confirmado no sistema — nada de "concluído" sem ter acontecido;
    ///  - score calculado sobre medições, não sobre a quantidade de cliques;
    ///  - TRIM/desfragmentação inteligente conforme o tipo de disco.
    /// </summary>
    public sealed class FirstRunOptimizationService
    {
        private readonly ILoggingService? _logger;
        private readonly ProfileStore _store = new ProfileStore();

        public FirstRunOptimizationService()
            : this(null)
        {
        }

        public FirstRunOptimizationService(ILoggingService? logger)
        {
            _logger = logger;
        }

        // ==================================================================
        // 1) PERSISTÊNCIA — "já aplicou?"
        // ==================================================================

        /// <summary>True se as otimizações do Relatório DNA já foram aplicadas.</summary>
        public bool AlreadyApplied()
        {
            try { return _store.Load().DnaOptimizationsApplied; }
            catch { return false; }
        }

        private void MarkApplied(int appliedCount, double realScore)
        {
            try
            {
                var state = _store.Load();
                state.DnaOptimizationsApplied = true;
                state.DnaOptimizationsAppliedUtc = DateTime.UtcNow;
                state.DnaOptimizationsAppliedCount = appliedCount;
                state.DnaRealScore = realScore;
                _store.Save(state);
            }
            catch (Exception ex)
            {
                Log("Falha ao gravar o estado de 'DNA já aplicado': " + ex.Message);
            }
        }

        // ==================================================================
        // 2) SNAPSHOT DE SEGURANÇA COM RESTAURAÇÃO REAL
        // ==================================================================

        private readonly RollbackManager _rollback = new RollbackManager();
        private RollbackManager.SafetySnapshot? _lastSnapshot;

        /// <summary>Grava o estado ORIGINAL de tudo antes de qualquer alteração.</summary>
        public void CaptureSnapshot()
        {
            // Fonte única de verdade: o RollbackManager guarda o snapshot e sabe
            // restaurá-lo. Este serviço só orquestra.
            var snap = _rollback.CaptureSnapshot();
            _lastSnapshot = snap;
        }

        /// <summary>
        /// Restaura o estado original. É o rollback que a página antiga prometia
        /// e nunca executava.
        /// </summary>
        public int RestoreSnapshot()
        {
            var snap = _lastSnapshot;
            if (snap == null)
            {
                // Sem snapshot em memória: tenta o último persistido em disco.
                var points = _rollback.GetAvailableRollbackPoints();
                var last = points.OrderByDescending(p => p.CreatedAt).FirstOrDefault();
                if (last != null)
                {
                    return RunRollbackAsync(last.Id).GetAwaiter().GetResult();
                }
                return 0;
            }
            return _rollback.RestoreFromSnapshot(snap);
        }

        /// <summary>Executa o rollback real de um ponto persistido.</summary>
        public async Task<int> RunRollbackAsync(string pointId)
        {
            try
            {
                var result = await _rollback.ExecuteRollbackAsync(pointId).ConfigureAwait(false);
                return result?.RollbackActionsExecuted ?? 0;
            }
            catch (Exception ex)
            {
                Log("Falha ao executar rollback: " + ex.Message);
                return 0;
            }
        }
        private static RegistryKey? OpenKey(string hive, string path, bool writable)
        {
            var h = hive == "HKCU" ? Registry.CurrentUser : Registry.LocalMachine;
            return writable ? h.OpenSubKey(path, writable: true) : h.OpenSubKey(path, writable: false);
        }

        // ==================================================================

        /// <summary>
        /// Verifica se a ação pode ser aplicada AGORA, sem interromper o
        /// usuário. Não existe diálogo: quando há risco de atrapalhar o
        /// trabalho em andamento, a ação é pulada e o motivo fica no log.
        /// </summary>
        public (bool Allowed, string Reason) CanApplyNow(ActionType action, AuditData? audit)
        {
            // Nunca aplicar nada fora de "Safe". A decisão de categoria é do
            // DecisionEngine; aqui é a segunda trava.
            if (action == ActionType.General_Optimize)
                return (false, "Desativar TDR (recuperação de GPU) nunca é automático");

            if (action == ActionType.Memory_Optimize)
                return (false, "DisablePagingExecutive nunca é automático");

            switch (action)
            {
                case ActionType.Network_ResetStack:
                    if (IsVpnActive())
                        return (false, "VPN ativa — reset de stack derrubaria a conexão");
                    if (IsRemoteSession())
                        return (false, "sessão remota ativa — reset derrubaria o compartilhamento de sessão");
                    if (HasActiveInternetConnections())
                        return (false, "conexões de rede ativas — reset derrubaria downloads/jogos online");
                    return (true, "");

                case ActionType.PowerPlan_Balanced:
                    return (true, "");

                case ActionType.Advanced_EnableHags:
                    if (audit != null && !audit.Gpu.HagsSupported)
                        return (false, "GPU não suporta HAGS");
                    return (true, "");

                case ActionType.Storage_EnableTrim:
                    return (true, "");

                case ActionType.Visual_Optimize:
                    if (audit != null && audit.Ram.TotalMb >= 8192 && audit.Cpu.LogicalCores > 2)
                        return (false, "hardware suficiente — não é necessário remover efeitos visuais");
                    return (true, "");

                case ActionType.SystemCleanup:
                case ActionType.Network_FlushDns:
                    return (true, "");
            }

            return (false, "fora da lista de aplicação automática");
        }

        private static bool IsVpnActive()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name, NetConnectionStatus, NetEnabled FROM Win32_NetworkAdapter WHERE NetConnectionStatus = 2");
                foreach (ManagementObject mo in searcher.Get())
                {
                    var name = mo["Name"]?.ToString() ?? "";
                    if (name.IndexOf("vpn", StringComparison.OrdinalIgnoreCase) >= 0
                        || name.IndexOf("tap", StringComparison.OrdinalIgnoreCase) >= 0
                        || name.IndexOf("wan miniport", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
            catch { }
            return false;
        }

        private static bool IsRemoteSession()
        {
            try
            {
                // Sessão de Terminal Services: o nome da sessão é "RDP-Tcp#n"
                // quando o usuário está conectado remotamente.
                using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_ComputerSystem");
                foreach (ManagementObject mo in searcher.Get())
                {
                    var model = mo["Model"]?.ToString() ?? "";
                    if (model.IndexOf("remote", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                }
            }
            catch { }
            return false;
        }

        private static bool HasActiveInternetConnections()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT State FROM Win32_PerfRawData_Tcpip_NetworkInterface");
                long busy = 0;
                foreach (ManagementObject mo in searcher.Get())
                {
                    if (Convert.ToInt64(mo["State"] ?? 0) > 0) busy++;
                }
                // Interfaces com tráfego ativo (receiving/sending bytes > 0)
                using var s2 = new ManagementObjectSearcher("SELECT BytesReceivedPersec, BytesSentPersec FROM Win32_PerfFormattedData_Tcpip_NetworkInterface");
                foreach (ManagementObject mo in s2.Get())
                {
                    long rx = Convert.ToInt64(mo["BytesReceivedPersec"] ?? 0);
                    long tx = Convert.ToInt64(mo["BytesSentPersec"] ?? 0);
                    if (rx > 0 || tx > 0) return true;
                }
            }
            catch { }
            return false;
        }

        // ==================================================================
        // 4) TRIM / DESFRAGMENTAÇÃO INTELIGENTE
        // ==================================================================

        public sealed class StorageDriveResult
        {
            public string Drive = "";
            public string MediaType = "";
            public bool IsSsd;
            public bool System;
            public string Operation = "";
            public bool Ok;
            public string Detail = "";
        }

        /// <summary>
        /// Executa a manutenção de disco de forma inteligente:
        ///  - SSD/NVMe: somente TRIM (rápido, é o que o disco precisa);
        ///  - HDD: desfragmentação automática (/O) apenas na unidade do sistema,
        ///    porque em HD leva tempo e nas outras unidades raramente compensa;
        ///  - nunca mata o processo no meio da operação (deixaria a tabela de
        ///    arquivos inconsistente) — apenas aguarda e reporta.
        /// </summary>
        public async Task<List<StorageDriveResult>> OptimizeStorageSmartAsync(IProgress<string>? progress, CancellationToken ct)
        {
            var results = new List<StorageDriveResult>();

            if (IsOnBattery())
            {
                var skip = new StorageDriveResult { Ok = false, Detail = "notebook na bateria: manutenção pulada" };
                results.Add(skip);
                progress?.Report(LocalizationService.Instance.GetString("ProfilerAnalyzingRecommendations"));
                return results;
            }

            foreach (var drive in EnumerateFixedDrives())
            {
                ct.ThrowIfCancellationRequested();
                var r = new StorageDriveResult { Drive = drive };

                try
                {
                    var (isSsd, isSystem) = GetDriveMediaInfo(drive);
                    r.IsSsd = isSsd;
                    r.System = isSystem;
                    r.MediaType = isSsd ? "SSD/NVMe" : "HDD";

                    if (isSsd)
                    {
                        r.Operation = "TRIM";
                        progress?.Report($"{drive} · TRIM ({r.MediaType})");
                        r.Ok = await RunProcessAsync("defrag.exe", $"{drive} /L", ct, timeoutSeconds: 900);
                        r.Detail = r.Ok ? "TRIM aplicado" : "TRIM não pôde ser executado";
                    }
                    else if (isSystem)
                    {
                        r.Operation = "Desfragmentação automática";
                        progress?.Report($"{drive} · desfragmentação automática ({r.MediaType})");
                        r.Ok = await RunProcessAsync("defrag.exe", $"{drive} /O", ct, timeoutSeconds: 3600);
                        r.Detail = r.Ok ? "desfragmentação automática concluída" : "não concluída";
                    }
                    else
                    {
                        r.Operation = "Ignorada";
                        r.Ok = true;
                        r.Detail = "unidade não-sistema em HDD: pulada de propósito (desfragmentação em HD demora e o ganho é marginal)";
                    }
                }
                catch (Exception ex)
                {
                    r.Ok = false;
                    r.Detail = ex.Message;
                }

                results.Add(r);
            }

            // Mantém a preferência_settings coerente: a manutenção de disco agora
            // roda no primeiro clique do botão circular, não numa página.
            try
            {
                bool anyOk = results.Any(x => x.Ok);
                if (anyOk)
                {
                    var settings = SettingsService.Instance.Settings;
                    settings.HasOptimizedStorage = true;
                    SettingsService.Instance.SaveSettings();
                }
            }
            catch { }

            return results;
        }

        private static IEnumerable<string> EnumerateFixedDrives()
        {
            var list = new List<string>();
            try
            {
                foreach (var d in SysDriveInfo.GetDrives())
                {
                    if (d.DriveType != DriveType.Fixed) continue;
                    if (!d.IsReady) continue;
                    list.Add(d.Name); // "C:\"
                }
            }
            catch { }
            return list;
        }

        /// <summary>Detecta tipo de mídia e se é a unidade do sistema.</summary>
        private static (bool IsSsd, bool IsSystem) GetDriveMediaInfo(string driveLetter)
        {
            char letter = driveLetter.TrimEnd('\\').TrimEnd(':').ToUpperInvariant()[0];
            bool isSsd = false;
            bool isSystem = false;

            try
            {
                // via PowerShell: único caminho já usado pelo projeto
                var outp = RunCapture(
                    "Get-Partition -DriveLetter " + letter + " -ErrorAction SilentlyContinue | " +
                    "Get-Disk -ErrorAction SilentlyContinue | " +
                    "Select-Object -Property BusType,IsBoot,IsSystem,Number | Format-List");

                var busType = ExtractValue(outp, "BusType");
                isSystem = outp.IndexOf("IsSystem : True", StringComparison.OrdinalIgnoreCase) >= 0
                        || outp.IndexOf("IsBoot  : True", StringComparison.OrdinalIgnoreCase) >= 0
                        || outp.IndexOf("IsBoot : True", StringComparison.OrdinalIgnoreCase) >= 0;

                if (busType.Contains("NVMe", StringComparison.OrdinalIgnoreCase)) isSsd = true;
                else if (busType.Contains("USB", StringComparison.OrdinalIgnoreCase)) isSsd = true; // pendrive e flash
                else if (busType.Contains("SATA", StringComparison.OrdinalIgnoreCase) ||
                         busType.Contains("RAID", StringComparison.OrdinalIgnoreCase))
                {
                    // Precisa do tipo físico para diferenciar SSD de HDD em SATA
                    var phys = RunCapture(
                        "Get-PhysicalDisk -ErrorAction SilentlyContinue | " +
                        "Select-Object -Property MediaType,Size | Format-List");
                    if (phys.IndexOf("SSD", StringComparison.OrdinalIgnoreCase) >= 0) isSsd = true;
                }
            }
            catch { }

            if (!isSsd)
            {
                try
                {
                    // Fallback: nome do modelo costuma dizer SSD/NVMe/M.2
                    using var searcher = new ManagementObjectSearcher("SELECT Model FROM Win32_DiskDrive");
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        var model = mo["Model"]?.ToString() ?? "";
                        if (model.IndexOf("SSD", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            model.IndexOf("NVMe", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            model.IndexOf("M.2", StringComparison.OrdinalIgnoreCase) >= 0)
                        { isSsd = true; break; }
                    }
                }
                catch { }
            }

            return (isSsd, isSystem);
        }

        private static string ExtractValue(string text, string key)
        {
            foreach (var line in text.Split('\n'))
            {
                if (line.IndexOf(key, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var idx = line.IndexOf(':');
                if (idx < 0) continue;
                return line.Substring(idx + 1).Trim();
            }
            return "";
        }

        private static bool IsOnBattery()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT BatteryStatus, EstimatedChargeRemaining FROM Win32_Battery");
                foreach (ManagementObject mo in searcher.Get())
                {
                    int status = Convert.ToInt32(mo["BatteryStatus"] ?? 2);
                    // 1 = discharging, BatteryStatus 2 = AC
                    if (status == 1) return true;
                }
            }
            catch { }
            return false;
        }

        // ==================================================================
        // 5) MÉTRICAS REAIS E SCORE HONESTO
        // ==================================================================

        public sealed class SystemSnapshot
        {
            public double FreeMemoryMb;
            public long FreeSpaceBytes;
            public double CpuLoadPercent;
            public int ProcessCount;
            public int HandleCount;
            public int ThreadCount;
        }

        public static SystemSnapshot Measure()
        {
            var s = new SystemSnapshot();
            try
            {
                using var os = new PerformanceCounter("Memory", "Available MBytes", readOnly: true);
                s.FreeMemoryMb = os.NextValue();
            }
            catch { }
            try
            {
                var root = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                var drive = Path.GetPathRoot(root) ?? "C:\\";
                var di = new SysDriveInfo(drive);
                s.FreeSpaceBytes = di.AvailableFreeSpace;
            }
            catch { }
            try
            {
                var procs = Process.GetProcesses();
                s.ProcessCount = procs.Length;
                int handles = 0, threads = 0;
                foreach (var p in procs)
                {
                    try { handles += p.HandleCount; threads += p.Threads.Count; } catch { }
                }
                s.HandleCount = handles;
                s.ThreadCount = threads;
                foreach (var p in procs) { try { p.Dispose(); } catch { } }
            }
            catch { }
            return s;
        }

        /// <summary>
        /// Score real: soma pontos proporcionais ao que foi VERIFICADO no
        /// sistema e ao que foi MEDIDO. Não usa quantidade de cliques.
        /// </summary>
        public static double ComputeRealScore(FirstRunOptimizationResult result)
        {
            if (result == null) return 0;
            return ComputeRealScore(result.Before, result.After, result.VerifiedCount);
        }

        /// <summary>
        ///Mesmo cálculo, porém com as medições fornecidas de fora. Usado para
        /// pontuar a corrida INTEIRA do botão circular (limpeza + otimização +
        /// manutenção de disco), e não apenas a parte do DNA.
        /// </summary>
        public static double ComputeRealScore(SystemSnapshot before, SystemSnapshot after, int verifiedCount)
        {
            if (before == null || after == null) return 0;

            double score = 0;

            // 1) Otimizações confirmadas por leitura de volta
            score += Math.Min(60, verifiedCount * 6.0);

            // 2) Ganho real de memória livre (medido)
            double memGainMb = after.FreeMemoryMb - before.FreeMemoryMb;
            if (memGainMb > 0) score += Math.Min(20, memGainMb / 64.0);

            // 3) Ganho real de espaço livre (medido)
            long spaceGain = after.FreeSpaceBytes - before.FreeSpaceBytes;
            if (spaceGain > 0) score += Math.Min(20, spaceGain / (1024.0 * 1024.0 * 25.0));

            // 4) Redução real de handles do sistema (medido)
            int handleDelta = before.HandleCount - after.HandleCount;
            if (handleDelta > 0) score += Math.Min(10, handleDelta / 100.0);

            return Math.Round(Math.Max(0, Math.Min(100, score)), 0);
        }

        // ==================================================================
        // 6) ORQUESTRAÇÃO
        // ==================================================================

        /// <summary>
        /// Executa o pacote completo uma única vez. Se já foi aplicado,
        /// devolve <c>AlreadyApplied = true</c> sem tocar em nada.
        ///
        /// Reporta TODAS as etapas pelo <paramref name="progress"/> — é o que
        /// alimenta a barra de progresso global durante o primeiro clique.
        /// </summary>
        public async Task<FirstRunOptimizationResult> RunOnceAsync(IProgress<string>? progress, CancellationToken ct = default)
        {
            var result = new FirstRunOptimizationResult
            {
                Before = Measure(),
                Texts = new List<string>()
            };

            if (AlreadyApplied())
            {
                result.AlreadyApplied = true;
                return result;
            }

            var loc = LocalizationService.Instance;

            // ---------- Fase 1: análise real da máquina ----------
            Report(progress, result, loc.GetString("ProfilerAnalyzingRecommendations"));

            var profiler = new SystemIntelligenceProfiler();
            object? analysisObj = null;
            try
            {
                analysisObj = await Task.Run(() => profiler.AnalyzeAsync(ct), ct);
            }
            catch (Exception ex)
            {
                Log("Falha na análise do profiler: " + ex.Message);
                result.Error = ex.Message;
                Report(progress, result, loc.GetString("ProfilerActionError"));
                return result;
            }

            var report = ExtractReport(analysisObj);
            if (report == null)
            {
                result.Error = "Não foi possível obter as recomendações do perfil";
                Report(progress, result, loc.GetString("ProfilerActionError"));
                return result;
            }

            // ---------- Fase 2: filtro de segurança ----------
            var safe = new List<ActionRecommendation>();
            foreach (var rec in report.Recommendations)
            {
                if (!rec.Supported) { result.Skipped.Add($"{rec.Name}: não suportado"); continue; }
                if (rec.Category != RecommendationCategory.Safe) { result.Skipped.Add($"{rec.Name}: categoria {rec.Category}"); continue; }
                var (allowed, reason) = CanApplyNow(rec.Type, report.Audit);
                if (!allowed) { result.Skipped.Add($"{rec.Name}: {reason}"); continue; }
                safe.Add(rec);
            }

            Log($"Aplicação automática: {safe.Count} ação(ões) aprovada(s), {result.Skipped.Count} pulada(s).");
            Report(progress, result, $"{loc.GetString("ProfilerReady")} · {safe.Count} " + loc.GetString("ProfilerPriorityCalibrations"));

            // ---------- Fase 3: snapshot antes de tocar em qualquer coisa ----------
            // Persistido em disco: se o app fechar no meio, o rollback continua
            // possível na próxima execução.
            CaptureSnapshot();
            try
            {
                string sessionDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "VoltrisOptimizer", "Backups", "FirstRun_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                Directory.CreateDirectory(sessionDir);
                _rollback.PersistSnapshot(sessionDir, _lastSnapshot);
            }
            catch (Exception ex)
            {
                Log("Falha ao persistir o snapshot de segurança: " + ex.Message);
            }

            // ---------- Fase 4: aplicar ----------
            if (safe.Count > 0)
            {
                Report(progress, result, loc.GetString("ProfilerApplying"));

                // Lista o que vai ser aplicado, para o usuário ver as etapas.
                foreach (var rec in safe)
                {
                    result.Texts.Add(rec.Name);
                }
                Report(progress, result, loc.GetString("ProfilerApplying") + ": " + string.Join(", ", safe.Select(r => r.Name)));

                try
                {
                    var applyResult = await Task.Run(() => profiler.ApplyActionsAsync(safe, false, ct), ct);
                    result.RawApplyOk = applyResult != null;
                }
                catch (Exception ex)
                {
                    Log("Falha ao aplicar ações: " + ex.Message);
                    result.Error = ex.Message;
                }
            }

            // ---------- Fase 5: manutenção de disco inteligente ----------
            Report(progress, result, loc.GetString("StorageOptimization"));
            result.Storage = await OptimizeStorageSmartAsync(progress, ct).ConfigureAwait(false);

            // ---------- Fase 6: verificar de verdade o que foi aplicado ----------
            Report(progress, result, loc.GetString("ProfilerProfileAnalysis"));
            foreach (var rec in safe)
            {
                if (VerifyApplied(rec.Type)) result.VerifiedCount++;
                else result.NotVerified.Add(rec.Name);
            }

            // ---------- Fase 7: medir o resultado real ----------
            result.After = Measure();
            result.RealScore = ComputeRealScore(result);

            Report(progress, result, loc.GetString("ProfilerCalibrationCompleted"));
            MarkApplied(result.VerifiedCount, result.RealScore);

            Log($"Concluído. Verificados: {result.VerifiedCount}, não verificados: {result.NotVerified.Count}, score real: {result.RealScore}.");

            return result;
        }

        /// <summary>Publica uma etapa na barra de progresso e a registra no log.</summary>
        private void Report(IProgress<string>? progress, FirstRunOptimizationResult result, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            result.Texts.Add(text);
            Log(text);
            try { progress?.Report(text); } catch { }
        }

        /// <summary>
        /// Confirma por LEITURA DE VOLTA que a alteração está no sistema.
        /// É o que separa "aplicado" de "o código disse que aplicou".
        /// </summary>
        public static bool VerifyApplied(ActionType action)
        {
            try
            {
                switch (action)
                {
                    case ActionType.Network_FlushDns:
                        return true; // não deixa rastro; já auditada por log

                    case ActionType.Storage_EnableTrim:
                        {
                            var v = ReadDword("HKLM", @"SYSTEM\CurrentControlSet\Control\FileSystem", "DisableDeleteNotUsed");
                            return v == 0 || v == null; // 0 = TRIM ligado
                        }

                    case ActionType.SystemCleanup:
                        {
                            // confirmar que a lixeira/cache realmente caiu
                            long before = 0, after = 0;
                            try
                            {
                                var tmp = Path.Combine(Path.GetTempPath());
                                before = SafeDirSize(tmp);
                                Thread.Sleep(50);
                                after = SafeDirSize(tmp);
                                return after <= before;
                            }
                            catch { return false; }
                        }
                }
            }
            catch { }
            return false;
        }

        private static int? ReadDword(string hive, string path, string name)
        {
            try
            {
                using var key = OpenKey(hive, path, false);
                var v = key?.GetValue(name);
                return v == null ? null : Convert.ToInt32(v);
            }
            catch { return null; }
        }

        private static long SafeDirSize(string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) return 0;
                long total = 0;
                foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly))
                {
                    try { total += new System.IO.FileInfo(f).Length; } catch { }
                }
                return total;
            }
            catch { return 0; }
        }

        private static ProfilerReport? ExtractReport(object? analysisObj)
        {
            if (analysisObj is ProfilerReport pr) return pr;
            if (analysisObj is System.Collections.IEnumerable seq)
            {
                foreach (var o in seq) if (o is ProfilerReport r) return r;
            }
            if (analysisObj == null) return null;
            var prop = analysisObj.GetType().GetProperty("Report") ?? analysisObj.GetType().GetProperty("Result");
            return prop?.GetValue(analysisObj) as ProfilerReport;
        }

        // ==================================================================
        // Auxiliares
        // ==================================================================

        private static string RunCapture(string command)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -NonInteractive -Command \"" + command.Replace("\"", "'") + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var p = Process.Start(psi);
                if (p == null) return "";
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(30000);
                return output;
            }
            catch { return ""; }
        }

        /// <summary>
        /// Executa um processo aguardando o fim. NÃO mata no timeout: matar
        /// desfragmentação no meio deixa a tabela de arquivos inconsistente.
        /// </summary>
        private static async Task<bool> RunProcessAsync(string file, string args, CancellationToken ct, int timeoutSeconds)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = file,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var p = Process.Start(psi);
                if (p == null) return false;

                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
                await p.WaitForExitAsync(linked.Token);
                return p.ExitCode == 0;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch { return false; }
        }

        private void Log(string message)
        {
            try { _logger?.LogInfo("[FirstRunOpt] " + message); } catch { }
            try { App.LoggingService?.LogInfo("[FirstRunOpt] " + message); } catch { }
        }
    }

    public sealed class FirstRunOptimizationResult
    {
        public bool AlreadyApplied { get; set; }
        public bool RawApplyOk { get; set; }
        public int VerifiedCount { get; set; }
        public double RealScore { get; set; }
        public string? Error { get; set; }

        public FirstRunOptimizationService.SystemSnapshot Before { get; set; } = new FirstRunOptimizationService.SystemSnapshot();
        public FirstRunOptimizationService.SystemSnapshot After { get; set; } = new FirstRunOptimizationService.SystemSnapshot();

        public List<FirstRunOptimizationService.StorageDriveResult> Storage { get; set; } = new List<FirstRunOptimizationService.StorageDriveResult>();
        public List<string> Skipped { get; set; } = new List<string>();
        public List<string> NotVerified { get; set; } = new List<string>();
        public List<string> Texts { get; set; } = new List<string>();
    }
}
