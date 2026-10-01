using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace VoltrisOptimizer.Core;

public class OperationResult<T>
{
	public bool Success { get; private set; }

	public string? ErrorMessage { get; private set; }

	public string? Details { get; private set; }

	public Dictionary<string, object> Metadata { get; private set; }

	public DateTime Timestamp { get; private set; }

	public bool WasValidated { get; private set; }

	public T? Value { get; private set; }

	private OperationResult()
	{
		Metadata = new Dictionary<string, object>();
		Timestamp = DateTime.Now;
	}

	public static OperationResult<T> CreateSuccess(T value, string? details = null, bool validated = true)
	{
		if (!validated)
		{
			throw new InvalidOperationException("CRITICAL: Cannot create Success result without validation.");
		}
		return new OperationResult<T>
		{
			Success = true,
			Value = value,
			Details = details,
			WasValidated = true
		};
	}

	public static OperationResult<T> CreateFailure(string errorMessage, string? details = null)
	{
		return new OperationResult<T>
		{
			Success = false,
			ErrorMessage = errorMessage,
			Details = details,
			Value = default(T),
			WasValidated = true
		};
	}

	public OperationResult<T> WithMetadata(string key, object value)
	{
		Metadata[key] = value;
		return this;
	}

	public string GetFullMessage()
	{
		List<string> list = new List<string>();
		if (Success)
		{
			list.Add("✓ SUCCESS");
			if (!string.IsNullOrEmpty(Details))
			{
				list.Add("Details: " + Details);
			}
		}
		else
		{
			list.Add("✗ FAILURE");
			list.Add("Error: " + ErrorMessage);
			if (!string.IsNullOrEmpty(Details))
			{
				list.Add("Details: " + Details);
			}
		}
		if (Metadata.Any())
		{
			list.Add("Metadata: " + string.Join(", ", Metadata.Select<KeyValuePair<string, object>, string>(delegate(KeyValuePair<string, object> kv)
			{
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(1, 2);
				defaultInterpolatedStringHandler.AppendFormatted(kv.Key);
				defaultInterpolatedStringHandler.AppendLiteral("=");
				defaultInterpolatedStringHandler.AppendFormatted<object>(kv.Value);
				return defaultInterpolatedStringHandler.ToStringAndClear();
			})));
		}
		return string.Join(" | ", list);
	}
}
public class OperationResult
{
	public bool Success { get; private set; }

	public string? ErrorMessage { get; private set; }

	public string? Details { get; private set; }

	public Dictionary<string, object> Metadata { get; private set; }

	public DateTime Timestamp { get; private set; }

	public bool WasValidated { get; private set; }

	private OperationResult()
	{
		Metadata = new Dictionary<string, object>();
		Timestamp = DateTime.Now;
	}

	public static OperationResult CreateSuccess(string? details = null, bool validated = true)
	{
		if (!validated)
		{
			throw new InvalidOperationException("CRITICAL: Cannot create Success result without validation. This is a safeguard against false positives in production.");
		}
		return new OperationResult
		{
			Success = true,
			Details = details,
			WasValidated = true
		};
	}

	public static OperationResult CreateFailure(string errorMessage, string? details = null)
	{
		if (string.IsNullOrWhiteSpace(errorMessage))
		{
			errorMessage = "Unknown error occurred";
		}
		return new OperationResult
		{
			Success = false,
			ErrorMessage = errorMessage,
			Details = details,
			WasValidated = true
		};
	}

	public OperationResult WithMetadata(string key, object value)
	{
		Metadata[key] = value;
		return this;
	}

	public string GetFullMessage()
	{
		List<string> list = new List<string>();
		if (Success)
		{
			list.Add("✓ SUCCESS");
			if (!string.IsNullOrEmpty(Details))
			{
				list.Add("Details: " + Details);
			}
		}
		else
		{
			list.Add("✗ FAILURE");
			list.Add("Error: " + ErrorMessage);
			if (!string.IsNullOrEmpty(Details))
			{
				list.Add("Details: " + Details);
			}
		}
		if (Metadata.Any())
		{
			list.Add("Metadata: " + string.Join(", ", Metadata.Select<KeyValuePair<string, object>, string>(delegate(KeyValuePair<string, object> kv)
			{
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(1, 2);
				defaultInterpolatedStringHandler.AppendFormatted(kv.Key);
				defaultInterpolatedStringHandler.AppendLiteral("=");
				defaultInterpolatedStringHandler.AppendFormatted<object>(kv.Value);
				return defaultInterpolatedStringHandler.ToStringAndClear();
			})));
		}
		return string.Join(" | ", list);
	}

	public static OperationResult Combine(params OperationResult[] results)
	{
		if (results == null || results.Length == 0)
		{
			return CreateFailure("No results to combine");
		}
		List<OperationResult> list = results.Where((OperationResult r) => !r.Success).ToList();
		if (list.Any())
		{
			string text = string.Join("; ", list.Select((OperationResult f) => f.ErrorMessage));
			return CreateFailure("Multiple failures: " + text).WithMetadata("FailureCount", list.Count).WithMetadata("TotalCount", results.Length);
		}
		DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(25, 1);
		defaultInterpolatedStringHandler.AppendLiteral("All ");
		defaultInterpolatedStringHandler.AppendFormatted(results.Length);
		defaultInterpolatedStringHandler.AppendLiteral(" operations succeeded");
		return CreateSuccess(defaultInterpolatedStringHandler.ToStringAndClear());
	}
}
