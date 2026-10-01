using VoltrisOptimizer.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.Thermal;
using VoltrisOptimizer.Services.Thermal.Models;
using VoltrisOptimizer.Helpers;
using System.IO;
using VoltrisOptimizer.Core;
using VoltrisOptimizer.Services.Monitoring.Interfaces;

namespace VoltrisOptimizer.Services
{
    public class GameDiagnosticsService
    {
        private readonly ILoggingService _logger;

        public class ProcessInfo
        {
            public string Name { get; set; } = "";
            public double CpuPercent { get; set; }
            public long RamBytes { get; set; }
            public bool IsPrivateMemory { get; set; }
            public int Pid { get; set; }
        }

        public class Sample
        {
            public DateTime T { get; set; }
            public double CpuPercent { get; set; }
            public double CpuQueue { get; set; }
            public double CpuCurrentMhz { get; set; }
            public double CpuMaxMhz { get; set; }
            public double CpuDpcPercent { get; set; }
            public double CpuInterruptPercent { get; set; }
            public double CpuTemperature { get; set; }
            public bool CpuThrottling { get; set; }
            public double RamUsedGb { get; set; }
            public double RamStandbyGb { get; set; }
            public double RamTotalGb { get; set; }
            public double RamPageFaultsPerSec { get; set; }
            public double DiskReadsPerSec { get; set; }
            public double DiskWritesPerSec { get; set; }
            public double DiskQueueLen { get; set; }
            public double DiskLatencySec { get; set; }
            public double GpuUtilPercent { get; set; }
            public double GpuVramUsedMb { get; set; }
            public double GpuVramTotalMb { get; set; }
            public double GpuTemperature { get; set; }
            public bool GpuThrottling { get; set; }
            public double NetJitterMs { get; set; }
            public double Fps { get; set; }
            public GameDiagnosticsService.DiagnosticCause Cause { get; set; } = GameDiagnosticsService.DiagnosticCause.Undefined;
            public List<ProcessInfo> TopProcesses { get; set; } = new();
        }

        public event Action<IReadOnlyList<Sample>>? SamplesUpdated;

        private readonly List<Sample> _samples = new List<Sample>(600);
        private readonly object _samplesLock = new object();
        private CancellationTokenSource? _cts;
        private Task? _pmTask;
        private IEtwFrameTimeMonitor? _fpsMonitor;
        private double _currentFps;
        
        private readonly IThermalMonitorService _thermalMonitor;
        private readonly NativeSystemMetrics _nativeMetrics = new();
        private readonly DiagnosticPersistenceService _persistence;
        
        public GameDiagnosticsService(IThermalMonitorService? thermalMonitor = null)
        {
            _logger = App.LoggingService ?? throw new InvalidOperationException("LoggingService não disponível");
            _logger.LogInfo("[GameDiagnosticsService] Inicializando servico de diagnostico");
            _thermalMonitor = thermalMonitor ?? App.ThermalMonitorService!;
            _persistence = new DiagnosticPersistenceService();
            _fpsMonitor = ServiceLocator.GetService<IEtwFrameTimeMonitor>();
            _logger.LogInfo($"[GameDiagnosticsService] Inicializado. ThermalMonitor: {(_thermalMonitor != null ? "OK" : "NULO")}, FpsMonitor: {(_fpsMonitor != null ? "OK" : "NULO")}");
        }
        
        public void Start(string? gameName = null)
        {
            Stop();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            if (_fpsMonitor != null) _fpsMonitor.MetricsUpdated += OnFpsMetricsUpdated;
            
            // Iniciar sessão de diagnóstico
            _persistence.StartSession(gameName);
            
            Task.Run(() => RunLoop(token), token);
            // Legacy PresentMon FPS capture disabled
        }

        public void Stop()
        {
            try { _cts?.Cancel(); } catch { }
            _cts = null;
            try { _pmTask?.Dispose(); } catch { }
        if (_fpsMonitor != null) _fpsMonitor.MetricsUpdated -= OnFpsMetricsUpdated;
            _pmTask = null;
            
            // Finalizar sessão de diagnóstico
            try { _persistence?.EndSession(); } catch { }
        }

