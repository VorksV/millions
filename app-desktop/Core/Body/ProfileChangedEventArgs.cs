using System;
using VoltrisOptimizer.Core.Brain.V2;

namespace VoltrisOptimizer.Core.Body;

public sealed class ProfileChangedEventArgs : EventArgs
{
	public VoltrisBrainV2.ProfileType OldProfile { get; init; }

	public VoltrisBrainV2.ProfileType NewProfile { get; init; }

	public string Reason { get; init; } = string.Empty;

}
