using System;
using System.Collections.Generic;
using System.Linq;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.Shield.Network
{
    /// <summary>
    /// Classificador inteligente de dispositivos baseado em fabricante e hostname
    /// </summary>
    public class DeviceClassifier
    {
        private readonly ILoggingService _logger;
        private readonly Dictionary<string, DeviceClassificationRule> _rules;
        
        public DeviceClassifier(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _rules = new Dictionary<string, DeviceClassificationRule>(StringComparer.OrdinalIgnoreCase);
            InitializeRules();
        }
        
        /// <summary>
        /// Classifica um dispositivo baseado no fabricante e hostname
        /// </summary>
        public DeviceClassificationResult ClassifyDevice(string vendor, string hostname, bool isGateway)
        {
            if (string.IsNullOrEmpty(vendor) || vendor == "Unknown")
                return null;
            
            var vendorLower = vendor.ToLowerInvariant();
            var hostnameLower = hostname?.ToLowerInvariant() ?? "";
            
            // Se é gateway, classificar como router
            if (isGateway)
            {
                return new DeviceClassificationResult
                {
                    DeviceType = "Router",
                    Category = DeviceCategory.NetworkInfrastructure,
                    OperatingSystem = "Router OS",
                    Icon = "🌐",
                    Confidence = 40
                };
            }
            
            // Primeiro: tentar inferir do hostname (mais preciso)
            var hostnameResult = InferFromHostname(hostnameLower);
            if (hostnameResult != null && hostnameResult.Confidence >= 25)
            {
                return hostnameResult;
            }
            
            // Segundo: verificar regras específicas de fabricante combinadas com hostname
            foreach (var rule in _rules.Values)
            {
                if (rule.VendorPatterns.Any(p => vendorLower.Contains(p.ToLowerInvariant())))
                {
                    if (hostnameResult != null)
                    {
                        return new DeviceClassificationResult
                        {
                            DeviceType = hostnameResult.DeviceType ?? rule.DefaultDeviceType,
                            Category = hostnameResult.Category != DeviceCategory.Other ? hostnameResult.Category : rule.DefaultCategory,
                            OperatingSystem = hostnameResult.OperatingSystem ?? rule.DefaultOS,
                            Icon = hostnameResult.Icon ?? rule.Icon ?? "🖥️",
                            Confidence = 30 + hostnameResult.Confidence,
                            IsPortable = hostnameResult.IsPortable || rule.IsPortable,
                            DeviceModel = hostnameResult.DeviceModel
                        };
                    }
                    
                    return new DeviceClassificationResult
                    {
                        DeviceType = rule.DefaultDeviceType,
                        Category = rule.DefaultCategory,
                        OperatingSystem = rule.DefaultOS,
                        Icon = rule.Icon ?? "🖥️",
                        Confidence = 25,
                        IsPortable = rule.IsPortable
                    };
                }
            }
            
            // Terceiro: se tivermos resultado de hostname de baixa confiança, usar ele
            if (hostnameResult != null)
            {
                return hostnameResult;
            }
            
            return null;
        }
        
        private DeviceClassificationResult InferFromHostname(string hostname)
        {
            if (string.IsNullOrEmpty(hostname))
                return null;

            // Samsung model patterns: SM-XXXX, GT-XXXX, SC-XXXX
            var smMatch = System.Text.RegularExpressions.Regex.Match(hostname, @"^(sm-|gt-|sc-)[a-z0-9]+");
            if (smMatch.Success)
            {
                return new DeviceClassificationResult
                {
                    DeviceType = "Smartphone",
                    Category = DeviceCategory.Smartphone,
                    OperatingSystem = "Android",
                    Icon = "📱",
                    Confidence = 35,
                    IsPortable = true,
                    DeviceModel = "Samsung " + smMatch.Value.ToUpperInvariant()
                };
            }

            // Xiaomi patterns
            if (System.Text.RegularExpressions.Regex.IsMatch(hostname, @"^(mi |mi-|redmi|poco)"))
            {
                return new DeviceClassificationResult
                {
                    DeviceType = "Smartphone",
                    Category = DeviceCategory.Smartphone,
                    OperatingSystem = "Android",
                    Icon = "📱",
                    Confidence = 30,
                    IsPortable = true,
                    DeviceModel = "Xiaomi " + hostname.Split(' ')[0].ToUpperInvariant()
                };
            }

            // Huawei patterns
            if (System.Text.RegularExpressions.Regex.IsMatch(hostname, @"^(lya-|ele-|vog-|mar-|clt-)"))
            {
                return new DeviceClassificationResult
                {
                    DeviceType = "Smartphone",
                    Category = DeviceCategory.Smartphone,
                    OperatingSystem = "HarmonyOS",
                    Icon = "📱",
                    Confidence = 30,
                    IsPortable = true,
                    DeviceModel = "Huawei " + hostname.Split('-')[0].ToUpperInvariant()
                };
            }

            // OnePlus
            if (hostname.StartsWith("oneplus") || System.Text.RegularExpressions.Regex.IsMatch(hostname, @"^kb\d+"))
            {
                return new DeviceClassificationResult
                {
                    DeviceType = "Smartphone",
                    Category = DeviceCategory.Smartphone,
                    OperatingSystem = "Android",
                    Icon = "📱",
                    Confidence = 30,
                    IsPortable = true,
                    DeviceModel = "OnePlus"
                };
            }

            // Motorola
            if (hostname.StartsWith("moto") || System.Text.RegularExpressions.Regex.IsMatch(hostname, @"^xt\d+"))
            {
                return new DeviceClassificationResult
                {
                    DeviceType = "Smartphone",
                    Category = DeviceCategory.Smartphone,
                    OperatingSystem = "Android",
                    Icon = "📱",
                    Confidence = 30,
                    IsPortable = true,
                    DeviceModel = "Motorola"
                };
            }

            // LG phone
            if (hostname.StartsWith("lg-") || hostname.StartsWith("lm-"))
            {
                return new DeviceClassificationResult
                {
                    DeviceType = "Smartphone",
                    Category = DeviceCategory.Smartphone,
                    OperatingSystem = "Android",
                    Icon = "📱",
                    Confidence = 25,
                    IsPortable = true,
                    DeviceModel = "LG"
                };
            }

            // Nokia
            if (hostname.StartsWith("ta-"))
            {
                return new DeviceClassificationResult
                {
                    DeviceType = "Smartphone",
                    Category = DeviceCategory.Smartphone,
                    OperatingSystem = "Android",
                    Icon = "📱",
                    Confidence = 25,
                    IsPortable = true,
                    DeviceModel = "Nokia"
                };
            }

            // Windows Desktop with prefix
            if (hostname.StartsWith("desktop-"))
            {
                return new DeviceClassificationResult
                {
                    DeviceType = "Desktop",
                    Category = DeviceCategory.DesktopPC,
                    OperatingSystem = "Windows",
                    Icon = "🖥️",
                    Confidence = 25
                };
            }

            if (hostname.StartsWith("workstation-"))
            {
                return new DeviceClassificationResult
                {
                    DeviceType = "Desktop",
                    Category = DeviceCategory.DesktopPC,
                    OperatingSystem = "Windows",
                    Icon = "🖥️",
                    Confidence = 25
                };
            }

            // Desktop PC
            if (hostname.Contains("desktop") || hostname.Contains("pc-") || hostname.Contains("tower"))
            {
                return new DeviceClassificationResult
                {
                    DeviceType = "Desktop",
                    Category = DeviceCategory.DesktopPC,
                    OperatingSystem = "Windows",
                    Icon = "🖥️",
                    Confidence = 20
                };
            }

            // Windows Laptop
            if (hostname.Contains("laptop") || hostname.Contains("notebook"))
            {
                return new DeviceClassificationResult
                {
                    DeviceType = "Laptop",
                    Category = DeviceCategory.Notebook,
                    OperatingSystem = "Windows",
                    Icon = "💻",
                    Confidence = 20,
                    IsPortable = true
                };
            }

            // MacBook naming patterns
            if (hostname.StartsWith("macbook-pro-") || hostname.StartsWith("macbook-air-"))
            {
                return new DeviceClassificationResult
                {
                    DeviceType = "Laptop",
                    Category = DeviceCategory.Notebook,
                    OperatingSystem = "macOS",
                    Icon = "💻",
                    Confidence = 30,
                    IsPortable = true,
                    DeviceModel = "Apple MacBook"
                };
            }

            if (hostname.Contains("macbook"))
            {
                return new DeviceClassificationResult
                {
                    DeviceType = "Laptop",
                    Category = DeviceCategory.Notebook,
                    OperatingSystem = "macOS",
                    Icon = "💻",
                    Confidence = 25,
                    IsPortable = true,
                    DeviceModel = "Apple MacBook"
                };
            }

            // iMac / Mac desktop
            if (hostname.Contains("imac") || hostname.StartsWith("mac-pro-") || hostname.Contains("macmini"))
            {
                return new DeviceClassificationResult
                {
                    DeviceType = "Desktop",
                    Category = DeviceCategory.DesktopPC,
                    OperatingSystem = "macOS",
                    Icon = "🖥️",
                    Confidence = 25,
                    DeviceModel = "Apple Mac"
                };
            }

            if (hostname.Contains("mac-") && !hostname.Contains("macbook"))
            {
                return new DeviceClassificationResult
                {
                    DeviceType = "Desktop",
                    Category = DeviceCategory.DesktopPC,
                    OperatingSystem = "macOS",
                    Icon = "🖥️",
                    Confidence = 20,
                    DeviceModel = "Apple Mac"
                };
            }

            // iPhone
            if (hostname.Contains("iphone"))
            {
                return new DeviceClassificationResult
                {
                    DeviceType = "Smartphone",
                    Category = DeviceCategory.Smartphone,
                    OperatingSystem = "iOS",
                    Icon = "📱",
                    Confidence = 25,
                    IsPortable = true,
                    DeviceModel = "Apple iPhone"
                };
            }

            // iPad
            if (hostname.Contains("ipad"))
            {
                return new DeviceClassificationResult
                {
                    DeviceType = "Tablet",
                    Category = DeviceCategory.Tablet,
                    OperatingSystem = "iPadOS",
                    Icon = "📱",
                    Confidence = 25,
                    IsPortable = true,
                    DeviceModel = "Apple iPad"
                };
            }

            // Android
            if (hostname.Contains("android"))
            {
                return new DeviceClassificationResult
                {
                    DeviceType = "Smartphone",
                    Category = DeviceCategory.Smartphone,
                    OperatingSystem = "Android",
                    Icon = "📱",
                    Confidence = 20,
                    IsPortable = true
                };
            }

            // Samsung Galaxy
            if (hostname.Contains("galaxy"))
            {
                return new DeviceClassificationResult
                {
                    DeviceType = "Smartphone",
                    Category = DeviceCategory.Smartphone,
                    OperatingSystem = "Android",
                    Icon = "📱",
                    Confidence = 25,
                    IsPortable = true,
                    DeviceModel = "Samsung Galaxy"
                };
            }

            // Google Pixel
            if (hostname.Contains("pixel"))
            {
                return new DeviceClassificationResult
                {
                    DeviceType = "Smartphone",
                    Category = DeviceCategory.Smartphone,
                    OperatingSystem = "Android",
                    Icon = "📱",
                    Confidence = 25,
                    IsPortable = true,
                    DeviceModel = "Google Pixel"
                };
            }

            // Router
            if (hostname.Contains("router") || hostname.Contains("gateway"))
            {
                return new DeviceClassificationResult
                {
                    DeviceType = "Router",
                    Category = DeviceCategory.NetworkInfrastructure,
                    OperatingSystem = "Router OS",
                    Icon = "🌐",
                    Confidence = 20
                };
            }

            // Smart TV
            if (hostname.Contains("tv") || hostname.Contains("smarttv") || hostname.Contains("bravia") || hostname.Contains("samsungtv") || hostname.Contains("lgwebos"))
            {
                return new DeviceClassificationResult
                {
                    DeviceType = "Smart TV",
                    Category = DeviceCategory.SmartTV,
                    OperatingSystem = "TV OS",
                    Icon = "📺",
                    Confidence = 20
                };
            }

            // Printer
            if (hostname.Contains("printer") || hostname.Contains("print-") || hostname.Contains("canon-") || hostname.Contains("epson-") || hostname.Contains("hp-"))
            {
                return new DeviceClassificationResult
                {
                    DeviceType = "Printer",
                    Category = DeviceCategory.Printer,
                    OperatingSystem = "Printer OS",
                    Icon = "🖨️",
                    Confidence = 25
                };
            }

            // Game Console
            if (hostname.Contains("playstation") || hostname.Contains("ps4") || hostname.Contains("ps5") || hostname.Contains("ps-"))
            {
                return new DeviceClassificationResult
                {
                    DeviceType = "Game Console",
                    Category = DeviceCategory.GameConsole,
                    OperatingSystem = "PlayStation OS",
                    Icon = "🎮",
                    Confidence = 25,
                    DeviceModel = "Sony PlayStation"
                };
            }

            if (hostname.Contains("xbox"))
            {
                return new DeviceClassificationResult
                {
                    DeviceType = "Game Console",
                    Category = DeviceCategory.GameConsole,
                    OperatingSystem = "Xbox OS",
                    Icon = "🎮",
                    Confidence = 25,
                    DeviceModel = "Microsoft Xbox"
                };
            }

            if (hostname.Contains("nintendo") || hostname.Contains("switch"))
            {
                return new DeviceClassificationResult
                {
                    DeviceType = "Game Console",
                    Category = DeviceCategory.GameConsole,
                    OperatingSystem = "Nintendo OS",
                    Icon = "🎮",
                    Confidence = 25,
                    DeviceModel = "Nintendo Switch"
                };
            }

            // Raspberry Pi
            if (hostname.Contains("raspberrypi") || hostname.Contains("raspberry"))
            {
                return new DeviceClassificationResult
                {
                    DeviceType = "Single Board Computer",
                    Category = DeviceCategory.IoT,
                    OperatingSystem = "Linux",
                    Icon = "🔧",
                    Confidence = 25
                };
            }

            // NAS
            if (hostname.Contains("nas") || hostname.Contains("storage") || hostname.Contains("synology") || hostname.Contains("qnap"))
            {
                return new DeviceClassificationResult
                {
                    DeviceType = "NAS",
                    Category = DeviceCategory.NAS,
                    OperatingSystem = "NAS OS",
                    Icon = "🗄️",
                    Confidence = 20
                };
            }

            // IP Camera
            if (hostname.Contains("camera") || hostname.Contains("ipcam") || hostname.Contains("cam-") || hostname.Contains("dvr"))
            {
                return new DeviceClassificationResult
                {
                    DeviceType = "IP Camera",
                    Category = DeviceCategory.SecurityCamera,
                    OperatingSystem = "Camera OS",
                    Icon = "📹",
                    Confidence = 20
                };
            }

            // Chromecast / Streaming
            if (hostname.Contains("chromecast") || hostname.Contains("roku") || hostname.Contains("firetv") || hostname.Contains("appletv"))
            {
                return new DeviceClassificationResult
                {
                    DeviceType = "Streaming Device",
                    Category = DeviceCategory.StreamingDevice,
                    OperatingSystem = "Streaming OS",
                    Icon = "📡",
                    Confidence = 25
                };
            }

            // Smart Speaker
            if (hostname.Contains("echo") || hostname.Contains("alexa") || hostname.Contains("googlehome"))
            {
                return new DeviceClassificationResult
                {
                    DeviceType = "Smart Speaker",
                    Category = DeviceCategory.SmartSpeaker,
                    OperatingSystem = "IoT OS",
                    Icon = "🔊",
                    Confidence = 25
                };
            }

            // IoT generic
            if (hostname.Contains("iot") || hostname.Contains("sensor") || hostname.Contains("smartplug") || hostname.Contains("bulb") || hostname.Contains("esp32") || hostname.Contains("arduino"))
            {
                return new DeviceClassificationResult
                {
                    DeviceType = "IoT Device",
                    Category = DeviceCategory.IoT,
                    OperatingSystem = "IoT OS",
                    Icon = "🏠",
                    Confidence = 20
                };
            }

            // Linux PC
            if (hostname.Contains("ubuntu") || hostname.Contains("debian") || hostname.Contains("fedora") || hostname.Contains("arch") || hostname.Contains("linux"))
            {
                return new DeviceClassificationResult
                {
                    DeviceType = "Desktop",
                    Category = DeviceCategory.DesktopPC,
                    OperatingSystem = "Linux",
                    Icon = "🐧",
                    Confidence = 20
                };
            }

            return null;
        }
        
        private void InitializeRules()
        {
            // Apple Devices
            _rules["Apple"] = new DeviceClassificationRule
            {
                VendorPatterns = new[] { "Apple" },
                DefaultDeviceType = "Apple Device",
                DefaultOS = "iOS/macOS",
                Icon = "🍎"
            };
            
            // Samsung - Smartphones e TVs
            _rules["Samsung"] = new DeviceClassificationRule
            {
                VendorPatterns = new[] { "Samsung" },
                DefaultDeviceType = "Smartphone",
                DefaultOS = "Android",
                Icon = "📱"
            };
            
            // Xiaomi - Smartphones
            _rules["Xiaomi"] = new DeviceClassificationRule
            {
                VendorPatterns = new[] { "Xiaomi" },
                DefaultDeviceType = "Smartphone",
                DefaultOS = "Android",
                Icon = "📱"
            };
            
            // Huawei - Smartphones e Routers
            _rules["Huawei"] = new DeviceClassificationRule
            {
                VendorPatterns = new[] { "Huawei" },
                DefaultDeviceType = "Smartphone/Router",
                DefaultOS = "Android/HarmonyOS",
                Icon = "📱"
            };
            
            // Motorola - Smartphones
            _rules["Motorola"] = new DeviceClassificationRule
            {
                VendorPatterns = new[] { "Motorola" },
                DefaultDeviceType = "Smartphone",
                DefaultOS = "Android",
                Icon = "📱"
            };
            
            // TP-Link - Routers
            _rules["TP-Link"] = new DeviceClassificationRule
            {
                VendorPatterns = new[] { "TP-Link" },
                DefaultDeviceType = "Router",
                DefaultOS = "Router OS",
                Icon = "🌐"
            };
            
            // D-Link - Routers
            _rules["D-Link"] = new DeviceClassificationRule
            {
                VendorPatterns = new[] { "D-Link" },
                DefaultDeviceType = "Router",
                DefaultOS = "Router OS",
                Icon = "🌐"
            };
            
            // Asus - Routers e PCs
            _rules["Asus"] = new DeviceClassificationRule
            {
                VendorPatterns = new[] { "Asus" },
                DefaultDeviceType = "Router/PC",
                DefaultOS = "Router OS/Windows",
                Icon = "🌐"
            };
            
            // Cisco - Enterprise Networking
            _rules["Cisco"] = new DeviceClassificationRule
            {
                VendorPatterns = new[] { "Cisco" },
                DefaultDeviceType = "Network Device",
                DefaultOS = "IOS",
                Icon = "🌐"
            };
            
            // Netgear - Routers
            _rules["Netgear"] = new DeviceClassificationRule
            {
                VendorPatterns = new[] { "Netgear" },
                DefaultDeviceType = "Router",
                DefaultOS = "Router OS",
                Icon = "🌐"
            };
            
            // Ubiquiti - Enterprise WiFi
            _rules["Ubiquiti"] = new DeviceClassificationRule
            {
                VendorPatterns = new[] { "Ubiquiti" },
                DefaultDeviceType = "Access Point",
                DefaultOS = "UniFi OS",
                Icon = "📡"
            };
            
            // MikroTik - Routers
            _rules["MikroTik"] = new DeviceClassificationRule
            {
                VendorPatterns = new[] { "MikroTik" },
                DefaultDeviceType = "Router",
                DefaultOS = "RouterOS",
                Icon = "🌐"
            };
            
            // Intel - Network Adapters
            _rules["Intel"] = new DeviceClassificationRule
            {
                VendorPatterns = new[] { "Intel" },
                DefaultDeviceType = "Laptop",
                DefaultOS = "Windows",
                Icon = "💻"
            };
            
            // Realtek - Network Adapters
            _rules["Realtek"] = new DeviceClassificationRule
            {
                VendorPatterns = new[] { "Realtek" },
                DefaultDeviceType = "PC",
                DefaultOS = "Windows/Linux",
                Icon = "💻"
            };
            
            // Microsoft - Surface, Xbox
            _rules["Microsoft"] = new DeviceClassificationRule
            {
                VendorPatterns = new[] { "Microsoft" },
                DefaultDeviceType = "PC/Console",
                DefaultOS = "Windows",
                Icon = "💻"
            };
            
            // Dell - PCs
            _rules["Dell"] = new DeviceClassificationRule
            {
                VendorPatterns = new[] { "Dell" },
                DefaultDeviceType = "PC",
                DefaultOS = "Windows",
                Icon = "💻"
            };
            
            // HP - PCs e Printers
            _rules["HP"] = new DeviceClassificationRule
            {
                VendorPatterns = new[] { "HP" },
                DefaultDeviceType = "PC/Printer",
                DefaultOS = "Windows",
                Icon = "💻"
            };
            
            // Lenovo - PCs
            _rules["Lenovo"] = new DeviceClassificationRule
            {
                VendorPatterns = new[] { "Lenovo" },
                DefaultDeviceType = "PC",
                DefaultOS = "Windows",
                Icon = "💻"
            };
            
            // Sony - PlayStation, TVs
            _rules["Sony"] = new DeviceClassificationRule
            {
                VendorPatterns = new[] { "Sony" },
                DefaultDeviceType = "Console/TV",
                DefaultOS = "PlayStation OS",
                Icon = "🎮"
            };
            
            // LG - TVs
            _rules["LG"] = new DeviceClassificationRule
            {
                VendorPatterns = new[] { "LG" },
                DefaultDeviceType = "Smart TV",
                DefaultOS = "webOS",
                Icon = "📺"
            };
            
            // Google - Chromecast, Nest
            _rules["Google"] = new DeviceClassificationRule
            {
                VendorPatterns = new[] { "Google" },
                DefaultDeviceType = "Streaming Device",
                DefaultOS = "Android TV",
                Icon = "📡"
            };
            
            // Amazon - Echo, Fire TV
            _rules["Amazon"] = new DeviceClassificationRule
            {
                VendorPatterns = new[] { "Amazon" },
                DefaultDeviceType = "Smart Speaker/Streaming",
                DefaultOS = "Fire OS",
                Icon = "🔊"
            };
            
            // Raspberry Pi - IoT
            _rules["Raspberry Pi"] = new DeviceClassificationRule
            {
                VendorPatterns = new[] { "Raspberry Pi" },
                DefaultDeviceType = "Single Board Computer",
                DefaultOS = "Linux",
                Icon = "🔧"
            };
            
            // Nvidia - Shield TV
            _rules["Nvidia"] = new DeviceClassificationRule
            {
                VendorPatterns = new[] { "Nvidia" },
                DefaultDeviceType = "Streaming Device",
                DefaultOS = "Android TV",
                Icon = "📡"
            };
            
            // Roku - Streaming
            _rules["Roku"] = new DeviceClassificationRule
            {
                VendorPatterns = new[] { "Roku" },
                DefaultDeviceType = "Streaming Device",
                DefaultOS = "Roku OS",
                Icon = "📡"
            };
            
            // Canon - Printers
            _rules["Canon"] = new DeviceClassificationRule
            {
                VendorPatterns = new[] { "Canon" },
                DefaultDeviceType = "Printer",
                DefaultOS = "Printer OS",
                Icon = "🖨️"
            };
            
            // Epson - Printers
            _rules["Epson"] = new DeviceClassificationRule
            {
                VendorPatterns = new[] { "Epson" },
                DefaultDeviceType = "Printer",
                DefaultOS = "Printer OS",
                Icon = "🖨️"
            };
            
            // Brother - Printers
            _rules["Brother"] = new DeviceClassificationRule
            {
                VendorPatterns = new[] { "Brother" },
                DefaultDeviceType = "Printer",
                DefaultOS = "Printer OS",
                Icon = "🖨️"
            };
            
            // Philips - Hue, TVs
            _rules["Philips"] = new DeviceClassificationRule
            {
                VendorPatterns = new[] { "Philips" },
                DefaultDeviceType = "Smart Home/TV",
                DefaultOS = "IoT OS",
                Icon = "💡"
            };
            
            // Ring - Security Cameras
            _rules["Ring"] = new DeviceClassificationRule
            {
                VendorPatterns = new[] { "Ring" },
                DefaultDeviceType = "Security Camera",
                DefaultOS = "IoT OS",
                Icon = "📹"
            };
            
            // Nest - Smart Home
            _rules["Nest"] = new DeviceClassificationRule
            {
                VendorPatterns = new[] { "Nest" },
                DefaultDeviceType = "Smart Home Device",
                DefaultOS = "IoT OS",
                Icon = "🏠"
            };
        }
    }
    
    public enum DeviceCategory
    {
        DesktopPC,
        Notebook,
        Smartphone,
        Tablet,
        SmartTV,
        GameConsole,
        Printer,
        NetworkInfrastructure,
        IoT,
        NAS,
        StreamingDevice,
        SmartSpeaker,
        SecurityCamera,
        Server,
        Other
    }

    public class DeviceClassificationRule
    {
        public string[] VendorPatterns { get; set; }
        public string DefaultDeviceType { get; set; }
        public DeviceCategory DefaultCategory { get; set; } = DeviceCategory.Other;
        public string DefaultOS { get; set; }
        public string Icon { get; set; }
        public bool IsPortable { get; set; }
    }
    
    public class DeviceClassificationResult
    {
        public string DeviceType { get; set; }
        public DeviceCategory Category { get; set; } = DeviceCategory.Other;
        public string OperatingSystem { get; set; }
        public string Icon { get; set; }
        public int Confidence { get; set; }
        public bool IsPortable { get; set; }
        public string DeviceModel { get; set; }
    }
}
