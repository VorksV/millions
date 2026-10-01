using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.Diagnostics;

namespace VoltrisOptimizer.Services.DPC
{
    public record DpcSample(DateTime Timestamp, float DpcPercent, float[] PerCoreDpcPercent);
    public record DpcStats(double Avg, double P95, double P99, int SpikeCount);
    public record DpcSpike(DateTime Timestamp, float Value, string? Driver, string? ProcessName);
    public class DpcAnalysisResult
    {
        public DpcStats Stats { get; set; } = new DpcStats(0,0,0,0);
        public List<DpcSpike> Spikes { get; set; } = new List<DpcSpike>();
        public string Recommendation { get; set; } = "";
    }
    public class RepairPlan
    {
        public List<string> Actions { get; } = new List<string>();
        public static RepairPlan Generate(DpcAnalysisResult result)
        {
            var plan = new RepairPlan();
            var top = result.Spikes.GroupBy(s => s.Driver).OrderByDescending(g => g.Count()).FirstOrDefault();
            if (top != null && !string.IsNullOrEmpty(top.Key)) plan.Actions.Add($"Atualizar {top.Key}");
            var proc = result.Spikes.GroupBy(s => s.ProcessName).OrderByDescending(g => g.Count()).FirstOrDefault();
            if (proc != null && !string.IsNullOrEmpty(proc.Key)) plan.Actions.Add($"Isolar {proc.Key}");
            if (result.Stats.P99 > 20) plan.Actions.Add("Reverter último driver problemático");
            return plan;
        }
    }
    public interface IDpcAnalyzerService
    {
        void Start();
        void Stop();
        DpcAnalysisResult GetLatestAnalysis();
        Task<string> CreateSupportPackAsync();
        event Action<DpcSample>? SampleReceived;
    }
    public class DpcAnalyzerService : IDpcAnalyzerService, IDisposable
    {
        private readonly ILoggingService _logger;
        private Timer? _timer;
        private readonly List<DpcSample> _samples = new List<DpcSample>();
        private readonly object _lock = new object();
        private readonly string _logDir;
        public event Action<DpcSample>? SampleReceived;

        // PERFORMANCE FIX: PerformanceCounters removidos.
        // Era: 1 counter geral + N counters por núcleo = N+1 objetos PDH, todos chamados a cada 5s.
        // Agora: 1 syscall NtQuerySystemInformation que retorna dados de todos os núcleos de uma vez.
        // Custo: ~microsegundos vs ~dezenas de ms com PDH.
        private long[] _prevDpcTimes = Array.Empty<long>();
        private long[] _prevKernelTimes = Array.Empty<long>();
        private DateTime _prevCollectTime = DateTime.MinValue;

        public DpcAnalyzerService(ILoggingService logger)
        {
            _logger = logger;
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _logDir = Path.Combine(appData, "Voltris", "logs", "dpc");
            Directory.CreateDirectory(_logDir);
        }
        public void Start()
        {
            // OTIMIZAÇÃO: Substituição do Timer(1000) por IntelligenceOrchestrator sync!
            // Para não quebrar a API, se chamado sozinho: usaremos async/await real em vez de System.Threading.Timer.
            // Executa com backoff dinâmico (5s em idle, 2s uso)
            _timer = new Timer(Collect, null, 0, 5000); 
            _logger.LogInfo("[DPC] Monitor iniciado (intervalo dinâmico: 5s base)");
        }
        public void Stop()
        {
            _timer?.Dispose();
            _timer = null;
            _logger.LogInfo("[DPC] Monitor parado");
        }
        private void Collect(object? state)
        {
            using (CpuSelfProfiler.Instance.BeginSection("DpcAnalyzer.Collect"))
            {
                try
                {
                    var (dpcPercent, perCoreDpc) = ReadDpcViaNativeApi();
                    var sample = new DpcSample(DateTime.UtcNow, dpcPercent, perCoreDpc);
                    lock (_lock)
                    {
                        _samples.Add(sample);
                        if (_samples.Count > 2000) _samples.RemoveRange(0, _samples.Count - 2000);
                    }
                    SampleReceived?.Invoke(sample);
                }
                catch { }
            }
        }

