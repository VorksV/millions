using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Drivers
{
    public class DriverUpdateService
    {
        private readonly NvidiaDriverApi _nvidiaApi;
        private readonly AmdDriverApi _amdApi;
        private readonly IntelDriverApi _intelApi;
        private readonly WindowsUpdateFallback _wuaFallback;
        private readonly MicrosoftUpdateCatalogService _catalogService;
        private readonly UniversalDriverDetectionService _universalDetector;

        public IntelDriverApi IntelApi => _intelApi;

        public DriverUpdateService(string nvidiaApiKey = "PROD_NVDL_KEY", string amdApiToken = "PROD_AMD_TOKEN")
        {
            var sw = Stopwatch.StartNew();
            App.LoggingService?.LogInfo($"[DriverUpdateService] Constructor - ENTER [nvidiaApiKey={nvidiaApiKey?[..Math.Min(8, nvidiaApiKey.Length)]}..., amdApiToken={amdApiToken?[..Math.Min(8, amdApiToken.Length)]}...]");
            try
            {
                App.LoggingService?.LogInfo("[DriverUpdateService] Inicializando sistema profissional de detecção de drivers...");
                _nvidiaApi = new NvidiaDriverApi(nvidiaApiKey);
                App.LoggingService?.LogDebug("[DriverUpdateService] NvidiaDriverApi initialized");
                _amdApi = new AmdDriverApi(amdApiToken);
                App.LoggingService?.LogDebug("[DriverUpdateService] AmdDriverApi initialized");
                _intelApi = new IntelDriverApi();
                App.LoggingService?.LogDebug("[DriverUpdateService] IntelDriverApi initialized");
                _wuaFallback = new WindowsUpdateFallback();
                App.LoggingService?.LogDebug("[DriverUpdateService] WindowsUpdateFallback initialized");
                _catalogService = new MicrosoftUpdateCatalogService();
                App.LoggingService?.LogDebug("[DriverUpdateService] MicrosoftUpdateCatalogService initialized");
                _universalDetector = new UniversalDriverDetectionService();
                App.LoggingService?.LogDebug("[DriverUpdateService] UniversalDriverDetectionService initialized");
                App.LoggingService?.LogSuccess("[DriverUpdateService] Sistema profissional inicializado com sucesso!");
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError("[DriverUpdateService] Falha na inicialização do sistema profissional.", ex);
            }
            App.LoggingService?.LogInfo($"[DriverUpdateService] Constructor - EXIT [{sw.ElapsedMilliseconds}ms]");
        }

        public async Task<List<DriverUpdate>> CheckForUpdatesAsync(List<DeviceInfo> devices, bool quickScanMode = true)
        {
            var sw = Stopwatch.StartNew();
            App.LoggingService?.LogInfo($"[DriverUpdateService] CheckForUpdatesAsync - ENTER [devices.Count={devices.Count}, quickScan={quickScanMode}]");
            System.Diagnostics.Debug.WriteLine($"[DriverUpdateService] CheckForUpdatesAsync - Iniciando para {devices.Count} dispositivos, quickScan={quickScanMode}");
            
            try
            {
                App.LoggingService?.LogInfo($"[DriverUpdateService] Querying universal detector for {devices.Count} devices...");
                var universalUpdates = await _universalDetector.CheckAllDriversAsync(devices);
                App.LoggingService?.LogSuccess($"[DriverUpdateService] Universal detector returned {universalUpdates.Count} updates");
                
                if (universalUpdates.Any())
                {
                    App.LoggingService?.LogInfo($"[DriverUpdateService] Using universal updates directly: {universalUpdates.Count} found");
                    sw.Stop();
                    App.LoggingService?.LogInfo($"[DriverUpdateService] CheckForUpdatesAsync - EXIT [{sw.ElapsedMilliseconds}ms] - Returning {universalUpdates.Count} universal updates");
                    return universalUpdates;
                }

                App.LoggingService?.LogInfo($"[DriverUpdateService] ENTER - Fallback chain (vendor APIs, WUA, Catalog) for {devices.Count} devices");

                var updates = new List<DriverUpdate>();
                string osVersion = GetOsVersion();

                // Fase 1: APIs das Fabricantes
                App.LoggingService?.LogInfo($"[DriverUpdateService] Phase 1 - Querying vendor APIs for {devices.Count} devices");
                foreach (var device in devices)
                {
                    try
                    {
                        DriverPackage package = null;
                        string primaryId = (device.HardwareIds?.Split(';') ?? new string[0]).FirstOrDefault();

                        if (string.IsNullOrEmpty(primaryId))
                        {
                            App.LoggingService?.LogDebug($"[DriverUpdateService] Skipping {device.Description} - no hardware ID");
                            continue;
                        }

                        if (device.Vendor == "NVIDIA")
                        {
                            App.LoggingService?.LogInfo($"[DriverUpdateService] Querying NVIDIA API for {device.Description} [{primaryId}]");
                            package = await _nvidiaApi.GetLatestDriverAsync(primaryId, osVersion);
                            App.LoggingService?.LogInfo($"[DriverUpdateService] NVIDIA API result for {device.Description}: {(package != null ? $"v{package.Version}" : "null")}");
                        }
                        else if (device.Vendor == "AMD")
                        {
                            App.LoggingService?.LogInfo($"[DriverUpdateService] Querying AMD API for {device.Description} [{primaryId}]");
                            package = await _amdApi.GetLatestDriverAsync(primaryId, osVersion);
                            App.LoggingService?.LogInfo($"[DriverUpdateService] AMD API result for {device.Description}: {(package != null ? $"v{package.Version}" : "null")}");
                        }
                        else if (device.Vendor == "Intel")
                        {
                            App.LoggingService?.LogInfo($"[DriverUpdateService] Querying Intel API for {device.Description} [{primaryId}]");
                            package = await _intelApi.GetLatestDriverAsync(new CurrentDriverInfo
                            {
                                HardwareId = primaryId,
                                DeviceName = device.DeviceName,
                                Version = device.DriverVersion ?? "0.0.0.0",
                                Vendor = "Intel"
                            });
                            App.LoggingService?.LogInfo($"[DriverUpdateService] Intel API result for {device.Description}: {(package != null ? $"v{package.Version}" : "null")}");
                        }
                        else
                        {
                            App.LoggingService?.LogDebug($"[DriverUpdateService] No vendor API for {device.Description} (vendor={device.Vendor}) - skipping to next phase");
                        }

                        if (package != null)
                        {
                            var driverUpdate = new DriverUpdate
                            {
                                Device = device,
                                NewDriver = package,
                                UpdateReason = UpdateReason.MajorVersionUpgrade
                            };

                            updates.Add(driverUpdate);
                            App.LoggingService?.LogInfo($"[DriverUpdateService] Update Direto (API) encontrado para {device.Description}");
                            DriverDiagnosticsExporter.LogUpdateDecision(device.DeviceName, device.DriverVersion ?? "?", package.Version, package.DownloadUrl ?? package.SourceUrl ?? "?", true, "Vendor API fallback match");
                        }
                        else
                        {
                            App.LoggingService?.LogDebug($"[DriverUpdateService] No update from vendor API for {device.Description} ({device.Vendor})");
                        }
                    }
                    catch (Exception ex)
                    {
                        App.LoggingService?.LogError($"[DriverUpdateService] Erro na consulta de API para {device.Description}", ex);
                    }
                }

                App.LoggingService?.LogInfo($"[DriverUpdateService] Vendor API phase complete: {updates.Count} updates found out of {devices.Count} devices");

                if (!quickScanMode)
                {
                    App.LoggingService?.LogInfo($"[DriverUpdateService] ENTER - WUA/Catalog fallback chain");
                    var stillSearching = devices.Where(d => !updates.Any(u => u.Device.DeviceInstanceId == d.DeviceInstanceId)).ToList();
                    App.LoggingService?.LogInfo($"[DriverUpdateService] Still searching for {stillSearching.Count} devices after vendor API phase");

                    var uniqueDevicesForWua = stillSearching.Where(d =>
                        d.IsProblem ||
                        (d.Category != null && (d.Category == "Áudio" || d.Category == "Vídeo" || d.Category == "Rede"))
                    ).ToList();

                    if (uniqueDevicesForWua.Any())
                    {
                        App.LoggingService?.LogInfo($"[DriverUpdateService] Backup WUA activated for {uniqueDevicesForWua.Count} critical/OEM items");
                        var fallbackResults = await _wuaFallback.SearchWindowsUpdateAsync(uniqueDevicesForWua);
                        if (fallbackResults != null && fallbackResults.Any())
                        {
                            App.LoggingService?.LogSuccess($"[DriverUpdateService] WUA fallback found {fallbackResults.Count} updates");
                            updates.AddRange(fallbackResults);
                        }
                        else
                        {
                            App.LoggingService?.LogInfo($"[DriverUpdateService] WUA fallback returned no results");
                        }

                        var stillNoMatch = uniqueDevicesForWua.Where(d => !updates.Any(u => u.Device.DeviceInstanceId == d.DeviceInstanceId)).ToList();
                        if (stillNoMatch.Any())
                        {
                            App.LoggingService?.LogInfo($"[DriverUpdateService] Iniciando Fase 3 (Catalog) para {stillNoMatch.Count} itens...");
                            foreach (var device in stillNoMatch)
                            {
                                App.LoggingService?.LogInfo($"[DriverUpdateService] Querying Microsoft Update Catalog for {device.Description}...");
                                var catalogResults = await _catalogService.SearchCatalogAsync(device);
                                if (catalogResults != null && catalogResults.Any())
                                {
                                    App.LoggingService?.LogSuccess($"[DriverUpdateService] Catalog found {catalogResults.Count} updates for {device.Description}");
                                    updates.AddRange(catalogResults);
                                }
                                else
                                {
                                    App.LoggingService?.LogInfo($"[DriverUpdateService] Catalog returned no results for {device.Description}");
                                }
                            }
                        }
                        else
                        {
                            App.LoggingService?.LogInfo($"[DriverUpdateService] All devices matched after WUA phase");
                        }
                    }
                    else
                    {
                        App.LoggingService?.LogInfo($"[DriverUpdateService] No devices qualify for WUA fallback");
                    }
                    App.LoggingService?.LogInfo($"[DriverUpdateService] EXIT - WUA/Catalog fallback chain complete");
                }
                else
                {
                    App.LoggingService?.LogDebug("[DriverUpdateService] WUA/Catalog skip (quickScanMode=true)");
                }

                App.LoggingService?.LogSuccess($"[DriverUpdateService] Busca concluída com {updates.Count} candidatos selecionados.");
                sw.Stop();
                App.LoggingService?.LogInfo($"[DriverUpdateService] CheckForUpdatesAsync - EXIT [{sw.ElapsedMilliseconds}ms] - Returning {updates.Count} total updates");
                return updates;
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[DriverUpdateService] Erro crítico na verificação de drivers: {ex.Message}", ex);
                App.LoggingService?.LogInfo($"[DriverUpdateService] CheckForUpdatesAsync - EXIT [{sw.ElapsedMilliseconds}ms] - Error: {ex.Message}");
                return new List<DriverUpdate>();
            }
        }

        private string GetOsVersion()
        {
            var sw = Stopwatch.StartNew();
            App.LoggingService?.LogInfo($"[DriverUpdateService] GetOsVersion - ENTER");
            System.Diagnostics.Debug.WriteLine($"[DriverUpdateService] GetOsVersion - Obtendo versão do SO");
            var version = Environment.OSVersion.VersionString;
            App.LoggingService?.LogInfo($"[DriverUpdateService] GetOsVersion - EXIT [{sw.ElapsedMilliseconds}ms] -> {version}");
            System.Diagnostics.Debug.WriteLine($"[DriverUpdateService] GetOsVersion - Versão do SO: {version}");
            return version;
        }
    }
}
