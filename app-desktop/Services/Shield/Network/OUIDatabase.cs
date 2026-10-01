using System;
using System.Collections.Generic;
using System.Linq;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.Shield.Network
{
    /// <summary>
    /// Base de dados IEEE OUI (Organizationally Unique Identifier)
    /// Mapeia prefixos MAC para fabricantes
    /// </summary>
    public class OUIDatabase
    {
        private readonly ILoggingService _logger;
        private readonly Dictionary<string, string> _ouiMap;
        
        public OUIDatabase(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _ouiMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            InitializeDatabase();
            _logger.LogInfo($"[OUIDatabase] Carregado com {_ouiMap.Count} fabricantes");
        }
        
        /// <summary>
        /// Obtém o fabricante a partir do MAC Address
        /// </summary>
        public string GetVendorFromMac(string macAddress)
        {
            if (string.IsNullOrEmpty(macAddress) || macAddress == "Unknown" || macAddress == "Desconhecido")
                return "Unknown";
            
            try
            {
                // Remover separadores e pegar apenas os primeiros 6 caracteres
                var cleanMac = macAddress.Replace(":", "").Replace("-", "").Replace(".", "").Trim();
                
                if (cleanMac.Length < 6)
                {
                    _logger.LogWarning($"[OUIDatabase] MAC muito curto: {macAddress}");
                    return "Unknown";
                }
                
                var firstOctet = Convert.ToInt32(cleanMac.Substring(0, 2), 16);
                if ((firstOctet & 0x02) != 0)
                {
                    _logger.LogInfo($"[OUIDatabase] MAC localmente administrado (endereço privado): {macAddress}");
                    return "Private/Unknown";
                }

                // Extrair OUI (primeiros 6 caracteres)
                var oui = cleanMac.Substring(0, 6).ToUpperInvariant();
                
                if (_ouiMap.TryGetValue(oui, out var vendor))
                {
                    return vendor;
                }
                
                return "Unknown";
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[OUIDatabase] Erro ao processar MAC {macAddress}: {ex.Message}");
                return "Unknown";
            }
        }
        
        /// <summary>
        /// Inicializa a base de dados com fabricantes conhecidos
        /// </summary>
        private void InitializeDatabase()
        {
            // APPLE - Dispositivos iOS, macOS
            AddVendor("001451", "Apple");
            AddVendor("0016CB", "Apple");
            AddVendor("0017F2", "Apple");
            AddVendor("0019E3", "Apple");
            AddVendor("001B63", "Apple");
            AddVendor("001EC2", "Apple");
            AddVendor("002312", "Apple");
            AddVendor("002436", "Apple");
            AddVendor("002500", "Apple");
            AddVendor("0026B0", "Apple");
            AddVendor("0026BB", "Apple");
            AddVendor("003065", "Apple");
            AddVendor("0050E4", "Apple");
            AddVendor("00A040", "Apple");
            AddVendor("10DDB1", "Apple");
            AddVendor("3C0754", "Apple");
            AddVendor("5C5948", "Apple");
            AddVendor("624DA6", "Apple"); // MAC do usuário (possível iPhone com MAC aleatório)
            AddVendor("7C6D62", "Apple");
            AddVendor("A4C361", "Apple");
            AddVendor("F0B479", "Apple");
            AddVendor("D023DB", "Apple");
            AddVendor("E0ACCB", "Apple");
            AddVendor("F82793", "Apple");
            
            // SAMSUNG - Smartphones, TVs, Tablets
            AddVendor("0002FC", "Samsung");
            AddVendor("000F86", "Samsung");
            AddVendor("001377", "Samsung");
            AddVendor("001632", "Samsung");
            AddVendor("001D25", "Samsung");
            AddVendor("002566", "Samsung");
            AddVendor("0026FC", "Samsung");
            AddVendor("34AA8B", "Samsung");
            AddVendor("5C0A5B", "Samsung");
            AddVendor("6C221A", "Samsung"); // MAC do usuário
            AddVendor("C8BA94", "Samsung");
            AddVendor("E4121D", "Samsung");
            AddVendor("D0176A", "Samsung");
            AddVendor("E81132", "Samsung");
            AddVendor("F0E77E", "Samsung");
            AddVendor("B853AC", "Samsung");
            
            // XIAOMI - Smartphones Android
            AddVendor("34CE00", "Xiaomi");
            AddVendor("50EC50", "Xiaomi");
            AddVendor("64B473", "Xiaomi");
            AddVendor("786A89", "Xiaomi");
            AddVendor("8CFABA", "Xiaomi");
            AddVendor("F8A45F", "Xiaomi");
            AddVendor("F4F5D8", "Xiaomi");
            AddVendor("28E14C", "Xiaomi");
            AddVendor("F0B429", "Xiaomi");
            
            // HUAWEI - Smartphones, Routers
            AddVendor("0018E7", "Huawei");
            AddVendor("001E10", "Huawei");
            AddVendor("002568", "Huawei");
            AddVendor("00259E", "Huawei");
            AddVendor("00E0FC", "Huawei");
            AddVendor("48F8B3", "Huawei");
            AddVendor("C83A35", "Huawei");
            AddVendor("D0C857", "Huawei");
            AddVendor("E0979F", "Huawei");
            
            // TP-LINK - Routers, Access Points
            AddVendor("001D0F", "TP-Link");
            AddVendor("0C8268", "TP-Link");
            AddVendor("1C3BF3", "TP-Link");
            AddVendor("50C7BF", "TP-Link");
            AddVendor("58AEF1", "TP-Link"); // MAC do usuário (gateway)
            AddVendor("A0F3C1", "TP-Link");
            AddVendor("C46E1F", "TP-Link");
            AddVendor("D8EB97", "TP-Link");
            AddVendor("E848B8", "TP-Link");
            AddVendor("F4F26D", "TP-Link");
            
            // INTEL - Network Adapters, PCs
            AddVendor("001B21", "Intel");
            AddVendor("3C6AA7", "Intel");
            AddVendor("7085C2", "Intel");
            AddVendor("A0A8CD", "Intel");
            AddVendor("D4BED9", "Intel");
            AddVendor("DC2148", "Intel"); // MAC do usuário (Intel Wireless-AC 9462)
            AddVendor("E4A7A0", "Intel");
            
            // REALTEK - Network Adapters
            AddVendor("00E04C", "Realtek");
            AddVendor("3EA81D", "Realtek"); // MAC do usuário (possível adapter virtual ou dispositivo)
            AddVendor("525400", "Realtek");
            AddVendor("B0B98A", "Realtek");
            AddVendor("E0699A", "Realtek");
            
            // MICROSOFT - Surface, Xbox
            AddVendor("000D3A", "Microsoft");
            AddVendor("7C1E52", "Microsoft");
            AddVendor("98E0D9", "Microsoft");
            AddVendor("D0509", "Microsoft");
            
            // GOOGLE - Chromecast, Nest, Pixel
            AddVendor("3C5A37", "Google");
            AddVendor("54605D", "Google");
            AddVendor("F4F5D8", "Google");
            AddVendor("D0C5D3", "Google");
            
            // AMAZON - Echo, Fire TV, Kindle
            AddVendor("00FC8B", "Amazon");
            AddVendor("44650D", "Amazon");
            AddVendor("74C246", "Amazon");
            AddVendor("F0D2F1", "Amazon");
            
            // SONY - PlayStation, TVs, Cameras
            AddVendor("001A80", "Sony");
            AddVendor("0023BE", "Sony");
            AddVendor("00D9D1", "Sony");
            AddVendor("7C6198", "Sony");
            AddVendor("E0E751", "Sony");
            
            // LG - TVs, Smartphones
            AddVendor("001C62", "LG Electronics");
            AddVendor("0026E2", "LG Electronics");
            AddVendor("10F96F", "LG Electronics");
            AddVendor("A0D795", "LG Electronics");
            AddVendor("B4E1C4", "LG Electronics");
            
            // D-LINK - Routers, Switches
            AddVendor("001195", "D-Link");
            AddVendor("001346", "D-Link");
            AddVendor("001E58", "D-Link");
            AddVendor("1C7EE5", "D-Link");
            AddVendor("C0A0BB", "D-Link");
            
            // ASUS - Routers, Motherboards
            AddVendor("001731", "Asus");
            AddVendor("001EA6", "Asus");
            AddVendor("0026B6", "Asus");
            AddVendor("107B44", "Asus");
            AddVendor("2C56DC", "Asus");
            
            // CISCO - Enterprise Networking
            AddVendor("000142", "Cisco");
            AddVendor("00096C", "Cisco");
            AddVendor("001120", "Cisco");
            AddVendor("0016C7", "Cisco");
            AddVendor("001F9E", "Cisco");
            
            // MOTOROLA - Smartphones, Modems
            AddVendor("000CE2", "Motorola");
            AddVendor("001A1B", "Motorola");
            AddVendor("0025E5", "Motorola");
            AddVendor("D4E8B2", "Motorola");
            
            // DELL - PCs, Servers
            AddVendor("001C23", "Dell");
            AddVendor("0026B9", "Dell");
            AddVendor("B8CA3A", "Dell");
            AddVendor("D4AE52", "Dell");
            
            // HP - PCs, Printers
            AddVendor("001438", "HP");
            AddVendor("001E0B", "HP");
            AddVendor("002264", "HP");
            AddVendor("D48564", "HP");
            
            // LENOVO - PCs, Tablets
            AddVendor("00215D", "Lenovo");
            AddVendor("5CF9DD", "Lenovo");
            AddVendor("E4B318", "Lenovo");
            
            // NETGEAR - Routers, Switches
            AddVendor("001B2F", "Netgear");
            AddVendor("001E2A", "Netgear");
            AddVendor("0024B2", "Netgear");
            AddVendor("A0040A", "Netgear");
            
            // UBIQUITI - Enterprise WiFi
            AddVendor("00156D", "Ubiquiti");
            AddVendor("0418D6", "Ubiquiti");
            AddVendor("24A43C", "Ubiquiti");
            AddVendor("F09FC2", "Ubiquiti");
            
            // MIKROTIK - Routers
            AddVendor("00030D", "MikroTik");
            AddVendor("4C5E0C", "MikroTik");
            AddVendor("D4CA6D", "MikroTik");
            
            // RASPBERRY PI - IoT, Servers
            AddVendor("B827EB", "Raspberry Pi");
            AddVendor("DCA632", "Raspberry Pi");
            AddVendor("E45F01", "Raspberry Pi");
            
            // NVIDIA - Shield TV, GPUs
            AddVendor("000004", "Nvidia");
            AddVendor("00044B", "Nvidia");
            AddVendor("001D60", "Nvidia");
            
            // BROADCOM - Network Chips
            AddVendor("001018", "Broadcom");
            AddVendor("0090A9", "Broadcom");
            AddVendor("B4B676", "Broadcom");
            
            // QUALCOMM - Chipsets
            AddVendor("000A94", "Qualcomm");
            AddVendor("0015E9", "Qualcomm");
            AddVendor("D85D4C", "Qualcomm");
            
            // PHILIPS - Hue, TVs
            AddVendor("001788", "Philips");
            AddVendor("00178D", "Philips");
            AddVendor("ECB5FA", "Philips");
            
            // RING - Doorbells, Cameras
            AddVendor("74C63B", "Ring");
            AddVendor("B0C7DE", "Ring");
            
            // NEST - Thermostats, Cameras
            AddVendor("18B430", "Nest Labs");
            AddVendor("64168D", "Nest Labs");
            
            // ROKU - Streaming Devices
            AddVendor("00173F", "Roku");
            AddVendor("B0A737", "Roku");
            AddVendor("D8313", "Roku");
            
            // CANON - Printers, Cameras
            AddVendor("001279", "Canon");
            AddVendor("0019A9", "Canon");
            AddVendor("F0D1A9", "Canon");
            
            // EPSON - Printers
            AddVendor("001279", "Epson");
            AddVendor("0026AB", "Epson");
            AddVendor("64EB8C", "Epson");
            
            // BROTHER - Printers
            AddVendor("002586", "Brother");
            AddVendor("00E0B0", "Brother");
            AddVendor("30055C", "Brother");
            
            // ACER - PCs, Monitors
            AddVendor("001D0F", "Acer");
            AddVendor("0025D3", "Acer");
            AddVendor("F0DEF1", "Acer");
            
            // TOSHIBA - PCs, TVs
            AddVendor("001560", "Toshiba");
            AddVendor("0026C6", "Toshiba");
            AddVendor("B8F6B1", "Toshiba");
        }
        
        private void AddVendor(string oui, string vendor)
        {
            if (!_ouiMap.ContainsKey(oui))
            {
                _ouiMap[oui] = vendor;
            }
        }
    }
}
