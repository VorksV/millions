using System;
using Microsoft.Extensions.DependencyInjection;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Core;

public static class ServiceLocator
{
	private static IServiceProvider? _serviceProvider;

	private static readonly object _lock = new object();

	private static bool _isInitialized;

	public static bool IsInitialized => _isInitialized;

	public static ILoggingService? Logger => GetService<ILoggingService>();

	public static IRegistryService? Registry => GetService<IRegistryService>();

	public static IProcessRunner? ProcessRunner => GetService<IProcessRunner>();

	public static ISystemInfoService? SystemInfo => GetService<ISystemInfoService>();

	public static SystemSafetyService? SystemSafety => GetService<SystemSafetyService>();

	public static void Initialize(IServiceProvider serviceProvider)
	{
		lock (_lock)
		{
			if (_isInitialized)
			{
				throw new InvalidOperationException("ServiceLocator já foi inicializado.");
			}
			_serviceProvider = serviceProvider ?? throw new ArgumentNullException("serviceProvider");
			_isInitialized = true;
		}
	}

	public static T? GetService<T>() where T : class
	{
		App.LoggingService?.LogEntry($"GetService<{typeof(T).Name}>");
		if (!_isInitialized || _serviceProvider == null)
		{
			App.LoggingService?.LogCache("ServiceCache", "miss", $"ServiceLocator not initialized");
			return null;
		}
		var service = _serviceProvider.GetService<T>();
		if (service != null)
			App.LoggingService?.LogCache("ServiceCache", "hit", typeof(T).Name);
		else
			App.LoggingService?.LogCache("ServiceCache", "miss", typeof(T).Name);
		return service;
	}

	public static T GetRequiredService<T>() where T : class
	{
		if (!_isInitialized || _serviceProvider == null)
		{
			throw new InvalidOperationException("ServiceLocator não foi inicializado.");
		}
		return _serviceProvider.GetRequiredService<T>();
	}

	internal static void Reset()
	{
		lock (_lock)
		{
			_serviceProvider = null;
			_isInitialized = false;
		}
	}
}
