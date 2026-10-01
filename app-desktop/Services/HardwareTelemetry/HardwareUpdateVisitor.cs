using System.Collections.Generic;
using LibreHardwareMonitor.Hardware;

namespace VoltrisOptimizer.Services.HardwareTelemetry
{
    /// <summary>
    /// Visitor otimizado que atualiza e lê dados de todos os hardwares de forma eficiente,
    /// evitando o uso de LINQ (sem alocações extras por frame).
    /// </summary>
    public class HardwareUpdateVisitor : IVisitor
    {
        private readonly List<ISensor> _cpuSensors = new List<ISensor>();
        private readonly List<ISensor> _gpuSensors = new List<ISensor>();
        private readonly List<ISensor> _ramSensors = new List<ISensor>();
        private readonly List<ISensor> _motherboardSensors = new List<ISensor>();
        private readonly List<ISensor> _storageSensors = new List<ISensor>();
        
        private bool _isFirstSweep = true;

        public void VisitComputer(IComputer computer)
        {
            computer.Traverse(this);
            _isFirstSweep = false;
        }

        public void VisitHardware(IHardware hardware)
        {
            hardware.Update(); // Atualiza apenas os hardwares em nível de raiz
            foreach (IHardware subHardware in hardware.SubHardware)
            {
                subHardware.Accept(this);
            }
            // Na primeira varredura, cachamos a referência direta aos sensores, evitando
            // precisar iterar IHardware toda vez e usar LINQ para descobrir o tipo.
            if (_isFirstSweep)
            {
                switch (hardware.HardwareType)
                {
                    case HardwareType.Cpu:
                        foreach (var sensor in hardware.Sensors) _cpuSensors.Add(sensor);
                        break;
                    case HardwareType.GpuNvidia:
                    case HardwareType.GpuAmd:
                    case HardwareType.GpuIntel:
                        foreach (var sensor in hardware.Sensors) _gpuSensors.Add(sensor);
                        break;
                    case HardwareType.Memory:
                        foreach (var sensor in hardware.Sensors) _ramSensors.Add(sensor);
                        break;
                    case HardwareType.Motherboard:
                        foreach (var sensor in hardware.Sensors) _motherboardSensors.Add(sensor);
                        break;
                    case HardwareType.Storage:
                        foreach (var sensor in hardware.Sensors) _storageSensors.Add(sensor);
                        break;
                }
            }
        }

        public void VisitSensor(ISensor sensor) { }
        public void VisitParameter(IParameter parameter) { }

