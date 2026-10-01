using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Cleanup.Modules
{
    public class ShaderCacheCleanupModule : BaseCleanupModule
    {
        public override string Name => "Caches de Shader (DirectX, NVIDIA, AMD)";

        private readonly string[] _shaderDirectories = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\D3DSCache",
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\NVIDIA\GLCache",
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\NVIDIA\DXCache",
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\AMD\DxCache",
            @"C:\ProgramData\NVIDIA Corporation\NV_Cache"
        };

        public override Task<long> AnalyzeAsync(CancellationToken ct)
        {
            return Task.Run(() =>
            {
                long totalSize = 0;
                foreach (var dir in _shaderDirectories)
                {
                    ct.ThrowIfCancellationRequested();
                    totalSize += GetDirectorySizeSafe(dir);
                }
                return totalSize;
            });
        }

        public override Task<long> CleanAsync(IProgress<string> progress, CancellationToken ct)
        {
            return Task.Run(() =>
            {
                long totalCleaned = 0;
                foreach (var dir in _shaderDirectories)
                {
                    ct.ThrowIfCancellationRequested();
                    progress?.Report($"Limpando cache de shaders: {dir}...");
                    totalCleaned += CleanDirectorySafe(dir, ct, progress);
                }
                return totalCleaned;
            });
        }
    }
}