        private async Task RunLoop(CancellationToken token)
        {
            SafePerformanceCounter? cpu = null;
            SafePerformanceCounter? qlen = null;
            SafePerformanceCounter? dpc = null;
            SafePerformanceCounter? intr = null;
            SafePerformanceCounter? pf = null;
            SafePerformanceCounter? diskR = null;
            SafePerformanceCounter? diskW = null;
            SafePerformanceCounter? diskQ = null;
            SafePerformanceCounter? diskLat = null;
            List<SafePerformanceCounter>? gpu3d = null;
            List<SafePerformanceCounter>? vramUsed = null;
            SafePerformanceCounter? ramAvailCombined = null;
            double maxMhz = 0;
            double totalRamGb = 0;
            double vramTot = 0;
            try
            {
                cpu = new SafePerformanceCounter("Processor", "% Processor Time", "_Total"); cpu.NextValue();
                qlen = new SafePerformanceCounter("System", "Processor Queue Length"); qlen.NextValue();
                try { dpc = new SafePerformanceCounter("Processor", "% DPC Time", "_Total"); dpc.NextValue(); } catch { }
                try { intr = new SafePerformanceCounter("Processor", "% Interrupt Time", "_Total"); intr.NextValue(); } catch { }
                try { pf = new SafePerformanceCounter("Memory", "Page Faults/sec"); pf.NextValue(); } catch { }
                try { diskR = new SafePerformanceCounter("PhysicalDisk", "Disk Reads/sec", "_Total"); diskR.NextValue(); } catch { }
                try { diskW = new SafePerformanceCounter("PhysicalDisk", "Disk Writes/sec", "_Total"); diskW.NextValue(); } catch { }
                try { diskQ = new SafePerformanceCounter("PhysicalDisk", "Avg. Disk Queue Length", "_Total"); diskQ.NextValue(); } catch { }
                try { diskLat = new SafePerformanceCounter("PhysicalDisk", "Avg. Disk sec/Transfer", "_Total"); diskLat.NextValue(); } catch { }
                try
                {
                    var cat = new SafePerformanceCounterCategory("GPU Engine");
                    gpu3d = new List<SafePerformanceCounter>();
                    foreach (var inst in cat.GetInstanceNames())
                    {
                        if (inst.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase))
                        {
                            try { gpu3d.Add(new SafePerformanceCounter("GPU Engine", "Utilization Percentage", inst)); } catch { }
                        }
                    }
                }
                catch { gpu3d = null; }
                try
                {
                    var catMem = new SafePerformanceCounterCategory("GPU Adapter Memory");
                    vramUsed = new List<SafePerformanceCounter>();
                    foreach (var inst in catMem.GetInstanceNames())
                    {
                        try { vramUsed.Add(new SafePerformanceCounter("GPU Adapter Memory", "Dedicated Usage", inst)); } catch { }
                    }
                }
                catch { vramUsed = null; }
                try
                {
                    using var s = new ManagementObjectSearcher("SELECT MaxClockSpeed FROM Win32_Processor");
                    foreach (ManagementObject o in s.Get()) {
                using var __dispose_o = o; maxMhz = Convert.ToDouble(o["MaxClockSpeed"] ?? 0); break; }
                }
                catch { maxMhz = 0; }
                
                // Usar NativeSystemMetrics para RAM total (mais confiável que WMI)
                var memInfo = _nativeMetrics.GetMemoryUsage();
                totalRamGb = memInfo.TotalGb;

                try
                {
                    using var s = new ManagementObjectSearcher("SELECT AdapterRAM FROM Win32_VideoController");
                    _logger.LogInfo("[GameDiagnosticsService] Detectando VRAM total via WMI Win32_VideoController.AdapterRAM");
                    foreach (ManagementObject o in s.Get())
                    {
                using var __dispose_o = o;
                        object? rawVal = o["AdapterRAM"];
                        _logger.LogInfo($"[GameDiagnosticsService] AdapterRAM bruto: {rawVal} (tipo={rawVal?.GetType().Name})");
                        
                        long vram = 0;
                        if (rawVal is uint uintVal)
                        {
                            // WMI retorna uint32, máximo ~4GB. Para GPUs >4GB, fazer estimativa
                            vram = uintVal;
                            _logger.LogInfo($"[GameDiagnosticsService] AdapterRAM como uint32: {vram} bytes ({vram/1024.0/1024.0:F0} MB)");
                            if (vram >= 4294967295 || vram <= 0)
                            {
                                _logger.LogWarning("[GameDiagnosticsService] AdapterRAM parece truncado (provavelmente GPU com >4GB VRAM). Estimando pela contagem de adaptadores.");
                            }
                        }
                        else
                        {
                            long.TryParse(rawVal?.ToString() ?? "0", out vram);
                            _logger.LogInfo($"[GameDiagnosticsService] AdapterRAM parsed: {vram} bytes");
                        }
                        
                        double vramMb = vram / (1024.0 * 1024.0);
                        _logger.LogInfo($"[GameDiagnosticsService] VRAM calculada: {vramMb:F0} MB");
                        if (vramMb > vramTot) vramTot = vramMb;
                    }
                }
                catch (Exception exVram)
                {
                    _logger.LogWarning($"[GameDiagnosticsService] Erro ao detectar VRAM: {exVram.Message}");
                    vramTot = 0;
                }
                _logger.LogInfo($"[GameDiagnosticsService] VRAM total final: {vramTot:F0} MB");

            }
            catch { }

