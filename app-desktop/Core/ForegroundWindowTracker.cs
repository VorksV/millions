using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;

namespace VoltrisOptimizer.Core;

public sealed class ForegroundWindowTracker : IDisposable
{
	private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

	private struct MSG
	{
		public IntPtr hwnd;

		public uint message;

		public IntPtr wParam;

		public IntPtr lParam;

		public uint time;

		public Point pt;
	}

	public static readonly ForegroundWindowTracker Instance = new ForegroundWindowTracker();

	private IntPtr _hookHandle = IntPtr.Zero;

	private WinEventDelegate? _delegateRef;

	private bool _started;

	private readonly object _lock = new object();

	private const uint EVENT_SYSTEM_FOREGROUND = 3u;

	private const uint WINEVENT_OUTOFCONTEXT = 0u;

	private const uint WINEVENT_SKIPOWNPROCESS = 2u;

	public int CurrentPid { get; private set; }

	public IntPtr CurrentHwnd { get; private set; }

	public event EventHandler<int>? ForegroundChanged;

	private ForegroundWindowTracker()
	{
	}

	public void Start()
	{
		lock (_lock)
		{
			if (_started)
			{
				return;
			}
			_started = true;
		}
		RefreshCurrent();
		Thread thread = new Thread(HookThread)
		{
			IsBackground = true,
			Name = "ForegroundHookThread"
		};
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
	}

	public void Stop()
	{
		lock (_lock)
		{
			if (_started)
			{
				_started = false;
				if (_hookHandle != IntPtr.Zero)
				{
					UnhookWinEvent(_hookHandle);
					_hookHandle = IntPtr.Zero;
				}
			}
		}
	}

	private void HookThread()
	{
		_delegateRef = OnWinEvent;
		_hookHandle = SetWinEventHook(3u, 3u, IntPtr.Zero, _delegateRef, 0u, 0u, 2u);
		if (_hookHandle != IntPtr.Zero)
		{
			MSG lpMsg;
			while (_started && GetMessage(out lpMsg, IntPtr.Zero, 0u, 0u) > 0)
			{
				TranslateMessage(ref lpMsg);
				DispatchMessage(ref lpMsg);
			}
		}
	}

	private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
	{
		if (hwnd == IntPtr.Zero)
		{
			return;
		}
		try
		{
			GetWindowThreadProcessId(hwnd, out var lpdwProcessId);
			if (lpdwProcessId != 0)
			{
				CurrentHwnd = hwnd;
				CurrentPid = (int)lpdwProcessId;
				this.ForegroundChanged?.Invoke(this, (int)lpdwProcessId);
			}
		}
		catch
		{
		}
	}

	private void RefreshCurrent()
	{
		try
		{
			IntPtr foregroundWindow = GetForegroundWindow();
			if (foregroundWindow == IntPtr.Zero)
			{
				CurrentHwnd = IntPtr.Zero;
				CurrentPid = 0;
			}
			else
			{
				GetWindowThreadProcessId(foregroundWindow, out var lpdwProcessId);
				CurrentHwnd = foregroundWindow;
				CurrentPid = (int)lpdwProcessId;
			}
		}
		catch
		{
		}
	}

	public void InvalidateStalePid()
	{
		RefreshCurrent();
	}

	public void Dispose()
	{
		Stop();
	}

	[DllImport("user32.dll")]
	private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

	[DllImport("user32.dll")]
	private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

	[DllImport("user32.dll")]
	private static extern IntPtr GetForegroundWindow();

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

	[DllImport("user32.dll")]
	private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

	[DllImport("user32.dll")]
	private static extern bool TranslateMessage(ref MSG lpMsg);

	[DllImport("user32.dll")]
	private static extern IntPtr DispatchMessage(ref MSG lpmsg);
}
