using System;
using System.Threading;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Models
{
    public enum PressureLevel { Low, Medium, High, Critical }

    /// <summary>
    /// DSL 5.0 - Proactive System State
    /// Snapshot sincronizado de toda a telemetria do kernel.
    /// Thread-safe: campos numéricos usam volatile/Interlocked para evitar race conditions.
    /// </summary>
    public class SystemState50
    {
        // Métricas de Pressão e Latência (Kernel)
        // PressureLevel é enum (int-backed), seguro para leitura/escrita atômica em x86/x64
        private volatile int _cpuPressure;
        private volatile int _memoryPressure;
        private volatile int _ioPressure;
        
        public PressureLevel CpuPressure 
        { 
            get => (PressureLevel)_cpuPressure; 
            set => _cpuPressure = (int)value; 
        }
        public PressureLevel MemoryPressure 
        { 
            get => (PressureLevel)_memoryPressure; 
            set => _memoryPressure = (int)value; 
        }
        public PressureLevel IoPressure 
        { 
            get => (PressureLevel)_ioPressure; 
            set => _ioPressure = (int)value; 
        }
        
        // Floats: volatile não garante atomicidade em 32-bit para float,
        // mas em .NET x64 leituras/escritas de float são atômicas.
        // Usamos volatile para garantir visibilidade entre threads.
        private volatile float _cpuQueueLength;
        private volatile float _dpcTime;
        private volatile float _interruptTime;
        private volatile float _hardPageFaults;
        private volatile float _diskQueueLength;
        private volatile float _diskActiveTime;
        private volatile float _contextSwitchesPerSec;
        
        public float CpuQueueLength { get => _cpuQueueLength; set => _cpuQueueLength = value; }
        public float DpcTime { get => _dpcTime; set => _dpcTime = value; }
        public float InterruptTime { get => _interruptTime; set => _interruptTime = value; }
        public float HardPageFaults { get => _hardPageFaults; set => _hardPageFaults = value; }
        public float DiskQueueLength { get => _diskQueueLength; set => _diskQueueLength = value; }
        public float DiskActiveTime { get => _diskActiveTime; set => _diskActiveTime = value; }
        public float ContextSwitchesPerSec { get => _contextSwitchesPerSec; set => _contextSwitchesPerSec = value; }
        
        // Recursos e Estado — double é 64-bit, usar Interlocked para atomicidade
        private long _availableRamMbBits;
        private long _cpuUsagePercentBits;
        
        public double AvailableRamMb 
        { 
            get => BitConverter.Int64BitsToDouble(Interlocked.Read(ref _availableRamMbBits)); 
            set => Interlocked.Exchange(ref _availableRamMbBits, BitConverter.DoubleToInt64Bits(value)); 
        }
        public double CpuUsagePercent 
        { 
            get => BitConverter.Int64BitsToDouble(Interlocked.Read(ref _cpuUsagePercentBits)); 
            set => Interlocked.Exchange(ref _cpuUsagePercentBits, BitConverter.DoubleToInt64Bits(value)); 
        }
        
        private volatile uint _commitChargePercent;
        public uint CommitChargePercent { get => _commitChargePercent; set => _commitChargePercent = value; }
        
        private long _lastInputActivityMs;
        public long LastInputActivityMs 
        { 
            get => Interlocked.Read(ref _lastInputActivityMs); 
            set => Interlocked.Exchange(ref _lastInputActivityMs, value); 
        }
        
        // Contexto — booleans e ints são atômicos em .NET
        private volatile bool _inputLatencyRisk;
        private volatile bool _thermalThrottlingDetected;
        private volatile int _foregroundPid;
        
        public bool InputLatencyRisk { get => _inputLatencyRisk; set => _inputLatencyRisk = value; }
        public bool ThermalThrottlingDetected { get => _thermalThrottlingDetected; set => _thermalThrottlingDetected = value; }
        public int ForegroundPid { get => _foregroundPid; set => _foregroundPid = value; }
        public IntelligentProfileType CurrentProfile { get; set; }
    }
}