            int loopCount = 0;
            while (!token.IsCancellationRequested)
            {
                loopCount++;
                try
                {
                    var t = DateTime.UtcNow;
                    double cpuPct = Safe(cpu);
                    double q = Safe(qlen);
                    double dpcPct = Safe(dpc);
                    double intrPct = Safe(intr);
                    double pfsec = Safe(pf);
                    double rps = Safe(diskR);
                    double wps = Safe(diskW);
                    double dq = Safe(diskQ);
                    double dlat = Safe(diskLat);
                    double gpu = -1;
                    double vram = -1;
                    try
                    {
                        if (gpu3d != null && gpu3d.Count > 0)
                        {
                            double sum = 0; foreach (var c in gpu3d) { try { sum += c.NextValue(); } catch { } }
                            gpu = Math.Max(0, Math.Min(100, sum));
                        }
                    }
                    catch { gpu = -1; }
                    try
                    {
                        if (vramUsed != null && vramUsed.Count > 0)
                        {
                            double sum = 0; foreach (var c in vramUsed) { try { sum += c.NextValue(); } catch { } }
                            vram = sum / (1024 * 1024);
                        }
                    }
                    catch { vram = -1; }
                    // Coleta de frequência CPU (Otimizada: a cada ~6 segundos via loopCount)
                    double curMhz = _samples.Count > 0 ? _samples[^1].CpuCurrentMhz : 0;
                    if (loopCount % 8 == 0) // aprox 6s (750ms * 8)
                    {
                        try
                        {
                            using var s = new ManagementObjectSearcher("SELECT CurrentClockSpeed FROM Win32_Processor");
                            foreach (ManagementObject o in s.Get()) {
                using var __dispose_o = o; curMhz = Convert.ToDouble(o["CurrentClockSpeed"] ?? 0); break; }
                        }
                        catch { }
                    }

                    // Usar NativeSystemMetrics para RAM em tempo real (exclui standby/cache)
                    var mem = _nativeMetrics.GetMemoryUsage();
                    double usedGb = mem.InUseGb;
                    double standbyGb = mem.StandbyGb;
                    totalRamGb = mem.TotalGb;
                    _logger.LogDebug($"[GameDiagnosticsService] RAM: em_uso={usedGb:F2}GB / standby={standbyGb:F2}GB / total={totalRamGb:F2}GB ({mem.UsagePercent}%) | PF={pfsec:F0}/s");
                    double fps = _currentFps;


                    // Coleta de Top Processos (Otimizada: a cada ~3 segundos para economizar CPU)
                    var topProcs = new List<ProcessInfo>();
                    if (loopCount % 4 == 0)
                    {
                        try
                        {
                            var allProcs = Process.GetProcesses();
                            _logger.LogDebug($"[GameDiagnosticsService] Enumerando {allProcs.Length} processos do sistema");

                            var procList = allProcs
                                .Where(p => p.Id != 0 && p.Id != 4)
                                .Select(p => {
                                    try {
                                        // Usar PrivateMemorySize64 (memória privada real) em vez de WorkingSet64
                                        // WorkingSet64 inclui memória compartilhada e pode inflar valores drasticamente
                                        // (ex: "Memory Compression" mostra 21GB WorkingSet mas usa pouca memória privada)
                                        long privateBytes = 0;
                                        try { privateBytes = p.PrivateMemorySize64; } catch { }
                                        
                                        // Fallback: se PrivateMemorySize64 falhou ou é 0, usar WorkingSet64
                                        if (privateBytes <= 0)
                                        {
                                            try { privateBytes = p.WorkingSet64; } catch { }
                                            _logger.LogDebug($"[GameDiagnosticsService] Processo {p.ProcessName} (PID={p.Id}): PrivateMemory=0, usando WorkingSet64={privateBytes} bytes");
                                        }

                                        return new ProcessInfo {
                                            Name = p.ProcessName,
                                            Pid = p.Id,
                                            RamBytes = privateBytes,
                                            IsPrivateMemory = privateBytes > 0
                                        };
                                    } catch (Exception exProc) {
                                        _logger.LogDebug($"[GameDiagnosticsService] Erro ao ler processo: {exProc.Message}");
                                        return null;
                                    }
                                })
                                .Where(p => p != null)
                                .OrderByDescending(p => p!.RamBytes)
                                .Take(5)
                                .ToList()!;

                            _logger.LogInfo($"[GameDiagnosticsService] Top 5 processos de memoria:");
                            foreach (var p in procList)
                            {
                                string memType = p.IsPrivateMemory ? "privada" : "workingSet";
                                _logger.LogInfo($"[GameDiagnosticsService]   #{procList.IndexOf(p)+1}: {p.Name} (PID={p.Pid}) = {p.RamBytes} bytes ({p.RamBytes/1024.0/1024.0:F1} MB) [{memType}]");
                            }
                            topProcs = procList;
                        }
                        catch (Exception exProcs)
                        {
                            _logger.LogWarning($"[GameDiagnosticsService] Erro ao coletar processos: {exProcs.Message}");
                        }
                    }
                    else if (_samples.Count > 0)
                    {
                        topProcs = _samples[^1].TopProcesses;
                    }
                    
                    // Coletar dados térmicos (Serviço Global Centralizado)
                    var thermal = await _thermalMonitor.GetCurrentMetricsAsync();
                    double cpuTemp = thermal.CpuTemperature;
                    double gpuTemp = thermal.GpuTemperature;
                    bool cpuThrottling = thermal.CpuThrottling;
                    bool gpuThrottling = thermal.GpuThrottling;
                    
                    var cause = Infer(cpuPct, q, dpcPct, intrPct, pfsec, dq, dlat, gpu, usedGb, totalRamGb, curMhz, maxMhz, cpuTemp, cpuThrottling, gpuTemp, gpuThrottling);
                    _logger.LogInfo($"[GameDiagnosticsService] Sample #{loopCount}: CPU={cpuPct:F1}% RAM={usedGb:F1}/{totalRamGb:F1}GB GPU={gpu:F1}% VRAM={vram:F0}MB FPS={fps:F0} Causa={cause} ({LocalizeCauseKey(cause)})");
                    var sample = new Sample
                    {
                        T = t,
                        CpuPercent = cpuPct,
                        CpuQueue = q,
                        CpuCurrentMhz = curMhz,
                        CpuMaxMhz = maxMhz,
                        CpuDpcPercent = dpcPct,
                        CpuInterruptPercent = intrPct,
                        CpuTemperature = cpuTemp,
                        CpuThrottling = cpuThrottling,
                        RamUsedGb = usedGb,
                        RamStandbyGb = standbyGb,
                        RamTotalGb = totalRamGb,
                        RamPageFaultsPerSec = pfsec,
                        DiskReadsPerSec = rps,
                        DiskWritesPerSec = wps,
                        DiskQueueLen = dq,
                        DiskLatencySec = dlat,
                        GpuUtilPercent = gpu,
                        GpuVramUsedMb = vram,
                        GpuVramTotalMb = vramTot,
                        GpuTemperature = gpuTemp,
                        GpuThrottling = gpuThrottling,
                        Fps = fps,
                        Cause = cause,
                        TopProcesses = topProcs
                    };
                    List<Sample> snapshot;
                    lock (_samplesLock)
                    {
                        _samples.Add(sample);
                        if (_samples.Count > 600) _samples.RemoveRange(0, _samples.Count - 600);
                        snapshot = _samples.ToList();
                    }
                    
                    // Adicionar à persistência (SaaS)
                    try { _persistence?.AddSample(sample); } catch { }
                    
                    SamplesUpdated?.Invoke(snapshot);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception) { /* Log error if needed */ }

                await Task.Delay(750, token);
            }

