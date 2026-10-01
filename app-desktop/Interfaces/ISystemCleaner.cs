using System;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Interfaces
{
    /// <summary>
    /// Interface para serviço de limpeza do sistema
    /// </summary>
    public interface ISystemCleaner
    {
        Task<long> GetTempFilesSizeAsync(CancellationToken ct = default);
        Task<long> GetRecycleBinSizeAsync(CancellationToken ct = default);
        Task<long> GetThumbnailsSizeAsync(CancellationToken ct = default);
        Task<long> GetBrowserCacheSizeAsync(CancellationToken ct = default);
        Task<long> CalculateTotalSizeAsync(bool cleanTemp, bool cleanRecycle, bool cleanThumbnails, bool cleanBrowsers, CancellationToken ct = default);
        Task<long> CleanCacheAsync(Action<int>? progressCallback = null, CancellationToken ct = default);
        Task<long> CleanTempFilesAsync(Action<int>? progressCallback = null, CancellationToken ct = default);
        Task<bool> EmptyRecycleBinAsync(Action<int>? progressCallback = null, CancellationToken ct = default);
        Task<long> CleanThumbnailsAsync(Action<int>? progressCallback = null, CancellationToken ct = default);
        Task<long> CleanBrowserCacheAsync(Action<int>? progressCallback = null, CancellationToken ct = default);
    }
}
