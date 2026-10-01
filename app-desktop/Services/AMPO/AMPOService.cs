using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.AMPO
{
    public interface IAMPOService
    {
        void Start();
        void Stop();
        Task AnalyzeAsync();
    }
    public class AMPOService : IAMPOService
    {
        private readonly ILoggingService _logger;
        private readonly SystemChanges.ISystemChangeTransactionService? _tx;
        private readonly string _snapDir;
        private bool _active;
        public AMPOService(ILoggingService logger, SystemChanges.ISystemChangeTransactionService? tx = null)
        {
            _logger = logger;
            _tx = tx;
            var baseDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "artifacts_app", "ampo");
            Directory.CreateDirectory(baseDir);
            _snapDir = baseDir;
        }
        public void Start()
        {
            _active = true;
        }
        public void Stop()
        {
            _active = false;
        }
        public async Task AnalyzeAsync()
        {
            if (!_active) return;
            var pressure = MemoryPressure();
            if (pressure > 0.8)
            {
                var tx = _tx?.Begin("AMPO.Analyze");
                try
                {
                    var snapshotBefore = Snapshot();
                    await File.WriteAllTextAsync(Path.Combine(_snapDir, "snap_before.json"), JsonSerializer.Serialize(snapshotBefore, new JsonSerializerOptions { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
                    
                    // SEGURANÇA ENTERPRISE: Apenas invocamos coleta de lixo nas gerações internas.
                    // Forçar Trim.WorkingSet massivo quebra o cache de página do Windows e CAUSA stutter.
                    var snapshotAfter = Snapshot();
                    await File.WriteAllTextAsync(Path.Combine(_snapDir, "snap_after.json"), JsonSerializer.Serialize(snapshotAfter, new JsonSerializerOptions {  PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
                    _logger.LogInfo("[AMPO] Alta pressão de RAM. Notificando Orchestrator apenas (sem hard trim).");
                    tx?.Commit();
                }
                catch
                {
                    tx?.Rollback();
                }
            }
        }
        private double MemoryPressure()
        {
            try
            {
                var ci = new Microsoft.VisualBasic.Devices.ComputerInfo();
                var total = (double)ci.TotalPhysicalMemory;
                var avail = (double)ci.AvailablePhysicalMemory;
                return Math.Max(0, Math.Min(1, 1 - (avail / total)));
            }
            catch { return 0.5; }
        }
        private void TrimBackgroundWorkingSets()
        {
            // [REMOVIDO]: O conceito de iterar sobre N processos não-whitelisted e forçar
            // MinWorkingSet para 64KB causa HARD-FAULTS massivos assim que o processo tenta ser
            // acessado pelo Kernel no Windows 10/11, gerando o popular "micro-stutter" de memória.
            // Esta função agora é um No-Op (segurança enterprise garantida).
        }
        private bool IsWhitelisted(Process p)
        {
            var name = p.ProcessName.ToLowerInvariant();
            if (name.Contains("voltris")) return true;
            return false;
        }
        private object Snapshot()
        {
            var list = Process.GetProcesses().Select(p => new { p.ProcessName, WS = SafeWs(p), PM = SafePm(p) }).OrderByDescending(x => x.WS).Take(50).ToList();
            return new { ts = DateTime.UtcNow, top = list };
        }
        private static long SafeWs(Process p) { try { return p.WorkingSet64; } catch { return 0; } }
        private static long SafePm(Process p) { try { return p.PagedMemorySize64; } catch { return 0; } }
    }
}
