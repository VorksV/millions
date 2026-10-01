using System;
using System.Runtime.CompilerServices;
using VoltrisOptimizer.Core.Body;

namespace VoltrisOptimizer.Core.Intelligence;

public class TemporalPattern
{
	public DayOfWeek DayOfWeek { get; set; }

	public int Hour { get; set; }

	public OperationalContext Context { get; set; }

	public int Occurrences { get; set; }

	public double Probability { get; set; }

	public DateTime LastUpdated { get; set; } = DateTime.UtcNow;


	public string Key
	{
		get
		{
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(2, 3);
			defaultInterpolatedStringHandler.AppendFormatted(DayOfWeek);
			defaultInterpolatedStringHandler.AppendLiteral("-");
			defaultInterpolatedStringHandler.AppendFormatted(Hour);
			defaultInterpolatedStringHandler.AppendLiteral("-");
			defaultInterpolatedStringHandler.AppendFormatted(Context);
			return defaultInterpolatedStringHandler.ToStringAndClear();
		}
	}
}
