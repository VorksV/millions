using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.Cleanup.Interfaces;

namespace VoltrisOptimizer.Services.Cleanup.Modules
{
    public abstract class BaseCleanupModule : ICleanupModule
    {
        public abstract string Name { get; }

        public abstract Task<long> AnalyzeAsync(CancellationToken ct);

        public abstract Task<long> CleanAsync(IProgress<string> progress, CancellationToken ct);

        protected long GetDirectorySizeSafe(string directoryPath)
        {
            long size = 0;
            if (!Directory.Exists(directoryPath)) return 0;

            try
            {
                foreach (var file in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        size += new FileInfo(file).Length;
                    }
                    catch { /* Ignora arquivos sem permissão ou deletados simultaneamente */ }
                }
            }
            catch { /* Ignora se o diretório principal for inacessível */ }

            return size;
        }

        protected long CleanDirectorySafe(string directoryPath, CancellationToken ct, IProgress<string> progress = null)
        {
            long freedSpace = 0;
            if (!Directory.Exists(directoryPath)) return 0;

            try
            {
                foreach (var file in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories))
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var info = new FileInfo(file);
                        long size = info.Length;
                        
                        // Opcional: Avisar progresso qual arquivo está deletando (se quiser muito detalhe)
                        // progress?.Report($"Deletando {info.Name}");
                        
                        info.Delete();
                        freedSpace += size;
                    }
                    catch (IOException) { /* Arquivo em uso, pula silenciosamente */ }
                    catch (UnauthorizedAccessException) { /* Sem permissão */ }
                    catch { /* Outras exceções */ }
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                // Ignora se não puder enumerar
            }

            return freedSpace;
        }
    }
}
