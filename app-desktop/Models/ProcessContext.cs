using System;
using System.Diagnostics;

namespace VoltrisOptimizer.Models
{
    /// <summary>
    /// DSL 5.0 - Process Context
    /// Mantém o histórico e metadados de um processo para decisões de governança.
    /// </summary>
    public class ProcessContext
    {
        public int Pid { get; set; }
        public string ProcessName { get; set; } = string.Empty;
        public DateTime LastSeen { get; set; }
        
        // Histórico de Performance
        public double LastCpuUsage { get; set; }
        public long LastWorkingSet { get; set; }
        public ProcessPriorityClass OriginalPriority { get; set; }
        public bool IsThrottled { get; set; }
        
        // Métricas de Comportamento
        public int SpikeCount { get; set; }
        public DateTime LastBoostTime { get; set; }

        public ProcessContext(int pid, string name)
        {
            Pid = pid;
            ProcessName = name;
            LastSeen = DateTime.Now;
        }
    }
}
