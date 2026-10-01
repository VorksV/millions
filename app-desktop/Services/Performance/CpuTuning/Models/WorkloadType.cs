namespace VoltrisOptimizer.Services.Performance.CpuTuning.Models
{
    /// <summary>
    /// Workload classification for intelligent tuning decisions
    /// </summary>
    public enum WorkloadType
    {
        /// <summary>
        /// CPU usage > 88%, GPU usage < 75%
        /// </summary>
        CpuBound,
        
        /// <summary>
        /// GPU usage > 92%, CPU usage < 65%
        /// </summary>
        GpuBound,
        
        /// <summary>
        /// Both CPU and GPU usage > 80%
        /// </summary>
        Balanced,
        
        /// <summary>
        /// RAM usage > 90%
        /// </summary>
        MemoryBound,
        
        /// <summary>
        /// CPU usage < 30% for > 10 seconds
        /// </summary>
        Idle
    }
}