        /// <summary>
        /// Popula o snapshot atual de forma extrema-otimizada.
        /// </summary>
        public HardwareSnapshot GenerateSnapshot()
        {
            var snap = new HardwareSnapshot();
            
            // Variáveis locais para agregação
            double maxCpuTemp = double.NaN;
            double totalCpuUsage = double.NaN;
            double cpuPackagePower = double.NaN;
            double maxCpuClock = double.NaN;
            double cpuVoltage = double.NaN;

            double maxGpuTemp = double.NaN;
            double totalGpuUsage = double.NaN;
            double gpuCoreClock = double.NaN;
            double gpuMemoryClock = double.NaN;
            double gpuPower = double.NaN;
            double gpuMemoryUsed = double.NaN;
            double gpuMemoryTotal = double.NaN;

            double ramUsage = double.NaN;
            double ramAvailable = double.NaN;

            double maxSsdTemp = double.NaN;
            double mbTemp = double.NaN;
            double fanRpm = double.NaN;

            // CPU Process
            for (int i = 0; i < _cpuSensors.Count; i++)
            {
                var s = _cpuSensors[i];
                if (!s.Value.HasValue) continue;
                var val = s.Value.Value;

                if (s.SensorType == SensorType.Temperature)
                {
                    if (double.IsNaN(maxCpuTemp) || val > maxCpuTemp) maxCpuTemp = val;
                }
                else if (s.SensorType == SensorType.Load && s.Name.Contains("Total"))
                {
                    totalCpuUsage = val;
                }
                else if (s.SensorType == SensorType.Clock && s.Name.Contains("Core"))
                {
                    if (double.IsNaN(maxCpuClock) || val > maxCpuClock) maxCpuClock = val;
                }
                else if (s.SensorType == SensorType.Power && s.Name.Contains("Package"))
                {
                    cpuPackagePower = val;
                }
                else if (s.SensorType == SensorType.Voltage && (s.Name.Contains("Core") || s.Name.Contains("VID")))
                {
                    if (double.IsNaN(cpuVoltage) || val > cpuVoltage) cpuVoltage = val;
                }
            }

            // GPU Process
            for (int i = 0; i < _gpuSensors.Count; i++)
            {
                var s = _gpuSensors[i];
                if (!s.Value.HasValue) continue;
                var val = s.Value.Value;

                if (s.SensorType == SensorType.Temperature && s.Name.Contains("Core"))
                {
                    maxGpuTemp = val;
                }
                else if (s.SensorType == SensorType.Load && s.Name.Contains("Core"))
                {
                    totalGpuUsage = val;
                }
                else if (s.SensorType == SensorType.Clock && s.Name.Contains("Core"))
                {
                    gpuCoreClock = val;
                }
                else if (s.SensorType == SensorType.Clock && s.Name.Contains("Memory"))
                {
                    gpuMemoryClock = val;
                }
                else if (s.SensorType == SensorType.Power && s.Name.Contains("Total"))
                {
                    gpuPower = val;
                }
                else if (s.SensorType == SensorType.SmallData && s.Name.Contains("Memory Used"))
                {
                    gpuMemoryUsed = val;
                }
                else if (s.SensorType == SensorType.SmallData && s.Name.Contains("Memory Total"))
                {
                    gpuMemoryTotal = val;
                }
            }

            // RAM Process
            for (int i = 0; i < _ramSensors.Count; i++)
            {
                var s = _ramSensors[i];
                if (!s.Value.HasValue) continue;
                if (s.SensorType == SensorType.Load) ramUsage = s.Value.Value;
                if (s.SensorType == SensorType.Data && s.Name.Contains("Available")) ramAvailable = s.Value.Value;
            }

            // SSD Process
            for (int i = 0; i < _storageSensors.Count; i++)
            {
                var s = _storageSensors[i];
                if (!s.Value.HasValue) continue;
                if (s.SensorType == SensorType.Temperature)
                {
                    if (double.IsNaN(maxSsdTemp) || s.Value.Value > maxSsdTemp) maxSsdTemp = s.Value.Value;
                }
            }

            // Motherboard Process
            for (int i = 0; i < _motherboardSensors.Count; i++)
            {
                var s = _motherboardSensors[i];
                if (!s.Value.HasValue) continue;
                if (s.SensorType == SensorType.Temperature) mbTemp = s.Value.Value;
                if (s.SensorType == SensorType.Fan) fanRpm = s.Value.Value;
            }

            return new HardwareSnapshot
            {
                Timestamp = System.DateTime.UtcNow,
                CpuTemperature = maxCpuTemp,
                CpuUsage = totalCpuUsage,
                CpuClock = maxCpuClock,
                CpuPackagePower = cpuPackagePower,
                CpuVoltage = cpuVoltage,
                GpuTemperature = maxGpuTemp,
                GpuUsage = totalGpuUsage,
                GpuCoreClock = gpuCoreClock,
                GpuMemoryClock = gpuMemoryClock,
                GpuPower = gpuPower,
                GpuMemoryUsed = gpuMemoryUsed,
                GpuMemoryTotal = gpuMemoryTotal,
                RamUsage = ramUsage,
                RamAvailable = ramAvailable,
                SsdTemperature = maxSsdTemp,
                MotherboardTemperature = mbTemp,
                FanRpm = fanRpm
            };
        }
    }
}