            try { cpu?.Dispose(); } catch { }
            try { qlen?.Dispose(); } catch { }
            try { dpc?.Dispose(); } catch { }
            try { intr?.Dispose(); } catch { }
            try { pf?.Dispose(); } catch { }
            try { diskR?.Dispose(); } catch { }
            try { diskW?.Dispose(); } catch { }
            try { diskQ?.Dispose(); } catch { }
            try { diskLat?.Dispose(); } catch { }
            if (gpu3d != null) { foreach (var c in gpu3d) { try { c.Dispose(); } catch { } } }
            if (vramUsed != null) { foreach (var c in vramUsed) { try { c.Dispose(); } catch { } } }
        }

        private static double Safe(SafePerformanceCounter? c)
        {
            try { return c != null ? c.NextValue() : 0; } catch { return 0; }
        }

        private void OnFpsMetricsUpdated(object? sender, FrameMetrics e)
        {
            _currentFps = e.CurrentFps;
        }

        /// <summary>
        /// Categorias de diagnóstico. São ENUMERAÇÕES de domínio, nunca traduções:
        /// usar a string traduzida como chave de dicionário quebrava a comparação
        /// em <c>BuildAnalysis</c> e em <c>GenerateDescription</c>, que só comparam
        /// com as chaves em português. Resultado: em inglês/espanhol a análise caía
        /// sempre no caso padrão ("Sistema estável") e todo incidente era criado com
        /// causa "Undefined".
        /// </summary>
        public enum DiagnosticCause
        {
            Undefined = 0,
            ThermalThrottling,
            CpuScheduling,
            DriversDpcInterrupt,
            MemoryPaging,
            DiskIo,
            GpuRender
        }

