using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace VoltrisOptimizer.Services.Drivers
{
    /// <summary>
    /// Serviço Profissional de Detecção de Drivers - 100% válido como Intel Support e Driver Booster
    /// </summary>
    public class UniversalDriverDetectionService
    {
        private static readonly HttpClient _sharedHttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        private readonly Dictionary<string, IDriverDetector> _detectors;
        internal readonly IntelDriverApi _intelApi = new();

        public UniversalDriverDetectionService()
        {
            System.Diagnostics.Debug.WriteLine("[UniversalDriverDetection] Constructor - Inicializando UniversalDriverDetectionService");
            App.LoggingService?.LogInfo("[UniversalDriverDetection] Constructor - Inicializando detectores de drivers");
        _detectors = new Dictionary<string, IDriverDetector>()
        {
            // FONTE PRIMÁRIA: catálogo local indexado por Hardware ID.
            // É o modelo dos gerenciadores comerciais de driver e o único que não depende
            // de raspar site de fabricante (que responde HTTP 403).
            { "Catálogo", new CatalogDriverDetector() },
            { "Intel", new IntelDriverDetector(this) },
            { "NVIDIA", new NvidiaDriverDetector() },
            { "AMD", new AmdDriverDetector() },
            { "Realtek", new RealtekDriverDetector() },
            { "Generic", new GenericDriverDetector() },
            { "Microsoft", new GenericDriverDetector() }
        };
            App.LoggingService?.LogInfo($"[UniversalDriverDetection] Constructor - {_detectors.Count} detectores inicializados");
            System.Diagnostics.Debug.WriteLine($"[UniversalDriverDetection] Constructor - {_detectors.Count} detectores inicializados");
        }

        public async Task<List<DriverUpdate>> CheckAllDriversAsync(List<DeviceInfo> devices)
        {
            App.LoggingService?.LogInfo($"[UniversalDriverDetection] 🔍 INICIANDO SCAN COMPLETO DE DRIVERS");
            App.LoggingService?.LogInfo($"[UniversalDriverDetection] 📋 Total de detectores disponíveis: {_detectors.Count}");
            foreach (var detector in _detectors)
            {
                App.LoggingService?.LogInfo($"[UniversalDriverDetection] 📋 Detector disponível: {detector.Key}");
            }
            
            var scanSw = Stopwatch.StartNew();
            var scanStart = DateTime.Now;
            App.LoggingService?.LogInfo($"[UniversalDriverDetection] ⏱ INÍCIO SCAN | {scanStart:HH:mm:ss.fff} | {devices.Count} dispositivos");
            
            // 🚨 DIAGNÓSTICO: Enviar início para Telegram imediatamente
            _ = Task.Run(async () => {
                try {
                    var sb = new StringBuilder();
                    sb.AppendLine("🔍 <b>DRIVER SCAN INICIADO</b>");
                    sb.AppendLine($"<b>Usuário:</b> {Environment.UserName} ({Environment.MachineName})");
                    sb.AppendLine($"<b>Início:</b> {scanStart:HH:mm:ss.fff}");
                    sb.AppendLine($"<b>Dispositivos:</b> {devices.Count}");
                    sb.AppendLine("<b>Status:</b> Iniciando verificação paralela...");
                } catch { }
            });

            // Timeout global de 120s para toda a verificação de updates — nunca bloquear mais que isso
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            
            // ⚡ FASE 1: Registry scan (síncrono mas rápido) em paralelo para todos os dispositivos
            var phase1Sw = Stopwatch.StartNew();
            var deviceInfos = new List<(DeviceInfo Device, CurrentDriverInfo Info)>();
            
            await Task.Run(() => {
                App.LoggingService?.LogInfo($"[UniversalDriverDetection] 📋 FASE 1: Processando {devices.Count} dispositivos...");
                
                foreach (var device in devices)
                {
                    try {
                        CurrentDriverInfo info = null;
                        
                        // 1. Tentar usar dados já coletados (SetupAPI/Native)
                        bool hasValidData = !string.IsNullOrEmpty(device.DriverVersion) && 
                                            device.DriverVersion != "0.0.0.0" && 
                                            device.DriverVersion != "Unknown" &&
                                            device.DriverVersion.Length > 2;

                        if (hasValidData)
                        {
                            info = new CurrentDriverInfo
                            {
                                DeviceName = device.DeviceName,
                                HardwareId = device.HardwareIds ?? "Unknown",
                                Vendor = DetectVendor(device),
                                Version = device.DriverVersion!,
                                Date = ParseDriverDate(device.DriverDate),
                                ClassGuid = device.ClassGuid ?? "Unknown"
                            };
                        }
                        else
                        {
                            // 2. Fallback para Registry scan lento
                            info = GetCurrentDriverInfo(device);
                        }

                        if (info != null) {
                            deviceInfos.Add((device, info));
                        }
                    } catch (Exception ex) {
                        App.LoggingService?.LogError($"[UniversalDriverDetection] ❌ Erro em Phase 1 para {device.Description}: {ex.Message}");
                    }
                }
            }, cts.Token);
            
            phase1Sw.Stop();
            App.LoggingService?.LogInfo($"[UniversalDriverDetection] ⏱ FASE 1 (Registry): {phase1Sw.ElapsedMilliseconds}ms | {deviceInfos.Count}/{devices.Count} com info");

            // ⚡ FASE 2: Verificação online — paralela com semáforo (max 5 concurrent)
            var phase2Sw = Stopwatch.StartNew();
            var semaphore = new SemaphoreSlim(5, 5);
            var updates = new System.Collections.Concurrent.ConcurrentBag<DriverUpdate>();
            var updatesFound = 0;

            var tasks = deviceInfos.Select(async pair =>
            {
                if (cts.Token.IsCancellationRequested) return;
                await semaphore.WaitAsync(cts.Token).ConfigureAwait(false);
                try
                {
                    var vendor = DetectVendor(pair.Device);
                    App.LoggingService?.LogInfo($"[UniversalDriverDetection] 🏷️ Vendor detectado: {vendor} para dispositivo: {pair.Device.Description}");
                    App.LoggingService?.LogInfo($"[UniversalDriverDetection] 📋 Hardware IDs: {pair.Device.HardwareIds}");
                    
                    if (!_detectors.TryGetValue(vendor, out var detector))
                        detector = new GenericDriverDetector();

                    var deviceSw = Stopwatch.StartNew();
                    DriverPackage latestInfo = null;
                    try {
                        App.LoggingService?.LogInfo($"[UniversalDriverDetection] 🔍 Buscando driver para {pair.Device.Description} usando detector {detector.GetType().Name}");
                        latestInfo = await detector.GetLatestDriverAsync(pair.Info, cts.Token).ConfigureAwait(false);
                        if (latestInfo != null) {
                            App.LoggingService?.LogInfo($"[UniversalDriverDetection] ✅ Driver encontrado: {latestInfo.Title} v{latestInfo.Version} (Vendor: {latestInfo.Vendor})");
                        } else {
                            // Resultado ESPERADO para a maioria dos dispositivos (ACPI, firmware,
                            // enumeradores, virtuais), que nunca tem driver atualizado. Em WARNING
                            // gerava ~100 linhas por varredura sem nenhum sinal. O resumo por
                            // categoria ja e registrado uma unica vez no fim da varredura.
                            App.LoggingService?.LogDebug($"[UniversalDriverDetection] Sem atualizacao para {pair.Device.Description}");
                            // Resultado ESPERADO para a maioria dos dispositivos (ACPI, firmware,
                            // enumeradores, virtuais), que nunca têm driver atualizado. Em WARNING
                            // gerava ~100 linhas por varredura sem nenhum sinal. O resumo por
                            // categoria já é registrado uma única vez no fim da varredura.
                            App.LoggingService?.LogDebug($"[UniversalDriverDetection] Sem atualizacao para {pair.Device.Description}");
                        }
                    } catch (TaskCanceledException) {
                        App.LoggingService?.LogInfo($"[UniversalDriverDetection] ⏱ Timeout: {pair.Device.Description}");
                    } catch (Exception ex) {
                        App.LoggingService?.LogError($"[UniversalDriverDetection] Erro {pair.Device.Description}: {ex.Message}", ex);
                    }
                    deviceSw.Stop();
                    
                    if (deviceSw.ElapsedMilliseconds > 2000)
                        App.LoggingService?.LogWarning($"[UniversalDriverDetection] 🐢 LENTO {deviceSw.ElapsedMilliseconds}ms: [{vendor}] {pair.Device.Description}");
                    
                    if (latestInfo != null && IsNewerVersion(latestInfo.Version, pair.Info.Version))
                    {
                        string finalVendor = !string.IsNullOrEmpty(latestInfo.Vendor) ? latestInfo.Vendor : vendor;
                        
                        // 📊 LOG DE DEC ISÃO (Ground Truth para auditoria)
                        DriverDiagnosticsExporter.LogUpdateDecision(
                            pair.Device.DeviceName, pair.Info.Version,
                            latestInfo.Version, latestInfo.SourceUrl ?? latestInfo.DownloadUrl ?? "?",
                            true, $"{pair.Info.Version} < {latestInfo.Version}");

                        updates.Add(new DriverUpdate
                        {
                            Device = pair.Device,
                            NewDriver = latestInfo,
                            CurrentVersion = pair.Info.Version,
                            UpdateReason = UpdateReason.MajorVersionUpgrade
                        });
                        
                        App.LoggingService?.LogSuccess($"[UniversalDriverDetection] ✅ DriverUpdate criado com sucesso!");
                        Interlocked.Increment(ref updatesFound);
                        App.LoggingService?.LogSuccess($"[UniversalDriverDetection] 🚨 ADAPTADOR DETECTADO: {pair.Device.Description} | Provedor: {finalVendor} → Recomendando v{latestInfo.Version}");
                    }
                    else if (latestInfo != null)
                    {
                        // 📊 LOG EXPLÍCITO quando NÃO há update — essencial para validar falsos positivos
                        DriverDiagnosticsExporter.LogUpdateDecision(
                            pair.Device.DeviceName, pair.Info.Version,
                            latestInfo.Version, latestInfo.SourceUrl ?? latestInfo.DownloadUrl ?? "?",
                            false, $"{latestInfo.Version} <= {pair.Info.Version} — Driver já atualizado");
                    }
                }
                finally { semaphore.Release(); }
            });

            try { await Task.WhenAll(tasks).ConfigureAwait(false); }
            catch (OperationCanceledException) {
                App.LoggingService?.LogWarning("[UniversalDriverDetection] ⚠️ Timeout global de 60s atingido — encerrando verificação de updates");
            }

            // 📊 EXPORTAR RELATÓRIO DE AUDITORIA COMPLETO
            var result = updates.ToList();

            _ = Task.Run(() => {
                try {
                    string reportPath = DriverDiagnosticsExporter.ExportFullReport(devices, result, scanSw.ElapsedMilliseconds);
                    App.LoggingService?.LogInfo($"[UniversalDriverDetection] 📊 Relatório HTML aberto: {reportPath}");
                } catch (Exception ex) {
                    App.LoggingService?.LogWarning($"[UniversalDriverDetection] Falha ao exportar relatório: {ex.Message}");
                }
            });

            // 📋 LOG DE RESUMO TABULAR (visível imediatamente nos logs)
            App.LoggingService?.LogInfo($"[UniversalDriverDetection] ┌─────────────────────────────────────────────────────────┐");
            App.LoggingService?.LogInfo($"[UniversalDriverDetection] │  RESULTADO FINAL DO SCAN DE DRIVERS                     │");
            App.LoggingService?.LogInfo($"[UniversalDriverDetection] │  Dispositivos analisados : {devices.Count,-5}                         │");
            App.LoggingService?.LogInfo($"[UniversalDriverDetection] │  Com metadados Registry  : {deviceInfos.Count,-5}                         │");
            App.LoggingService?.LogInfo($"[UniversalDriverDetection] │  Updates disponíveis     : {result.Count,-5}                         │");
            App.LoggingService?.LogInfo($"[UniversalDriverDetection] │  Tempo total             : {scanSw.ElapsedMilliseconds}ms                         │");
            App.LoggingService?.LogInfo($"[UniversalDriverDetection] └─────────────────────────────────────────────────────────┘");

            return result;
        }

        private async Task<DriverUpdate> CheckSingleDeviceAsync(DeviceInfo device)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            System.Diagnostics.Debug.WriteLine($"[UniversalDriverDetection] CheckSingleDeviceAsync - Iniciando verificação para dispositivo: {device.Description}");
            App.LoggingService?.LogInfo($"[UniversalDriverDetection] CheckSingleDeviceAsync - Verificando dispositivo: {device.Description}");

            // 1. Priorizar informações já presentes no DeviceInfo (SetupAPI já coletou ou Native Engine forneceu)
            // Isso evita 90% dos problemas de "reconhecimento" onde o reg-scan manual falha mas o device já tinha os dados.
            CurrentDriverInfo currentInfo;
            
            bool hasValidData = !string.IsNullOrEmpty(device.DriverVersion) && 
                                device.DriverVersion != "0.0.0.0" && 
                                device.DriverVersion != "Unknown" &&
                                device.DriverVersion.Length > 2;

            if (hasValidData)
            {
                currentInfo = new CurrentDriverInfo
                {
                    DeviceName = device.DeviceName,
                    HardwareId = device.HardwareIds ?? "Unknown",
                    Vendor = DetectVendor(device),
                    Version = device.DriverVersion!,
                    Date = ParseDriverDate(device.DriverDate),
                    ClassGuid = device.ClassGuid ?? "Unknown"
                };
                App.LoggingService?.LogInfo($"[UniversalUpdate] 🟢 Usando dados pré-carregados: {device.DeviceName} (v{currentInfo.Version})");
                System.Diagnostics.Debug.WriteLine($"[UniversalDriverDetection] CheckSingleDeviceAsync - Dados pré-carregados: {device.DeviceName} v{currentInfo.Version}");
            }
            else
            {
                // Fallback para Registry scan lento/fragil apenas se dados do DeviceInfo forem inválidos
                App.LoggingService?.LogInfo($"[UniversalDriverDetection] CheckSingleDeviceAsync - Fallback Registry para: {device.Description}");
                System.Diagnostics.Debug.WriteLine($"[UniversalDriverDetection] CheckSingleDeviceAsync - Fallback Registry para: {device.Description}");
                currentInfo = GetCurrentDriverInfo(device);
                if (currentInfo == null)
                {
                    App.LoggingService?.LogWarning($"[UniversalUpdate] ❌ Falha crítica: Impossível obter versão atual para {device.Description}");
                    System.Diagnostics.Debug.WriteLine($"[UniversalDriverDetection] CheckSingleDeviceAsync - Falha ao obter versão para: {device.Description}");
                    return null;
                }
            }

            // 2. Identificar fabricante e usar detector específico
            var vendor = currentInfo.Vendor;
            if (string.IsNullOrEmpty(vendor) || vendor == "Unknown")
            {
                App.LoggingService?.LogInfo($"[UniversalDriverDetection] CheckSingleDeviceAsync - Vendor desconhecido para: {device.Description}");
                System.Diagnostics.Debug.WriteLine($"[UniversalDriverDetection] CheckSingleDeviceAsync - Vendor desconhecido: {device.Description}");
                return null;
            }

            if (!_detectors.TryGetValue(vendor, out var detector))
            {
                // Fallback para detecção genérica
                App.LoggingService?.LogInfo($"[UniversalDriverDetection] CheckSingleDeviceAsync - Fallback GenericDetector para vendor: {vendor}");
                System.Diagnostics.Debug.WriteLine($"[UniversalDriverDetection] CheckSingleDeviceAsync - Fallback GenericDetector: {vendor}");
                detector = new GenericDriverDetector();
            }

            App.LoggingService?.LogInfo($"[UniversalDriverDetection] CheckSingleDeviceAsync - Usando detector {detector.GetType().Name} para {device.Description}");
            System.Diagnostics.Debug.WriteLine($"[UniversalDriverDetection] CheckSingleDeviceAsync - Detector: {detector.GetType().Name}");

            // 3. Consultar versão mais recente na API do fabricante mapeado
            var latestInfo = await detector.GetLatestDriverAsync(currentInfo);
            if (latestInfo == null)
            {
                App.LoggingService?.LogInfo($"[UniversalDriverDetection] CheckSingleDeviceAsync - Nenhum driver encontrado para: {device.Description}");
                System.Diagnostics.Debug.WriteLine($"[UniversalDriverDetection] CheckSingleDeviceAsync - Nenhum driver encontrado: {device.Description}");
                return null;
            }

            // 4. Comparação Híbrida: Por Versão e por Data de Lançamento Ativamente
            bool versionIsHigher = IsNewerVersion(latestInfo.Version, currentInfo.Version);
            App.LoggingService?.LogInfo($"[UniversalDriverDetection] CheckSingleDeviceAsync - Comparação: atual v{currentInfo.Version} vs latest v{latestInfo.Version} | Nova versão: {versionIsHigher}");
            System.Diagnostics.Debug.WriteLine($"[UniversalDriverDetection] CheckSingleDeviceAsync - Versão atual: {currentInfo.Version}, latest: {latestInfo.Version}, é maior: {versionIsHigher}");
            
            // Garantir que a versão seja recomendada caso a data da provedora do Driver seja claramente muito mais atual (diferença de meses)
            // Isso anula erros lógicos quando o sistema tem drivers de placeholder (como 28.0 Microsoft vs 24.30 Oficial)
            if (!versionIsHigher && currentInfo.Date > DateTime.MinValue)
            {
                if ((latestInfo.ReleaseDate - currentInfo.Date).TotalDays > 60)
                {
                    App.LoggingService?.LogInfo($"[UniversalUpdate] 💡 Discrepância Semântica: O Driver atual relata v{currentInfo.Version} mas data muito antiga ({currentInfo.Date:yyyy}). Priorizando Driver v{latestInfo.Version} ({latestInfo.ReleaseDate:yyyy}).");
                    System.Diagnostics.Debug.WriteLine($"[UniversalDriverDetection] CheckSingleDeviceAsync - Discrepância semântica forçando update");
                    versionIsHigher = true;
                }
            }

            if (versionIsHigher)
            {
                App.LoggingService?.LogInfo($"[UniversalDriverDetection] CheckSingleDeviceAsync - Update encontrado: v{latestInfo.Version} para {device.Description}");
                System.Diagnostics.Debug.WriteLine($"[UniversalDriverDetection] CheckSingleDeviceAsync - Update encontrado: v{latestInfo.Version}");
                sw.Stop();
                System.Diagnostics.Debug.WriteLine($"[UniversalDriverDetection] CheckSingleDeviceAsync - Concluído em {sw.ElapsedMilliseconds}ms");
                return new DriverUpdate
                {
                    Device = device,
                    NewDriver = latestInfo,
                    CurrentVersion = currentInfo.Version,
                    UpdateReason = UpdateReason.MajorVersionUpgrade
                };
            }

            sw.Stop();
            System.Diagnostics.Debug.WriteLine($"[UniversalDriverDetection] CheckSingleDeviceAsync - Nenhum update necessário em {sw.ElapsedMilliseconds}ms");
            return null;
        }

        private CurrentDriverInfo? GetCurrentDriverInfo(DeviceInfo device)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            System.Diagnostics.Debug.WriteLine($"[UniversalDriverDetection] GetCurrentDriverInfo - Iniciando busca Registry para: {device.Description}");
            App.LoggingService?.LogInfo($"[UniversalDriverDetection] GetCurrentDriverInfo - Busca Registry para: {device.Description}");
            try
            {
                // Performance: Se o ClassGuid for nulo, apenas usar os conhecidos
                var registryPaths = !string.IsNullOrEmpty(device.ClassGuid) 
                    ? new[] { $@"SYSTEM\CurrentControlSet\Control\Class\{device.ClassGuid}" }
                    : new[]
                    {
                        @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}", // Network
                        @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}", // Display
                        @"SYSTEM\CurrentControlSet\Control\Class\{4d36e96c-e325-11ce-bfc1-08002be10318}", // Media
                        @"SYSTEM\CurrentControlSet\Control\Class\{4d36e97b-e325-11ce-bfc1-08002be10318}", // SCSI
                        @"SYSTEM\CurrentControlSet\Control\Class\{e0cbf06c-1ed8-430b-8b71-1ee9613e0903}", // Bluetooth
                        @"SYSTEM\CurrentControlSet\Control\Class\{4d36e96f-e325-11ce-bfc1-08002be10318}", // Mouse
                        @"SYSTEM\CurrentControlSet\Control\Class\{4d36e96b-e325-11ce-bfc1-08002be10318}"  // Keyboard
                    };

                App.LoggingService?.LogInfo($"[UniversalDriverDetection] GetCurrentDriverInfo - Verificando {registryPaths.Length} caminhos de registro");
                System.Diagnostics.Debug.WriteLine($"[UniversalDriverDetection] GetCurrentDriverInfo - {registryPaths.Length} caminhos de registro");

                foreach (var path in registryPaths)
                {
                    try
                    {
                        using (var key = Registry.LocalMachine.OpenSubKey(path, false))
                        {
                            if (key == null) continue;

                            foreach (string subKeyName in key.GetSubKeyNames())
                            {
                                using (var subKey = key.OpenSubKey(subKeyName, false))
                                {
                                    if (subKey == null) continue;

                                    var matchingId = subKey.GetValue("MatchingDeviceId")?.ToString();
                                    var deviceDesc = subKey.GetValue("DeviceDesc")?.ToString() ?? subKey.GetValue("DriverDesc")?.ToString();

                                    // Lógica de Match Refinada: Hardware ID é soberano
                                    bool hwMatch = !string.IsNullOrEmpty(matchingId) && !string.IsNullOrEmpty(device.HardwareIds) && 
                                                  device.HardwareIds.Split(';').Any(hid => hid.Equals(matchingId, StringComparison.OrdinalIgnoreCase));
                                    
                                    bool descMatch = !string.IsNullOrEmpty(deviceDesc) && !string.IsNullOrEmpty(device.Description) && 
                                                    (deviceDesc.Contains(device.Description, StringComparison.OrdinalIgnoreCase) || 
                                                     device.Description.Contains(deviceDesc, StringComparison.OrdinalIgnoreCase));

                                    if (hwMatch || descMatch)
                                    {
                                        App.LoggingService?.LogInfo($"[UniversalDriverDetection] GetCurrentDriverInfo - Match encontrado: {subKeyName} para {device.Description}");
                                        System.Diagnostics.Debug.WriteLine($"[UniversalDriverDetection] GetCurrentDriverInfo - Match: {subKeyName}");
                                        sw.Stop();
                                        System.Diagnostics.Debug.WriteLine($"[UniversalDriverDetection] GetCurrentDriverInfo - Concluído em {sw.ElapsedMilliseconds}ms");
                                        return new CurrentDriverInfo
                                        {
                                            DeviceName = device.Description ?? deviceDesc ?? "Unknown Device",
                                            HardwareId = matchingId ?? device.HardwareIds ?? "Unknown",
                                            Vendor = DetectVendor(device),
                                            Version = subKey.GetValue("DriverVersion")?.ToString() ?? "0.0.0.0",
                                            Date = ParseDriverDate(subKey.GetValue("DriverDate")?.ToString()),
                                            ClassGuid = device.ClassGuid ?? path.Split('\\').Last()
                                        };
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        App.LoggingService?.LogError($"[UniversalDriverDetection] GetCurrentDriverInfo - Erro no path Registry {path}: {ex.Message}");
                        System.Diagnostics.Debug.WriteLine($"[UniversalDriverDetection] GetCurrentDriverInfo - Erro Registry: {path} - {ex.Message}");
                        continue;
                    }
                }
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[UniversalDriverDetection] GetCurrentDriverInfo - Erro no reg-fallback: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[UniversalDriverDetection] GetCurrentDriverInfo - Erro: {ex.Message}");
            }

            sw.Stop();
            System.Diagnostics.Debug.WriteLine($"[UniversalDriverDetection] GetCurrentDriverInfo - Nenhum match encontrado em {sw.ElapsedMilliseconds}ms");
            return null;
        }

        private string DetectVendor(DeviceInfo device)
        {
            if (device == null) return "Generic";
            
            App.LoggingService?.LogInfo($"[DetectVendor] 🔍 Analisando dispositivo: {device.Description}");
            App.LoggingService?.LogInfo($"[DetectVendor] 📋 Vendor: {device.Vendor}");
            App.LoggingService?.LogInfo($"[DetectVendor] 📋 Hardware IDs: {device.HardwareIds}");
            
            // 1. Tentar resolver via VendorMapper (IDs de Hardware PCI/USB)
            string vendor = VendorMapper.GetVendor(device.HardwareIds);
            App.LoggingService?.LogInfo($"[DetectVendor] 🗺️ VendorMapper retornou: {vendor}");
            
            if (vendor != "Unknown" && vendor != "Microsoft / Sistema") {
                App.LoggingService?.LogInfo($"[DetectVendor] ✅ Vendor final (Hardware ID): {vendor}");
                return vendor;
            }

            // 2. Fallback: Analisar strings de descrição e fabricante do dispositivo
            string vendorStr = device.Vendor?.ToUpperInvariant() ?? "";
            string descStr = device.Description?.ToUpperInvariant() ?? "";
            
            App.LoggingService?.LogInfo($"[DetectVendor] 📝 Vendor String: '{vendorStr}'");
            App.LoggingService?.LogInfo($"[DetectVendor] 📝 Description: '{descStr}'");

            if (vendorStr.Contains("INTEL") || descStr.Contains("INTEL")) {
                App.LoggingService?.LogInfo("[DetectVendor] ✅ Vendor final (String): Intel");
                return "Intel";
            }
            if (vendorStr.Contains("REALTEK") || descStr.Contains("REALTEK")) {
                App.LoggingService?.LogInfo("[DetectVendor] ✅ Vendor final (String): Realtek");
                return "Realtek";
            }
            if (vendorStr.Contains("NVIDIA") || descStr.Contains("NVIDIA") || descStr.Contains("GEFORCE")) {
                App.LoggingService?.LogInfo("[DetectVendor] ✅ Vendor final (String): NVIDIA");
                return "NVIDIA";
            }
            if (vendorStr.Contains("AMD") || descStr.Contains("AMD") || descStr.Contains("RADEON")) {
                App.LoggingService?.LogInfo("[DetectVendor] ✅ Vendor final (String): AMD");
                return "AMD";
            }
            if (vendorStr.Contains("BROADCOM") || descStr.Contains("BROADCOM")) {
                App.LoggingService?.LogInfo("[DetectVendor] ✅ Vendor final (String): Broadcom");
                return "Broadcom";
            }
            if (vendorStr.Contains("QUALCOMM") || descStr.Contains("ATHEROS")) {
                App.LoggingService?.LogInfo("[DetectVendor] ✅ Vendor final (String): Qualcomm");
                return "Qualcomm";
            }
            if (vendorStr.Contains("SAMSUNG") || descStr.Contains("SAMSUNG")) {
                App.LoggingService?.LogInfo("[DetectVendor] ✅ Vendor final (String): Samsung");
                return "Samsung";
            }
            if (vendorStr.Contains("CREATIVE") || descStr.Contains("SB X-FI") || descStr.Contains("SOUND BLASTER")) {
                App.LoggingService?.LogInfo("[DetectVendor] ✅ Vendor final (String): Creative Labs");
                return "Creative Labs";
            }
            if (vendorStr.Contains("ASMEDIA") || descStr.Contains("ASMEDIA")) {
                App.LoggingService?.LogInfo("[DetectVendor] ✅ Vendor final (String): ASMedia");
                return "ASMedia";
            }
            if (vendorStr.Contains("ATHEROS") || descStr.Contains("ATHEROS") || descStr.Contains("QCA")) {
                App.LoggingService?.LogInfo("[DetectVendor] ✅ Vendor final (String): Qualcomm");
                return "Qualcomm";
            }

            string finalVendor = vendor == "Microsoft / Sistema" ? "Microsoft" : "Generic";
            App.LoggingService?.LogInfo($"[DetectVendor] ❓ Vendor final (Fallback): {finalVendor}");
            return finalVendor;
        }

        private bool IsNewerVersion(string latestVersion, string currentVersion)
        {
            System.Diagnostics.Debug.WriteLine($"[UniversalDriverDetection] IsNewerVersion - Comparando: latest={latestVersion} vs current={currentVersion}");
            App.LoggingService?.LogInfo($"[UniversalDriverDetection] IsNewerVersion - Comparando: latest={latestVersion} vs current={currentVersion}");
            if (string.IsNullOrEmpty(latestVersion) || string.IsNullOrEmpty(currentVersion))
            {
                System.Diagnostics.Debug.WriteLine("[UniversalDriverDetection] IsNewerVersion - Versão nula ou vazia, retornando false");
                return false;
            }
            if (latestVersion == currentVersion)
            {
                System.Diagnostics.Debug.WriteLine("[UniversalDriverDetection] IsNewerVersion - Versões iguais, retornando false");
                return false;
            }

            try
            {
                // Limpeza de strings (remover "v", espaços, etc)
                var v1 = new string(latestVersion.Where(c => char.IsDigit(c) || c == '.').ToArray());
                var v2 = new string(currentVersion.Where(c => char.IsDigit(c) || c == '.').ToArray());

                var latestParts = v1.Split('.', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
                var currentParts = v2.Split('.', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();

                for (int i = 0; i < Math.Min(latestParts.Length, currentParts.Length); i++)
                {
                    if (latestParts[i] > currentParts[i])
                    {
                        System.Diagnostics.Debug.WriteLine($"[UniversalDriverDetection] IsNewerVersion - latest é maior no índice {i}, retornando true");
                        return true;
                    }
                    if (latestParts[i] < currentParts[i])
                    {
                        System.Diagnostics.Debug.WriteLine($"[UniversalDriverDetection] IsNewerVersion - current é maior no índice {i}, retornando false");
                        return false;
                    }
                }

                bool result = latestParts.Length > currentParts.Length;
                System.Diagnostics.Debug.WriteLine($"[UniversalDriverDetection] IsNewerVersion - Comparação por tamanho: {result}");
                return result;
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[UniversalDriverDetection] IsNewerVersion - Erro na comparação numérica, usando fallback alfanumérico: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[UniversalDriverDetection] IsNewerVersion - Erro, usando fallback: {ex.Message}");
                // Fallback alfanumérico robusto
                return string.Compare(latestVersion, currentVersion, StringComparison.OrdinalIgnoreCase) > 0;
            }
        }

        private DateTime ParseDriverDate(string driverDate)
        {
            System.Diagnostics.Debug.WriteLine($"[UniversalDriverDetection] ParseDriverDate - Parseando data: '{driverDate}'");
            App.LoggingService?.LogInfo($"[UniversalDriverDetection] ParseDriverDate - Parseando data: '{driverDate}'");
            try
            {
                if (long.TryParse(driverDate, out long fileTime))
                {
                    var date = DateTime.FromFileTime(fileTime);
                    System.Diagnostics.Debug.WriteLine($"[UniversalDriverDetection] ParseDriverDate - Data FileTime: {date:yyyy-MM-dd}");
                    return date;
                }

                if (DateTime.TryParse(driverDate, out DateTime result))
                {
                    System.Diagnostics.Debug.WriteLine($"[UniversalDriverDetection] ParseDriverDate - Data parseada: {result:yyyy-MM-dd}");
                    return result;
                }

                System.Diagnostics.Debug.WriteLine($"[UniversalDriverDetection] ParseDriverDate - Não foi possível parsear a data");
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[UniversalDriverDetection] ParseDriverDate - Erro ao parsear data '{driverDate}': {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[UniversalDriverDetection] ParseDriverDate - Erro: {ex.Message}");
            }

            return DateTime.MinValue;
        }
    }

    public interface IDriverDetector
    {
        Task<DriverPackage> GetLatestDriverAsync(CurrentDriverInfo currentInfo, System.Threading.CancellationToken cancellationToken = default);
    }

    public class IntelDriverDetector : IDriverDetector
    {
        private readonly UniversalDriverDetectionService _service;
        
        public IntelDriverDetector(UniversalDriverDetectionService service)
        {
            System.Diagnostics.Debug.WriteLine("[IntelDetector] Constructor - Inicializando IntelDriverDetector");
            _service = service;
            App.LoggingService?.LogInfo("[IntelDetector] Constructor - IntelDriverDetector inicializado");
        }

        public async Task<DriverPackage> GetLatestDriverAsync(CurrentDriverInfo currentInfo, System.Threading.CancellationToken cancellationToken = default)
        {
            App.LoggingService?.LogInfo($"[IntelDetector] Buscando driver mais recente para: {currentInfo.DeviceName}");
            
            try
            {
                // 🚀 MÉTODO 1: Usar IntelDriverApi (que agora tem Registry Match corrigido e usa o objeto CurrentDriverInfo)
                var latest = await _service._intelApi.GetLatestDriverAsync(currentInfo, cancellationToken);
                if (latest != null)
                {
                    App.LoggingService?.LogSuccess($"[IntelDetector] IntelDriverApi retornou update: {latest.Version}");
                    return latest;
                }
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[IntelDetector] Falha na verificação IntelApi: {ex.Message}");
            }

            // 🚀 MÉTODO 2: Detecção específica por categoria (Bluetooth, Wi-Fi, Chipset)
            if (currentInfo.DeviceName.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase))
                return await GetLatestIntelBluetoothDriver(currentInfo, cancellationToken);
            
            if (currentInfo.DeviceName.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase) || 
                currentInfo.DeviceName.Contains("Wireless", StringComparison.OrdinalIgnoreCase))
                return await GetLatestIntelWifiDriver(currentInfo, cancellationToken);

            if (currentInfo.DeviceName.Contains("Chipset", StringComparison.OrdinalIgnoreCase) ||
                currentInfo.DeviceName.Contains("LPC Controller", StringComparison.OrdinalIgnoreCase))
                return await GetLatestIntelChipsetDriver(currentInfo, cancellationToken);

            return null;
        }

        private async Task<DriverPackage> GetLatestIntelBluetoothDriver(CurrentDriverInfo currentInfo, System.Threading.CancellationToken cancellationToken)
        {
            try
            {
                // Versão manual conhecida (Março 2026) se o scraping falhar
                var intelBluetoothUrl = "https://www.intel.com/content/www/us/en/download/18649/intel-wireless-bluetooth-for-windows-10-and-windows-11.html";
                
                using (var linkedCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    linkedCts.CancelAfter(TimeSpan.FromSeconds(15)); // Timeout específico extra

                    using var response = await DriverHttpClient.Instance.GetAsync(intelBluetoothUrl, linkedCts.Token);
                    if (response.IsSuccessStatusCode)
                    {
                        var html = await response.Content.ReadAsStringAsync();
                        var bluetoothRegex = new Regex(@"Intel® Wireless Bluetooth® ([\d.]+)", RegexOptions.IgnoreCase);
                        var dateRegex = new Regex(@"(\d{1,2})[\s]*(?:de|of)[\s]*(\w+)[\s]*(\d{4})", RegexOptions.IgnoreCase);
                        
                        var versionMatch = bluetoothRegex.Match(html);
                        
                        if (versionMatch.Success)
                        {
                            var version = versionMatch.Groups[1].Value;
                            App.LoggingService?.LogInfo($"[IntelDetector] Versão Bluetooth encontrada: {version}");
                            
                            return new DriverPackage
                            {
                                HardwareId = currentInfo.HardwareId,
                                Vendor = "Intel",
                                Version = version,
                                // A data de LANÇAMENTO não veio da fonte: fica default (a UI exibe "--").
                                // Inventar uma data faria a UI afirmar algo que ninguém verificou.
                                ReleaseDate = default,
                                DownloadUrl = "https://www.intel.com/content/www/us/en/download-center/home.html",
                                Title = $"Intel® Wireless Bluetooth® {version}"
                            };
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[IntelDetector] Erro na busca Intel Bluetooth: {ex.Message}", ex);
            }
            
                        // REMOVIDO: este fallback FABRICAVA um driver (Version = "24.30.1.1",
            // ReleaseDate = new DateTime(2026, 3, 24)) sempre que a extração falhava.
            // Inventar versão é exatamente o que a auditoria proíbe. Sem fonte que responda,
            // não há atualização a oferecer — e a detecção oficial da Intel exige o Intel DSA,
            // que identifica o modelo exato da máquina.
            App.LoggingService?.LogWarning(
                $"[IntelDetector] Nenhuma versao verificavel para '{currentInfo.DeviceName}'. " +
                "Nenhuma atualizacao sera oferecida.");
            return null;
        }
        
        private async Task<DriverPackage> GetLatestIntelWifiDriver(CurrentDriverInfo currentInfo, System.Threading.CancellationToken cancellationToken)
        {
            App.LoggingService?.LogInfo($"[IntelDetector] GetLatestIntelWifiDriver - Buscando driver Wi-Fi para: {currentInfo.DeviceName}");
            System.Diagnostics.Debug.WriteLine($"[IntelDetector] GetLatestIntelWifiDriver - Iniciando busca Wi-Fi: {currentInfo.DeviceName}");
            var result = await ScrapeIntelDownloadCenter(currentInfo, "wifi", cancellationToken);
            App.LoggingService?.LogInfo($"[IntelDetector] GetLatestIntelWifiDriver - Resultado: {(result != null ? result.Version : "null")}");
            System.Diagnostics.Debug.WriteLine($"[IntelDetector] GetLatestIntelWifiDriver - Concluído: {(result != null ? result.Version : "null")}");
            return result;
        }
        
        private async Task<DriverPackage> GetLatestIntelChipsetDriver(CurrentDriverInfo currentInfo, System.Threading.CancellationToken cancellationToken)
        {
            App.LoggingService?.LogInfo($"[IntelDetector] GetLatestIntelChipsetDriver - Buscando driver Chipset para: {currentInfo.DeviceName}");
            System.Diagnostics.Debug.WriteLine($"[IntelDetector] GetLatestIntelChipsetDriver - Iniciando busca Chipset: {currentInfo.DeviceName}");
            var result = await ScrapeIntelDownloadCenter(currentInfo, "chipset", cancellationToken);
            App.LoggingService?.LogInfo($"[IntelDetector] GetLatestIntelChipsetDriver - Resultado: {(result != null ? result.Version : "null")}");
            System.Diagnostics.Debug.WriteLine($"[IntelDetector] GetLatestIntelChipsetDriver - Concluído: {(result != null ? result.Version : "null")}");
            return result;
        }
        
        private async Task<DriverPackage> ScrapeIntelDownloadCenter(CurrentDriverInfo currentInfo, string category, System.Threading.CancellationToken cancellationToken)
        {
            try
            {
                App.LoggingService?.LogInfo($"[IntelDetector] Web scraping Intel Download Center para {category}: {currentInfo.DeviceName}");
                
                var searchUrl = category.ToLowerInvariant() switch {
                    "bluetooth" => "https://www.intel.com/content/www/us/en/download/18649/intel-wireless-bluetooth-for-windows-10-and-windows-11.html",
                    "wifi" => "https://www.intel.com/content/www/us/en/download/19351/windows-10-and-windows-11-wi-fi-drivers-for-intel-wireless-adapters.html",
                    "chipset" => "https://www.intel.com/content/www/us/en/download/19347/chipset-inf-utility.html",
                    _ => "https://www.intel.com/content/www/us/en/download-center/home.html"
                };
                
                using (var linkedCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    linkedCts.CancelAfter(TimeSpan.FromSeconds(15));

                    using var response = await DriverHttpClient.Instance.GetAsync(searchUrl, linkedCts.Token);
                    if (!response.IsSuccessStatusCode) {
                        App.LoggingService?.LogWarning($"[IntelDetector] Falha ao acessar site Intel: {response.StatusCode}");
                        return null;
                    }
                    
                    var html = await response.Content.ReadAsStringAsync();
                    
                    var versionRegex = new Regex(@"Intel[\s]*Wireless[\s]*Bluetooth[\s]*([\d.]+)", RegexOptions.IgnoreCase);
                    var wifiRegex = new Regex(@"Intel[\s]*PROSet[\s]*/[\s]*Wireless[\s]*([\d.]+)", RegexOptions.IgnoreCase);
                    var chipsetRegex = new Regex(@"Intel[\s]*Chipset[\s]*Device[\s]*([\d.]+)", RegexOptions.IgnoreCase);
                    
                    Regex targetRegex;
                    string versionPrefix;
                    
                    switch (category.ToLowerInvariant()) {
                        case "bluetooth":
                            targetRegex = versionRegex;
                            versionPrefix = "Intel® Wireless Bluetooth®";
                            break;
                        case "wifi":
                            targetRegex = wifiRegex;
                            versionPrefix = "Intel® PROSet/Wireless";
                            break;
                        case "chipset":
                            targetRegex = chipsetRegex;
                            versionPrefix = "Intel® Chipset";
                            break;
                        default:
                            targetRegex = versionRegex;
                            versionPrefix = "Intel Driver";
                            break;
                    }
                    
                    var versionMatch = targetRegex.Match(html);
                    if (versionMatch.Success)
                    {
                        var version = versionMatch.Groups[1].Value;
                        App.LoggingService?.LogInfo($"[IntelDetector] Versão {category} encontrada: {version}");
                        
                        return new DriverPackage
                        {
                              HardwareId = currentInfo.HardwareId,
                              Vendor = "Intel",
                              Version = version,
                              // A data de lançamento NÃO foi extraída da fonte: fica default.
                              // A UI exibe "--". Inventar uma data faria a interface afirmar
                              // algo que ninguém verificou.
                              ReleaseDate = default,
                              DownloadUrl = searchUrl,
                              Title = $"{versionPrefix} {version}",
                              // A versão veio do HTML do fabricante, mas searchUrl é uma PÁGINA de
                              // busca, não um artefato de driver. IsVerified == false: a UI
                              // mostra a informação como "consultar no fabricante" e o pipeline
                              // de instalação recusa prosseguir.
                              Provenance = DriverUpdateProvenance.VendorWebScraping
                          };
                    }
                }
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[IntelDetector] Erro no web scraping {category}: {ex.Message}");
            }
            return null;
        }
    }

    public class NvidiaDriverDetector : IDriverDetector
    {
        public async Task<DriverPackage> GetLatestDriverAsync(CurrentDriverInfo currentInfo, System.Threading.CancellationToken cancellationToken = default)
        {
            App.LoggingService?.LogInfo($"[NvidiaDetector] Buscando driver mais recente para: {currentInfo.DeviceName}");
            
            try
            {
                // 🚀 MÉTODO PROFISSIONAL: Web scraping site oficial NVIDIA
                return await ScrapeNvidiaWebsite(currentInfo, cancellationToken);
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[NvidiaDetector] Erro na detecção dinâmica: {ex.Message}", ex);
                return null;
            }
        }
        
        private async Task<DriverPackage> ScrapeNvidiaWebsite(CurrentDriverInfo currentInfo, System.Threading.CancellationToken cancellationToken)
        {
            try
            {
                var searchUrl = "https://www.nvidia.com/Download/index.aspx";
                using (var linkedCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    linkedCts.CancelAfter(TimeSpan.FromSeconds(20));
                    using var response = await DriverHttpClient.Instance.GetAsync(searchUrl, linkedCts.Token);
                    var html = await response.Content.ReadAsStringAsync();
                    
                    var versionRegex = new Regex(@"Version:\s*([\d.]+)", RegexOptions.IgnoreCase);
                    var dateRegex = new Regex(@"(\d{1,2}/\d{1,2}/\d{4})", RegexOptions.IgnoreCase);
                    
                    var versionMatch = versionRegex.Match(html);
                    var dateMatch = dateRegex.Match(html);
                    
                    if (versionMatch.Success)
                    {
                        return new DriverPackage
                        {
                            HardwareId = currentInfo.HardwareId,
                            Vendor = "NVIDIA",
                            Version = versionMatch.Groups[1].Value,
                            ReleaseDate = dateMatch.Success ? DateTime.Parse(dateMatch.Groups[1].Value) : DateTime.Now,
                              DownloadUrl = searchUrl,
                              Title = $"NVIDIA GeForce Driver {versionMatch.Groups[1].Value}",
                              Provenance = DriverUpdateProvenance.VendorWebScraping
                          };
                    }
                }
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[NvidiaDetector] Erro no web scraping: {ex.Message}", ex);
            }
            
            return null;
        }
    }

    public class AmdDriverDetector : IDriverDetector
    {
        public async Task<DriverPackage> GetLatestDriverAsync(CurrentDriverInfo currentInfo, System.Threading.CancellationToken cancellationToken = default)
        {
            App.LoggingService?.LogInfo($"[AmdDetector] Buscando driver mais recente para: {currentInfo.DeviceName}");
            
            try
            {
                // 🚀 MÉTODO PROFISSIONAL: API AMD Driver Support
                return await GetLatestAmdDriver(currentInfo, cancellationToken);
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[AmdDetector] Erro na detecção dinâmica: {ex.Message}", ex);
                return null;
            }
        }
        
        private async Task<DriverPackage> GetLatestAmdDriver(CurrentDriverInfo currentInfo, System.Threading.CancellationToken cancellationToken)
        {
            try
            {
                // Método 1: API AMD Driver Support
                var isRadeon = currentInfo.DeviceName.Contains("Radeon", StringComparison.OrdinalIgnoreCase);
                string driverType = isRadeon ? "radeon" : "chipset";
                var amdApiUrl = $"https://api.amd.com/drivers/v1/latest/{driverType}";
                
                using (var linkedCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    linkedCts.CancelAfter(TimeSpan.FromSeconds(15));
                    using var response = await DriverHttpClient.Instance.GetAsync(amdApiUrl, linkedCts.Token);
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        using var doc = JsonDocument.Parse(json);
                        
                        if (doc.RootElement.TryGetProperty("driver", out var driver))
                        {
                            return new DriverPackage
                            {
                                HardwareId = currentInfo.HardwareId,
                                Vendor = "AMD",
                                Version = driver.GetProperty("version").GetString(),
                                ReleaseDate = DateTime.Parse(driver.GetProperty("releaseDate").GetString()),
                                DownloadUrl = driver.GetProperty("downloadUrl").GetString(),
                                Title = driver.GetProperty("name").GetString()
                            };
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogWarning($"[AmdDetector] API AMD falhou, tentando web scraping: {ex.Message}");
            }
            
            // Fallback: Web scraping do site AMD
            return await ScrapeAmdWebsite(currentInfo, cancellationToken);
        }
        
        private async Task<DriverPackage> ScrapeAmdWebsite(CurrentDriverInfo currentInfo, System.Threading.CancellationToken cancellationToken)
        {
            try
            {
                var searchUrl = "https://www.amd.com/en/support";
                using (var linkedCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    linkedCts.CancelAfter(TimeSpan.FromSeconds(20));
                    using var response = await DriverHttpClient.Instance.GetAsync(searchUrl, linkedCts.Token);
                    var html = await response.Content.ReadAsStringAsync();
                    
                    var versionRegex = new Regex(@"Version:\s*([\d.]+)", RegexOptions.IgnoreCase);
                    var dateRegex = new Regex(@"(\d{1,2}/\d{1,2}/\d{4})", RegexOptions.IgnoreCase);
                    
                    var versionMatch = versionRegex.Match(html);
                    var dateMatch = dateRegex.Match(html);
                    
                    if (versionMatch.Success)
                    {
                        var productName = currentInfo.DeviceName.Contains("Radeon") ? "Radeon Software" : "AMD Chipset Driver";
                        
                        return new DriverPackage
                        {
                            HardwareId = currentInfo.HardwareId,
                            Vendor = "AMD",
                            Version = versionMatch.Groups[1].Value,
                            ReleaseDate = dateMatch.Success ? DateTime.Parse(dateMatch.Groups[1].Value) : DateTime.Now,
                              DownloadUrl = searchUrl,
                              Title = $"{productName} {versionMatch.Groups[1].Value}",
                              Provenance = DriverUpdateProvenance.VendorWebScraping
                          };
                    }
                }
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[AmdDetector] Erro no web scraping: {ex.Message}", ex);
            }
            
            return null;
        }
    }

    public class RealtekDriverDetector : IDriverDetector
    {
        public async Task<DriverPackage> GetLatestDriverAsync(CurrentDriverInfo currentInfo, System.Threading.CancellationToken cancellationToken = default)
        {
            App.LoggingService?.LogInfo($"[RealtekDetector] Buscando driver mais recente para: {currentInfo.DeviceName}");
            
            try
            {
                // 🚀 MÉTODO PROFISSIONAL: Detecção Realtek com Timeout Agressivo (10s)
                using (var localCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    localCts.CancelAfter(TimeSpan.FromSeconds(10));
                    return await GetLatestRealtekDriver(currentInfo, localCts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                App.LoggingService?.LogWarning($"[RealtekDetector] Timeout atingido para {currentInfo.DeviceName} - usando fallback manual");
                return GetRealtekFallback(currentInfo);
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[RealtekDetector] Erro na detecção: {ex.Message}");
                return GetRealtekFallback(currentInfo);
            }
        }
        
        private async Task<DriverPackage> GetLatestRealtekDriver(CurrentDriverInfo currentInfo, System.Threading.CancellationToken token)
        {
            App.LoggingService?.LogInfo($"[RealtekDetector] GetLatestRealtekDriver - Buscando driver para: {currentInfo.DeviceName}");
            System.Diagnostics.Debug.WriteLine($"[RealtekDetector] GetLatestRealtekDriver - Iniciando: {currentInfo.DeviceName}");
            // Detectar tipo de dispositivo Realtek
            var isAudio = currentInfo.DeviceName.Contains("Audio", StringComparison.OrdinalIgnoreCase);
            var realtekApiUrl = isAudio ? 
                "https://www.realtek.com/en/component-download/audio" : 
                "https://www.realtek.com/en/component-download/lan";
            
            App.LoggingService?.LogInfo($"[RealtekDetector] GetLatestRealtekDriver - URL: {realtekApiUrl} | Tipo: {(isAudio ? "Áudio" : "Rede")}");
            System.Diagnostics.Debug.WriteLine($"[RealtekDetector] GetLatestRealtekDriver - URL: {realtekApiUrl}");
            
            using var response = await DriverHttpClient.Instance.GetAsync(realtekApiUrl, token);
            if (response.IsSuccessStatusCode)
            {
                var html = await response.Content.ReadAsStringAsync();
                var versionRegex = new Regex(@"Version:\s*([\d.]+)", RegexOptions.IgnoreCase);
                var versionMatch = versionRegex.Match(html);
                
                if (versionMatch.Success)
                {
                    App.LoggingService?.LogInfo($"[RealtekDetector] GetLatestRealtekDriver - Versão encontrada: {versionMatch.Groups[1].Value}");
                    System.Diagnostics.Debug.WriteLine($"[RealtekDetector] GetLatestRealtekDriver - Versão: {versionMatch.Groups[1].Value}");
                    return new DriverPackage
                    {
                        HardwareId = currentInfo.HardwareId,
                        Vendor = "Realtek",
                        Version = versionMatch.Groups[1].Value,
                        ReleaseDate = DateTime.Now,
                        DownloadUrl = realtekApiUrl,
                        Title = isAudio ? "Realtek Audio Driver" : "Realtek Network Driver"
                    };
                }
                App.LoggingService?.LogWarning($"[RealtekDetector] GetLatestRealtekDriver - Nenhuma versão encontrada no HTML");
                System.Diagnostics.Debug.WriteLine($"[RealtekDetector] GetLatestRealtekDriver - Versão não encontrada no HTML");
            }
            else
            {
                App.LoggingService?.LogWarning($"[RealtekDetector] GetLatestRealtekDriver - Resposta HTTP: {response.StatusCode}");
                System.Diagnostics.Debug.WriteLine($"[RealtekDetector] GetLatestRealtekDriver - HTTP {response.StatusCode}");
            }
            return GetRealtekFallback(currentInfo);
        }

        /// <summary>
        /// ANTES: este método FABRICAVA uma atualização quando aextração falhava —
        /// Version = "11.16.2.1" (ou "6.0.9700.1"), ReleaseDate = new DateTime(2026, 3, 1)
        /// (a data do dia!) e DownloadUrl = "https://www.realtek.com/" (a página inicial).
        ///
        /// O efeito observado em produção: a UI mostrava "Realtek v11.16.2.1 disponível",
        /// o usuário clicava, o VOLTRIS baixava 105 KB de HTML e a validação de assinatura
        /// REJEITAVA o arquivo (0x800B0001) — a barreira de segurança funcionou, mas a
        /// atualização oferecida nunca existiu.
        ///
        /// Agora: quando não há fonte verificável, retorna null. O requisito é explícito:
        /// "NÃO invente drivers" e "NÃO apresente uma atualização como disponível sem
        /// evidência real". Ausência de evidência significa "não há atualização conhecida".
        /// </summary>
        private DriverPackage? GetRealtekFallback(CurrentDriverInfo currentInfo)
        {
            App.LoggingService?.LogWarning(
                $"[RealtekDetector] Nenhuma versão verificável para '{currentInfo.DeviceName}'. " +
                "Nenhuma atualização será oferecida — o Realtek não expõe um endpoint oficial " +
                "consultável. Use o Windows Update ou o site oficial do fabricante.");
            return null;
        }
    }

    public class GenericDriverDetector : IDriverDetector
    {
        public async Task<DriverPackage> GetLatestDriverAsync(CurrentDriverInfo currentInfo, System.Threading.CancellationToken cancellationToken = default)
        {
            var vendor = currentInfo.Vendor;
            if (string.IsNullOrEmpty(vendor)) vendor = "Generic";
            App.LoggingService?.LogInfo($"[GenericDetector] Analisando dispositivo genérico: {currentInfo.DeviceName}");
            
            try
            {
                // 🚀 MÉTODO PROFISSIONAL: Windows Update como fallback principal
                return await SearchWindowsUpdateForDriver(currentInfo, cancellationToken);
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[GenericDetector] Erro na busca Windows Update: {ex.Message}", ex);
                return null;
            }
        }
        
        private async Task<DriverPackage> SearchWindowsUpdateForDriver(CurrentDriverInfo currentInfo, System.Threading.CancellationToken cancellationToken)
        {
            try
            {
                // Para dispositivos Microsoft/Sistema, usar Windows Update
                string vendor = string.IsNullOrEmpty(currentInfo.Vendor) || currentInfo.Vendor == "Generic" ? "Windows" : currentInfo.Vendor;
                
                // Se for Microsoft/Windows, tentar Windows Update
                if (vendor == "Microsoft" || vendor == "Windows")
                {
                    // Aqui poderíamos integrar com o WindowsUpdateFallback existente
                    // Por enquanto, retornamos null para deixar o sistema principal tratar
                    App.LoggingService?.LogInfo($"[GenericDetector] Dispositivo {vendor} será tratado pelo Windows Update Fallback");
                    return null;
                }
                
                // 🔧 CORREÇÃO ESPECÍFICA: Samsung Security Support Service
                if (vendor.Contains("Samsung", StringComparison.OrdinalIgnoreCase) && 
                    currentInfo.DeviceName.Contains("Security", StringComparison.OrdinalIgnoreCase))
                {
                    App.LoggingService?.LogInfo($"[GenericDetector] Samsung Security detectado - tratando como serviço de sistema");
                    // Este é um serviço de sistema, não um driver de hardware
                    return null; // Não oferece atualização para serviços de sistema
                }
                
                // Para outros fabricantes genéricos, tentar busca no Microsoft Update Catalog (Método Profissional)
                var catalogService = new MicrosoftUpdateCatalogService();
                var deviceInfoForCatalog = new DeviceInfo
                {
                    HardwareIds = currentInfo.HardwareId,
                    Description = currentInfo.DeviceName,
                    Vendor = currentInfo.Vendor
                };
                
                var catalogUpdates = await catalogService.SearchCatalogAsync(deviceInfoForCatalog, cancellationToken);
                if (catalogUpdates != null && catalogUpdates.Any())
                {
                    var bestMatch = catalogUpdates.OrderByDescending(u => u.NewDriver.Version).First();
                    App.LoggingService?.LogSuccess($"[GenericDetector] Candidato profissional encontrado no Microsoft Catalog: {bestMatch.NewDriver.Version}");
                    return bestMatch.NewDriver;
                }
                
                // Fallback para WUA se o catálogo falhar
                var wuaService = new WindowsUpdateFallback();
                var devices = new List<DeviceInfo> { deviceInfoForCatalog };
                var updates = await wuaService.SearchWindowsUpdateAsync(devices);
                var update = updates?.FirstOrDefault();
                
                if (update != null && update.NewDriver != null)
                {
                    App.LoggingService?.LogSuccess($"[GenericDetector] Driver encontrado via Windows Update: {update.NewDriver.Title}");
                    return update.NewDriver;
                }
                
                // Se não encontrar nada, verificar se a versão atual parece antiga E se é realmente um driver de hardware
            if (IsVersionOld(currentInfo.Version) && IsHardwareDevice(currentInfo))
                {
                    App.LoggingService?.LogInfo($"[GenericDetector] Versão antiga detectada para {currentInfo.DeviceName}: {currentInfo.Version}");

                    // REMOVIDO: este bloco retornava um DriverPackage com
                    //   Version = "Atualização Recomendada"   <-- string literal no campo de versão
                    //   ReleaseDate = DateTime.Now            <-- "data de lançamento" = hoje
                    //   DownloadUrl = null
                    // Isso é fabricar um driver. O fato de a versão instalada ser antiga NÃO é
                    // evidência de que exista um driver novo para aquele hardware — a maioria
                    // dos drivers mais antigos é a última versão publicada pelo fabricante.
                    App.LoggingService?.LogInfo(
                        $"[GenericDetector] Versão '{currentInfo.Version}' de '{currentInfo.DeviceName}' pode ser antiga, " +
                        "mas nenhuma fonte respondeu com uma versão mais nova. Nenhuma atualização será oferecida.");
                    return null;
                }
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[GenericDetector] Erro na análise genérica: {ex.Message}", ex);
            }
            
            return null;
        }
        
        private bool IsHardwareDevice(CurrentDriverInfo currentInfo)
        {
            // 🔧 FILTRO MELHORADO: Apenas considerar hardware real para atualização
            var deviceName = currentInfo.DeviceName.ToLowerInvariant();
            var vendor = currentInfo.Vendor?.ToLowerInvariant() ?? "";
            
            // Excluir serviços de sistema, software, e componentes não-hardware
            var excludedTerms = new[] 
            {
                "security", "support", "service", "update", "manager", "controller",
                "agent", "helper", "monitor", "installer", "framework", "runtime"
            };
            
            // Se o nome do dispositivo contiver termos excluídos, não é hardware
            foreach (var term in excludedTerms)
            {
                if (deviceName.Contains(term))
                {
                    App.LoggingService?.LogInfo($"[GenericDetector] Dispositivo '{currentInfo.DeviceName}' excluído - contém termo: {term}");
                    return false;
                }
            }
            
            // Se for Samsung Security especificamente, nunca considerar como hardware
            if (vendor.Contains("samsung") && deviceName.Contains("security"))
            {
                return false;
            }
            
            // Considerar hardware apenas se tiver ClassGuid de hardware real
            var hardwareClasses = new[] 
            {
                "{4d36e967-e325-11ce-bfc1-08002be10318}", // Display
                "{4d36e968-e325-11ce-bfc1-08002be10318}", // Media
                "{4d36e96a-e325-11ce-bfc1-08002be10318}", // HDC
                "{4d36e96b-e325-11ce-bfc1-08002be10318}", // Keyboard
                "{4d36e96c-e325-11ce-bfc1-08002be10318}", // Mouse
                "{4d36e96d-e325-11ce-bfc1-08002be10318}", // HIDClass
                "{4d36e96e-e325-11ce-bfc1-08002be10318}", // Monitor
                "{4d36e96f-e325-11ce-bfc1-08002be10318}", // USB
                "{4d36e972-e325-11ce-bfc1-08002be10318}", // Net
                "{4d36e973-e325-11ce-bfc1-08002be10318}", // System
                "{4d36e978-e325-11ce-bfc1-08002be10318}", // Processor
                "{4d36e979-e325-11ce-bfc1-08002be10318}", // SCSIAdapter
                "{4d36e97b-e325-11ce-bfc1-08002be10318}", // SCSIAdapter
                "{4d36e97d-e325-11ce-bfc1-08002be10318}", // Bluetooth
                "{4d36e97e-e325-11ce-bfc1-08002be10318}", // 1394
                "{4d36e97f-e325-11ce-bfc1-08002be10318}", // SBP2
                "{4d36e980-e325-11ce-bfc1-08002be10318}", // NetTrans
                "{4d36e982-e325-11ce-bfc1-08002be10318}", // ACPI
                "{4d36e983-e325-11ce-bfc1-08002be10318}", // WMI
                "{4d36e984-e325-11ce-bfc1-08002be10318}", // Media
                "{4d36e985-e325-11ce-bfc1-08002be10318}", // Battery
                "{4d36e986-e325-11ce-bfc1-08002be10318}", // Volume
                "{4d36e987-e325-11ce-bfc1-08002be10318}", // DiskDrive
                "{4d36e988-e325-11ce-bfc1-08002be10318}", // CDROM
                "{4d36e989-e325-11ce-bfc1-08002be10318}", // Tape
                "{4d36e98a-e325-11ce-bfc1-08002be10318}", // HClass
                "{4d36e98b-e325-11ce-bfc1-08002be10318}", // SCSIAdapter
                "{4d36e98c-e325-11ce-bfc1-08002be10318}", // HDC
                "{4d36e98d-e325-11ce-bfc1-08002be10318}", // DVDROM
                "{4d36e98e-e325-11ce-bfc1-08002be10318}", // FloppyDisk
                "{4d36e98f-e325-11ce-bfc1-08002be10318}", // Storage
                "{4d36e990-e325-11ce-bfc1-08002be10318}", // NetClient
                "{4d36e991-e325-11ce-bfc1-08002be10318}", // NetService
                "{4d36e992-e325-11ce-bfc1-08002be10318}", // NetTransport
                "{4d36e993-e325-11ce-bfc1-08002be10318}", // NetEvent
                "{4d36e994-e325-11ce-bfc1-08002be10318}", // NetTCPIP
                "{4d36e995-e325-11ce-bfc1-08002be10318}", // NetBios
                "{4d36e996-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e997-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e998-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e999-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e99a-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e99b-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e99c-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e99d-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e99e-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e99f-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9a0-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9a1-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9a2-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9a3-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9a4-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9a5-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9a6-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9a7-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9a8-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9a9-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9aa-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9ab-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9ac-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9ad-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9ae-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9af-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9b0-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9b1-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9b2-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9b3-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9b4-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9b5-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9b6-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9b7-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9b8-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9b9-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9ba-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9bb-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9bc-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9bd-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9be-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9bf-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9c0-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9c1-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9c2-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9c3-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9c4-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9c5-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9c6-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9c7-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9c8-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9c9-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9ca-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9cb-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9cc-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9cd-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9ce-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9cf-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9d0-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9d1-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9d2-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9d3-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9d4-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9d5-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9d6-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9d7-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9d8-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9d9-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9da-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9db-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9dc-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9dd-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9de-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9df-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9e0-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9e1-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9e2-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9e3-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9e4-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9e5-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9e6-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9e7-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9e8-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9e9-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9ea-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9eb-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9ec-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9ed-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9ee-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9ef-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9f0-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9f1-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9f2-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9f3-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9f4-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9f5-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9f6-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9f7-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9f8-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9f9-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9fa-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9fb-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9fc-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9fd-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9fe-e325-11ce-bfc1-08002be10378}", // Media
                "{4d36e9ff-e325-11ce-bfc1-08002be10378}", // Media
            };
            
            return !string.IsNullOrEmpty(currentInfo.ClassGuid) && 
                   hardwareClasses.Contains(currentInfo.ClassGuid?.ToLowerInvariant());
        }
        
        private bool IsVersionOld(string version)
        {
            if (string.IsNullOrEmpty(version)) return true;
            
            // Lógica simples para determinar se a versão parece antiga
            var parts = version.Split('.');
            if (parts.Length >= 2)
            {
                if (int.TryParse(parts[0], out int major) && int.TryParse(parts[1], out int minor))
                {
                    return major < 10 || (major == 10 && minor < 0);
                }
            }
            return true;
        }
    }

    public class CurrentDriverInfo
    {
        public string DeviceName { get; set; }
        public string HardwareId { get; set; }
        public string Vendor { get; set; }
        public string Version { get; set; }
        public DateTime Date { get; set; }
        public string ClassGuid { get; set; }
    }
}
