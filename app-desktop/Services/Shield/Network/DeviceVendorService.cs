using System;
using System.Collections.Generic;
using System.Linq;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.Shield.Network
{
    public class DeviceVendorService
    {
        private readonly ILoggingService _logger;
        private readonly Dictionary<string, string> _ouiDatabase;
        private readonly Dictionary<string, DeviceTypeInfo> _deviceTypePatterns;
        
        public DeviceVendorService(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _ouiDatabase = InitializeOUIDatabase();
            _deviceTypePatterns = InitializeDeviceTypePatterns();
        }
        
        public string GetVendorFromMac(string macAddress)
        {
            if (string.IsNullOrEmpty(macAddress) || macAddress == "Desconhecido")
                return "Desconhecido";
            
            try
            {
                // Extrair OUI (primeiros 3 octetos)
                var oui = macAddress.Replace(":", "").Replace("-", "").Substring(0, 6).ToUpperInvariant();
                
                if (_ouiDatabase.TryGetValue(oui, out var vendor))
                {
                    _logger.LogInfo($"[DeviceVendor] MAC {macAddress} identificado como {vendor}");
                    return vendor;
                }
                
                _logger.LogWarning($"[DeviceVendor] Fabricante desconhecido para MAC {macAddress}");
                return "Desconhecido";
            }
            catch (Exception ex)
            {
                _logger.LogError($"[DeviceVendor] Erro ao processar MAC {macAddress}", ex);
                return "Desconhecido";
            }
        }
        
        public DeviceTypeInfo GetDeviceType(string hostname, string vendor, string macAddress)
        {
            try
            {
                _logger.LogInfo($"[DeviceVendor] Detectando tipo de dispositivo: {hostname} ({vendor})");
                
                var hostnameLower = hostname.ToLowerInvariant();
                var vendorLower = vendor.ToLowerInvariant();
                
                // Verificar padrões específicos
                foreach (var pattern in _deviceTypePatterns)
                {
                    if (hostnameLower.Contains(pattern.Key) || vendorLower.Contains(pattern.Key))
                    {
                        _logger.LogSuccess($"[DeviceVendor] Dispositivo identificado como {pattern.Value.Type}");
                        return pattern.Value;
                    }
                }
                
                // Detecção por fabricante
                if (vendorLower.Contains("apple"))
                {
                    if (hostnameLower.Contains("iphone")) return new DeviceTypeInfo { Type = "Smartphone", Icon = "📱", Description = "iPhone" };
                    if (hostnameLower.Contains("ipad")) return new DeviceTypeInfo { Type = "Tablet", Icon = "📱", Description = "iPad" };
                    if (hostnameLower.Contains("mac")) return new DeviceTypeInfo { Type = "Computador", Icon = "💻", Description = "Mac" };
                    return new DeviceTypeInfo { Type = "Dispositivo Apple", Icon = "🍎", Description = "Apple Device" };
                }
                
                if (vendorLower.Contains("samsung") || vendorLower.Contains("xiaomi") || vendorLower.Contains("huawei") || vendorLower.Contains("motorola"))
                {
                    return new DeviceTypeInfo { Type = "Smartphone", Icon = "📱", Description = "Android" };
                }
                
                if (vendorLower.Contains("tp-link") || vendorLower.Contains("d-link") || vendorLower.Contains("cisco") || vendorLower.Contains("asus") && hostnameLower.Contains("router"))
                {
                    return new DeviceTypeInfo { Type = "Roteador", Icon = "🌐", Description = "Router" };
                }
                
                if (vendorLower.Contains("intel") || vendorLower.Contains("realtek") || vendorLower.Contains("microsoft"))
                {
                    if (hostnameLower.Contains("desktop") || hostnameLower.Contains("pc"))
                        return new DeviceTypeInfo { Type = "Desktop", Icon = "🖥️", Description = "PC" };
                    if (hostnameLower.Contains("laptop") || hostnameLower.Contains("notebook"))
                        return new DeviceTypeInfo { Type = "Notebook", Icon = "💻", Description = "Laptop" };
                    return new DeviceTypeInfo { Type = "Computador", Icon = "💻", Description = "PC" };
                }
                
                _logger.LogWarning($"[DeviceVendor] Tipo de dispositivo não identificado para {hostname}");
                return new DeviceTypeInfo { Type = "Desconhecido", Icon = "🖥️", Description = "Unknown Device" };
            }
            catch (Exception ex)
            {
                _logger.LogError($"[DeviceVendor] Erro ao detectar tipo de dispositivo", ex);
                return new DeviceTypeInfo { Type = "Desconhecido", Icon = "🖥️", Description = "Unknown" };
            }
        }
        
        private Dictionary<string, DeviceTypeInfo> InitializeDeviceTypePatterns()
        {
            return new Dictionary<string, DeviceTypeInfo>
            {
                { "iphone", new DeviceTypeInfo { Type = "Smartphone", Icon = "📱", Description = "iPhone" } },
                { "ipad", new DeviceTypeInfo { Type = "Tablet", Icon = "📱", Description = "iPad" } },
                { "macbook", new DeviceTypeInfo { Type = "Notebook", Icon = "💻", Description = "MacBook" } },
                { "imac", new DeviceTypeInfo { Type = "Desktop", Icon = "🖥️", Description = "iMac" } },
                { "android", new DeviceTypeInfo { Type = "Smartphone", Icon = "📱", Description = "Android" } },
                { "galaxy", new DeviceTypeInfo { Type = "Smartphone", Icon = "📱", Description = "Samsung Galaxy" } },
                { "pixel", new DeviceTypeInfo { Type = "Smartphone", Icon = "📱", Description = "Google Pixel" } },
                { "router", new DeviceTypeInfo { Type = "Roteador", Icon = "🌐", Description = "Router" } },
                { "gateway", new DeviceTypeInfo { Type = "Gateway", Icon = "🌐", Description = "Gateway" } },
                { "desktop", new DeviceTypeInfo { Type = "Desktop", Icon = "🖥️", Description = "Desktop PC" } },
                { "laptop", new DeviceTypeInfo { Type = "Notebook", Icon = "💻", Description = "Laptop" } },
                { "notebook", new DeviceTypeInfo { Type = "Notebook", Icon = "💻", Description = "Notebook" } },
                { "tv", new DeviceTypeInfo { Type = "Smart TV", Icon = "📺", Description = "Smart TV" } },
                { "chromecast", new DeviceTypeInfo { Type = "Streaming", Icon = "📡", Description = "Chromecast" } },
                { "alexa", new DeviceTypeInfo { Type = "Smart Speaker", Icon = "🔊", Description = "Amazon Alexa" } },
                { "echo", new DeviceTypeInfo { Type = "Smart Speaker", Icon = "🔊", Description = "Amazon Echo" } }};
        }
        
        private Dictionary<string, string> InitializeOUIDatabase()
        {
            // Base de dados simplificada com fabricantes mais comuns
            return new Dictionary<string, string>
            {
                // Apple
                { "001451", "Apple" },
                { "0016CB", "Apple" },
                { "0017F2", "Apple" },
                { "0019E3", "Apple" },
                { "001B63", "Apple" },
                { "001EC2", "Apple" },
                { "002312", "Apple" },
                { "002436", "Apple" },
                { "002500", "Apple" },
                { "0025BC", "Apple" },
                { "0026B0", "Apple" },
                { "0026BB", "Apple" },
                { "003065", "Apple" },
                { "0050E4", "Apple" },
                { "00A040", "Apple" },
                { "00D0B7", "Apple" },
                { "10DD B1", "Apple" },
                { "3C0754", "Apple" },
                { "5C5948", "Apple" },
                { "7C6D62", "Apple" },
                { "A4C361", "Apple" },
                { "F0B479", "Apple" },
                
                // Samsung
                { "0002FC", "Samsung" },
                { "000F86", "Samsung" },
                { "001377", "Samsung" },
                { "001632", "Samsung" },
                { "001D25", "Samsung" },
                { "002566", "Samsung" },
                { "0026FC", "Samsung" },
                { "34AA8B", "Samsung" },
                { "5C0A5B", "Samsung" },
                { "C8BA94", "Samsung" },
                { "E4121D", "Samsung" },
                
                // TP-Link
                { "001D0F", "TP-Link" },
                { "0C8268", "TP-Link" },
                { "1C3BF3", "TP-Link" },
                { "50C7BF", "TP-Link" },
                { "A0F3C1", "TP-Link" },
                { "C46E1F", "TP-Link" },
                
                // Xiaomi
                { "34CE00", "Xiaomi" },
                { "50EC50", "Xiaomi" },
                { "64B473", "Xiaomi" },
                { "786A89", "Xiaomi" },
                { "8CFABA", "Xiaomi" },
                { "F8A45F", "Xiaomi" },
                
                // Huawei
                { "0018E7", "Huawei" },
                { "001E10", "Huawei" },
                { "002568", "Huawei" },
                { "00259E", "Huawei" },
                { "00E0FC", "Huawei" },
                { "48F8B3", "Huawei" },
                { "C83A35", "Huawei" },
                
                // Intel
                { "001B21", "Intel" },
                { "0050F2", "Intel" },
                { "3C6AA7", "Intel" },
                { "7085C2", "Intel" },
                { "A0A8CD", "Intel" },
                
                // Realtek
                { "00E04C", "Realtek" },
                { "525400", "Realtek" },
                { "B0B98A", "Realtek" },
                
                // Microsoft
                { "000D3A", "Microsoft" },
                { "7C1E52", "Microsoft" },
                
                // Google
                { "3C5A37", "Google" },
                { "54605D", "Google" },
                { "F4F5D8", "Google" },
                
                // Amazon
                { "00FC8B", "Amazon" },
                { "44650D", "Amazon" },
                { "74C246", "Amazon" },
                
                // Sony
                { "001A80", "Sony" },
                { "0023BE", "Sony" },
                { "00D9D1", "Sony" },
                { "7C6198", "Sony" },
                
                // LG
                { "001C62", "LG" },
                { "0026E2", "LG" },
                { "10F96F", "LG" },
                { "A0D795", "LG" },
                
                // D-Link
                { "001195", "D-Link" },
                { "001346", "D-Link" },
                { "001E58", "D-Link" },
                { "1C7EE5", "D-Link" },
                
                // Asus
                { "001731", "Asus" },
                { "001EA6", "Asus" },
                { "0026B6", "Asus" },
                { "107B44", "Asus" },
                
                // Cisco
                { "000142", "Cisco" },
                { "00096C", "Cisco" },
                { "001120", "Cisco" },
                { "0016C7", "Cisco" }
            };
        }
    }
}

    
    public class DeviceTypeInfo
    {
        public string Type { get; set; } = "Desconhecido";
        public string Icon { get; set; } = "🖥️";
        public string Description { get; set; } = "Unknown Device";
    }
