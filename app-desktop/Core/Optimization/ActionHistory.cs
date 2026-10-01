using System;
using System.Collections.Generic;

public sealed class ActionHistory
{
	private readonly Dictionary<string, DateTime> _last = new Dictionary<string, DateTime>();

	public void Record(string action)
	{
		_last[action] = DateTime.UtcNow;
	}

	public void Mark(string action)
	{
		_last[action] = DateTime.UtcNow;
	}

	public bool ShouldSkip(string action, TimeSpan cooldown)
	{
		if (_last.TryGetValue(action, out var value))
		{
			return DateTime.UtcNow - value < cooldown;
		}
		return false;
	}
}
