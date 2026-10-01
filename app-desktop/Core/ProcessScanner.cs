using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using VoltrisOptimizer.Core.Native;

namespace VoltrisOptimizer.Core;

public class ProcessScanner
{
	public struct ProcessSample
	{
		public int ProcessId;

		public string ProcessName;

		public double CpuPercent;

		public Process? GetProcess()
		{
			try
			{
				return Process.GetProcessById(ProcessId);
			}
			catch (ArgumentException)
			{
				return null;
			}
		}
	}

	private readonly Dictionary<int, long> _cpuTicksCache = new Dictionary<int, long>();

	public List<ProcessSample> Scan(TimeSpan elapsed)
	{
		List<ProcessSample> list = new List<ProcessSample>();
		int num = 65536;
		IntPtr intPtr = Marshal.AllocHGlobal(num);
		try
		{
			int ReturnLength;
			int num2 = HardeningNativeMethods.NtQuerySystemInformation(5, intPtr, num, out ReturnLength);
			if (num2 == -1073741820)
			{
				Marshal.FreeHGlobal(intPtr);
				num = ReturnLength;
				intPtr = Marshal.AllocHGlobal(num);
				num2 = HardeningNativeMethods.NtQuerySystemInformation(5, intPtr, num, out var _);
			}
			if (num2 != 0)
			{
				return list;
			}
			double num3 = elapsed.TotalMilliseconds * 10000.0;
			if (num3 <= 0.0)
			{
				num3 = 1.0;
			}
			nint num4 = intPtr;
			while (true)
			{
				HardeningNativeMethods.SYSTEM_PROCESS_INFORMATION sYSTEM_PROCESS_INFORMATION = Marshal.PtrToStructure<HardeningNativeMethods.SYSTEM_PROCESS_INFORMATION>(num4);
				int num5 = sYSTEM_PROCESS_INFORMATION.UniqueProcessId.ToInt32();
				if (num5 != 0)
				{
					try
					{
						long num6 = sYSTEM_PROCESS_INFORMATION.UserTime + sYSTEM_PROCESS_INFORMATION.KernelTime;
						if (_cpuTicksCache.TryGetValue(num5, out var value))
						{
							double value2 = (double)(num6 - value) / num3 * 100.0 / (double)Environment.ProcessorCount;
							string processName = "";
							try
							{
								processName = ((sYSTEM_PROCESS_INFORMATION.ImageName.Length > 0) ? (Marshal.PtrToStringUni(sYSTEM_PROCESS_INFORMATION.ImageName.Buffer, (int)sYSTEM_PROCESS_INFORMATION.ImageName.Length / 2) ?? "") : "");
							}
							catch
							{
							}
							list.Add(new ProcessSample
							{
								ProcessId = num5,
								ProcessName = processName,
								CpuPercent = Math.Clamp(value2, 0.0, 100.0)
							});
						}
						_cpuTicksCache[num5] = num6;
					}
					catch
					{
					}
				}
				if (sYSTEM_PROCESS_INFORMATION.NextEntryOffset == 0)
				{
					break;
				}
				num4 = (nint)(num4 + sYSTEM_PROCESS_INFORMATION.NextEntryOffset);
			}
		}
		finally
		{
			if (intPtr != IntPtr.Zero)
			{
				Marshal.FreeHGlobal(intPtr);
			}
		}
		if (_cpuTicksCache.Count > list.Count * 2)
		{
			_cpuTicksCache.Clear();
		}
		return list;
	}
}
