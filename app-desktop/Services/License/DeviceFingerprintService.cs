using System;
using System.Management;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace VoltrisOptimizer.Services.License
{
    /// <summary>
    /// Serviço de fingerprint de dispositivo - gera ID único baseado em hardware
    /// </summary>
    public class DeviceFingerprintService
    {
        private static DeviceFingerprintService? _instance;
        public static DeviceFingerprintService Instance => _instance ??= new DeviceFingerprintService();
        
        private string? _cachedDeviceId;
        
        private DeviceFingerprintService() 
        {
            App.LoggingService?.LogTrace("[LICENSE] Serviço de Fingerprint inicializado");
        }
        
        /// <summary>
        /// Obtém o Device ID único do dispositivo (hash SHA256)
        /// </summary>
        public string GetDeviceId()
        {
            if (!string.IsNullOrEmpty(_cachedDeviceId))
                return _cachedDeviceId;
            
            App.LoggingService?.LogInfo("[LICENSE] Gerando novo Device ID único...");
            var fingerprint = new StringBuilder();
            
            // 1. Nome da máquina
            fingerprint.Append(GetMachineName());
            
            // 2. CPU ID
            fingerprint.Append(GetCpuId());
            
            // 3. GPU ID
            fingerprint.Append(GetGpuId());
            
            // 4. Serial do disco
            fingerprint.Append(GetDiskSerial());
            
            // 5. Windows GUID
            fingerprint.Append(GetWindowsGuid());
            
            // 6. Motherboard Serial
            fingerprint.Append(GetMotherboardSerial());
            
            // Gerar hash SHA256
            _cachedDeviceId = ComputeSHA256Hash(fingerprint.ToString());
            App.LoggingService?.LogSuccess($"[LICENSE] Device ID gerado com sucesso: {_cachedDeviceId.Substring(0, 8)}...");
            
            return _cachedDeviceId;
        }
        
        /// <summary>
        /// Obtém um Device ID curto (primeiros 32 caracteres)
        /// </summary>
        public string GetShortDeviceId()
        {
            return GetDeviceId().Substring(0, 32).ToUpperInvariant();
        }
        
        /// <summary>
        /// Obtém informações detalhadas do dispositivo
        /// </summary>
        public DeviceInfo GetDeviceInfo()
        {
            return new DeviceInfo
            {
                DeviceId = GetDeviceId(),
                MachineName = Environment.MachineName,
                OSVersion = Environment.OSVersion.VersionString,
                ProcessorCount = Environment.ProcessorCount,
                Is64Bit = Environment.Is64BitOperatingSystem,
                UserName = Environment.UserName
            };
        }
        
        private string GetMachineName()
        {
            try
            {
                return Environment.MachineName ?? "UNKNOWN";
            }
            catch
            {
                return "UNKNOWN";
            }
        }
        
        private string GetCpuId()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT ProcessorId FROM Win32_Processor");
                foreach (ManagementObject obj in searcher.Get())
                {
                using var __dispose_obj = obj;
                    return obj["ProcessorId"]?.ToString() ?? "";
                }
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogWarning($"[LICENSE] Falha ao obter CPU ID via WMI: {ex.Message}");
            }
            return "";
        }
        
        private string GetGpuId()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT PNPDeviceID FROM Win32_VideoController");
                foreach (ManagementObject obj in searcher.Get())
                {
                using var __dispose_obj = obj;
                    return obj["PNPDeviceID"]?.ToString() ?? "";
                }
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogWarning($"[LICENSE] Falha ao obter GPU ID via WMI: {ex.Message}");
            }
            return "";
        }
        
        private string GetDiskSerial()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_DiskDrive WHERE Index=0");
                foreach (ManagementObject obj in searcher.Get())
                {
                using var __dispose_obj = obj;
                    return obj["SerialNumber"]?.ToString()?.Trim() ?? "";
                }
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogWarning($"[LICENSE] Falha ao obter Serial do Disco via WMI: {ex.Message}");
            }
            return "";
        }
        
        private string GetWindowsGuid()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
                return key?.GetValue("MachineGuid")?.ToString() ?? "";
            }
            catch { }
            return "";
        }
        
        private string GetMotherboardSerial()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_BaseBoard");
                foreach (ManagementObject obj in searcher.Get())
                {
                using var __dispose_obj = obj;
                    return obj["SerialNumber"]?.ToString() ?? "";
                }
            }
            catch { }
            return "";
        }
        
        private string ComputeSHA256Hash(string input)
        {
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }
    }
    
    /// <summary>
    /// Informações do dispositivo
    /// </summary>
    public class DeviceInfo
    {
        public string DeviceId { get; set; } = "";
        public string MachineName { get; set; } = "";
        public string OSVersion { get; set; } = "";
        public int ProcessorCount { get; set; }
        public bool Is64Bit { get; set; }
        public string UserName { get; set; } = "";
    }
}

