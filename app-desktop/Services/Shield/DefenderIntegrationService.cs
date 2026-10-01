using System;
using System.Diagnostics;
using System.Management;
using System.Threading.Tasks;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services.Shield.Advanced;

namespace VoltrisOptimizer.Services.Shield
{
    public class DefenderIntegrationService
    {
        private readonly ILoggingService _logger;
        private readonly IProcessRunner _processRunner;
        private readonly DefenderScanService _defenderScanService;
        
        public DefenderIntegrationService(ILoggingService logger, IProcessRunner processRunner, DefenderScanService defenderScanService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
            _defenderScanService = defenderScanService ?? throw new ArgumentNullException(nameof(defenderScanService));
        }
        
        public async Task<DefenderStatus> GetStatusAsync()
        {
            _logger.LogInfo("[DefenderIntegration] Obtendo status do Windows Defender...");
            
            // Estratégia 1: WMI/CIM (mais confiável, sem dependência de encoding)
            var status = await Task.Run(TryGetStatusViaWmi);
            if (status != null)
            {
                _logger.LogSuccess($"[DefenderIntegration] Status obtido via WMI: Enabled={status.IsEnabled}, UpToDate={status.IsUpToDate}, Version={status.Version}");
                return status;
            }
            
            _logger.LogDebug("[DefenderIntegration] WMI indisponível; consultando SecurityCenter2");
            status = TryGetStatusViaSecurityCenter();
            if (status != null)
            {
                _logger.LogSuccess($"[DefenderIntegration] Status obtido via SecurityCenter2: Enabled={status.IsEnabled}, UpToDate={status.IsUpToDate}");
                return status;
            }
            
            // Estratégia 4: Último recurso - verificar se o processo MsMpEng está rodando
            var fallback = new DefenderStatus();
            fallback.IsEnabled = _processRunner.IsProcessRunning("MsMpEng");
            fallback.RealTimeProtectionEnabled = fallback.IsEnabled;
            fallback.IsUpToDate = false; // Sem confirmação, não assumir
            _logger.LogWarning($"[DefenderIntegration] Fallback por processo: MsMpEng rodando={fallback.IsEnabled}");
            return fallback;
        }
        
        private DefenderStatus? TryGetStatusViaWmi()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    @"root\Microsoft\Windows\Defender",
                    "SELECT * FROM MSFT_MpComputerStatus");
                
                foreach (ManagementObject obj in searcher.Get())
                {
                using var __dispose_obj = obj;
                    var status = new DefenderStatus();
                    
                    status.IsEnabled = GetWmiBool(obj, "AntivirusEnabled");
                    status.RealTimeProtectionEnabled = GetWmiBool(obj, "RealTimeProtectionEnabled");
                    
                    var sigVersion = obj["AntivirusSignatureVersion"];
                    status.Version = sigVersion?.ToString() ?? string.Empty;
                    
                    // Verificar frescor das definições (últimas 48 horas)
                    var lastUpdated = obj["AntivirusSignatureLastUpdated"];
                    if (lastUpdated is string dateStr && DateTime.TryParse(dateStr, out var parsedDate))
                    {
                        status.IsUpToDate = (DateTime.Now - parsedDate).TotalHours <= 48;
                    }
                    else if (lastUpdated is DateTime dt)
                    {
                        status.IsUpToDate = (DateTime.Now - dt).TotalHours <= 48;
                    }
                    else
                    {
                        // Se não conseguiu parsear a data, verificar via ManagementDateTime
                        try
                        {
                            var dmtfDate = obj["AntivirusSignatureLastUpdated"]?.ToString();
                            if (!string.IsNullOrEmpty(dmtfDate))
                            {
                                var converted = ManagementDateTimeConverter.ToDateTime(dmtfDate);
                                status.IsUpToDate = (DateTime.Now - converted).TotalHours <= 48;
                            }
                        }
                        catch { /* Não conseguiu parsear, manter false */ }
                    }
                    
                    return status;
                }
            }
            catch (ManagementException mex)
            {
                _logger.LogWarning($"[DefenderIntegration] WMI Defender namespace não disponível: {mex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[DefenderIntegration] Falha ao obter status via WMI: {ex.Message}");
            }
            
            return null;
        }
        
        private static bool GetWmiBool(ManagementObject obj, string propertyName)
        {
            try
            {
                var val = obj[propertyName];
                if (val is bool b) return b;
                if (val != null && bool.TryParse(val.ToString(), out var parsed)) return parsed;
            }
            catch { }
            return false;
        }
        
        private DefenderStatus? TryGetStatusViaSecurityCenter()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    @"root\SecurityCenter2",
                    "SELECT * FROM AntiVirusProduct");
                
                foreach (ManagementObject obj in searcher.Get())
                {
                using var __dispose_obj = obj;
                    var displayName = obj["displayName"]?.ToString() ?? "";
                    
                    // Procurar especificamente pelo Windows Defender / Microsoft Defender
                    if (!displayName.Contains("Windows Defender", StringComparison.OrdinalIgnoreCase) &&
                        !displayName.Contains("Microsoft Defender", StringComparison.OrdinalIgnoreCase))
                        continue;
                    
                    var status = new DefenderStatus();
                    
                    // productState é um bitmask: bits 12-15 = estado do produto, bits 4-7 = estado das definições
                    var productState = Convert.ToUInt32(obj["productState"]);
                    
                    // Bit 12 (0x1000) = ativado
                    status.IsEnabled = (productState & 0x1000) != 0;
                    status.RealTimeProtectionEnabled = status.IsEnabled;
                    
                    // Bit 4 (0x10) = definições desatualizadas (quando setado = desatualizado)
                    status.IsUpToDate = (productState & 0x10) == 0;
                    
                    return status;
                }
            }
            catch (ManagementException mex)
            {
                _logger.LogWarning($"[DefenderIntegration] SecurityCenter2 não disponível: {mex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[DefenderIntegration] Falha ao obter status via SecurityCenter2: {ex.Message}");
            }
            
            return null;
        }
        
        public async Task<bool> StartQuickScanAsync()
        {
            try
            {
                _logger.LogInfo("[DefenderIntegration] Iniciando scan rápido do Defender via MpCmdRun...");
                return await _defenderScanService.StartQuickScanAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError("[DefenderIntegration] Erro ao iniciar scan", ex);
                return false;
            }
        }
        
        public async Task<bool> UpdateDefinitionsAsync()
        {
            try
            {
                _logger.LogInfo("[DefenderIntegration] Atualizando definições do Defender via MpCmdRun...");
                return await _defenderScanService.UpdateDefinitionsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError("[DefenderIntegration] Erro ao atualizar definições", ex);
                return false;
            }
        }
        
        private bool CheckDefenderAvailable()
        {
            try
            {
                return _processRunner.IsProcessRunning("MsMpEng");
            }
            catch
            {
                return false;
            }
        }
    }
}
