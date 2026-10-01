using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.Shield.Network
{
    /// <summary>
    /// Motor de identificação profissional de dispositivos de rede
    /// Identifica fabricante, tipo de dispositivo e sistema operacional
    /// </summary>
    public class DeviceIdentificationEngine
    {
        private readonly ILoggingService _logger;
        private readonly OUIDatabase _ouiDatabase;
        private readonly DeviceClassifier _classifier;
        private readonly HostnameAnalyzer _hostnameAnalyzer;
        private readonly ConcurrentDictionary<string, DeviceIdentificationResult> _identificationCache;
        
        public DeviceIdentificationEngine(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _ouiDatabase = new OUIDatabase(_logger);
            _classifier = new DeviceClassifier(_logger);
            _hostnameAnalyzer = new HostnameAnalyzer(_logger);
            _identificationCache = new ConcurrentDictionary<string, DeviceIdentificationResult>();
            
            _logger.LogInfo("[DeviceIdentification] Engine inicializado com sucesso");
        }
        
        /// <summary>
        /// Identifica um dispositivo de rede de forma completa
        /// </summary>
        public async Task<DeviceIdentificationResult> IdentifyDeviceAsync(NetworkDevice device, string gatewayIP = null)
        {
            try
            {
                var cacheKey = $"{device.MacAddress}|{device.IPAddress}|{device.Hostname}|{device.OperatingSystem}";
                if (!string.IsNullOrEmpty(device.MacAddress) && device.MacAddress != "Unknown" && _identificationCache.TryGetValue(cacheKey, out var cached))
                {
                    // [FIX:A-3] CAMINHO DE CACHE PRIMEIRO, BANNER DEPOIS.
                    //
                    // BUG ORIGINAL: o banner de 7 linhas (INICIANDO IDENTIFICAÇÃO +
                    // IP/MAC/Hostname/Gateway) era impresso ANTES da consulta ao
                    // cache, e o caminho de cache ainda logava mais 5 linhas em
                    // LogInfo. Resultado: um cache hit — que não faz NENHUM
                    // trabalho — produzia 12 linhas.
                    //
                    // EVIDENCIA (2026-09-27): 169 chamadas em ~35 min (timer de
                    // 15s do NetworkMonitorService), das quais 160 foram cache
                    // hits. Ou seja, ~1.900 linhas de log — a maior fonte
                    // isolada de volume do voltris.log.txt, com 0 valor
                    // diagnóstico.
                    //
                    // Agora o cache hit é um LogDebug de 1 linha. O banner completo
                    // só é impresso quando há trabalho real de identificação.
                    _logger.LogDebug(
                        $"[DeviceIdentification] cache hit para {device.MacAddress} | " +
                        $"vendor={cached.Vendor} tipo={cached.DeviceType} os={cached.OperatingSystem} confianca={cached.ConfidenceLevel}%");
                    return cached;
                }

                _logger.LogInfo($"[DeviceIdentification] ========================================");
                _logger.LogInfo($"[DeviceIdentification] 🔄 INICIANDO IDENTIFICAÇÃO COMPLETA:");
                _logger.LogInfo($"[DeviceIdentification] 📡 IP: {device.IPAddress}");
                _logger.LogInfo($"[DeviceIdentification] 📡 MAC: {device.MacAddress}");
                _logger.LogInfo($"[DeviceIdentification] 📡 Hostname: {device.Hostname}");
                _logger.LogInfo($"[DeviceIdentification] 📡 Gateway: {gatewayIP ?? "N/A"}");
                _logger.LogInfo($"[DeviceIdentification] ========================================");
                
                var result = new DeviceIdentificationResult
                {
                    IPAddress = device.IPAddress,
                    MacAddress = device.MacAddress ?? "Unknown",
                    Hostname = device.Hostname ?? "Unknown",
                    Vendor = device.Vendor,
                    DeviceType = device.DeviceType,
                    DeviceCategory = device.DeviceCategory,
                    OperatingSystem = device.OperatingSystem,
                    Icon = device.Icon,
                    IsGateway = device.IsGateway,
                    IsPortable = device.IsPortable,
                    ConfidenceLevel = device.ConfidenceLevel
                };
                
                // 1. Identificar fabricante via OUI (apenas se MAC for válido)
                if (!string.IsNullOrEmpty(device.MacAddress) && device.MacAddress != "Unknown")
                {
                    _logger.LogInfo($"[DeviceIdentification] 🔍 IDENTIFICANDO FABRICANTE VIA OUI...");
                     result.Vendor = _ouiDatabase.GetVendorFromMac(device.MacAddress);
                     _logger.LogInfo($"[DeviceIdentification] 📋 Vendor OUI: {result.Vendor}");
                     var vendorKnown = !string.IsNullOrWhiteSpace(result.Vendor)
                         && !string.Equals(result.Vendor, "Unknown", StringComparison.OrdinalIgnoreCase)
                         && !string.Equals(result.Vendor, "Private/Unknown", StringComparison.OrdinalIgnoreCase);
                     result.ConfidenceLevel += vendorKnown ? 30 : 0;
                     
                     if (vendorKnown)
                     {
                         _logger.LogSuccess($"[DeviceIdentification] ✅ FABRICANTE IDENTIFICADO: {result.Vendor}");
                     }
                     else if (string.Equals(result.Vendor, "Private/Unknown", StringComparison.OrdinalIgnoreCase))
                     {
                         _logger.LogInfo($"[DeviceIdentification] ℹ MAC com endereço privado; fabricante OUI indisponível: {device.MacAddress}");
                     }
                     else
                     {
                         _logger.LogWarning($"[DeviceIdentification] ⚠️ FABRICANTE NÃO IDENTIFICADO para MAC {device.MacAddress}");
                     }
                }
                else
                {
                    result.Vendor = "Unknown";
                    _logger.LogWarning($"[DeviceIdentification] ❌ MAC INVÁLIDO OU DESCONHECIDO para {device.IPAddress}");
                }
                
                // 2. Detectar se é gateway
                result.IsGateway = IsGatewayDevice(device.IPAddress, gatewayIP);
                if (result.IsGateway)
                {
                    _logger.LogInfo($"[DeviceIdentification] ✓ Dispositivo identificado como GATEWAY");
                    result.DeviceType = "Router/Gateway";
                    result.Icon = "📡";
                    result.ConfidenceLevel += 40;
                }

                if (string.Equals(result.OperatingSystem, "iOS", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(result.OperatingSystem, "Android", StringComparison.OrdinalIgnoreCase))
                {
                    result.DeviceType = "Mobile";
                    result.ConfidenceLevel += 20;
                }
                else if (string.Equals(result.OperatingSystem, "Windows", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(result.OperatingSystem, "Windows Server", StringComparison.OrdinalIgnoreCase))
                {
                    result.DeviceType = "Desktop";
                    result.ConfidenceLevel += 20;
                }
                
                // 3. Analisar hostname PRIMEIRO para inferir informações (prioridade alta)
                if (!string.IsNullOrEmpty(device.Hostname) && device.Hostname != "Unknown" && device.Hostname != "Desconhecido")
                {
                    _logger.LogInfo($"[DeviceIdentification] 🔍 ANALISANDO HOSTNAME: {device.Hostname}");
                    var hostnameInfo = _hostnameAnalyzer.AnalyzeHostname(device.Hostname);
                    if (hostnameInfo != null)
                    {
                        _logger.LogSuccess($"[DeviceIdentification] ✅ HOSTNAME FORNECEU INFORMAÇÕES:");
                        _logger.LogInfo($"[DeviceIdentification]   - DeviceType: {hostnameInfo.DeviceType}");
                        _logger.LogInfo($"[DeviceIdentification]   - OS: {hostnameInfo.OperatingSystem}");
                        _logger.LogInfo($"[DeviceIdentification]   - Confiança: +{hostnameInfo.Confidence}%");
                        
                         if (!string.IsNullOrWhiteSpace(hostnameInfo.OperatingSystem)
                             && !string.Equals(hostnameInfo.OperatingSystem, "Unknown", StringComparison.OrdinalIgnoreCase))
                         {
                             result.OperatingSystem = hostnameInfo.OperatingSystem;
                         }
                         if (!string.IsNullOrWhiteSpace(hostnameInfo.DeviceType)
                             && !string.Equals(hostnameInfo.DeviceType, "Unknown", StringComparison.OrdinalIgnoreCase))
                         {
                             result.DeviceType = hostnameInfo.DeviceType;
                         }
                         result.ConfidenceLevel += hostnameInfo.Confidence;
                    }
                    else
                    {
                        _logger.LogDebug($"[DeviceIdentification] HOSTNAME NÃO FORNECEU INFORMAÇÕES ÚTEIS");
                    }
                }
                else
                {
                    _logger.LogDebug($"[DeviceIdentification] HOSTNAME INVÁLIDO OU DESCONHECIDO: {device.Hostname}");
                }
                
                // 4. Classificar dispositivo baseado no fabricante (apenas se vendor for conhecido E não temos info do hostname)
                if (!string.IsNullOrEmpty(result.Vendor) && result.Vendor != "Unknown")
                {
                    _logger.LogInfo($"[DeviceIdentification] 🔍 CLASSIFICANDO BASEADO NO VENDOR: {result.Vendor}");
                    var classification = _classifier.ClassifyDevice(result.Vendor, device.Hostname, result.IsGateway);
                    if (classification != null)
                    {
                        _logger.LogInfo($"[DeviceIdentification] CLASSIFICAÇÃO DO VENDOR:");
                        _logger.LogInfo($"[DeviceIdentification]   - DeviceType: {classification.DeviceType}");
                        _logger.LogInfo($"[DeviceIdentification]   - Icon: {classification.Icon}");
                        _logger.LogInfo($"[DeviceIdentification]   - Confiança: +{classification.Confidence}%");
                        
                         result.DeviceType = string.IsNullOrWhiteSpace(result.DeviceType) || result.DeviceType == "Unknown"
                             ? classification.DeviceType
                             : result.DeviceType;
                         result.OperatingSystem = string.IsNullOrWhiteSpace(result.OperatingSystem) || result.OperatingSystem == "Unknown"
                             ? classification.OperatingSystem
                             : result.OperatingSystem;
                         result.Icon = string.IsNullOrWhiteSpace(result.Icon) || result.Icon == "🖥️"
                             ? classification.Icon
                             : result.Icon;
                         result.ConfidenceLevel += classification.Confidence;
                    }
                }
                
                // 5. Determinar DeviceCategory e IsPortable baseado no DeviceType
                result.DeviceCategory = result.DeviceType switch
                {
                    "Desktop" or "PC" or "Linux PC" or "Single Board Computer" => "DesktopPC",
                    "Laptop" or "Notebook" => "Notebook",
                    "Smartphone" or "Mobile" => "Smartphone",
                    "Tablet" => "Tablet",
                    "Smart TV" or "SmartTV" => "SmartTV",
                    "Game Console" or "GamingConsole" => "GameConsole",
                    "Printer" => "Printer",
                    "Router" or "Router/Gateway" or "Gateway" => "NetworkInfrastructure",
                    "Server" => "Server",
                    "IoT" or "IoT Device" or "IP Camera" or "Smart Speaker" or "Streaming Device" or "Electric Vehicle" => "IoT",
                    "NAS" => "NAS",
                    _ => "Other"
                };
                result.IsPortable = result.DeviceCategory == "Notebook" || result.DeviceCategory == "Smartphone" || result.DeviceCategory == "Tablet";
                if (string.IsNullOrWhiteSpace(result.DeviceType) || result.DeviceType == "Unknown")
                    result.DeviceType = "Unknown";
                if (string.IsNullOrWhiteSpace(result.OperatingSystem) || result.OperatingSystem == "Unknown")
                    result.OperatingSystem = "Unknown";
                if (string.IsNullOrWhiteSpace(result.Icon))
                    result.Icon = "🔍";
                
                // Detectar modelo do dispositivo via hostname
                result.DeviceModel = InferDeviceModel(result.Hostname, result.Vendor);
                
                // 6. Gerar nome amigável
                result.FriendlyName = GenerateFriendlyName(result);
                
                // 7. Normalizar nível de confiança (0-100)
                result.ConfidenceLevel = Math.Min(100, result.ConfidenceLevel);
                
                _logger.LogSuccess($"[DeviceIdentification] ========================================");
                _logger.LogSuccess($"[DeviceIdentification] RESULTADO FINAL:");
                _logger.LogSuccess($"[DeviceIdentification] Nome: {result.FriendlyName}");
                _logger.LogSuccess($"[DeviceIdentification] Tipo: {result.DeviceType}");
                _logger.LogSuccess($"[DeviceIdentification] OS: {result.OperatingSystem}");
                _logger.LogSuccess($"[DeviceIdentification] Vendor: {result.Vendor}");
                _logger.LogSuccess($"[DeviceIdentification] Ícone: {result.Icon}");
                _logger.LogSuccess($"[DeviceIdentification] Confiança: {result.ConfidenceLevel}%");
                _logger.LogSuccess($"[DeviceIdentification] ========================================");
                
                // Adicionar ao cache apenas se MAC for válido
                if (!string.IsNullOrEmpty(device.MacAddress) && device.MacAddress != "Unknown")
                {
                    _identificationCache[cacheKey] = result;
                }
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[DeviceIdentification] ❌ Erro ao identificar dispositivo {device.IPAddress}", ex);
                return CreateFallbackResult(device);
            }
        }
        
        /// <summary>
        /// Identifica múltiplos dispositivos em paralelo
        /// </summary>
        public async Task<List<DeviceIdentificationResult>> IdentifyDevicesAsync(List<NetworkDevice> devices, string gatewayIP = null)
        {
            var tasks = devices.Select(d => IdentifyDeviceAsync(d, gatewayIP));
            var results = await Task.WhenAll(tasks);
            return results.ToList();
        }
        
        /// <summary>
        /// Limpa o cache de identificação
        /// </summary>
        public void ClearCache()
        {
            _identificationCache.Clear();
            _logger.LogInfo("[DeviceIdentification] Cache limpo");
        }
        
        private bool IsGatewayDevice(string ipAddress, string knownGateway)
        {
            if (string.IsNullOrEmpty(ipAddress))
                return false;
            
            if (!string.IsNullOrEmpty(knownGateway)
                && string.Equals(ipAddress, knownGateway, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }
        
        /// <summary>
        /// Infere o modelo do dispositivo baseado no hostname e vendor
        /// </summary>
        private string InferDeviceModel(string hostname, string vendor)
        {
            if (string.IsNullOrEmpty(hostname) || hostname == "Unknown")
                return null;

            var hostnameUpper = hostname.ToUpperInvariant();

            // Samsung: SM-*, GT-*, SC-*
            if (hostnameUpper.StartsWith("SM-") || hostnameUpper.Contains("SM-"))
            {
                var match = System.Text.RegularExpressions.Regex.Match(hostnameUpper, @"SM-[A-Z0-9]+");
                if (match.Success) return "Samsung " + match.Value;
            }
            if (hostnameUpper.StartsWith("GT-"))
            {
                var match = System.Text.RegularExpressions.Regex.Match(hostnameUpper, @"GT-[A-Z0-9]+");
                if (match.Success) return "Samsung " + match.Value;
            }
            if (hostnameUpper.StartsWith("SC-"))
            {
                var match = System.Text.RegularExpressions.Regex.Match(hostnameUpper, @"SC-[A-Z0-9]+");
                if (match.Success) return "Samsung " + match.Value;
            }

            // Apple: iPhone*, iPad*, MacBook*, Mac-*
            if (hostnameUpper.Contains("IPHONE"))
            {
                var match = System.Text.RegularExpressions.Regex.Match(hostnameUpper, @"IPHONE[\dA-Z,]+");
                if (match.Success) return "Apple " + match.Value;
                return "Apple iPhone";
            }
            if (hostnameUpper.Contains("IPAD"))
            {
                var match = System.Text.RegularExpressions.Regex.Match(hostnameUpper, @"IPAD[\dA-Z,]+");
                if (match.Success) return "Apple " + match.Value;
                return "Apple iPad";
            }
            if (hostnameUpper.Contains("MACBOOK"))
            {
                return "Apple MacBook";
            }
            if (hostnameUpper.StartsWith("MAC-"))
            {
                return "Apple Mac";
            }

            // Xiaomi: Mi*, Redmi*, POCO*
            if (hostnameUpper.StartsWith("MI ") || hostnameUpper.StartsWith("MI-") || hostnameUpper.Contains("MI ") || hostnameUpper.Contains("MI-"))
                return "Xiaomi Mi";
            if (hostnameUpper.Contains("REDMI"))
                return "Xiaomi Redmi";
            if (hostnameUpper.Contains("POCO"))
                return "Xiaomi POCO";

            // Huawei: LYA-*, ELE-*, VOG-*
            if (hostnameUpper.StartsWith("LYA-") || hostnameUpper.StartsWith("ELE-") || hostnameUpper.StartsWith("VOG-"))
                return "Huawei " + hostname.Split('-')[0];

            // OnePlus
            if (hostnameUpper.Contains("ONEPLUS"))
                return "OnePlus";
            if (hostnameUpper.StartsWith("KB20"))
                return "OnePlus KB200";

            // Motorola
            if (hostnameUpper.Contains("MOTO ") || hostnameUpper.StartsWith("MOTO"))
                return "Motorola";
            if (hostnameUpper.StartsWith("XT"))
            {
                var match = System.Text.RegularExpressions.Regex.Match(hostnameUpper, @"XT\d+");
                if (match.Success) return "Motorola " + match.Value;
            }

            // LG
            if (hostnameUpper.StartsWith("LG-") || hostnameUpper.StartsWith("LM-"))
                return "LG " + hostname.Split('-')[0];

            // Nokia
            if (hostnameUpper.StartsWith("TA-"))
                return "Nokia " + hostnameUpper;

            // Google Pixel
            if (hostnameUpper.Contains("PIXEL"))
                return "Google Pixel";

            // Desktop patterns: DESKTOP-XXXX
            if (hostnameUpper.StartsWith("DESKTOP-") && vendor != "Unknown")
                return $"{vendor} Desktop";
            if (hostnameUpper.StartsWith("WORKSTATION-") && vendor != "Unknown")
                return $"{vendor} Workstation";
            if (hostnameUpper.StartsWith("MACBOOK-PRO-"))
                return "Apple MacBook Pro";
            if (hostnameUpper.StartsWith("IMAC-"))
                return "Apple iMac";

            // ASUS routers
            if (hostnameUpper.Contains("RT-") && vendor == "Asus")
                return "ASUS Router";
            if (hostnameUpper.Contains("RT-AC") || hostnameUpper.Contains("RT-AX"))
                return "ASUS Router";

            return null;
        }

        private string GenerateFriendlyName(DeviceIdentificationResult result)
        {
            if (result.IsGateway)
            {
                return string.IsNullOrEmpty(result.Vendor) || result.Vendor == "Unknown"
                    ? "Network Router"
                    : $"{result.Vendor} Router";
            }
            
            if (!string.IsNullOrEmpty(result.Hostname) && result.Hostname != "Unknown")
            {
                return result.Hostname;
            }

            if (string.Equals(result.OperatingSystem, "iOS", StringComparison.OrdinalIgnoreCase))
                return "iPhone/iPad";
            if (string.Equals(result.OperatingSystem, "Android", StringComparison.OrdinalIgnoreCase))
                return "Dispositivo Android";
            if (string.Equals(result.OperatingSystem, "Windows", StringComparison.OrdinalIgnoreCase)
                || string.Equals(result.OperatingSystem, "Windows Server", StringComparison.OrdinalIgnoreCase))
                return "Dispositivo Windows";
            
            if (!string.IsNullOrEmpty(result.DeviceType) && result.DeviceType != "Unknown")
            {
                if (!string.IsNullOrEmpty(result.Vendor) && result.Vendor != "Unknown")
                {
                    return $"{result.Vendor} {result.DeviceType}";
                }
                return result.DeviceType;
            }
            
            if (!string.IsNullOrEmpty(result.Vendor) && result.Vendor != "Unknown")
            {
                return $"{result.Vendor} Device";
            }
            
            return "Unknown Device";
        }
        
        private DeviceIdentificationResult CreateFallbackResult(NetworkDevice device)
        {
            return new DeviceIdentificationResult
            {
                IPAddress = device.IPAddress,
                MacAddress = device.MacAddress,
                Hostname = device.Hostname,
                Vendor = string.IsNullOrWhiteSpace(device.Vendor) ? "Unknown" : device.Vendor,
                DeviceType = string.IsNullOrWhiteSpace(device.DeviceType) ? "Unknown" : device.DeviceType,
                DeviceCategory = string.IsNullOrWhiteSpace(device.DeviceCategory) ? "Other" : device.DeviceCategory,
                OperatingSystem = string.IsNullOrWhiteSpace(device.OperatingSystem) ? "Unknown" : device.OperatingSystem,
                Icon = string.IsNullOrWhiteSpace(device.Icon) ? "🖥️" : device.Icon,
                FriendlyName = string.IsNullOrWhiteSpace(device.FriendlyName) ? device.Hostname ?? "Unknown Device" : device.FriendlyName,
                IsGateway = false,
                IsPortable = false,
                ConfidenceLevel = 0
            };
        }
    }
    
    /// <summary>
    /// Resultado da identificação de dispositivo
    /// </summary>
    public class DeviceIdentificationResult
    {
        public string IPAddress { get; set; }
        public string MacAddress { get; set; }
        public string Hostname { get; set; }
        public string Vendor { get; set; }
        public string DeviceType { get; set; }
        public string DeviceCategory { get; set; } = "Other";
        public string DeviceModel { get; set; }
        public string OperatingSystem { get; set; }
        public string FriendlyName { get; set; }
        public string Icon { get; set; }
        public bool IsGateway { get; set; }
        public bool IsPortable { get; set; }
        public int ConfidenceLevel { get; set; }
    }
}
