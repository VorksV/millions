using System.Collections.Generic;

namespace VoltrisOptimizer.Core.Brain.V2;

public class HistoryFile
{
	public List<BrainSessionRecord> Sessions { get; set; } = new List<BrainSessionRecord>();

}
