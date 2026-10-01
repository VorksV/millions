using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services
{
    /// <summary>
    /// Serviço de limpeza do sistema de alta performance
    /// </summary>
    public class SystemCleaner : ISystemCleaner
    {
        private readonly ILoggingService _logger;
        private readonly BrowserCleanerService _browserCleaner;
        
        public SystemCleaner(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _browserCleaner = new BrowserCleanerService(_logger);
        }

        private IEnumerable<string> GetStandardTempPaths()
        {
            var winDir = Environment.GetEnvironmentVariable("WINDIR") ?? "C:\\Windows";
            return new[] {
                Path.GetTempPath(),
                Path.Combine(winDir, "Temp"),
                Path.Combine(winDir, "Prefetch"),
                Path.Combine(winDir, "SoftwareDistribution", "Download")
            };
        }

        /// <summary>
        /// Calcula o tamanho dos arquivos temporários SEM deletar (Otimizado)
        /// </summary>
        public async Task<long> GetTempFilesSizeAsync(CancellationToken ct = default)
        {
            return await Task.Run(() =>
            {
                long totalSize = 0;
                try
                {
                    var tempPaths = GetStandardTempPaths();
                    
                    // Paralelizar a contagem se houver múltiplos caminhos
                    object lockObj = new object();
                    Parallel.ForEach(tempPaths, new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Environment.ProcessorCount }, path =>
                    {
                        if (Directory.Exists(path))
                        {
                            long size = Helpers.FileSystemHelper.GetDirectorySize(path, maxSeconds: 5, maxFiles: 50000);
                            lock (lockObj) { totalSize += size; }
                        }
                    });
                }
                catch (Exception ex)
                {
                    _logger?.LogError($"[SystemCleaner] Erro global em GetTempFilesSizeAsync: {ex.Message}");
                }
                return totalSize;
            }, ct);
        }

        /// <summary>
        /// Calcula o tamanho da lixeira SEM esvaziar via Win32 API (Ultra rápido)
        /// </summary>
        public async Task<long> GetRecycleBinSizeAsync(CancellationToken ct = default)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var info = new Utils.Win32.ShellNativeMethods.SHQUERYRBINFO();
                    info.cbSize = (uint)Marshal.SizeOf(typeof(Utils.Win32.ShellNativeMethods.SHQUERYRBINFO));
                    int res = Utils.Win32.ShellNativeMethods.SHQueryRecycleBin(null, ref info);
                    if (res == 0) return info.i64Size;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning($"[SystemCleaner] Falha ao consultar lixeira via API Nativa: {ex.Message}");
                }
                return 0;
            }, ct);
        }

        /// <summary>
        /// Calcula o tamanho do cache de miniaturas SEM deletar
        /// </summary>
        public async Task<long> GetThumbnailsSizeAsync(CancellationToken ct = default)
        {
            return await Task.Run(() =>
            {
                string thumbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Explorer");
                return Helpers.FileSystemHelper.GetDirectorySize(thumbPath, maxSeconds: 2, maxFiles: 10000);
            }, ct);
        }

        /// <summary>
        /// Calcula o tamanho do cache de navegadores (Otimizado)
        /// </summary>
        public async Task<long> GetBrowserCacheSizeAsync(CancellationToken ct = default)
        {
            return await Task.Run(() =>
            {
                long totalSize = 0;
                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                
                string[] paths = {
                    Path.Combine(local, "Google", "Chrome", "User Data", "Default", "Cache"),
                    Path.Combine(local, "Microsoft", "Edge", "User Data", "Default", "Cache"),
                    Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data", "Default", "Cache")
                };

                foreach (var path in paths)
                {
                    if (ct.IsCancellationRequested) break;
                    totalSize += Helpers.FileSystemHelper.GetDirectorySize(path, maxSeconds: 2, maxFiles: 10000);
                }
                return totalSize;
            }, ct);
        }

        /// <summary>
        /// Calcula o tamanho total orquestrando tarefas paralelas
        /// </summary>
        public async Task<long> CalculateTotalSizeAsync(bool cleanTemp, bool cleanRecycle, bool cleanThumbnails, bool cleanBrowsers, CancellationToken ct = default)
        {
            var tasks = new List<Task<long>>();
            if (cleanTemp) tasks.Add(GetTempFilesSizeAsync(ct));
            if (cleanRecycle) tasks.Add(GetRecycleBinSizeAsync(ct));
            if (cleanThumbnails) tasks.Add(GetThumbnailsSizeAsync(ct));
            if (cleanBrowsers) tasks.Add(GetBrowserCacheSizeAsync(ct));

            var results = await Task.WhenAll(tasks);
            return results.Sum();
        }

        public async Task<long> CleanCacheAsync(Action<int>? progressCallback = null, CancellationToken ct = default)
        {
            return await Task.Run(async () =>
            {
                try
                {
                    _logger.LogInfo("Executando limpeza profunda (DISM)...");
                    progressCallback?.Invoke(10);
                    
                    var psi = new ProcessStartInfo
                    {
                        FileName = "dism.exe",
                        Arguments = "/Online /Cleanup-Image /StartComponentCleanup /Quiet",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    
                    using var proc = Process.Start(psi);
                    if (proc != null)
                    {
                        await proc.WaitForExitAsync(ct);
                    }
                    
                    _logger.LogSuccess("Limpeza DISM concluída");
                    progressCallback?.Invoke(100);
                    return 0;
                }
                catch (Exception ex)
                {
                    _logger.LogError("Erro DISM", ex);
                    progressCallback?.Invoke(100);
                    return 0;
                }
            }, ct);
        }

        /// <summary>
        /// Limpa arquivos temporários em Single-Pass (Super rápido)
        /// </summary>
        public async Task<long> CleanTempFilesAsync(Action<int>? progressCallback = null, CancellationToken ct = default)
        {
            return await Task.Run(() =>
            {
                long totalCleaned = 0;
                try
                {
                    var paths = GetStandardTempPaths().ToList();
                    int count = 0;
                    
                    foreach (var path in paths)
                    {
                        if (ct.IsCancellationRequested) break;
                        if (!Directory.Exists(path)) continue;

                        // OTIMIZAÇÃO: Single-Pass (Deletar e somar simultaneamente)
                        // Chromium-Aware: proteger sessões ativas do navegador no %TEMP%
                        var result = Helpers.FileSystemHelper.CleanDirectory(path, isSystemTemp: true);
                        totalCleaned += result.bytesFreed;
                        
                        count++;
                        progressCallback?.Invoke(10 + (int)(count * 100.0 / paths.Count * 0.9));
                    }
                    
                    _logger.LogSuccess($"Limpeza de temporários concluída: {Helpers.FileSystemHelper.FormatBytes(totalCleaned)} liberados.");
                }
                catch (Exception ex) { _logger.LogError("Erro na limpeza de temporários", ex); }
                progressCallback?.Invoke(100);
                return totalCleaned;
            }, ct);
        }

        public async Task<bool> EmptyRecycleBinAsync(Action<int>? progressCallback = null, CancellationToken ct = default)
        {
            return await Task.Run(() =>
            {
                try
                {
                    _logger.LogInfo("Esvaziando lixeira sem UI (sem diálogos do Shell)...");
                    bool res = Utils.Win32.RecycleBinHelper.EmptyWithoutUi();
                    
                    if (res) _logger.LogSuccess("Lixeira esvaziada com sucesso.");
                    else _logger.LogWarning("Lixeira já estava vazia (nada a excluir).");
                    
                    progressCallback?.Invoke(100);
                    return res;
                }
                catch (Exception ex) { _logger.LogError("Erro ao esvaziar lixeira", ex); return false; }
            }, ct);
        }

        public async Task<long> CleanThumbnailsAsync(Action<int>? progressCallback = null, CancellationToken ct = default)
        {
            return await Task.Run(() =>
            {
                string thumbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Explorer");
                var result = Helpers.FileSystemHelper.CleanDirectory(thumbPath, "thumbcache_*.db");
                _logger.LogSuccess($"Limpeza de miniaturas concluída: {Helpers.FileSystemHelper.FormatBytes(result.bytesFreed)} liberados.");
                progressCallback?.Invoke(100);
                return result.bytesFreed;
            }, ct);
        }

        public async Task<long> CleanBrowserCacheAsync(Action<int>? progressCallback = null, CancellationToken ct = default)
        {
            return await Task.Run(async () =>
            {
                var result = await _browserCleaner.CleanAsync(ct);
                progressCallback?.Invoke(100);
                if (result.IsSuccess)
                {
                    _logger?.LogSuccess($"Limpeza de navegadores concluída: {Helpers.FileSystemHelper.FormatBytes(result.BytesFreed)} liberados.");
                }
                else
                {
                    _logger?.LogWarning($"Limpeza de navegadores concluída com {result.IgnoredErrors.Count} arquivos não deletados. Espaço liberado: {Helpers.FileSystemHelper.FormatBytes(result.BytesFreed)}");
                }
                return result.BytesFreed;
            }, ct);
        }
    }
}
