namespace VoltrisOptimizer.Services.Performance.CpuTuning.Models
{
    /// <summary>
    /// Machine classification for adaptive tuning aggressiveness
    /// </summary>
    public enum MachineClass
    {
        /// <summary>
        /// Low-end hardware - conservative tuning only
        /// </summary>
        LowEnd,
        
        /// <summary>
        /// Entry-level hardware - conservative tuning
        /// </summary>
        Entry,
        
        /// <summary>
        /// Mid-range hardware - moderate tuning
        /// </summary>
        MidRange,
        
        /// <summary>
        /// High-end hardware - aggressive tuning allowed
        /// </summary>
        HighEnd,
        
        /// <summary>
        /// Enthusiast hardware - maximum tuning aggressiveness
        /// </summary>
        Enthusiast,
        
        /// <summary>
        /// Enterprise/Corporate - tuning disabled
        /// </summary>
        EnterpriseRestricted
    }
}
