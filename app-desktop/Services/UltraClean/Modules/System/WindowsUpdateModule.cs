using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.UltraClean.Modules.System
{
    public class WindowsUpdateModule : DirectoryCleanupModuleBase
    {
        public override string Id => "System.WindowsUpdateHistory";
        public override string Name => "Histórico do Windows Update";
        public override string Description => "Logs de instalação e histórico de atualizações do Windows";
        public override string Category => "Sistema";
        public override ModuleSafetyLevel Safety => ModuleSafetyLevel.Safe;
        public override ModuleRiskLevel Risk => ModuleRiskLevel.Low;
        public override bool RequiresAdmin => true;

        public WindowsUpdateModule(ILoggingService logger) : base(logger) { }

        protected override string[] TargetDirectories => new[]
        {
            @"C:\Windows\Logs\WindowsUpdate",
            @"C:\Windows\SoftwareDistribution\DataStore\Logs"
        };

        protected override string[] FilePatterns => new[] { "*.log", "*.etl" };

        public override async Task<bool> CleanAsync(CancellationToken ct, IProgress<double>? progress = null)
        {
            _logger.LogInfo("[UltraClean] Iniciando limpeza seletiva de logs do Windows Update (Enterprise Policy: >7 dias)");
            return await Task.Run(() =>
            {
                foreach (var dir in TargetDirectories)
                {
                    if (ct.IsCancellationRequested) return false;
                    try
                    {
                        var di = new DirectoryInfo(Environment.ExpandEnvironmentVariables(dir));
                        if (di.Exists)
                        {
                            var files = di.GetFiles();
                            _logger.LogDebug($"[UltraClean] Escaneando {files.Length} arquivos em {dir}");
                            foreach (var file in files)
                            {
                                if (ct.IsCancellationRequested) return false;
                                
                                // Enterprise Policy: Manter logs dos últimos 7 dias por segurança
                                if (file.LastWriteTime < DateTime.Now.AddDays(-7))
                                {
                                    try 
                                    { 
                                        file.Delete(); 
                                        _logger.LogTrace($"[UltraClean] Removido log antigo: {file.Name}");
                                    } 
                                    catch (Exception ex)
                                    {
                                        _logger.LogTrace($"[UltraClean] Falha ao deletar log {file.Name}: {ex.Message}");
                                    }
                                }
                            }
                        }
                        else
                        {
                            _logger.LogDebug($"[UltraClean] Diretório de update não encontrado: {dir}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"[UltraClean] Erro inesperado ao limpar logs de update em {dir}: {ex.Message}");
                    }
                }
                _logger.LogSuccess("[UltraClean] Limpeza seletiva do Windows Update concluída.");
                return true;
            }, ct);
        }
    }
}
