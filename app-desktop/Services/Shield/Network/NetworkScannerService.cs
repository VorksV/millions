using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.Shield.Network
{
    /// <summary>
    /// Mtodos de extensão para strings
    /// </summary>
    public static class StringExtensions
    {
        public static bool ContainsAny(this string source, params string[] values)
        {
            if (string.IsNullOrEmpty(source) || values == null || values.Length == 0)
                return false;

            return values.Any(value => !string.IsNullOrEmpty(value) && source.Contains(value, StringComparison.OrdinalIgnoreCase));
        }
    }
    public class NetworkScannerService
    {
        private readonly ILoggingService _logger;
        
        public NetworkScannerService(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        public async Task<NetworkRange> DetectLocalNetworkRangeAsync()
        {
            try
            {
                _logger.LogInfo("[NetworkScanner] Detectando range da rede local...");
                
                var gateway = GetDefaultGateway();
                if (gateway == null)
                {
                    _logger.LogWarning("[NetworkScanner] Gateway no encontrado");
                    return null;
                }
                
                var localIP = GetLocalIPAddress();
                if (localIP == null)
                {
                    _logger.LogWarning("[NetworkScanner] IP local no encontrado");
                    return null;
                }
                
                var range = CalculateNetworkRange(localIP);
                range.GatewayIP = gateway.ToString();
                
                _logger.LogSuccess($"[NetworkScanner] Range detectado: {range.StartIP} - {range.EndIP} (Gateway: {range.GatewayIP})");
                
                return range;
            }
            catch (Exception ex)
            {
                _logger.LogError("[NetworkScanner] Erro ao detectar range da rede", ex);
                return null;
            }
        }
        
        public async Task<List<NetworkDevice>> ScanNetworkAsync(NetworkRange range)
        {
            try
            {
                _logger.LogInfo($"[NetworkScanner] Iniciando scan da rede: {range.StartIP} - {range.EndIP}");
                
                var devices = new List<NetworkDevice>();
                var tasks = new List<Task<NetworkDevice>>();
                
                var startBytes = IPAddress.Parse(range.StartIP).GetAddressBytes();
                var endBytes = IPAddress.Parse(range.EndIP).GetAddressBytes();
                
                int start = startBytes[3];
                int end = endBytes[3];
                
                // Scan paralelo com limite de concorrncia
                var semaphore = new System.Threading.SemaphoreSlim(50);
                
                for (int i = start; i <= end; i++)
                {
                    var ip = $"{startBytes[0]}.{startBytes[1]}.{startBytes[2]}.{i}";
                    
                    tasks.Add(Task.Run(async () =>
                    {
                        await semaphore.WaitAsync();
                        try
                        {
                            return await ScanDeviceAsync(ip);
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }));
                }
                
                var results = await Task.WhenAll(tasks);
                devices = results.Where(d => d != null).ToList();
                
                _logger.LogSuccess($"[NetworkScanner] Scan concludo: {devices.Count} dispositivos encontrados");
                return devices;
            }
            catch (Exception ex)
            {
                _logger.LogError("[NetworkScanner] Erro no scan da rede", ex);
                return new List<NetworkDevice>();
            }
        }
        
        private async Task<NetworkDevice> ScanDeviceAsync(string ip)
        {
            try
            {
                // ETAPA 1: Ping para verificar se estáá online (mais rpido)
                bool isOnline = await PingHostAsync(ip);
                if (!isOnline)
                {
                    return null; // Dispositivo offline
                }
                
                _logger.LogDebug($"[NetworkScanner] Dispositivo online: {ip}");
                
                // ETAPA 2: Obter MAC address via ARP
                var macAddress = GetMacAddressFromARP(ip);
                
                // ETAPA 3: Criar dispositivo básico
                var device = new NetworkDevice
                {
                    IPAddress = ip,
                    MacAddress = macAddress ?? "Unknown",
                    IsOnline = true,
                    FirstSeen = DateTime.Now,
                    LastSeen = DateTime.Now
                };
                
                // Verificar se é o próprio computador
                var localIP = GetLocalIPAddress();
                if (localIP != null && ip == localIP.ToString())
                {
                    _logger.LogInfo($"[NetworkScanner] {ip} é o próprio computador, obtendo MAC do adaptador local");
                    device.MacAddress = GetLocalMacAddress();
                }
                
                try
                {
                    var hostEntry = await Dns.GetHostEntryAsync(ip);
                    if (!string.IsNullOrWhiteSpace(hostEntry.HostName) && !string.Equals(hostEntry.HostName, "Desconhecido", StringComparison.OrdinalIgnoreCase))
                    {
                        device.Hostname = hostEntry.HostName;
                        _logger.LogInfo($"[NetworkScanner] ✓ Hostname DNS encontrado para {ip}: {device.Hostname}");
                    }
                    else
                    {
                        device.Hostname = await GetNetBiosNameAsync(ip);
                        if (!string.IsNullOrWhiteSpace(device.Hostname))
                        {
                            _logger.LogInfo($"[NetworkScanner] ✓ Hostname NetBIOS encontrado para {ip}: {device.Hostname}");
                        }
                        else
                        {
                            device.Hostname = "Unknown";
                            device.FriendlyName = GenerateFriendlyName(device.MacAddress, device.Vendor);
                            _logger.LogInfo($"[NetworkScanner] ⚠ Hostname não resolvido para {ip}; usando nome sintético {device.FriendlyName}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    device.Hostname = "Unknown";
                    device.FriendlyName = GenerateFriendlyName(device.MacAddress, device.Vendor);
                    _logger.LogDebug($"[NetworkScanner] Hostname não resolvido para {ip}: {ex.Message}");
                }
                
                // ETAPA 4: Escaneamento de portas TCP para fingerprint
                try
                {
                    device.OpenPorts = await ProbeCommonTcpPortsAsync(ip);
                    if (device.OpenPorts.Count > 0)
                    {
                        _logger.LogInfo($"[NetworkScanner] Portas abertas em {ip}: {string.Join(", ", device.OpenPorts)}");
                        device.OperatingSystem = InferOsFromPorts(device.OpenPorts, device.OperatingSystem);
                    }
                }
                catch
                {
                    device.OpenPorts = new List<int>();
                }

                // DETEÇÃO INTELIGENTE DO TIPO DE DISPOSITIVO
                device.DeviceType = DetectDeviceType(device.Hostname, device.Vendor, device.MacAddress);
                if (string.Equals(device.OperatingSystem, "iOS", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(device.OperatingSystem, "Android", StringComparison.OrdinalIgnoreCase))
                {
                    device.DeviceType = "Mobile";
                }
                else if (string.Equals(device.OperatingSystem, "Windows", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(device.OperatingSystem, "Windows Server", StringComparison.OrdinalIgnoreCase))
                {
                    device.DeviceType = "Desktop";
                }
                device.DeviceCategory = DetectDeviceCategory(device.DeviceType);
                device.IsPortable = IsPortableDevice(device.DeviceCategory);

                // GARANTIR QUE DeviceType NUNCA SEJA NULO OU VAZIO
                if (string.IsNullOrEmpty(device.DeviceType) || device.DeviceType == "Unknown")
                {
                    device.DeviceType = "Unknown";
                }

                device.Icon = GetDeviceIcon(device.DeviceType);
                if (string.IsNullOrWhiteSpace(device.FriendlyName))
                {
                    device.FriendlyName = GenerateFriendlyName(device.MacAddress, device.Vendor);
                }

                return device;
            }
            catch
            {
                return null;
            }
        }
        
        private IPAddress GetDefaultGateway()
        {
            try
            {
                var gateway = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up)
                    .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .SelectMany(n => n.GetIPProperties()?.GatewayAddresses)
                    .Select(g => g?.Address)
                    .Where(a => a != null && a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    .FirstOrDefault();
                
                return gateway;
            }
            catch
            {
                return null;
            }
        }
        
        private IPAddress GetLocalIPAddress()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                var ip = host.AddressList
                    .FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork 
                                      && !IPAddress.IsLoopback(a));
                
                return ip;
            }
            catch
            {
                return null;
            }
        }
        
        private NetworkRange CalculateNetworkRange(IPAddress localIP)
        {
            var bytes = localIP.GetAddressBytes();
            
            // Assumir máscara /24 (255.255.255.0) para redes domésticas
            return new NetworkRange
            {
                StartIP = $"{bytes[0]}.{bytes[1]}.{bytes[2]}.1",
                EndIP = $"{bytes[0]}.{bytes[1]}.{bytes[2]}.254"
            };
        }
        
        private string GetMacAddressFromARP(string ipAddress)
        {
            try
            {
                _logger.LogInfo($"[NetworkScanner] Obtendo MAC address para {ipAddress}...");
                
                // Ping rápido para popular a tabela ARP antes de consultar
                try
                {
                    using var pingProc = new System.Diagnostics.Process
                    {
                        StartInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "ping",
                            Arguments = $"-n 1 -w 500 {ipAddress}",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            CreateNoWindow = true
                        }
                    };
                    pingProc.Start();
                    pingProc.WaitForExit(1500);
                }
                catch { }
                
                using var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "arp",
                        Arguments = "-a",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true,
                        StandardOutputEncoding = System.Text.Encoding.UTF8
                    }
                };
                
                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                
                _logger.LogDebug($"[NetworkScanner] ARP output completo:\n{output}");
                
                // Parse ARP output
                // Formato Windows: 192.168.1.1          00-11-22-33-44-55     dinâmico
                var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                
                foreach (var line in lines)
                {
                    // Verificar se a linha contém o IP que estávamos procurando
                    if (!line.Contains(ipAddress))
                        continue;
                    
                    _logger.LogDebug($"[NetworkScanner] Linha ARP encontrada para {ipAddress}: [{line}]");
                    
                    // Tentar extrair MAC address usando regex para maior precisão
                    var macPattern = @"([0-9A-Fa-f]{2}[-:]){5}([0-9A-Fa-f]{2})";
                    var match = System.Text.RegularExpressions.Regex.Match(line, macPattern);
                    
                    if (match.Success)
                    {
                        var mac = match.Value.Replace("-", ":").ToUpperInvariant();
                        _logger.LogSuccess($"[NetworkScanner] ✓ MAC encontrado para {ipAddress}: {mac}");
                        return mac;
                    }
                    
                    // Fallback: parsing manual
                    var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    _logger.LogDebug($"[NetworkScanner] Partes da linha: {string.Join(" | ", parts)}");
                    
                    foreach (var part in parts)
                    {
                        // Verificar se é um MAC address válido (formato XX-XX-XX-XX-XX-XX ou XX:XX:XX:XX:XX:XX)
                        if (part.Length >= 17 && (part.Contains("-") || part.Contains(":")))
                        {
                            var mac = part.Replace("-", ":").ToUpperInvariant();
                            
                            // Validar formato MAC (XX:XX:XX:XX:XX:XX)
                            var macParts = mac.Split(':');
                            if (macParts.Length == 6 && macParts.All(p => p.Length == 2 && p.All(c => "0123456789ABCDEF".Contains(c))))
                            {
                                _logger.LogSuccess($"[NetworkScanner] ✓ MAC encontrado (fallback) para {ipAddress}: {mac}");
                                return mac;
                            }
                        }
                    }
                }
                
                _logger.LogDebug($"[NetworkScanner] MAC não encontrado na tabela ARP para {ipAddress}");
                _logger.LogDebug($"[NetworkScanner] Dica: Execute 'ping {ipAddress}' primeiro para popular a tabela ARP");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[NetworkScanner] ❌ Erro ao obter MAC para {ipAddress}", ex);
            }
            
            return "Unknown";
        }
        
        /// <summary>
        /// Verifica se um host está online via ping
        /// </summary>
        private async Task<bool> PingHostAsync(string ipAddress)
        {
            try
            {
                using var ping = new System.Net.NetworkInformation.Ping();
                var reply = await ping.SendPingAsync(ipAddress, 1000); // 1 segundo timeout
                
                return reply.Status == IPStatus.Success;
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Obtém o MAC address do adaptador de rede local
        /// </summary>
        private string GetLocalMacAddress()
        {
            try
            {
                var localIP = GetLocalIPAddress();
                if (localIP == null)
                {
                    _logger.LogWarning("[NetworkScanner] Não foi possível obter IP local");
                    return "Unknown";
                }
                
                _logger.LogInfo($"[NetworkScanner] Procurando MAC do adaptador com IP {localIP}");
                
                // Obter todos os adaptadores de rede
                var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up)
                    .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback);
                
                foreach (var ni in interfaces)
                {
                    var ipProps = ni.GetIPProperties();
                    var unicastAddresses = ipProps.UnicastAddresses;
                    
                    // Verificar se este adaptador tem o IP local
                    foreach (var addr in unicastAddresses)
                    {
                        if (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                            addr.Address.Equals(localIP))
                        {
                            var mac = ni.GetPhysicalAddress().ToString();
                            
                            // Formatar MAC como XX:XX:XX:XX:XX:XX
                            if (mac.Length == 12)
                            {
                                var formattedMac = string.Join(":", Enumerable.Range(0, 6)
                                    .Select(i => mac.Substring(i * 2, 2)));
                                
                                _logger.LogSuccess($"[NetworkScanner] ✓ MAC local encontrado: {formattedMac} (Adaptador: {ni.Name})");
                                return formattedMac.ToUpperInvariant();
                            }
                        }
                    }
                }
                
                _logger.LogWarning("[NetworkScanner] ⚠ Não foi possível encontrar MAC do adaptador local");
            }
            catch (Exception ex)
            {
                _logger.LogError("[NetworkScanner] ❌ Erro ao obter MAC local", ex);
            }
            
            return "Unknown";
        }
        
        /// <summary>
        /// Tenta obter nome NetBIOS do dispositivo (fallback)
        /// </summary>
        private async Task<string> GetNetBiosNameAsync(string ipAddress)
        {
            try
            {
                // Usar nbtstat para tentar obter nome NetBIOS
                using var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "nbtstat",
                        Arguments = $"-A {ipAddress}",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true,
                        StandardOutputEncoding = System.Text.Encoding.UTF8
                    }
                };
                
                process.Start();
                var output = await process.StandardOutput.ReadToEndAsync();
                process.WaitForExit();
                
                // Parse do nbtstat para extrair nome
                var lines = output.Split('\n');
                foreach (var line in lines)
                {
                    if (line.Contains("<00>  UNIQUE") || line.Contains("<00>  GROUP"))
                    {
                        var name = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)[0];
                        if (!string.IsNullOrEmpty(name) && name != ipAddress)
                        {
                            _logger.LogInfo($"[NetworkScanner] ✓ Nome NetBIOS encontrado: {name}");
                            return name;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[NetworkScanner] Erro ao obter nome NetBIOS para {ipAddress}: {ex.Message}");
            }
            
            return null;
        }
        
        /// <summary>
        /// Escaneia portas TCP comuns para fingerprint de SO
        /// </summary>
        private async Task<List<int>> ProbeCommonTcpPortsAsync(string ipAddress)
        {
            var openPorts = new List<int>();
            var portsToScan = new[] { 22, 80, 443, 445, 3389, 5900, 8080, 8443, 62078, 23, 21, 25, 110, 143, 993, 587, 5222, 5223, 5353, 9090, 3306, 5432 };

            var tasks = portsToScan.Select(async port =>
            {
                using var timeoutCts = new CancellationTokenSource(300);
                try
                {
                    using var client = new TcpClient();
                    await client.ConnectAsync(ipAddress, port, timeoutCts.Token).ConfigureAwait(false);
                    if (client.Connected)
                    {
                        lock (openPorts) openPorts.Add(port);
                    }
                }
                catch (OperationCanceledException) { }
                catch (SocketException) { }
                catch (IOException) { }
            });

            await Task.WhenAll(tasks).ConfigureAwait(false);
            return openPorts;
        }

        /// <summary>
        /// Infere SO baseado nas portas abertas
        /// </summary>
        private string InferOsFromPorts(List<int> openPorts, string currentOs)
        {
            if (openPorts == null || openPorts.Count == 0)
                return currentOs;

            if (openPorts.Contains(445) && openPorts.Contains(3389))
                return "Windows Server";
            if (openPorts.Contains(3389) || openPorts.Contains(445) || openPorts.Contains(139))
                return "Windows";
            if (openPorts.Contains(62078))
                return "iOS";
            if (openPorts.Contains(22) && openPorts.Contains(443))
                return "Linux";
            if (openPorts.Contains(22) && !openPorts.Contains(443) && !openPorts.Contains(3389))
                return "Linux/Unix";
            if (openPorts.Contains(80) && openPorts.Contains(443) && !openPorts.Contains(3389) && !openPorts.Contains(22))
                return "Embedded OS";
            if (openPorts.Contains(8080) && openPorts.Contains(8443))
                return "Linux/Proxy";

            return currentOs;
        }

        /// <summary>
        /// Gera nome amigável baseado no vendor e MAC address
        /// </summary>
        private string GenerateFriendlyName(string macAddress, string vendor)
        {
            try
            {
                // Se tiver vendor conhecido, usar como base
                if (!string.IsNullOrEmpty(vendor) && vendor != "Unknown")
                {
                    var vendorName = vendor.Split(' ')[0]; // Pega primeira palavra do vendor
                    return $"{vendorName}-Device";
                }
                
                // Baseado no MAC address para identificação única
                if (!string.IsNullOrEmpty(macAddress) && macAddress != "Unknown")
                {
                    var lastBytes = macAddress.Split(':').TakeLast(2).ToArray();
                    return $"Device-{string.Join("", lastBytes)}";
                }
                
                return "Dispositivo-Rede";
            }
            catch
            {
                return "Dispositivo-Rede";
            }
        }
        
        /// <summary>
        /// Deteção inteligente do tipo de dispositivo baseado em hostname, vendor e MAC
        /// </summary>
        private string DetectDeviceType(string hostname, string vendor, string macAddress)
        {
            try
            {
                var hostnameLower = hostname?.ToLowerInvariant() ?? "";
                var vendorLower = vendor?.ToLowerInvariant() ?? "";
                
                // Roteadores/Gateways
                if (hostnameLower.ContainsAny("router", "gateway", "rt-", "tplink", "netgear", "dlink", "asus", "linksys") ||
                    vendorLower.ContainsAny("tp-link", "netgear", "d-link", "asus", "linksys", "cisco") ||
                    hostnameLower.EndsWith(".router") || hostnameLower.EndsWith(".gateway"))
                {
                    return "Router";
                }
                
                // Celulares/Smartphones (incluindo iPhones específicos)
                if (hostnameLower.ContainsAny("android", "samsung", "iphone", "xiaomi", "oppo", "vivo", "oneplus", "huawei", "motorola", "apple") ||
                    vendorLower.ContainsAny("samsung", "apple", "xiaomi", "oppo", "vivo", "oneplus", "huawei", "motorola") ||
                    hostnameLower.ContainsAny("phone", "mobile", "cell") ||
                    hostnameLower.StartsWith("iphone-") || hostnameLower.Contains("iphone"))
                {
                    return "Mobile";
                }
                
                // Notebooks/Laptops
                if (hostnameLower.ContainsAny("laptop", "notebook", "macbook", "ultrabook", "thinkpad", "latitude", "pavilion", "inspiron") ||
                    vendorLower.ContainsAny("dell", "hp", "lenovo", "apple", "asus", "acer", "msi") ||
                    hostnameLower.ContainsAny("lt-", "nb-"))
                {
                    return "Laptop";
                }
                
                // Tablets
                if (hostnameLower.ContainsAny("ipad", "tablet", "galaxy tab", "surface") ||
                    hostnameLower.ContainsAny("tb-", "tab-"))
                {
                    return "Tablet";
                }
                
                // Smart TVs
                if (hostnameLower.ContainsAny("tv", "television", "samsungtv", "lgwebos", "bravia") ||
                    hostnameLower.ContainsAny("smart-tv", "androidtv"))
                {
                    return "SmartTV";
                }
                
                // Dispositivos IoT
                if (hostnameLower.ContainsAny("iot", "sensor", "camera", "doorbell", "thermostat", "light", "switch") ||
                    hostnameLower.ContainsAny("esp32", "arduino", "raspberry"))
                {
                    return "IoT";
                }
                
                // Servidores
                if (hostnameLower.ContainsAny("server", "srv", "dc", "domain", "exchange", "sql", "oracle") ||
                    hostnameLower.StartsWith("srv-") || hostnameLower.StartsWith("dc-"))
                {
                    return "Server";
                }
                
                // Desktops (default para computadores)
                if (hostnameLower.ContainsAny("desktop", "pc", "workstation", "tower") ||
                    vendorLower.ContainsAny("intel", "amd", "nvidia"))
                {
                    return "Desktop";
                }
                
                // Placas de rede/Adaptadores
                if (hostnameLower.ContainsAny("ethernet", "nic", "adapter", "network") ||
                    vendorLower.ContainsAny("realtek", "broadcom", "intel", "killer"))
                {
                    return "NetworkCard";
                }
                
                // Impressoras
                if (hostnameLower.ContainsAny("printer", "hp-printer", "brother", "canon", "epson"))
                {
                    return "Printer";
                }
                
                // Consôle de jogos
                if (hostnameLower.ContainsAny("xbox", "playstation", "ps4", "ps5", "nintendo", "switch"))
                {
                    return "GamingConsole";
                }
                
                return "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }
        
        /// <summary>
        /// Retorna ícone emoji moderno e específico para o tipo de dispositivo detectado
        /// </summary>
        private string GetDeviceIcon(string deviceType)
        {
            return deviceType switch
            {
                "Router" => "📡",
                "Mobile" => "📱",
                "Laptop" => "💻",
                "Tablet" => "📋",
                "SmartTV" => "📺",
                "IoT" => "🏠",
                "Server" => "🖥️",
                "Desktop" => "🖥️",
                "NetworkCard" => "🔌",
                "Printer" => "🖨️",
                "GamingConsole" => "🎮",
                "Unknown" => "🔍",    // Lupa para dispositivos desconhecidos
                _ => "?"            // Fallback garantido
            };
        }

        /// <summary>
        /// Mapeia DeviceType para DeviceCategory
        /// </summary>
        private string DetectDeviceCategory(string deviceType)
        {
            return deviceType switch
            {
                "Desktop" => "DesktopPC",
                "Laptop" => "Notebook",
                "Mobile" => "Smartphone",
                "Tablet" => "Tablet",
                "SmartTV" => "SmartTV",
                "GamingConsole" => "GameConsole",
                "Printer" => "Printer",
                "Router" => "NetworkInfrastructure",
                "Server" => "Server",
                "IoT" => "IoT",
                "NetworkCard" => "NetworkInfrastructure",
                "NAS" => "NAS",
                _ => "Other"
            };
        }

        /// <summary>
        /// Determina se o dispositivo é portátil
        /// </summary>
        private bool IsPortableDevice(string category)
        {
            return category == "Notebook" || category == "Smartphone" || category == "Tablet";
        }
    }
    
    public class NetworkRange
    {
        public string StartIP { get; set; }
        public string EndIP { get; set; }
        public string GatewayIP { get; set; }
    }
    
    public class NetworkDevice : INetworkDevice
    {
        public string IPAddress { get; set; }
        public string MacAddress { get; set; }
        public string Hostname { get; set; }
        public string Vendor { get; set; }
        public string DeviceType { get; set; } = "Unknown";
        public string DeviceCategory { get; set; } = "Other";
        public string DeviceModel { get; set; }
        public string OperatingSystem { get; set; } = "Unknown";
        public string FriendlyName { get; set; }
        public string Icon { get; set; } = "🖥️";
        public bool IsGateway { get; set; }
        public bool IsOnline { get; set; }
        public bool IsPortable { get; set; }
        public List<int> OpenPorts { get; set; } = new List<int>();
        public DateTime FirstSeen { get; set; }
        public DateTime LastSeen { get; set; }
        public bool IsNew { get; set; }
        public int ConfidenceLevel { get; set; } = 0;
    }
}
