using System;
using System.CodeDom.Compiler;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Core.PreparePc.UI.Views;

public partial class PreparePcIntroView : UserControl
{
	public ObservableCollection<UsageMetric> UsageMetrics { get; set; } = new ObservableCollection<UsageMetric>();


	public ObservableCollection<KeyFeature> KeyFeatures { get; set; } = new ObservableCollection<KeyFeature>();


	public event EventHandler? OnStartRequested;

	public event EventHandler? OnSkip;

	public event EventHandler? OnSchedule;

	public PreparePcIntroView()
	{
		InitializeComponent();
		base.DataContext = this;
		LoadDefaultData();
		AnimateEntrance();
	}

	private void LoadDefaultData()
	{
		UsageMetrics.Add(new UsageMetric
		{
			IconData = "M21,16.5C21,16.88 20.79,17.21 20.47,17.38L12.57,21.82C12.41,21.94 12.21,22 12,22C11.79,22 11.59,21.94 11.43,21.82L3.53,17.38C3.21,17.21 3,16.88 3,16.5V7.5C3,7.12 3.21,6.79 3.53,6.62L11.43,2.18C11.59,2.06 11.79,2 12,2C12.21,2 12.41,2.06 12.57,2.18L20.47,6.62C20.79,6.79 21,7.12 21,7.5V16.5Z",
			Value = "92",
			Unit = "%",
			Label = "SISTEMA"
		});
		UsageMetrics.Add(new UsageMetric
		{
			IconData = "M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2M12,4A8,8 0 0,1 20,12A8,8 0 0,1 12,20A8,8 0 0,1 4,12A8,8 0 0,1 12,4M13,7H11V13H17V11H13V7Z",
			Value = "2.4",
			Unit = "GB",
			Label = "MEMÓRIA"
		});
		UsageMetrics.Add(new UsageMetric
		{
			IconData = "M7,2V5H10V2H7M14,2V5H17V2H14M21,2V5H24V2H21M7,8V11H10V8H7M14,8V11H17V8H14M21,8V11H24V8H21M2,13V15H22V13H2M2,18V20H22V18H2",
			Value = "15",
			Unit = "min",
			Label = "TEMPO"
		});
		KeyFeatures.Add(new KeyFeature
		{
			IconData = "M11,15H13V17H11V15M11,7H13V13H11V7M12,2C6.47,2 2,6.47 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2M12,20A8,8 0 0,1 4,12A8,8 0 0,1 12,4A8,8 0 0,1 20,12A8,8 0 0,1 12,20Z",
			Title = "Reparo Profundo",
			Description = "Restaura arquivos corrompidos do sistema e otimiza a integridade do Windows."
		});
		KeyFeatures.Add(new KeyFeature
		{
			IconData = "M13,10h-2V8h2V10zM13,16h-2v-4h2V16zM12,2C6.48,2,2,6.48,2,12s4.48,10,10,10s10-4.48,10-10S17.52,2,12,2z M12,20c-4.41,0-8-3.59-8-8s3.59-8,8-8s8,3.59,8,8S16.41,20,12,20z",
			Title = "Limpeza Inteligente",
			Description = "Remove lixo eletrônico, arquivos temporários e resíduos de atualizações antigas."
		});
	}

