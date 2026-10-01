using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.GameRepairEnterprise.Interfaces;
using VoltrisOptimizer.Services.GameRepairEnterprise.Models;
using VoltrisOptimizer.Services.GameRepairEnterprise.Engine;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.GameRepairEnterprise.Providers
{
    public class VulkanProvider : IGameDependencyProvider
    {
        public string ProviderName => "Vulkan Hardware Diagnostics";
        private readonly ISecureDownloadService _downloadService;
        private readonly ILoggingService _logger;

        public VulkanProvider(ISecureDownloadService downloadService, ILoggingService logger)
        {
            _downloadService = downloadService;
            _logger = logger;
        }

        [DllImport("vulkan-1.dll", CallingConvention = CallingConvention.StdCall, SetLastError = true)]
        private static extern int vkEnumerateInstanceVersion(out uint pApiVersion);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);

        public async Task<List<GameDependencyDiagnosis>> DiagnoseAsync(CancellationToken ct)
        {
            var results = new List<GameDependencyDiagnosis>();
            var diag = new GameDependencyDiagnosis { ComponentId = "Vulkan_Runtime", Title = "Vulkan API & Hardware Support" };
            
            _logger.LogInfo("[VulkanProvider] Iniciando diagnóstico de hardware via Vulkan...");

            IntPtr hModule = LoadLibrary("vulkan-1.dll");
            if (hModule == IntPtr.Zero)
            {
                _logger.LogWarning("[VulkanProvider] vulkan-1.dll não encontrado. Driver de vídeo ausente ou hardware incompatível.");
                diag.IsHealthy = false;
                diag.MissingEvidences.Add("vulkan-1.dll base library");
                results.Add(diag);
                return results;
            }

            try
            {
                int result = vkEnumerateInstanceVersion(out uint apiVersion);
                if (result == 0) // VK_SUCCESS
                {
                    uint major = (apiVersion >> 22) & 0x7F;
                    uint minor = (apiVersion >> 12) & 0x3FF;
                    uint patch = apiVersion & 0xFFF;
                    
                    _logger.LogInfo($"[VulkanProvider] Vulkan detectado com sucesso: Instância Versão {major}.{minor}.{patch}");
                    
                    diag.IsHealthy = true;
                    diag.Evidences.Add($"API Version {major}.{minor}.{patch}");
                }
                else
                {
                    _logger.LogWarning($"[VulkanProvider] vkEnumerateInstanceVersion falhou com código {result}.");
                    diag.IsHealthy = false;
                    diag.MissingEvidences.Add("vkEnumerateInstanceVersion falhou (Possível ambiente de Virtualização sem GPU Passthrough)");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[VulkanProvider] Falha catastrófica ao tentar P/Invoke Vulkan: {ex.Message}");
                diag.IsHealthy = false;
                diag.MissingEvidences.Add("P/Invoke Exception (Falha no Loader)");
            }
            finally
            {
                FreeLibrary(hModule);
            }

            results.Add(diag);
            return await Task.FromResult(results);
        }

        public async Task<GameDependencyRepairResult> RepairAsync(GameDependencyDiagnosis issue, IProgress<(GameDependencyState state, int percentage, string message)> progress, CancellationToken ct)
        {
            _logger.LogWarning("[VulkanProvider] Vulkan não pode ser instalado de forma autônoma. É necessário atualizar os drivers da NVIDIA/AMD/Intel.");
            
            return new GameDependencyRepairResult 
            { 
                Success = false, 
                Message = "Vulkan Runtime depende dos drivers oficias da GPU. Atualize seus drivers de vídeo." 
            };
        }

        public async Task<bool> ValidateAsync(GameDependencyDiagnosis issue, CancellationToken ct)
        {
            var results = await DiagnoseAsync(ct);
            return results.Count > 0 && results[0].IsHealthy;
        }

        public async Task RollbackAsync(GameDependencyDiagnosis issue, CancellationToken ct)
        {
            await Task.CompletedTask;
        }
    }
}
