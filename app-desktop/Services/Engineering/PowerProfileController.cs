using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Management;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.Engineering
{
    /// <summary>
    /// Controlador de perfis de energia do sistema
    /// Implementa controle dinâmico baseado na carga e contexto
    /// </summary>
    public class PowerProfileController
    {
        private readonly ILoggingService _logger;
        private static bool _wmiPowerPlanAvailable = true;

        public PowerProfileController(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogInfo("[PowerController] Controlador de perfis de energia inicializado");
        }

        /// <summary>
        /// Obtém o perfil de energia atual
        /// </summary>
        public async Task<PowerProfile> GetCurrentPowerProfileAsync()
        {
            return await Task.Run(async () =>
            {
                // MÉTODO 1: Tentar WMI primeiro (somente se suportado neste hardware/OS)
                if (_wmiPowerPlanAvailable)
                {
                    try
                    {
                        using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PowerPlan WHERE IsActive = TRUE");

                        foreach (ManagementObject obj in searcher.Get())
                        {
                using var __dispose_obj = obj;
                            // validação robusta de objeto WMI
                            if (obj == null || obj.Properties == null) continue;

                            var instanceId = obj["InstanceID"]?.ToString() ?? "";
                            var friendlyName = obj["ElementName"]?.ToString() ?? "";

                            // Verificar se temos dados válidos
                            if (!string.IsNullOrEmpty(instanceId) && !string.IsNullOrEmpty(friendlyName))
                            {
                                _logger.LogInfo($"[PowerController] Perfil WMI encontrado: {friendlyName}");
                                return ParsePowerProfile(instanceId, friendlyName);
                            }
                        }

                        _logger.LogInfo("[PowerController] Nenhum perfil ativo encontrado via WMI, tentando powercfg");
                    }
                    catch (ManagementException mex)
                    {
                        // InvalidClass = WMI Win32_PowerPlan ausente (comum em alguns builds/hardware)
                        // não tratar como falha grave
                        if (mex.ErrorCode == ManagementStatus.InvalidClass)
                        {
                            _wmiPowerPlanAvailable = false;
                            _logger.LogInfo($"[PowerController] Win32_PowerPlan indisponível (InvalidClass) WMI desativado, powercfg como caminho principal. Detalhe: {mex.Message}");
                            return await GetCurrentProfileViaPowerCfgAsync();
                        }

                        _logger.LogWarning($"[PowerController] Erro WMI ao obter perfil atual: {mex.Message} (ErrorCode: {mex.ErrorCode})");
                    }
                    catch (UnauthorizedAccessException uaex)
                    {
                        _logger.LogWarning($"[PowerController] Acesso negado ao obter perfil atual: {uaex.Message}");
                        return PowerProfile.Balanced;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"[PowerController] Erro genérico ao obter perfil atual: {ex.Message}");
                    }
                }

                // MÉTODO 2: Fallback com powercfg
                try
                {
                    return await GetCurrentProfileViaPowerCfgAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[PowerController] Erro no fallback powercfg: {ex.Message}");
                }

                // MÉTODO 3: Último recurso - Balanced
                _logger.LogInfo("[PowerController] Usando perfil Balanced como fallback final");
                return PowerProfile.Balanced;
            });
        }

        /// <summary>
        /// Obtém perfil atual usando powercfg (fallback robusto)
        /// </summary>
        private async Task<PowerProfile> GetCurrentProfileViaPowerCfgAsync()
        {
            try
            {
                using var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "powercfg",
                        Arguments = "/getactivescheme",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };

                process.Start();
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
                {
                    // Parse da saída do powercfg
                    // Exemplo: "Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced)"
                    var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

                    foreach (var line in lines)
                    {
                        if (line.Contains("Power Scheme GUID:"))
                        {
                            var parts = line.Split('(');
                            if (parts.Length >= 2)
                            {
                                var guidPart = parts[0].Trim();
                                var namePart = parts[1].Replace(")", "").Trim();

                                // Extrair GUID
                                var guidIndex = guidPart.IndexOf(":", StringComparison.OrdinalIgnoreCase);
                                if (guidIndex >= 0)
                                {
                                    var guid = guidPart.Substring(guidIndex + 1).Trim();
                                    _logger.LogInfo($"[PowerController] Perfil powercfg encontrado: {namePart} ({guid})");
                                    return ParsePowerProfile($"Microsoft: PowerPlan\\{{{guid}}}", namePart);
                                }
                            }
                        }
                    }
                }
                else
                {
                    var error = await process.StandardError.ReadToEndAsync();
                    _logger.LogWarning($"[PowerController] Erro powercfg: {error}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[PowerController] Erro ao executar powercfg: {ex.Message}");
            }

            return PowerProfile.Balanced;
        }

        /// <summary>
        /// Define um perfil de energia específico
        /// </summary>
        public async Task<bool> SetPowerProfileAsync(PowerProfile profile)
        {
            _logger.LogInfo($"[PowerController] Configurando perfil: {profile}");

            // --------------------------------------------------------------
            // [MIGRATED] PowerProfileController delegando ao Brain v2
            // Brain.RequestPowerProfile() controla EPP de forma coordenada.
            // O fallback abaixo (powercfg /setactive) mantido para alterar o
            // PLANO completo do Windows (escopo adicional ao do Brain).
            // --------------------------------------------------------------
            try
            {
                var brain = VoltrisOptimizer.App.BrainV2;
                if (brain != null && brain.IsRunning)
                {
                    var brainProfile = profile switch
                    {
                        PowerProfile.PowerSaver => VoltrisOptimizer.Core.Brain.V2.VoltrisBrainV2.PowerProfileKind.BatterySaver,
                        PowerProfile.Balanced => VoltrisOptimizer.Core.Brain.V2.VoltrisBrainV2.PowerProfileKind.Balanced,
                        PowerProfile.HighPerformance => VoltrisOptimizer.Core.Brain.V2.VoltrisBrainV2.PowerProfileKind.HighPerformance,
                        PowerProfile.UltimatePerformance => VoltrisOptimizer.Core.Brain.V2.VoltrisBrainV2.PowerProfileKind.UltraPerformance,
                        _ => VoltrisOptimizer.Core.Brain.V2.VoltrisBrainV2.PowerProfileKind.Balanced
                    };

                    var brainResult = await brain.RequestPowerProfileAsync(brainProfile);
                    _logger.LogInfo($"[MIGRATED] PowerProfileController - Brain v2 {brainProfile}: {brainResult.Reason}");
                }
            }
            catch (Exception brainEx)
            {
                _logger.LogWarning($"[MIGRATED] PowerProfileController - Brain falhou: {brainEx.Message}");
            }

            try
            {
                var guid = GetPowerProfileGuid(profile);
                if (string.IsNullOrEmpty(guid))
                {
                    _logger.LogWarning($"[PowerController] GUID não encontrado para perfil {profile}");
                    return false;
                }

                // [FIX:UNICO-DONO-DE-ENERGIA] Este serviço não troca mais o plano.
                //
                // Ele resolvia um GUID e rodava `powercfg /setactive {guid}`.
                // O nome da classe é o que torna isso enganoso: "PowerProfile"
                // sugere que ele é dono do perfil — e ele não é. O Perfil
                // Inteligente é o dono; este serviço é anterior a ele e escolhia
                // plano por conta própria a partir de uma lista própria de GUIDs
                // (High Performance, Ultimate, Balanced).
                //
                // Essas GUIDs são as de fábrica do Windows, não as do Perfil. Ou
                // seja: este serviço trocava o plano do Voltris por um plano
                // nativo do Windows, e o Perfil não tinha como saber que isso
                // tinha acontecido — ele só veria, na próxima leitura, que o
                // plano não era o dele.
                //
                // A partir daqui a porta é única. O nome do método e a intenção
                // do usuário continuam válidos: ele quer um perfil de energia.
                _logger.LogInfo(
                    $"[PowerController] '{profile}' agora delega ao Perfil Inteligente " +
                    $"(antes: powercfg /setactive {guid}, por fora do dono da energia).");

                return VoltrisOptimizer.Services.Power.ProfilePowerAuthority.RequestProfileApply(
                    "PowerProfileController.SetProfile",
                    $"perfil solicitado: {profile}",
                    _logger);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[PowerController] Exceção ao definir perfil {profile}", ex);
                return false;
            }
        }

        /// <summary>
        /// Obtém todos os perfis de energia disponíveis
        /// </summary>
        public async Task<PowerProfileInfo[]> GetAvailableProfilesAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    var profiles = new List<PowerProfileInfo>();
                    using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PowerPlan");

                    foreach (ManagementObject obj in searcher.Get())
                    {
                using var __dispose_obj = obj;
                        var instanceId = obj["InstanceID"]?.ToString() ?? "";
                        var friendlyName = obj["ElementName"]?.ToString() ?? "";
                        var isActive = obj["IsActive"] as bool? ?? false;

                        var profile = ParsePowerProfile(instanceId, friendlyName);
                        profiles.Add(new PowerProfileInfo
                        {
                            Profile = profile,
                            FriendlyName = friendlyName,
                            Guid = instanceId.Replace("Microsoft: PowerPlan\\{", "").Replace("}", ""),
                            IsActive = isActive
                        });
                    }

                    return profiles.ToArray();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[PowerController] Erro ao obter perfis disponíveis: {ex.Message}");
                    return Array.Empty<PowerProfileInfo>();
                }
            });
        }

        /// <summary>
        /// Verifica se um perfil específico está disponível
        /// </summary>
        public async Task<bool> IsProfileAvailableAsync(PowerProfile profile)
        {
            var availableProfiles = await GetAvailableProfilesAsync();
            return availableProfiles.Any(p => p.Profile == profile);
        }

        /// <summary>
        /// Obtém informações detalhadas do perfil atual
        /// </summary>
        public async Task<PowerPlanDetails> GetCurrentPowerPlanDetailsAsync()
        {
            _logger.LogInfo("[PowerController] Obtendo detalhes do plano de energia atual...");

            try
            {
                var currentProfile = await GetCurrentPowerProfileAsync();
                var details = new PowerPlanDetails
                {
                    Profile = currentProfile,
                    Timestamp = DateTime.UtcNow,
                    Success = true
                };

                // Obter configurações específicas do plano
                details.Settings = await GetPowerPlanSettingsAsync(currentProfile);

                // Obter informações de bateria se disponível
                details.BatteryInfo = await GetBatteryInfoAsync();

                _logger.LogInfo($"[PowerController] Detalhes obtidos para {currentProfile}: {details.Settings.Count} configurações");
                return details;
            }
            catch (Exception ex)
            {
                _logger.LogError("[PowerController] Erro ao obter detalhes do plano", ex);
                return new PowerPlanDetails
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Timestamp = DateTime.UtcNow
                };
            }
        }

        #region Métodos Privados

        private PowerProfile ParsePowerProfile(string instanceId, string friendlyName)
        {
            var guid = instanceId.Replace("Microsoft: PowerPlan\\{", "").Replace("}", "");

            return guid.ToLowerInvariant() switch
            {
                "a1841308-3541-4fab-bc81-f51556f20b4a" => PowerProfile.PowerSaver,
                "381b4222-f694-41f0-9685-ff5bb260df2e" => PowerProfile.Balanced,
                "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c" => PowerProfile.HighPerformance,
                "e9a42b02-d5df-448d-aa00-03f14749eb61" => PowerProfile.UltimatePerformance,
                _ => friendlyName.ToLowerInvariant() switch
                {
                    var name when name.Contains("economia") || name.Contains("saver") => PowerProfile.PowerSaver,
                    var name when name.Contains("balance") || name.Contains("balanced") => PowerProfile.Balanced,
                    var name when name.Contains("alta") || name.Contains("high") || name.Contains("performance") => PowerProfile.HighPerformance,
                    var name when name.Contains("ultimate") || name.Contains("máximo") => PowerProfile.UltimatePerformance,
                    _ => PowerProfile.Balanced
                }
            };
        }

        private string GetPowerProfileGuid(PowerProfile profile)
        {
            return profile switch
            {
                PowerProfile.PowerSaver => "a1841308-3541-4fab-bc81-f51556f20b4a",
                PowerProfile.Balanced => "381b4222-f694-41f0-9685-ff5bb260df2e",
                PowerProfile.HighPerformance => "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c",
                PowerProfile.UltimatePerformance => "e9a42b02-d5df-448d-aa00-03f14749eb61",
                _ => ""
            };
        }

        private async Task<Dictionary<string, object>> GetPowerPlanSettingsAsync(PowerProfile profile)
        {
            var settings = new Dictionary<string, object>();

            try
            {
                var guid = GetPowerProfileGuid(profile);
                if (string.IsNullOrEmpty(guid)) return settings;

                // Obter sub-grupos do plano de energia
                using var searcher = new ManagementObjectSearcher($"SELECT * FROM Win32_PowerSetting WHERE InstanceID LIKE '%{guid}%'");

                foreach (ManagementObject obj in searcher.Get())
                {
                using var __dispose_obj = obj;
                    var settingId = obj["InstanceID"]?.ToString() ?? "";
                    var settingName = obj["ElementName"]?.ToString() ?? "";
                    var settingValue = obj["SettingIndex"] ?? 0;

                    settings[settingName] = settingValue;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[PowerController] Erro ao obter configurações: {ex.Message}");
            }

            return settings;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public uint BatteryLifeTime;
            public uint BatteryFullLifeTime;
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS systemPowerStatus);

        private async Task<BatteryInfo> GetBatteryInfoAsync()
        {
            var batteryInfo = new BatteryInfo();

            try
            {
                if (GetSystemPowerStatus(out SYSTEM_POWER_STATUS status))
                {
                    // BatteryFlag == 128 significa "Sem bateria do sistema"
                    bool isPresent = status.BatteryFlag != 128 && status.BatteryFlag != 255;
                    batteryInfo.IsPresent = isPresent;
                    
                    if (isPresent)
                    {
                        batteryInfo.BatteryLifePercent = status.BatteryLifePercent != 255 ? status.BatteryLifePercent : 100;
                        batteryInfo.EstimatedChargeRemaining = batteryInfo.BatteryLifePercent;
                        
                        bool charging = (status.BatteryFlag & 8) != 0;
                        batteryInfo.Charging = charging;
                        
                        batteryInfo.BatteryStatus = charging ? "Charging" : 
                            (status.BatteryFlag & 4) != 0 ? "Critical" :
                            (status.BatteryFlag & 2) != 0 ? "Low" : "Discharging";
                    }
                    else
                    {
                        batteryInfo.BatteryLifePercent = 100;
                        batteryInfo.EstimatedChargeRemaining = 100;
                        batteryInfo.BatteryStatus = "No Battery";
                        batteryInfo.Charging = false;
                    }
                    
                    batteryInfo.PowerLineStatus = status.ACLineStatus == 1 ? "Online" : "Offline";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[PowerController] Erro ao obter informações da bateria: {ex.Message}");
            }

            return batteryInfo;
        }

        #endregion
    }

    #region Classes de Suporte

    public class PowerProfileInfo
    {
        public PowerProfile Profile { get; set; }
        public string FriendlyName { get; set; }
        public string Guid { get; set; }
        public bool IsActive { get; set; }
    }

    public class PowerPlanDetails
    {
        public DateTime Timestamp { get; set; }
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public PowerProfile Profile { get; set; }
        public Dictionary<string, object> Settings { get; set; } = new();
        public BatteryInfo BatteryInfo { get; set; } = new();
    }

    public class BatteryInfo
    {
        public bool IsPresent { get; set; }
        public int BatteryLifePercent { get; set; }
        public int EstimatedChargeRemaining { get; set; }
        public string BatteryStatus { get; set; }
        public bool Charging { get; set; }
        public string PowerLineStatus { get; set; }
    }

    #endregion
}
 
