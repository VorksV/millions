namespace VoltrisOptimizer.Core;

public class HardwareSummary
{
	public bool IsLaptop { get; set; }

	public bool HasSsd { get; set; }

	public double TotalRamGb { get; set; }
	
	// ✅ ADICIONADO: Propriedades de temperatura para V2 Architecture
	public float CpuTemperature { get; set; }
	
	public float GpuTemperature { get; set; }
}
