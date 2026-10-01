using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Gamer.Interfaces;

namespace VoltrisOptimizer.Services.Gamer.Implementation
{
    /// <summary>
    /// VOLTRIS RAM CACHE V4.1 APEX — Versão Profissional Ultra-Inteligente
    /// </summary>
    public class VoltrisRAMCacheService : IDisposable
    {
        private readonly ILoggingService _logger;
        private readonly IHardwareDetector _hardwareDetector;

        private bool _isActive;
        private int _cacheSizeMB;
        private MemoryMappedFile? _mmf;
        private MemoryMappedViewAccessor? _accessor;
        private CacheMode _currentMode = CacheMode.Balanced;

        private readonly ConcurrentDictionary<string, CacheEntry> _cacheEntries = new();
        private readonly object _lfuLock = new();

        private readonly ConcurrentQueue<string> _highQueue = new();
        private readonly ConcurrentQueue<string> _mediumQueue = new();
        private readonly ConcurrentQueue<string> _lowQueue = new();

        private long _totalHits, _totalMisses, _totalBytesServed, _totalFilesPreloaded;
        private DateTime _startTime;

        private const int MIN_CACHE_MB = 128;  // Mínimo seguro
        private const int MAX_CACHE_MB = 512;   // Máximo absoluto — nunca alocar mais que isso
        private const int MAX_ENTRIES = 1200;
        private const int MAX_READ_SIZE_MB = 8;

        // CancellationToken para controlar o ciclo de vida das tasks internas
        private CancellationTokenSource _cts = new();

        // Processos que caracterizam jogos de forma confiável (extensível)
        // Critérios: pasta comuns de instalação de jogos, flags de janela D3D/Vulkan ou
        // processos que NÃO são sistema operacional/serviços conhecidos.
        private static readonly HashSet<string> KnownNonGameProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            // Sistema operacional e serviços
            "svchost", "lsass", "csrss", "wininit", "winlogon", "services", "smss",
            "dwm", "explorer", "taskhost", "taskhostw", "spoolsv", "audiodg",
            "fontdrvhost", "conhost", "dllhost", "RuntimeBroker",
            // Antivírus e segurança comuns
            "MsMpEng", "NisSrv", "SecurityHealthService", "sg_agent",
            // Voltris e relacionados
            "VoltrisOptimizer", "VoltrisUpdater", "VoltrisOptimizerInstaller",
            // Browsers e apps comuns que certamente não são jogos
            "chrome", "msedge", "firefox", "opera", "brave",
            "discord", "slack", "teams", "zoom", "outlook", "winword", "excel",
            "Code", "devenv", "rider", "idea64", "powershell", "cmd",
            "notepad", "notepad++", "7zG", "WinRAR",
            // Runtimes .NET e Java
            "dotnet", "java", "javaw"
        };

        private static readonly string[] HighPriority = { ".exe", ".dll", ".ocx", ".sys" };
        private static readonly string[] MediumPriority = { ".pak", ".dat", ".bundle", ".assets", ".res", ".pkg", ".vpk", ".bsp", ".udk", ".uasset" };
        private static readonly string[] LowPriority = { ".cfg", ".ini", ".config", ".xml", ".json", ".txt", ".log" };

        public bool IsActive => _isActive;
        public int CacheSizeMB => _cacheSizeMB;
        public double HitRate => _totalHits + _totalMisses > 0 ? (double)_totalHits / (_totalHits + _totalMisses) * 100 : 0;
        public CacheMode CurrentMode => _currentMode;