        private DiagnosticCause Infer(double cpu, double q, double dpc, double intr, double pf, double dq, double dlat, double gpu, double ramUsedGb, double ramTotalGb, double curMhz, double maxMhz, double cpuTemp, bool cpuThrottling, double gpuTemp, bool gpuThrottling)
        {
            try
            {
                var scores = new Dictionary<DiagnosticCause, double>();

                // 1. Throttling térmico — só com TEMPERATURA REAL confirmada.
                if (cpuThrottling || gpuThrottling)
                {
                    double thermalScore = 0;
                    if (cpuThrottling && !double.IsNaN(cpuTemp)) thermalScore += (cpuTemp - 70) * 2;
                    if (gpuThrottling && !double.IsNaN(gpuTemp)) thermalScore += (gpuTemp - 70) * 2;
                    if (thermalScore > 0)
                        scores[DiagnosticCause.ThermalThrottling] = thermalScore;
                }

                // 2. CPU/Scheduling (saturação real)
                if (cpu > 85 || q > 2)
                {
                    double cpuScore = (cpu - 80) * 1.5 + (q > 2 ? q * 10 : 0);
                    scores[DiagnosticCause.CpuScheduling] = cpuScore;
                }

                // 3. Drivers/DPC/Interrupt
                if (dpc > 5 || intr > 5)
                {
                    double driverScore = (dpc > 5 ? dpc * 5 : 0) + (intr > 5 ? intr * 5 : 0);
                    scores[DiagnosticCause.DriversDpcInterrupt] = driverScore;
                }

                // 4. Memória/Paging
                if (ramTotalGb > 1.0)
                {
                    double ramUsagePercent = (ramUsedGb / ramTotalGb) * 100;
                    if (ramUsagePercent > 94 && pf > 10000)
                    {
                        double memScore = (ramUsagePercent - 90) * 2 + (pf / 1000);
                        scores[DiagnosticCause.MemoryPaging] = memScore;
                    }
                }

                // 5. Disco/IO
                if (dq > 1.0 || dlat > 0.05)
                {
                    double diskScore = (dq > 1.0 ? dq * 20 : 0) + (dlat > 0.05 ? dlat * 1000 : 0);
                    scores[DiagnosticCause.DiskIo] = diskScore;
                }

                // 6. GPU/Render (GPU saturada enquanto CPU não está)
                if (gpu >= 85 && cpu < 70)
                {
                    scores[DiagnosticCause.GpuRender] = (gpu - 80) * 2;
                }

                // 7. Energia/Throttling (frequência reduzida SEM causa térmica).
                //    Usa += (e não =) para não SOBRESCREVER a pontuação térmica acima.
                if (maxMhz > 0 && curMhz > 0 && !cpuThrottling)
                {
                    double clockRatio = curMhz / maxMhz;
                    if (clockRatio < 0.6 && cpu > 60)
                    {
                        scores[DiagnosticCause.ThermalThrottling] =
                            scores.TryGetValue(DiagnosticCause.ThermalThrottling, out var prev) ? prev : 0
                            + (0.6 - clockRatio) * 100;
                    }
                }

                if (scores.Count > 0)
                {
                    var top = scores.OrderByDescending(kvp => kvp.Value).First();
                    // Limiar mínimo evita causas sem relevância estatística.
                    if (top.Value >= 10) return top.Key;
                }
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogWarning(
                    $"[GameDiagnostics] Falha na inferência de causa raiz: {ex.Message}");
            }
            return DiagnosticCause.Undefined;
        }

