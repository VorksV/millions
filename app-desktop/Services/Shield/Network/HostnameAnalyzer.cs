using System;
using System.Collections.Generic;
using System.Linq;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.Shield.Network
{
    /// <summary>
    /// Analisador de hostname para inferir tipo de dispositivo e sistema operacional
    /// </summary>
    public class HostnameAnalyzer
    {
        private readonly ILoggingService _logger;
        private readonly List<HostnamePattern> _patterns;
        
        public HostnameAnalyzer(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _patterns = new List<HostnamePattern>();
            InitializePatterns();
        }
        
        /// <summary>
        /// Analisa o hostname para extrair informações do dispositivo
        /// </summary>
        public HostnameAnalysisResult AnalyzeHostname(string hostname)
        {
            if (string.IsNullOrEmpty(hostname) || hostname == "Unknown")
                return null;
            
            var hostnameLower = hostname.ToLowerInvariant();
            
            // Verificar padrões conhecidos
            foreach (var pattern in _patterns)
            {
                if (pattern.Keywords.Any(k => hostnameLower.Contains(k.ToLowerInvariant())))
                {
                    return new HostnameAnalysisResult
                    {
                        DeviceType = pattern.DeviceType,
                        OperatingSystem = pattern.OperatingSystem,
                        Confidence = pattern.Confidence
                    };
                }
            }
            
            // Análise heurística adicional
            return PerformHeuristicAnalysis(hostnameLower);
        }
        
        private HostnameAnalysisResult PerformHeuristicAnalysis(string hostname)
        {
            // Windows naming patterns: DESKTOP-XXXX
            if (hostname.StartsWith("desktop-"))
            {
                return new HostnameAnalysisResult
                {
                    DeviceType = "Desktop",
                    OperatingSystem = "Windows",
                    Confidence = 20
                };
            }

            // Windows naming patterns: WORKSTATION-XXXX
            if (hostname.StartsWith("workstation-"))
            {
                return new HostnameAnalysisResult
                {
                    DeviceType = "Desktop",
                    OperatingSystem = "Windows",
                    Confidence = 20
                };
            }

            // Generic PC
            if (hostname.StartsWith("pc-"))
            {
                return new HostnameAnalysisResult
                {
                    DeviceType = "Desktop",
                    OperatingSystem = "Windows",
                    Confidence = 15
                };
            }

            if (hostname.StartsWith("laptop-") || hostname.StartsWith("nb-"))
            {
                return new HostnameAnalysisResult
                {
                    DeviceType = "Laptop",
                    OperatingSystem = "Windows",
                    Confidence = 15
                };
            }

            // Mac naming patterns: MacBook-Pro-XXXX, MacBook-Air-XXXX, iMac-XXXX
            if (hostname.StartsWith("macbook-pro-") || hostname.StartsWith("macbook-air-"))
            {
                return new HostnameAnalysisResult
                {
                    DeviceType = "Laptop",
                    OperatingSystem = "macOS",
                    Confidence = 25
                };
            }

            if (hostname.StartsWith("macbook-"))
            {
                return new HostnameAnalysisResult
                {
                    DeviceType = "Laptop",
                    OperatingSystem = "macOS",
                    Confidence = 20
                };
            }

            if (hostname.StartsWith("imac-") || hostname.StartsWith("mac-pro-") || hostname.StartsWith("macmini"))
            {
                return new HostnameAnalysisResult
                {
                    DeviceType = "Desktop",
                    OperatingSystem = "macOS",
                    Confidence = 25
                };
            }

            // Linux patterns
            if (hostname.Contains("ubuntu") || hostname.Contains("debian") || hostname.Contains("fedora") || hostname.Contains("arch") || hostname.Contains("manjaro"))
            {
                return new HostnameAnalysisResult
                {
                    DeviceType = "Linux PC",
                    OperatingSystem = "Linux",
                    Confidence = 20
                };
            }

            // Samsung model patterns: SM-XXXX, GT-XXXX, SC-XXXX
            if (System.Text.RegularExpressions.Regex.IsMatch(hostname, @"^(sm-|gt-|sc-)[a-z0-9]+"))
            {
                return new HostnameAnalysisResult
                {
                    DeviceType = "Smartphone",
                    OperatingSystem = "Android",
                    Confidence = 30
                };
            }

            // Xiaomi model patterns
            if (System.Text.RegularExpressions.Regex.IsMatch(hostname, @"^(mi |mi-|redmi|poco)"))
            {
                return new HostnameAnalysisResult
                {
                    DeviceType = "Smartphone",
                    OperatingSystem = "Android",
                    Confidence = 25
                };
            }

            // Huawei model patterns
            if (System.Text.RegularExpressions.Regex.IsMatch(hostname, @"^(lya-|ele-|vog-|mar-|clt-)"))
            {
                return new HostnameAnalysisResult
                {
                    DeviceType = "Smartphone",
                    OperatingSystem = "HarmonyOS/Android",
                    Confidence = 25
                };
            }

            // OnePlus model patterns
            if (hostname.StartsWith("oneplus") || System.Text.RegularExpressions.Regex.IsMatch(hostname, @"^kb\d+"))
            {
                return new HostnameAnalysisResult
                {
                    DeviceType = "Smartphone",
                    OperatingSystem = "Android",
                    Confidence = 25
                };
            }

            // Motorola model patterns
            if (hostname.StartsWith("moto") || System.Text.RegularExpressions.Regex.IsMatch(hostname, @"^xt\d+"))
            {
                return new HostnameAnalysisResult
                {
                    DeviceType = "Smartphone",
                    OperatingSystem = "Android",
                    Confidence = 25
                };
            }

            // LG phone patterns
            if (hostname.StartsWith("lg-") || hostname.StartsWith("lm-"))
            {
                return new HostnameAnalysisResult
                {
                    DeviceType = "Smartphone",
                    OperatingSystem = "Android",
                    Confidence = 20
                };
            }

            // Nokia patterns
            if (hostname.StartsWith("ta-"))
            {
                return new HostnameAnalysisResult
                {
                    DeviceType = "Smartphone",
                    OperatingSystem = "Android",
                    Confidence = 20
                };
            }

            // Generic patterns: DESKTOP-XXXXXX (Windows auto-generated)
            if (hostname.Length >= 12 && hostname.All(c => char.IsLetterOrDigit(c) || c == '-'))
            {
                return new HostnameAnalysisResult
                {
                    DeviceType = "PC",
                    OperatingSystem = "Windows",
                    Confidence = 10
                };
            }

            return null;
        }
        
        private void InitializePatterns()
        {
            // Windows Desktop
            _patterns.Add(new HostnamePattern
            {
                Keywords = new[] { "desktop", "pc", "workstation" },
                DeviceType = "Desktop",
                OperatingSystem = "Windows",
                Confidence = 25
            });

            // Windows Laptop
            _patterns.Add(new HostnamePattern
            {
                Keywords = new[] { "laptop", "notebook", "nb-" },
                DeviceType = "Laptop",
                OperatingSystem = "Windows",
                Confidence = 20
            });

            // Apple iPhone
            _patterns.Add(new HostnamePattern
            {
                Keywords = new[] { "iphone", "iphone-de-", "iphone-da-", "iphone de", "iphone da" },
                DeviceType = "Smartphone",
                OperatingSystem = "iOS",
                Confidence = 25
            });

            // Apple iPad
            _patterns.Add(new HostnamePattern
            {
                Keywords = new[] { "ipad" },
                DeviceType = "Tablet",
                OperatingSystem = "iPadOS",
                Confidence = 25
            });

            // Apple MacBook
            _patterns.Add(new HostnamePattern
            {
                Keywords = new[] { "macbook" },
                DeviceType = "Laptop",
                OperatingSystem = "macOS",
                Confidence = 25
            });

            // Apple Mac Pro/Mac Mini/Mac Studio
            _patterns.Add(new HostnamePattern
            {
                Keywords = new[] { "mac-pro-", "macmini", "mac-studio-" },
                DeviceType = "Desktop",
                OperatingSystem = "macOS",
                Confidence = 25
            });

            // Apple iMac
            _patterns.Add(new HostnamePattern
            {
                Keywords = new[] { "imac", "mac-" },
                DeviceType = "Desktop",
                OperatingSystem = "macOS",
                Confidence = 25
            });

            // Android (genérico)
            _patterns.Add(new HostnamePattern
            {
                Keywords = new[] { "android", "pixel" },
                DeviceType = "Smartphone",
                OperatingSystem = "Android",
                Confidence = 20
            });

            // Samsung Galaxy smartphones
            _patterns.Add(new HostnamePattern
            {
                Keywords = new[] { "galaxy", "sm-", "gt-", "sc-" },
                DeviceType = "Smartphone",
                OperatingSystem = "Android",
                Confidence = 25
            });

            // Xiaomi
            _patterns.Add(new HostnamePattern
            {
                Keywords = new[] { "xiaomi", "mi ", "mi-", "redmi", "poco", "miui" },
                DeviceType = "Smartphone",
                OperatingSystem = "Android",
                Confidence = 25
            });

            // Huawei
            _patterns.Add(new HostnamePattern
            {
                Keywords = new[] { "huawei", "lya-", "ele-", "vog-", "mate", "p30", "p40", "p50" },
                DeviceType = "Smartphone",
                OperatingSystem = "HarmonyOS/Android",
                Confidence = 25
            });

            // OnePlus
            _patterns.Add(new HostnamePattern
            {
                Keywords = new[] { "oneplus", "kb200", "op-" },
                DeviceType = "Smartphone",
                OperatingSystem = "Android",
                Confidence = 25
            });

            // Motorola
            _patterns.Add(new HostnamePattern
            {
                Keywords = new[] { "moto ", "moto-", "xt" },
                DeviceType = "Smartphone",
                OperatingSystem = "Android",
                Confidence = 25
            });

            // LG smartphone
            _patterns.Add(new HostnamePattern
            {
                Keywords = new[] { "lg-", "lm-" },
                DeviceType = "Smartphone",
                OperatingSystem = "Android",
                Confidence = 20
            });

            // Nokia
            _patterns.Add(new HostnamePattern
            {
                Keywords = new[] { "ta-" },
                DeviceType = "Smartphone",
                OperatingSystem = "Android",
                Confidence = 20
            });

            // Router/Gateway
            _patterns.Add(new HostnamePattern
            {
                Keywords = new[] { "router", "gateway", "modem" },
                DeviceType = "Router",
                OperatingSystem = "Router OS",
                Confidence = 25
            });

            // Smart TV
            _patterns.Add(new HostnamePattern
            {
                Keywords = new[] { "tv", "smarttv", "television", "samsungtv", "lgwebos", "bravia" },
                DeviceType = "Smart TV",
                OperatingSystem = "TV OS",
                Confidence = 20
            });

            // Raspberry Pi
            _patterns.Add(new HostnamePattern
            {
                Keywords = new[] { "raspberrypi", "raspberry", "rpi" },
                DeviceType = "Single Board Computer",
                OperatingSystem = "Linux",
                Confidence = 25
            });

            // Linux Server
            _patterns.Add(new HostnamePattern
            {
                Keywords = new[] { "server", "srv", "ubuntu", "debian", "centos", "fedora", "linux" },
                DeviceType = "Server",
                OperatingSystem = "Linux",
                Confidence = 20
            });

            // PlayStation
            _patterns.Add(new HostnamePattern
            {
                Keywords = new[] { "playstation", "ps4", "ps5", "ps-" },
                DeviceType = "Game Console",
                OperatingSystem = "PlayStation OS",
                Confidence = 25
            });

            // Xbox
            _patterns.Add(new HostnamePattern
            {
                Keywords = new[] { "xbox" },
                DeviceType = "Game Console",
                OperatingSystem = "Xbox OS",
                Confidence = 25
            });

            // Nintendo Switch
            _patterns.Add(new HostnamePattern
            {
                Keywords = new[] { "nintendo", "switch" },
                DeviceType = "Game Console",
                OperatingSystem = "Nintendo OS",
                Confidence = 25
            });

            // Chromecast
            _patterns.Add(new HostnamePattern
            {
                Keywords = new[] { "chromecast" },
                DeviceType = "Streaming Device",
                OperatingSystem = "Android TV",
                Confidence = 25
            });

            // Amazon Echo
            _patterns.Add(new HostnamePattern
            {
                Keywords = new[] { "echo", "alexa" },
                DeviceType = "Smart Speaker",
                OperatingSystem = "Fire OS",
                Confidence = 25
            });

            // Fire TV
            _patterns.Add(new HostnamePattern
            {
                Keywords = new[] { "firetv", "fire-tv" },
                DeviceType = "Streaming Device",
                OperatingSystem = "Fire OS",
                Confidence = 25
            });

            // Roku
            _patterns.Add(new HostnamePattern
            {
                Keywords = new[] { "roku" },
                DeviceType = "Streaming Device",
                OperatingSystem = "Roku OS",
                Confidence = 25
            });

            // Apple TV
            _patterns.Add(new HostnamePattern
            {
                Keywords = new[] { "appletv", "apple-tv" },
                DeviceType = "Streaming Device",
                OperatingSystem = "tvOS",
                Confidence = 25
            });

            // Printer
            _patterns.Add(new HostnamePattern
            {
                Keywords = new[] { "printer", "print", "canon", "epson", "hp-", "brother", "xerox" },
                DeviceType = "Printer",
                OperatingSystem = "Printer OS",
                Confidence = 20
            });

            // NAS (Network Attached Storage)
            _patterns.Add(new HostnamePattern
            {
                Keywords = new[] { "nas", "storage", "synology", "qnap", "wd-" },
                DeviceType = "NAS",
                OperatingSystem = "NAS OS",
                Confidence = 20
            });

            // IP Camera
            _patterns.Add(new HostnamePattern
            {
                Keywords = new[] { "camera", "cam", "ipcam", "cam-", "dvr", "nvr" },
                DeviceType = "IP Camera",
                OperatingSystem = "Camera OS",
                Confidence = 20
            });

            // IoT devices (genérico)
            _patterns.Add(new HostnamePattern
            {
                Keywords = new[] { "iot", "sensor", "smartplug", "bulb", "thermostat", "esp32", "esp8266", "arduino" },
                DeviceType = "IoT Device",
                OperatingSystem = "IoT OS",
                Confidence = 20
            });

            // Tesla
            _patterns.Add(new HostnamePattern
            {
                Keywords = new[] { "tesla", "model-3", "model-s", "model-x", "model-y" },
                DeviceType = "Electric Vehicle",
                OperatingSystem = "Tesla OS",
                Confidence = 25
            });
        }
    }
    
    public class HostnamePattern
    {
        public string[] Keywords { get; set; }
        public string DeviceType { get; set; }
        public string OperatingSystem { get; set; }
        public int Confidence { get; set; }
    }
    
    public class HostnameAnalysisResult
    {
        public string DeviceType { get; set; }
        public string OperatingSystem { get; set; }
        public int Confidence { get; set; }
    }
}
