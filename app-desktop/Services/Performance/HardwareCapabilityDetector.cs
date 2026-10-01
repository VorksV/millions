using System;
using System.Management;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.Performance
{
    /// <summary>
    /// Detecta capacidades de hardware para validar suporte a features de performance
    /// CORREÇÃO: RISCO #4 - Validação de hardware suportado
    /// </summary>
    public class HardwareCapabilityDetector
    {
        private readonly ILoggingService _logger;
        private readonly System.Threading.Tasks.Task _detectionTask;
        private HardwareCapabilities _capabilities;

        public HardwareCapabilityDetector(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // Safe fallback defaults
            _capabilities = new HardwareCapabilities
            {
                Manufacturer = CpuManufacturer.Unknown,
                ProcessorName = "CPU Genérica",
                PhysicalCores = Math.Max(2, Environment.ProcessorCount / 2),
                LogicalProcessors = Environment.ProcessorCount,
                IntelGeneration = 0,
                AMDArchitecture = "Unknown",
                SupportsTurboBoost = false,
                SupportsHeteroPolicy = false,
                SupportsCoreParking = Environment.ProcessorCount >= 4,
                SupportsFrequencyScaling = true
            };

            // Iniciar detecção assíncrona em background
            _detectionTask = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var caps = DetectCapabilitiesInternal();
                    lock (this)
                    {
                        _capabilities = caps;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError("[HardwareDetector] Falha na detecção assíncrona", ex);
                }
            });
        }

        public HardwareCapabilities Capabilities
        {
            get
            {
                lock (this)
                {
                    return _capabilities;
                }
            }
        }

        private HardwareCapabilities DetectCapabilitiesInternal()
        {
            var caps = new HardwareCapabilities();

            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Processor"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                using var __dispose_obj = obj;
                        string manufacturer = obj["Manufacturer"]?.ToString() ?? "";
                        string name = obj["Name"]?.ToString() ?? "";
                        uint cores = Convert.ToUInt32(obj["NumberOfCores"] ?? 0);
                        uint logicalProcessors = Convert.ToUInt32(obj["NumberOfLogicalProcessors"] ?? 0);

                        caps.Manufacturer = DetermineManufacturer(manufacturer, name);
                        caps.ProcessorName = name;
                        caps.PhysicalCores = (int)cores;
                        caps.LogicalProcessors = (int)logicalProcessors;

                        // Detectar geração Intel
                        if (caps.Manufacturer == CpuManufacturer.Intel)
                        {
                            caps.IntelGeneration = DetectIntelGeneration(name);
                        }

                        // Detectar arquitetura AMD
                        if (caps.Manufacturer == CpuManufacturer.AMD)
                        {
                            caps.AMDArchitecture = DetectAMDArchitecture(name);
                        }

                        break; // Apenas primeiro processador
                    }
                }

                // Determinar suporte a features
                caps.SupportsTurboBoost = DetermineTurboBoostSupport(caps);
                caps.SupportsHeteroPolicy = DetermineHeteroPolicySupport(caps);
                caps.SupportsCoreParking = caps.PhysicalCores >= 4; // Mínimo 4 cores
                caps.SupportsFrequencyScaling = true; // Todos suportam

                _logger.LogInfo($"[HardwareDetector] CPU: {caps.ProcessorName}");
                _logger.LogInfo($"[HardwareDetector] Fabricante: {caps.Manufacturer}, Cores: {caps.PhysicalCores}");
                _logger.LogInfo($"[HardwareDetector] Turbo: {caps.SupportsTurboBoost}, Hetero: {caps.SupportsHeteroPolicy}");
            }
            catch (Exception ex)
            {
                _logger.LogError("[HardwareDetector] Erro ao detectar capacidades de hardware", ex);
                // Fallback seguro
                caps.Manufacturer = CpuManufacturer.Unknown;
                caps.SupportsTurboBoost = false;
                caps.SupportsHeteroPolicy = false;
                caps.SupportsCoreParking = false;
                caps.SupportsFrequencyScaling = true;
            }

            return caps;
        }

        private CpuManufacturer DetermineManufacturer(string manufacturer, string name)
        {
            string combined = (manufacturer + " " + name).ToUpperInvariant();

            if (combined.Contains("INTEL"))
                return CpuManufacturer.Intel;
            if (combined.Contains("AMD") || combined.Contains("ADVANCED MICRO DEVICES"))
                return CpuManufacturer.AMD;
            if (combined.Contains("ARM") || combined.Contains("QUALCOMM") || combined.Contains("SNAPDRAGON"))
                return CpuManufacturer.ARM;

            return CpuManufacturer.Unknown;
        }

        private int DetectIntelGeneration(string name)
        {
            // Intel Core i7-12700K -> 12th gen
            // Intel Core i5-10400 -> 10th gen
            if (name.Contains("Core"))
            {
                var parts = name.Split('-');
                if (parts.Length >= 2)
                {
                    string model = parts[1].Trim();
                    if (model.Length >= 2 && int.TryParse(model.Substring(0, 2), out int gen))
                    {
                        return gen;
                    }
                }
            }

            return 0; // Desconhecido
        }

        private string DetectAMDArchitecture(string name)
        {
            string upper = name.ToUpperInvariant();

            if (upper.Contains("RYZEN 7000") || upper.Contains("7950X") || upper.Contains("7900X"))
                return "Zen 4";
            if (upper.Contains("RYZEN 5000") || upper.Contains("5950X") || upper.Contains("5900X"))
                return "Zen 3";
            if (upper.Contains("RYZEN 3000") || upper.Contains("3950X") || upper.Contains("3900X"))
                return "Zen 2";
            if (upper.Contains("RYZEN 2000") || upper.Contains("2700X"))
                return "Zen+";
            if (upper.Contains("RYZEN 1000") || upper.Contains("1800X"))
                return "Zen";

            return "Unknown";
        }

        private bool DetermineTurboBoostSupport(HardwareCapabilities caps)
        {
            // Intel: Turbo Boost disponível em Core i5/i7/i9
            if (caps.Manufacturer == CpuManufacturer.Intel)
            {
                return caps.ProcessorName.Contains("Core i") || 
                       caps.ProcessorName.Contains("Xeon");
            }

            // AMD: Precision Boost disponível em Ryzen
            if (caps.Manufacturer == CpuManufacturer.AMD)
            {
                return caps.ProcessorName.Contains("Ryzen") || 
                       caps.ProcessorName.Contains("Threadripper");
            }

            // ARM: Geralmente não suporta
            return false;
        }

        private bool DetermineHeteroPolicySupport(HardwareCapabilities caps)
        {
            // Apenas Intel 12th gen+ (Alder Lake, Raptor Lake)
            if (caps.Manufacturer == CpuManufacturer.Intel)
            {
                return caps.IntelGeneration >= 12;
            }

            return false;
        }
    }

    /// <summary>
    /// Capacidades de hardware detectadas
    /// </summary>
    public class HardwareCapabilities
    {
        public CpuManufacturer Manufacturer { get; set; }
        public string ProcessorName { get; set; } = string.Empty;
        public int PhysicalCores { get; set; }
        public int LogicalProcessors { get; set; }
        public int IntelGeneration { get; set; }
        public string AMDArchitecture { get; set; } = string.Empty;

        // Features suportadas
        public bool SupportsTurboBoost { get; set; }
        public bool SupportsHeteroPolicy { get; set; }
        public bool SupportsCoreParking { get; set; }
        public bool SupportsFrequencyScaling { get; set; }
    }

    public enum CpuManufacturer
    {
        Unknown,
        Intel,
        AMD,
        ARM
    }
}