        /// <summary>
        /// Traduz a categoria de causa para exibição.
        /// </summary>
        public static string LocalizeCauseKey(DiagnosticCause cause) => cause switch
        {
            DiagnosticCause.ThermalThrottling => "GameDiagThermalThrottle",
            DiagnosticCause.CpuScheduling => "GameDiagCpuScheduling",
            DiagnosticCause.DriversDpcInterrupt => "GameDiagDriversDpc",
            DiagnosticCause.MemoryPaging => "GameDiagMemoryPaging",
            DiagnosticCause.DiskIo => "GameDiagDiskIo",
            DiagnosticCause.GpuRender => "GameDiagGpuRender",
            _ => "GameDiagUndefined"
        };

        private void TryStartPresentMon(CancellationToken token)
        {
            try
            {
                // LEGACY KILL SWITCHED - TODO: Fetch PID from Orchestrator
                var pid = 0; // App.GamerOptimizer?.GetMonitoredGameProcessId() ?? 0;
                if (pid <= 0) return;
                var exe = FindPresentMonExecutable();
                if (string.IsNullOrWhiteSpace(exe) || !System.IO.File.Exists(exe)) return;
                _pmTask = Task.Run(() =>
                {
                    try
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = exe,
                            Arguments = $"-process_id {pid} -qpc_time -csv",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true
                        };
                        using var p = Process.Start(psi);
                        if (p == null) return;
                        var reader = p.StandardOutput;
                        while (!token.IsCancellationRequested && !reader.EndOfStream)
                        {
                            var line = reader.ReadLine();
                            if (string.IsNullOrWhiteSpace(line)) continue;
                            try
                            {
                                var parts = line.Split(',');
                                double ms = 0;
                                for (int i = parts.Length - 1; i >= 0; i--)
                                {
                                    if (double.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out ms))
                                    {
                                        break;
                                    }
                                }
                                if (ms > 0 && ms < 200)
                                {
                                    // _ftQueue.Enqueue(ms); // Legacy queue disabled
                                    // while (_ftQueue.Count > 240) { _ftQueue.TryDequeue(out _); } // Legacy queue disabled
                                }
                            }
                            catch { }
                        }
                        try { if (!p.HasExited) p.Kill(); } catch { }
                    }
                    catch { }
                }, token);
            }
            catch { }
        }

        private string? FindPresentMonExecutable()
        {
            try
            {
                var local = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PresentMon.exe");
                if (System.IO.File.Exists(local)) return local;
                var prog = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                var intelPath = System.IO.Path.Combine(prog, "Intel", "PresentMon", "PresentMon.exe");
                if (System.IO.File.Exists(intelPath)) return intelPath;
            }
            catch { }
            return null;
        }

        public IReadOnlyList<Sample> GetSamplesSnapshot()
        {
            try
            {
                lock (_samplesLock)
                {
                    return _samples.ToList();
                }
            }
            catch { return Array.Empty<Sample>(); }
        }
        
        /// <summary>
        /// Obtém incidentes ativos detectados
        /// </summary>
        public List<DiagnosticIncident> GetActiveIncidents()
        {
            return _persistence?.GetActiveIncidents() ?? new List<DiagnosticIncident>();
        }
        
        /// <summary>
        /// Carrega sessões salvas anteriormente
        /// </summary>
        public List<DiagnosticSession> LoadSessions(int maxCount = 10)
        {
            return _persistence?.LoadSessions(maxCount) ?? new List<DiagnosticSession>();
        }
        
        /// <summary>
        /// Marca incidente como resolvido
        /// </summary>
        public void ResolveIncident(string incidentId)
        {
            _persistence?.ResolveIncident(incidentId);
        }
        
        /// <summary>
        /// Limpa todos os incidentes
        /// </summary>
        public void ClearIncidents()
        {
            _persistence?.ClearIncidents();
        }
        
        public void Dispose()
        {
            Stop();
            (_thermalMonitor as IDisposable)?.Dispose();
        }
    }
}

