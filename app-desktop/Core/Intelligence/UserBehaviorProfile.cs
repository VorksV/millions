using System;

namespace VoltrisOptimizer.Core.Intelligence;

public class UserBehaviorProfile
{
	public UserCategory Category { get; set; } = UserCategory.Casual;


	public double AggressionScore { get; set; } = 50.0;


	public double TotalGamingHours { get; set; }

	public double TotalWorkHours { get; set; }

	public int TotalSessions { get; set; }

	public DateTime LastCalculated { get; set; } = DateTime.UtcNow;


	public double GamingRatio => (TotalSessions == 0) ? 0.0 : (TotalGamingHours / (TotalGamingHours + TotalWorkHours + 1.0));
}