        public VoltrisRAMCacheService(ILoggingService logger, IHardwareDetector hardwareDetector)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _hardwareDetector = hardwareDetector ?? throw new ArgumentNullException(nameof(hardwareDetector));
            _logger.LogSuccess("[VOLTRIS APEX V4.1] Sistema de Cache carregado com sucesso.");
        }

        // Sobrecarga para manter compatibilidade com seu orchestrator
        public bool Start(int cacheSizeMB = 512)
        {
            return Start(CacheMode.Balanced, cacheSizeMB);
        }

        public bool Start(CacheMode mode, int forcedSizeMB = 0)
        {
            if (_isActive) return false;

            var totalRamGB = _hardwareDetector.GetTotalRamGb();
            _currentMode = mode;

            // CORREÇÃO: nunca alocar mais de MAX_CACHE_MB (512 MB) independente do forcedSizeMB passado
            _cacheSizeMB = forcedSizeMB > 0
                ? Math.Clamp(forcedSizeMB, MIN_CACHE_MB, MAX_CACHE_MB)
                : CalculateUltraAdaptiveCacheSize(totalRamGB, mode);

            _cts = new CancellationTokenSource();

            try
            {
                long bytes = (long)_cacheSizeMB * 1024 * 1024;
                _mmf = MemoryMappedFile.CreateNew(null, bytes, MemoryMappedFileAccess.ReadWrite);
                _accessor = _mmf.CreateViewAccessor();

                _isActive = true;
                _startTime = DateTime.Now;

                _logger.LogSuccess($"[VOLTRIS APEX V4.1] CACHE INICIADO → {_cacheSizeMB} MB (RAM: {totalRamGB:F1} GB | Modo: {mode})");

                var token = _cts.Token;
                Task.Run(() => MonitorAndPreloadAsync(token), token);
                Task.Run(() => LogStatisticsAsync(token), token);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[VOLTRIS APEX] Erro ao iniciar cache: {ex.Message}", ex);
                Stop();
                return false;
            }
        }

        private int CalculateUltraAdaptiveCacheSize(double totalRamGB, CacheMode mode)
        {
            // CORREÇÃO: tamanhos proporcionais e razoáveis — um otimizador não deve consumir GBs de RAM
            int baseSize = mode switch
            {
                CacheMode.Light      => 128,
                CacheMode.Aggressive => 512,
                _                    => totalRamGB switch   // Balanced: proporcional mas limitado
                {
                    < 8  => 128,
                    < 16 => 256,
                    _    => 512
                }
            };

            // Nunca ultrapassar o limite absoluto de 512 MB
            return Math.Clamp(baseSize, MIN_CACHE_MB, MAX_CACHE_MB);
        }

        // === MÉTODO EXIGIDO PELO SEU CÓDIGO ===
        public CacheStatistics GetStatistics()
        {
            return new CacheStatistics
            {
                IsActive = _isActive,
                CacheSizeMB = _cacheSizeMB,
                TotalEntries = _cacheEntries.Count,
                HitRate = HitRate,
                TotalHits = _totalHits,
                TotalMisses = _totalMisses,
                TotalBytesServed = _totalBytesServed,
                TotalFilesPreloaded = _totalFilesPreloaded,
                UsagePercent = GetUsagePercent(),
                UptimeMinutes = (DateTime.Now - _startTime).TotalMinutes
            };
        }

        private async Task MonitorAndPreloadAsync(CancellationToken cancellationToken)
        {
            _logger.LogInfo("[VOLTRIS APEX] Monitoramento inteligente iniciado.");

            var scanned = new ConcurrentDictionary<string, DateTime>();

            while (_isActive && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // CORREÇÃO: intervalo maior (10s) e filtro real de jogos
                    await Task.Delay(10000, cancellationToken);

                    var games = Process.GetProcesses()
                        .Where(p => !p.HasExited && IsLikelyGame(p.ProcessName))
                        .ToList();

                    foreach (var p in games)
                    {
                        if (cancellationToken.IsCancellationRequested) break;
                        await PreloadProcessAsync(p, cancellationToken);
                    }

                    if (DateTime.Now.Minute % 2 == 0 && DateTime.Now.Second < 15)
                        await SmartDirectoryScanAsync(scanned);
                }
                catch (OperationCanceledException) { break; }
                catch { }
            }

            _logger.LogInfo("[VOLTRIS APEX] Monitoramento encerrado.");
        }

        /// <summary>
        /// Determina se um processo é provavelmente um jogo.
        /// Critério: não é um processo de sistema/OS/browser conhecido E
        /// tem mais de 4 caracteres no nome (filtra processos de sistema curtos).
        /// </summary>
        private bool IsLikelyGame(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length <= 4)
                return false;

            // Excluir processos conhecidos que definitivamente NÃO são jogos
            if (KnownNonGameProcesses.Contains(name))
                return false;

            // Heurística: processos de sistema geralmente terminam com "svc", "host", "srv", "mgr"
            var lower = name.ToLowerInvariant();
            if (lower.EndsWith("svc") || lower.EndsWith("host") ||
                lower.EndsWith("srv") || lower.EndsWith("mgr") ||
                lower.StartsWith("mssec") || lower.StartsWith("nt"))
                return false;

            return true;
        }

        private async Task PreloadProcessAsync(Process proc, CancellationToken cancellationToken)
        {
            try
            {
                foreach (ProcessModule m in proc.Modules)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    if (!string.IsNullOrEmpty(m.FileName))
                        await TryPreloadFileAsync(m.FileName, cancellationToken);
                }
            }
            catch { }
            finally { proc.Dispose(); }
        }

        private async Task TryPreloadFileAsync(string filePath, CancellationToken cancellationToken)
        {
            if (!File.Exists(filePath)) return;
            var fi = new FileInfo(filePath);
            if (fi.Length == 0) return;

            var key = GetCacheKey(filePath, 0, (int)Math.Min(fi.Length, MAX_READ_SIZE_MB * 1024 * 1024));
            if (_cacheEntries.ContainsKey(key)) return;

            try
            {
                var data = await File.ReadAllBytesAsync(filePath, cancellationToken);
                int size = Math.Min(data.Length, MAX_READ_SIZE_MB * 1024 * 1024);
                Put(filePath, 0, size, data.Take(size).ToArray());

                Interlocked.Increment(ref _totalFilesPreloaded);
                _logger.LogInfo($"[VOLTRIS APEX] Preloaded: {Path.GetFileName(filePath)} ({size / 1024 / 1024:F1} MB)");
            }
            catch (OperationCanceledException) { }
            catch { }
        }

        private async Task SmartDirectoryScanAsync(ConcurrentDictionary<string, DateTime> scanned) 
        {
            // Implementação simplificada por enquanto
            await Task.CompletedTask;
        }

        public bool TryGet(string filePath, long offset, int length, out byte[] data)
        {
            data = Array.Empty<byte>();
            if (!_isActive) return false;

            var key = GetCacheKey(filePath, offset, length);
            if (_cacheEntries.TryGetValue(key, out var entry))
            {
                data = entry.Data;
                entry.AccessCount++;
                entry.LastAccessAt = DateTime.Now;
                Interlocked.Increment(ref _totalHits);
                Interlocked.Add(ref _totalBytesServed, length);
                return true;
            }

            Interlocked.Increment(ref _totalMisses);
            return false;
        }

        public void Put(string filePath, long offset, int length, byte[] data)
        {
            if (!_isActive || data.Length == 0) return;

            var key = GetCacheKey(filePath, offset, length);
            var priority = GetPriority(Path.GetExtension(filePath));

            if (_cacheEntries.Count >= MAX_ENTRIES || GetUsagePercent() > 88)
                EvictLFU(priority);

            var entry = new CacheEntry
            {
                FilePath = filePath,
                Offset = offset,
                Length = length,
                Data = data,
                CreatedAt = DateTime.Now,
                LastAccessAt = DateTime.Now,
                AccessCount = 1,
                Priority = priority
            };

            _cacheEntries[key] = entry;
            AddToQueue(key, priority);
        }

        private string GetCacheKey(string path, long offset, int length) => $"{path.ToLowerInvariant()}:{offset}:{length}";

        private CachePriority GetPriority(string ext)
        {
            ext = ext.ToLowerInvariant();
            if (HighPriority.Contains(ext)) return CachePriority.High;
            if (MediumPriority.Contains(ext)) return CachePriority.Medium;
            return LowPriority.Contains(ext) ? CachePriority.Low : CachePriority.Medium;
        }

        private void AddToQueue(string key, CachePriority p)
        {
            switch (p)
            {
                case CachePriority.High: _highQueue.Enqueue(key); break;
                case CachePriority.Medium: _mediumQueue.Enqueue(key); break;
                default: _lowQueue.Enqueue(key); break;
            }
        }

        private void EvictLFU(CachePriority newPriority)
        {
            lock (_lfuLock)
            {
                var candidates = newPriority == CachePriority.High 
                    ? _lowQueue.Concat(_mediumQueue).ToList() 
                    : _lowQueue.ToList();

                var toEvict = candidates
                    .Where(k => _cacheEntries.ContainsKey(k))
                    .Select(k => new { Key = k, Entry = _cacheEntries[k] })
                    .OrderBy(x => x.Entry.AccessCount)
                    .Take(20)
                    .ToList();

                foreach (var item in toEvict)
                {
                    _cacheEntries.TryRemove(item.Key, out _);
                    RemoveFromQueues(item.Key);
                }
            }
        }

        private void RemoveFromQueues(string key)
        {
            RemoveFromQueue(_highQueue, key);
            RemoveFromQueue(_mediumQueue, key);
            RemoveFromQueue(_lowQueue, key);
        }

        private void RemoveFromQueue(ConcurrentQueue<string> queue, string key)
        {
            var items = queue.ToArray();
            queue.Clear();
            foreach (var item in items.Where(i => i != key))
                queue.Enqueue(item);
        }

        private double GetUsagePercent()
        {
            if (_cacheSizeMB == 0) return 0;
            long used = _cacheEntries.Values.Sum(e => (long)e.Length);
            return (double)used / (_cacheSizeMB * 1024 * 1024) * 100;
        }

        private async Task LogStatisticsAsync(CancellationToken cancellationToken)
        {
            while (_isActive && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(30000, cancellationToken);
                    _logger.LogInfo($"[VOLTRIS STATS] Hits: {_totalHits} | Misses: {_totalMisses} | HitRate: {HitRate:F1}% | Preloaded: {_totalFilesPreloaded} | Usage: {GetUsagePercent():F1}%");
                }
                catch (OperationCanceledException) { break; }
                catch { }
            }
        }

        public void Stop()
        {
            if (!_isActive) return;
            _isActive = false;

            // Sinalizar cancelamento para as tasks internas
            try { _cts.Cancel(); } catch { }

            _accessor?.Dispose();
            _mmf?.Dispose();
            _cacheEntries.Clear();
            _highQueue.Clear();
            _mediumQueue.Clear();
            _lowQueue.Clear();

            _logger.LogSuccess("[VOLTRIS APEX] Cache finalizado.");
        }

        public void Dispose() => Stop();
    }

    public enum CacheMode { Light, Balanced, Aggressive }
    public enum CachePriority { Low, Medium, High }

    public class CacheEntry
    {
        public string FilePath { get; set; } = string.Empty;
        public long Offset { get; set; }
        public int Length { get; set; }
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public DateTime CreatedAt { get; set; }
        public DateTime LastAccessAt { get; set; }
        public int AccessCount { get; set; }
        public CachePriority Priority { get; set; }
    }

    public class CacheStatistics
    {
        public bool IsActive { get; set; }
        public int CacheSizeMB { get; set; }
        public int TotalEntries { get; set; }
        public double HitRate { get; set; }
        public long TotalHits { get; set; }
        public long TotalMisses { get; set; }
        public long TotalBytesServed { get; set; }
        public long TotalFilesPreloaded { get; set; }
        public double UsagePercent { get; set; }
        public double UptimeMinutes { get; set; }
    }
}