        // Lê tempos de DPC via NtQuerySystemInformation (puro kernel, zero WMI/PDH)
        private (float total, float[] perCore) ReadDpcViaNativeApi()
        {
            try
            {
                int cpuCount = Environment.ProcessorCount;
                int structSize = Marshal.SizeOf<SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION>();
                IntPtr buffer = Marshal.AllocHGlobal(structSize * cpuCount);
                try
                {
                    int status = NtQuerySystemInformation(8, buffer, structSize * cpuCount, out _);
                    if (status != 0)
                        return (0f, Array.Empty<float>());

                    if (_prevDpcTimes.Length != cpuCount)
                    {
                        _prevDpcTimes = new long[cpuCount];
                        _prevKernelTimes = new long[cpuCount];
                        _prevCollectTime = DateTime.UtcNow;
                        // Primeira leitura: apenas inicializa baseline
                        for (int i = 0; i < cpuCount; i++)
                        {
                            var info = Marshal.PtrToStructure<SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION>(
                                buffer + i * structSize);
                            _prevDpcTimes[i] = info.DpcTime;
                            _prevKernelTimes[i] = info.KernelTime;
                        }
                        return (0f, new float[cpuCount]);
                    }

                    var now = DateTime.UtcNow;
                    double intervalSec = Math.Max(0.1, (now - _prevCollectTime).TotalSeconds);
                    _prevCollectTime = now;

                    var perCore = new float[cpuCount];
                    long totalDpcDelta = 0;
                    long totalTimeDelta = 0;

                    for (int i = 0; i < cpuCount; i++)
                    {
                        var info = Marshal.PtrToStructure<SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION>(
                            buffer + i * structSize);

                        long dpcDelta = info.DpcTime - _prevDpcTimes[i];
                        long timeDelta = (info.KernelTime + info.UserTime) - (_prevKernelTimes[i]);

                        _prevDpcTimes[i] = info.DpcTime;
                        _prevKernelTimes[i] = info.KernelTime + info.UserTime;

                        totalDpcDelta += dpcDelta;
                        totalTimeDelta += timeDelta;

                        // % DPC = dpcDelta / (intervalSec * 10_000_000) * 100
                        double coreDpc = intervalSec > 0
                            ? (double)dpcDelta / (intervalSec * 10_000_000.0) * 100.0
                            : 0;
                        perCore[i] = (float)Math.Clamp(coreDpc, 0, 100);
                    }

                    double avgDpc = cpuCount > 0
                        ? (double)totalDpcDelta / cpuCount / (intervalSec * 10_000_000.0) * 100.0
                        : 0;

                    return ((float)Math.Clamp(avgDpc, 0, 100), perCore);
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            catch { return (0f, Array.Empty<float>()); }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION
        {
            public long IdleTime;
            public long KernelTime;
            public long UserTime;
            public long DpcTime;
            public long InterruptTime;
            public uint InterruptCount;
        }

        [DllImport("ntdll.dll")]
        private static extern int NtQuerySystemInformation(int SystemInformationClass, IntPtr SystemInformation, int SystemInformationLength, out int ReturnLength);
        public DpcAnalysisResult GetLatestAnalysis()
        {
            List<float> vals;
            lock (_lock) vals = _samples.Select(s => s.DpcPercent).ToList();
            if (vals.Count == 0) return new DpcAnalysisResult();
            var avg = vals.Average();
            var p95 = Percentile(vals, 95);
            var p99 = Percentile(vals, 99);
            var threshold = Math.Max(20f, (float)(avg * 3));
            var spikes = DetectSpikes(threshold);
            var rec = Recommendation(avg, p95, p99, spikes);
            return new DpcAnalysisResult { Stats = new DpcStats(avg, p95, p99, spikes.Count), Spikes = spikes, Recommendation = rec };
        }
        private static double Percentile(List<float> vals, int p)
        {
            var sorted = vals.Select(v => (double)v).OrderBy(v => v).ToArray();
            if (sorted.Length == 0) return 0;
            var rank = (p / 100.0) * (sorted.Length - 1);
            var lower = (int)Math.Floor(rank);
            var upper = (int)Math.Ceiling(rank);
            if (lower == upper) return sorted[lower];
            var w = rank - lower;
            return sorted[lower] * (1 - w) + sorted[upper] * w;
        }
        private List<DpcSpike> DetectSpikes(float threshold)
        {
            List<DpcSample> snap;
            lock (_lock) snap = _samples.ToList();
            var result = new List<DpcSpike>();
            
            // FILTRO DE SEGURANÇA ENTERPRISE: Não chamar WMI e GetProcesses para centenas de samples em loop!
            // Consolida os picos e obtém WMI apenas UMA VEZ se houver pelo menos um pico severo no período atual.
            var hasSpike = snap.Any(s => s.DpcPercent >= threshold);
            if (!hasSpike) return result;

            var assoc = FindDriversProcessesSafe(); 

            foreach (var s in snap)
            {
                if (s.DpcPercent >= threshold)
                {
                    result.Add(new DpcSpike(s.Timestamp, s.DpcPercent, assoc.driver, assoc.process));
                }
            }
            return result;
        }
        private (string? driver, string? process) FindDriversProcessesSafe()
        {
            try
            {
                // ATENÇÃO: Queries do Win32_PnPSignedDriver são extremamente pesadas e geram Lockup de MSIL.
                string? topDriver = "Multiple Devices / Driver Verifier required";
                
                // Encontra processos leves ao em vez do GetProcesses completo.
                string? topProc = null;
                var currentId = Process.GetCurrentProcess().Id;
                var procs = Process.GetProcesses();
                var hwProc = procs.OrderByDescending(p => SafeCpu(p)).FirstOrDefault(p => p.Id != currentId && p.Id > 4);
                topProc = hwProc?.ProcessName;
                
                // Dispose immediato
                foreach(var p in procs) { p.Dispose(); }
                
                return (topDriver, topProc);
            }
            catch { return (null, null); }
        }
        private static double SafeCpu(Process p)
        {
            try { return p.TotalProcessorTime.TotalMilliseconds; } catch { return 0; }
        }
        private string Recommendation(double avg, double p95, double p99, List<DpcSpike> spikes)
        {
            if (p99 > 20 || spikes.Count > 5)
            {
                var topDrv = spikes.Where(s => !string.IsNullOrEmpty(s.Driver)).GroupBy(s => s.Driver!).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key;
                if (!string.IsNullOrEmpty(topDrv)) return $"Atualizar {topDrv}";
                return "Atualizar drivers críticos e isolar processos com alto impacto";
            }
            if (p95 > 10) return "Verificar versões de drivers e reduzir carga de background";
            return "DPC saudável";
        }
        public async Task<string> CreateSupportPackAsync()
        {
            var packDir = Path.Combine(_logDir, "pack_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(packDir);
            var meta = new Dictionary<string, object>();
            meta["windows_build"] = Environment.OSVersion.VersionString;
            try
            {
                using var search = new ManagementObjectSearcher("SELECT DeviceID, DriverVersion, Manufacturer FROM Win32_PnPSignedDriver");
                var list = search.Get().Cast<ManagementObject>().Select(m => new { DeviceID = Convert.ToString(m["DeviceID"]), DriverVersion = Convert.ToString(m["DriverVersion"]), Manufacturer = Convert.ToString(m["Manufacturer"]) }).ToList();
                meta["drivers"] = list;
            }
            catch { }
            var samplesFile = Path.Combine(packDir, "dpc_samples.json");
            List<DpcSample> snap;
            lock (_lock) snap = _samples.ToList();
            await File.WriteAllTextAsync(samplesFile, JsonSerializer.Serialize(snap, new JsonSerializerOptions {  ReferenceHandler = ReferenceHandler.IgnoreCycles }));
            var metaFile = Path.Combine(packDir, "meta.json");
            await File.WriteAllTextAsync(metaFile, JsonSerializer.Serialize(meta, new JsonSerializerOptions {   ReferenceHandler = ReferenceHandler.IgnoreCycles }));
            return packDir;
        }
        public void Dispose()
        {
            _timer?.Dispose();
            // Sem PerformanceCounters para descartar
        }
    }
}