	private void AnimateEntrance()
	{
		DoubleAnimation animation = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(500.0))
		{
			EasingFunction = new CubicEase
			{
				EasingMode = EasingMode.EaseOut
			}
		};
		BeginAnimation(UIElement.OpacityProperty, animation);
	}

	private void StartButton_Click(object sender, RoutedEventArgs e)
	{
		this.OnStartRequested?.Invoke(this, EventArgs.Empty);
	}

	private void SkipButton_Click(object sender, RoutedEventArgs e)
	{
		this.OnSkip?.Invoke(this, EventArgs.Empty);
	}

	private void ScheduleButton_Click(object sender, RoutedEventArgs e)
	{
		ShowScheduleDialog();
	}

	private void ShowScheduleDialog()
	{
		Window dialog = new Window
		{
			Title = "Agendar Prepare PC",
			Width = 480.0,
			MinHeight = 380.0,
			WindowStartupLocation = WindowStartupLocation.CenterScreen,
			Owner = Window.GetWindow((DependencyObject)(object)this),
			WindowStyle = WindowStyle.None,
			AllowsTransparency = true,
			Background = Brushes.Transparent,
			ResizeMode = ResizeMode.NoResize,
			Topmost = true
		};
		dialog.SizeToContent = SizeToContent.Height;
		Border border = new Border
		{
			Background = new SolidColorBrush(Color.FromRgb(10, 10, 15)),
			BorderBrush = new SolidColorBrush(Color.FromRgb(30, 30, 46)),
			BorderThickness = new Thickness(1.0),
			CornerRadius = new CornerRadius(16.0),
			Padding = new Thickness(24.0)
		};
		StackPanel stackPanel = new StackPanel
		{
			Margin = new Thickness(0.0, 0.0, 0.0, 4.0)
		};
		StackPanel stackPanel2 = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Margin = new Thickness(0.0, 0.0, 0.0, 20.0)
		};
		System.Windows.Shapes.Path element = new System.Windows.Shapes.Path
		{
			Data = Geometry.Parse("M19,19H5V8H19M16,1V3H8V1H6V3H5C3.89,3 3,3.89 3,5V19A2,2 0 0,0 5,21H19A2,2 0 0,0 21,19V5C21,3.89 20.1,3 19,3H18V1M17,12H12V17H17V12Z"),
			Fill = (Brush)(TryFindResource("PrimaryBrush") ?? Brushes.RoyalBlue),
			Width = 24.0,
			Height = 24.0,
			Stretch = Stretch.Uniform,
			Margin = new Thickness(0.0, 0.0, 12.0, 0.0),
			VerticalAlignment = VerticalAlignment.Center
		};
		stackPanel2.Children.Add(element);
		StackPanel stackPanel3 = new StackPanel();
		stackPanel3.Children.Add(new TextBlock
		{
			Text = "Agendar Prepare PC",
			FontSize = 18.0,
			FontWeight = FontWeights.SemiBold,
			Foreground = (Brush)(TryFindResource("TextPrimaryBrush") ?? Brushes.White)
		});
		stackPanel3.Children.Add(new TextBlock
		{
			Text = "Escolha quando executar a preparação",
			FontSize = 12.0,
			Foreground = (Brush)(TryFindResource("TextMutedBrush") ?? Brushes.Gray),
			Margin = new Thickness(0.0, 4.0, 0.0, 0.0)
		});
		stackPanel2.Children.Add(stackPanel3);
		stackPanel.Children.Add(stackPanel2);
		StackPanel stackPanel4 = new StackPanel
		{
			Margin = new Thickness(0.0, 0.0, 0.0, 20.0)
		};
		Border option1 = CreateScheduleOption("M12,2L13.09,8.26L22,9L13.09,9.74L12,16L10.91,9.74L2,9L10.91,8.26L12,2Z", "Na próxima inicialização", "O Prepare PC será executado quando você abrir o Voltris novamente", isSelected: true);
		stackPanel4.Children.Add(option1);
		Border option2 = CreateScheduleOption("M12,20A8,8 0 0,0 20,12A8,8 0 0,0 12,4A8,8 0 0,0 4,12A8,8 0 0,0 12,20M12,2A10,10 0 0,1 22,12A10,10 0 0,1 12,22C6.47,22 2,17.5 2,12A10,10 0 0,1 12,2M12.5,7V12.25L17,14.92L16.25,16.15L11,13V7H12.5Z", "Em algumas horas", "Agende para executar em 2, 4 ou 8 horas", isSelected: false);
		stackPanel4.Children.Add(option2);
		Border option3 = CreateScheduleOption("M19,19H5V8H19M16,1V3H8V1H6V3H5C3.89,3 3,3.89 3,5V19A2,2 0 0,0 5,21H19A2,2 0 0,0 21,19V5C21,3.89 20.1,3 19,3H18V1M17,12H12V17H17V12Z", "Data e hora específica", "Escolha exatamente quando executar", isSelected: false);
		stackPanel4.Children.Add(option3);
		stackPanel.Children.Add(stackPanel4);
		StackPanel hoursPanel = new StackPanel
		{
			Visibility = Visibility.Collapsed,
			Margin = new Thickness(0.0, 0.0, 0.0, 16.0)
		};
		TextBlock element2 = new TextBlock
		{
			Text = "Selecione o intervalo:",
			Foreground = (Brush)(TryFindResource("TextSecondaryBrush") ?? Brushes.LightGray),
			FontSize = 13.0,
			Margin = new Thickness(0.0, 0.0, 0.0, 8.0)
		};
		hoursPanel.Children.Add(element2);
		StackPanel stackPanel5 = new StackPanel
		{
			Orientation = Orientation.Horizontal
		};
		Button hours2Btn = CreateHoursButton("2 horas");
		Button hours4Btn = CreateHoursButton("4 horas");
		Button hours8Btn = CreateHoursButton("8 horas");
		stackPanel5.Children.Add(hours2Btn);
		stackPanel5.Children.Add(hours4Btn);
		stackPanel5.Children.Add(hours8Btn);
		hoursPanel.Children.Add(stackPanel5);
		stackPanel.Children.Add(hoursPanel);
		StackPanel datePanel = new StackPanel
		{
			Visibility = Visibility.Collapsed,
			Margin = new Thickness(0.0, 0.0, 0.0, 16.0)
		};
		DatePicker datePicker = new DatePicker
		{
			SelectedDate = DateTime.Now.AddDays(1.0),
			SelectedDateFormat = DatePickerFormat.Short,
			Background = Brushes.White,
			Foreground = Brushes.Black,
			BorderBrush = (Brush)(TryFindResource("DarkBorderBrush") ?? Brushes.Gray),
			Margin = new Thickness(0.0, 0.0, 0.0, 8.0)
		};
		datePicker.Loaded += delegate
		{
			try
			{
				TextBox textBox = FindVisualChild<TextBox>((DependencyObject)(object)datePicker);
				if (textBox != null)
				{
					textBox.Background = Brushes.White;
					textBox.Foreground = Brushes.Black;
					textBox.Padding = new Thickness(8.0, 4.0, 8.0, 4.0);
				}
			}
			catch
			{
			}
		};
		datePanel.Children.Add(datePicker);
		stackPanel.Children.Add(datePanel);
		int selectedOption = 0;
		int selectedHours = 2;
		option1.MouseDown += delegate
		{
			selectedOption = 0;
			UpdateOptionSelection(option1, option2, option3, 0);
			hoursPanel.Visibility = Visibility.Collapsed;
			datePanel.Visibility = Visibility.Collapsed;
		};
		option2.MouseDown += delegate
		{
			selectedOption = 1;
			UpdateOptionSelection(option1, option2, option3, 1);
			hoursPanel.Visibility = Visibility.Visible;
			datePanel.Visibility = Visibility.Collapsed;
		};
		option3.MouseDown += delegate
		{
			selectedOption = 2;
			UpdateOptionSelection(option1, option2, option3, 2);
			hoursPanel.Visibility = Visibility.Collapsed;
			datePanel.Visibility = Visibility.Visible;
		};
		hours2Btn.Click += delegate
		{
			selectedHours = 2;
			UpdateHoursSelection(hours2Btn, hours4Btn, hours8Btn, 0);
		};
		hours4Btn.Click += delegate
		{
			selectedHours = 4;
			UpdateHoursSelection(hours2Btn, hours4Btn, hours8Btn, 1);
		};
		hours8Btn.Click += delegate
		{
			selectedHours = 8;
			UpdateHoursSelection(hours2Btn, hours4Btn, hours8Btn, 2);
		};
		StackPanel stackPanel6 = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			HorizontalAlignment = HorizontalAlignment.Right,
			Margin = new Thickness(0.0, 16.0, 0.0, 0.0)
		};
		Button button = new Button
		{
			Content = "Cancelar",
			Padding = new Thickness(20.0, 10.0, 20.0, 10.0),
			Cursor = Cursors.Hand,
			Margin = new Thickness(0.0, 0.0, 10.0, 0.0),
			FontFamily = new FontFamily("Segoe UI Variable, Segoe UI")
		};
		button.Style = (Style)TryFindResource("NeonSecondaryButtonStyle");
		button.Click += delegate
		{
			dialog.Close();
		};
		Button button2 = new Button
		{
			Padding = new Thickness(24.0, 12.0, 24.0, 12.0),
			FontWeight = FontWeights.SemiBold,
			FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"),
			Cursor = Cursors.Hand
		};
		button2.Style = (Style)TryFindResource("NeonButtonStyle");
		StackPanel stackPanel7 = new StackPanel
		{
			Orientation = Orientation.Horizontal
		};
		System.Windows.Shapes.Path element3 = new System.Windows.Shapes.Path
		{
			Data = Geometry.Parse("M9,16.17L4.83,12L3.41,13.41L9,19L21,7L19.59,5.59L9,16.17Z"),
			Fill = Brushes.White,
			Width = 16.0,
			Height = 16.0,
			Stretch = Stretch.Uniform,
			Margin = new Thickness(0.0, 0.0, 8.0, 0.0),
			VerticalAlignment = VerticalAlignment.Center
		};
		stackPanel7.Children.Add(element3);
		stackPanel7.Children.Add(new TextBlock
		{
			Text = "Confirmar Agendamento",
			VerticalAlignment = VerticalAlignment.Center
		});
		button2.Content = stackPanel7;
		button2.Click += delegate
		{
			DateTime scheduledTime = selectedOption switch
			{
				0 => DateTime.MinValue, 
				1 => DateTime.Now.AddHours(selectedHours), 
				2 => datePicker.SelectedDate ?? DateTime.Now.AddDays(1.0), 
				_ => DateTime.MinValue};
			SaveSchedule(scheduledTime, selectedOption);
			dialog.Close();
			ShowScheduleConfirmation(scheduledTime, selectedOption);
			this.OnSchedule?.Invoke(this, EventArgs.Empty);
		};
		stackPanel6.Children.Add(button);
		stackPanel6.Children.Add(button2);
		stackPanel.Children.Add(stackPanel6);
		ScrollViewer scrollViewer = (ScrollViewer)(border.Child = new ScrollViewer
		{
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
			Content = stackPanel
		});
		dialog.Content = border;
		border.MouseDown += delegate(object s, MouseButtonEventArgs e)
		{
			if (e.ChangedButton == MouseButton.Left)
			{
				dialog.DragMove();
			}
		};
		dialog.ShowDialog();
	}

	private void SaveSchedule(DateTime scheduledTime, int scheduleType)
	{
		try
		{
			string path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Voltris", "preparepc_schedule.json");
			ScheduleData value = new ScheduleData
			{
				ScheduledTime = scheduledTime,
				ScheduleType = scheduleType,
				CreatedAt = DateTime.Now,
				IsNextStartup = (scheduleType == 0)
			};
			string directoryName = System.IO.Path.GetDirectoryName(path);
			if (!string.IsNullOrEmpty(directoryName))
			{
				Directory.CreateDirectory(directoryName);
			}
			string contents = JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true,
				ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
				PropertyNamingPolicy = JsonNamingPolicy.CamelCase
			});
			File.WriteAllText(path, contents);
			ILoggingService? loggingService = App.LoggingService;
			if (loggingService != null)
			{
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(43, 2);
				defaultInterpolatedStringHandler.AppendLiteral("[PreparePc] Agendamento salvo: Type=");
				defaultInterpolatedStringHandler.AppendFormatted(scheduleType);
				defaultInterpolatedStringHandler.AppendLiteral(", Time=");
				defaultInterpolatedStringHandler.AppendFormatted(scheduledTime);
				loggingService!.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
			}
			if (scheduleType == 1 || scheduleType == 2)
			{
				CreateWindowsScheduledTask(scheduledTime);
			}
		}
		catch (Exception exception)
		{
			App.LoggingService?.LogError("[PreparePc] Erro ao salvar agendamento", exception);
		}
	}

	private void CreateWindowsScheduledTask(DateTime scheduledTime)
	{
		try
		{
			string value = Assembly.GetExecutingAssembly().Location.Replace(".dll", ".exe");
			string value2 = "VoltrisPreparePc";
			ProcessStartInfo obj = new ProcessStartInfo
			{
				FileName = "schtasks"
			};
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(59, 4);
			defaultInterpolatedStringHandler.AppendLiteral("/create /tn \"");
			defaultInterpolatedStringHandler.AppendFormatted(value2);
			defaultInterpolatedStringHandler.AppendLiteral("\" /tr \"\\\"");
			defaultInterpolatedStringHandler.AppendFormatted(value);
			defaultInterpolatedStringHandler.AppendLiteral("\\\" --preparepc\" /sc once /st ");
			defaultInterpolatedStringHandler.AppendFormatted(scheduledTime, "HH:mm");
			defaultInterpolatedStringHandler.AppendLiteral(" /sd ");
			defaultInterpolatedStringHandler.AppendFormatted(scheduledTime, "dd/MM/yyyy");
			defaultInterpolatedStringHandler.AppendLiteral(" /f");
			obj.Arguments = defaultInterpolatedStringHandler.ToStringAndClear();
			obj.UseShellExecute = false;
			obj.CreateNoWindow = true;
			ProcessStartInfo startInfo = obj;
			using Process process = Process.Start(startInfo);
			process?.WaitForExit(5000);
		}
		catch (Exception ex)
		{
			App.LoggingService?.LogWarning("[PreparePc] Não foi possível criar tarefa agendada: " + ex.Message);
		}
	}

	private void ShowScheduleConfirmation(DateTime scheduledTime, int scheduleType)
	{
		string text;
		switch (scheduleType)
		{
		case 0:
			text = "O Prepare PC será executado na próxima vez que você iniciar o Voltris.";
			break;
		case 1:
		{
			double totalHours = (scheduledTime - DateTime.Now).TotalHours;
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(49, 2);
			defaultInterpolatedStringHandler.AppendLiteral("O Prepare PC foi agendado para daqui a ");
			defaultInterpolatedStringHandler.AppendFormatted(totalHours, "F0");
			defaultInterpolatedStringHandler.AppendLiteral(" horas (");
			defaultInterpolatedStringHandler.AppendFormatted(scheduledTime, "HH:mm");
			defaultInterpolatedStringHandler.AppendLiteral(").");
			text = defaultInterpolatedStringHandler.ToStringAndClear();
			break;
		}
		default:
		{
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(32, 1);
			defaultInterpolatedStringHandler.AppendLiteral("O Prepare PC foi agendado para ");
			defaultInterpolatedStringHandler.AppendFormatted(scheduledTime, "dd/MM/yyyy às HH:mm");
			defaultInterpolatedStringHandler.AppendLiteral(".");
			text = defaultInterpolatedStringHandler.ToStringAndClear();
			break;
		}
		}
		MessageBox.Show(text + LocalizationService.Instance.GetString("PreparePcScheduleCancelHint"), LocalizationService.Instance.GetString("PreparePcScheduleConfirmedTitle"), MessageBoxButton.OK, MessageBoxImage.Asterisk);
	}

	private Border CreateScheduleOption(string iconPath, string title, string description, bool isSelected)
	{
		Border border = new Border
		{
			Background = (isSelected ? new SolidColorBrush(Color.FromRgb(37, 37, 53)) : new SolidColorBrush(Color.FromRgb(18, 18, 26))),
			BorderBrush = (isSelected ? ((Brush)(TryFindResource("PrimaryBrush") ?? Brushes.RoyalBlue)) : new SolidColorBrush(Color.FromRgb(30, 30, 46))),
			BorderThickness = new Thickness((!isSelected) ? 1 : 2),
			CornerRadius = new CornerRadius(12.0),
			Padding = new Thickness(16.0, 12.0, 16.0, 12.0),
			Margin = new Thickness(0.0, 0.0, 0.0, 10.0),
			Cursor = Cursors.Hand
		};
		if (isSelected)
		{
			border.Effect = new DropShadowEffect
			{
				BlurRadius = 15.0,
				ShadowDepth = 0.0,
				Color = Colors.Purple,
				Opacity = 0.4
			};
		}
		StackPanel stackPanel = new StackPanel
		{
			Orientation = Orientation.Horizontal
		};
		System.Windows.Shapes.Path element = new System.Windows.Shapes.Path
		{
			Data = Geometry.Parse(iconPath),
			Fill = (isSelected ? ((Brush)(TryFindResource("PrimaryBrush") ?? Brushes.RoyalBlue)) : ((Brush)(TryFindResource("TextSecondaryBrush") ?? Brushes.Gray))),
			Width = 20.0,
			Height = 20.0,
			Stretch = Stretch.Uniform,
			Margin = new Thickness(0.0, 0.0, 14.0, 0.0),
			VerticalAlignment = VerticalAlignment.Center
		};
		StackPanel stackPanel2 = new StackPanel();
		stackPanel2.Children.Add(new TextBlock
		{
			Text = title,
			FontSize = 14.0,
			FontWeight = FontWeights.SemiBold,
			FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"),
			Foreground = (Brush)(TryFindResource("TextPrimaryBrush") ?? Brushes.White)
		});
		stackPanel2.Children.Add(new TextBlock
		{
			Text = description,
			FontSize = 11.0,
			FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"),
			Foreground = (Brush)(TryFindResource("TextMutedBrush") ?? Brushes.Gray),
			Margin = new Thickness(0.0, 3.0, 0.0, 0.0)
		});
		stackPanel.Children.Add(element);
		stackPanel.Children.Add(stackPanel2);
		border.Child = stackPanel;
		border.Tag = isSelected;
		return border;
	}

	private Button CreateHoursButton(string text)
	{
		return new Button
		{
			Content = text,
			Padding = new Thickness(16.0, 8.0, 16.0, 8.0),
			Background = (Brush)(TryFindResource("DarkPanelAltBrush") ?? new SolidColorBrush(Color.FromRgb(20, 20, 25))),
			Foreground = (Brush)(TryFindResource("TextSecondaryBrush") ?? Brushes.Gray),
			BorderThickness = new Thickness(1.0),
			BorderBrush = (Brush)(TryFindResource("DarkBorderBrush") ?? Brushes.Gray),
			Margin = new Thickness(0.0, 0.0, 8.0, 0.0),
			Cursor = Cursors.Hand
		};
	}

	private void UpdateOptionSelection(Border opt1, Border opt2, Border opt3, int selected)
	{
		Border[] array = new Border[3] { opt1, opt2, opt3 };
		for (int i = 0; i < array.Length; i++)
		{
			array[i].Background = ((i == selected) ? ((Brush)(TryFindResource("DarkHoverBrush") ?? new SolidColorBrush(Color.FromRgb(30, 30, 40)))) : ((Brush)(TryFindResource("DarkPanelAltBrush") ?? new SolidColorBrush(Color.FromRgb(20, 20, 25)))));
			array[i].BorderBrush = ((i == selected) ? ((Brush)(TryFindResource("PrimaryBrush") ?? Brushes.RoyalBlue)) : ((Brush)(TryFindResource("DarkBorderBrush") ?? Brushes.DimGray)));
			array[i].BorderThickness = new Thickness((i != selected) ? 1 : 2);
		}
	}

	private void UpdateHoursSelection(Button btn2, Button btn4, Button btn8, int selected)
	{
		Button[] array = new Button[3] { btn2, btn4, btn8 };
		for (int i = 0; i < array.Length; i++)
		{
			array[i].Background = ((i == selected) ? ((Brush)(TryFindResource("VoltrisGradientBrush") ?? Brushes.RoyalBlue)) : ((Brush)(TryFindResource("DarkPanelAltBrush") ?? new SolidColorBrush(Color.FromRgb(20, 20, 25)))));
			array[i].Foreground = ((i == selected) ? Brushes.White : ((Brush)(TryFindResource("TextSecondaryBrush") ?? Brushes.Gray)));
		}
	}

	private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
	{
		for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
		{
			DependencyObject child = VisualTreeHelper.GetChild(parent, i);
			T val = (T)(object)((child is T) ? child : null);
			if (val != null)
			{
				return val;
			}
			T val2 = FindVisualChild<T>(child);
			if (val2 != null)
			{
				return val2;
			}
		}
		return default(T);
	}
}
