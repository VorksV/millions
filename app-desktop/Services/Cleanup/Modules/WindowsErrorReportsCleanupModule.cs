using System;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Cleanup.Modules
{
    public class WindowsErrorReportsCleanupModule : BaseCleanupModule
    {
        public override string Name => "Relatórios de Erro do Windows (WER)";

        private readonly string[] _werDirectories = new[]
        {
            @"C:\ProgramData\Microsoft\Windows\WER\ReportArchive",
            @"C:\ProgramData\Microsoft\Windows\WER\ReportQueue",
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\Microsoft\Windows\WER\ReportArchive",
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\Microsoft\Windows\WER\ReportQueue"
        };

        public override Task<long> AnalyzeAsync(CancellationToken ct)
        {
            return Task.Run(() =>
            {
                long totalSize = 0;
                foreach (var dir in _werDirectories)
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
                foreach (var dir in _werDirectories)
                {
                    ct.ThrowIfCancellationRequested();
                    progress?.Report($"Limpando relatórios de erro: {dir}...");
                    totalCleaned += CleanDirectorySafe(dir, ct, progress);
                }
                return totalCleaned;
            });
        }
    }
}
