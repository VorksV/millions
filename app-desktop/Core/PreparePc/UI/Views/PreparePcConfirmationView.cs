using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using VoltrisOptimizer.Core.PreparePc.Interfaces;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Core.PreparePc.UI.Views;

public partial class PreparePcConfirmationView : UserControl
{
	private readonly PreparePcManager _manager;

	private readonly PreCheckResult? _preCheckResult;

	private PreparePcOptions _options = new PreparePcOptions();


	public event EventHandler<PreparePcOptions>? OnProceed;

	public event EventHandler? OnSkip;

	public event EventHandler? OnSchedule;

	public PreparePcConfirmationView()
	{
		InitializeComponent();
		_manager = new PreparePcManager(App.LoggingService);
		base.Loaded += async delegate
		{
			ScrollViewer scrollViewer = FindParent<ScrollViewer>((DependencyObject)(object)this);
			if (scrollViewer != null)
			{
				scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
				scrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
			}
			await LoadPreChecksAsync();
		};
	}

	private T? FindParent<T>(DependencyObject child) where T : DependencyObject
	{
		DependencyObject parent = VisualTreeHelper.GetParent(child);
		if (parent == null)
		{
			return default(T);
		}
		T val = (T)(object)((parent is T) ? parent : null);
		if (val != null)
		{
			return val;
		}
		return FindParent<T>(parent);
	}

	public PreparePcConfirmationView(PreCheckResult preCheckResult)
		: this()
	{
		_preCheckResult = preCheckResult;
	}

	private async Task LoadPreChecksAsync()
	{
		try
		{
			PreCheckResult preCheckResult = _preCheckResult;
			PreCheckResult preCheckResult2 = preCheckResult;
			if (preCheckResult2 == null)
			{
				preCheckResult2 = await _manager.RunPreChecksAsync();
			}
			PreCheckResult result = preCheckResult2;
			if (!result.CanProceed)
			{
				MessageBox.Show(string.Format(LocalizationService.Instance.GetString("PreparePcPrereqWarning"), string.Join("\n", result.Errors)), LocalizationService.Instance.GetString("PreparePcSystemWarningTitle"), MessageBoxButton.OK, MessageBoxImage.Exclamation);
			}
		}
		catch (Exception ex2)
		{
			Exception ex = ex2;
			App.LoggingService?.LogError("[PreparePc] Erro ao carregar pré-checks", ex);
		}
	}

	private void StandardMode_Click(object sender, RoutedEventArgs e)
	{
		_options.Mode = PreparePcMode.Recommended;
		this.OnProceed?.Invoke(this, _options);
	}

	private void AdvancedMode_Click(object sender, RoutedEventArgs e)
	{
		_options.Mode = PreparePcMode.Full;
		this.OnProceed?.Invoke(this, _options);
	}

	private void BackButton_Click(object sender, RoutedEventArgs e)
	{
		this.OnSkip?.Invoke(this, EventArgs.Empty);
	}

	private string FormatTime(int seconds)
	{
		DefaultInterpolatedStringHandler defaultInterpolatedStringHandler;
		if (seconds < 60)
		{
			defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(1, 1);
			defaultInterpolatedStringHandler.AppendFormatted(seconds);
			defaultInterpolatedStringHandler.AppendLiteral("s");
			return defaultInterpolatedStringHandler.ToStringAndClear();
		}
		if (seconds < 3600)
		{
			defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(3, 1);
			defaultInterpolatedStringHandler.AppendFormatted(seconds / 60);
			defaultInterpolatedStringHandler.AppendLiteral("min");
			return defaultInterpolatedStringHandler.ToStringAndClear();
		}
		defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(5, 2);
		defaultInterpolatedStringHandler.AppendFormatted(seconds / 3600);
		defaultInterpolatedStringHandler.AppendLiteral("h ");
		defaultInterpolatedStringHandler.AppendFormatted(seconds % 3600 / 60);
		defaultInterpolatedStringHandler.AppendLiteral("min");
		return defaultInterpolatedStringHandler.ToStringAndClear();
	}
}
