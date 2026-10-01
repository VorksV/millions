using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VoltrisOptimizer;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Scheduler;
using VoltrisOptimizer.UI.Controls;

namespace VoltrisOptimizer.UI.Views
{
    public partial class SchedulerView : UserControl
    {
        /// <summary>Índices do ScheduleTypeComboBox. Mantidos em constante para
        /// que a posição da UI e o enum nunca divirjam silenciosamente.</summary>
        private const int IdxOnce = 0;
        private const int IdxDaily = 1;
        private const int IdxWeekly = 2;
        private const int IdxMonthly = 3;
        private const int IdxOnStartup = 4;
        private const int IdxOnLogon = 5;

        private bool _populating;
        private string? _editingTaskId;

        public SchedulerView()
        {
            InitializeComponent();
            Loaded += SchedulerView_Loaded;
            Unloaded += SchedulerView_Unloaded;
            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;

            PopulateScheduleTypes();
            PopulateTimePickers();
            PopulateDatePickers();
            PopulateMonthDayPicker();
        }

        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _populating = true;
                PopulateScheduleTypes();
                _populating = false;
                UpdateTimePickerVisibility();
                RefreshTasks();
            }));
        }

        private void SchedulerView_Loaded(object sender, RoutedEventArgs e)
        {
            App.TelemetryService?.TrackEvent("PAGE_VIEW", "Scheduler", "Load", success: true);
            RefreshTasks();
            UpdateTimePickerVisibility();
        }

        private void SchedulerView_Unloaded(object sender, RoutedEventArgs e)
        {
            LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Preenchimento dos seletores
        // ─────────────────────────────────────────────────────────────────────────

        private void PopulateScheduleTypes()
        {
            ScheduleTypeComboBox.Items.Clear();
            ScheduleTypeComboBox.Items.Add(LocalizationService.Instance["Scheduler_Once"]);
            ScheduleTypeComboBox.Items.Add(LocalizationService.Instance["Scheduler_Daily"]);
            ScheduleTypeComboBox.Items.Add(LocalizationService.Instance["Scheduler_Weekly"]);
            ScheduleTypeComboBox.Items.Add(LocalizationService.Instance["Scheduler_Monthly"]);
            ScheduleTypeComboBox.Items.Add(LocalizationService.Instance["Scheduler_OnBoot"]);
            ScheduleTypeComboBox.Items.Add(LocalizationService.Instance["Scheduler_OnLogon"]);
            if (ScheduleTypeComboBox.SelectedIndex < 0)
                ScheduleTypeComboBox.SelectedIndex = IdxDaily;
        }

        private void PopulateTimePickers()
        {
            if (HourComboBox.Items.Count == 0)
            {
                for (int i = 0; i < 24; i++) HourComboBox.Items.Add(i.ToString("D2"));
                HourComboBox.SelectedIndex = 8; // 08:00 por padrão
            }
            if (MinuteComboBox.Items.Count == 0)
            {
                for (int i = 0; i < 60; i += 5) MinuteComboBox.Items.Add(i.ToString("D2"));
                MinuteComboBox.SelectedIndex = 0; // :00 por padrão
            }
        }

        private void PopulateDatePickers()
        {
            if (DayComboBox.Items.Count == 0)
            {
                for (int d = 1; d <= 31; d++) DayComboBox.Items.Add(d.ToString("D2"));
                DayComboBox.SelectedIndex = 0;
            }

            if (MonthComboBox.Items.Count == 0)
            {
                MonthComboBox.Items.Add(LocalizationService.Instance["Scheduler_MonthJanuary"]);
                MonthComboBox.Items.Add(LocalizationService.Instance["Scheduler_MonthFebruary"]);
                MonthComboBox.Items.Add(LocalizationService.Instance["Scheduler_MonthMarch"]);
                MonthComboBox.Items.Add(LocalizationService.Instance["Scheduler_MonthApril"]);
                MonthComboBox.Items.Add(LocalizationService.Instance["Scheduler_MonthMay"]);
                MonthComboBox.Items.Add(LocalizationService.Instance["Scheduler_MonthJune"]);
                MonthComboBox.Items.Add(LocalizationService.Instance["Scheduler_MonthJuly"]);
                MonthComboBox.Items.Add(LocalizationService.Instance["Scheduler_MonthAugust"]);
                MonthComboBox.Items.Add(LocalizationService.Instance["Scheduler_MonthSeptember"]);
                MonthComboBox.Items.Add(LocalizationService.Instance["Scheduler_MonthOctober"]);
                MonthComboBox.Items.Add(LocalizationService.Instance["Scheduler_MonthNovember"]);
                MonthComboBox.Items.Add(LocalizationService.Instance["Scheduler_MonthDecember"]);
            }

            if (YearComboBox.Items.Count == 0)
            {
                int start = DateTime.Now.Year;
                for (int y = 0; y < 5; y++) YearComboBox.Items.Add((start + y).ToString(CultureInfo.InvariantCulture));
                YearComboBox.SelectedIndex = 0;
            }
        }

        private void PopulateMonthDayPicker()
        {
            MonthDaysPanel.ItemsSource = Enumerable.Range(1, 31).ToList();
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Visibilidade conforme o tipo de agendamento
        // ─────────────────────────────────────────────────────────────────────────

        private void ScheduleTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_populating) return;
            UpdateTimePickerVisibility();
        }

        private void UpdateTimePickerVisibility()
        {
            if (TimePickerPanel == null || DaysPickerPanel == null || ScheduleTypeComboBox == null) return;

            var idx = ScheduleTypeComboBox.SelectedIndex;

            // Horário: relevante para Execução única, Diário, Semanal e Mensal.
            bool needsTime = idx == IdxOnce || idx == IdxDaily || idx == IdxWeekly || idx == IdxMonthly;
            TimePickerPanel.Visibility = needsTime ? Visibility.Visible : Visibility.Collapsed;

            // Data completa: só para Execução única (evita o horário ficar sem dia).
            if (DatePickerPanel != null)
                DatePickerPanel.Visibility = idx == IdxOnce ? Visibility.Visible : Visibility.Collapsed;

            // Dia da semana: só para Semanal.
            DaysPickerPanel.Visibility = idx == IdxWeekly ? Visibility.Visible : Visibility.Collapsed;

            // Dia do mês: só para Mensal.
            if (MonthDayPickerPanel != null)
                MonthDayPickerPanel.Visibility = idx == IdxMonthly ? Visibility.Visible : Visibility.Collapsed;

            // O botão "Adicionar" vira "Salvar" durante a edição.
            if (CancelEditButton != null)
                CancelEditButton.Visibility = _editingTaskId != null ? Visibility.Visible : Visibility.Collapsed;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Lista de tarefas
        // ─────────────────────────────────────────────────────────────────────────

        private void RefreshTasks()
        {
            if (TasksListBox == null) return;
            TasksListBox.Items.Clear();

            var svc = App.SchedulerService;
            if (svc == null)
            {
                EmptyMessage.Visibility = Visibility.Visible;
                TaskCountText.Text = LocalizationService.Instance["Scheduler_ServiceUnavailable"];
                return;
            }

            var tasks = svc.GetTasks();

            if (tasks.Count == 0)
            {
                EmptyMessage.Visibility = Visibility.Visible;
                TaskCountText.Text = LocalizationService.Instance["Scheduler_NoTasks"];
                return;
            }

            EmptyMessage.Visibility = Visibility.Collapsed;
            TaskCountText.Text = tasks.Count == 1
                ? LocalizationService.Instance["Scheduler_TaskCount_Singular"]
                : string.Format(LocalizationService.Instance["Scheduler_TaskCount_Plural"], tasks.Count);

            foreach (var task in tasks)
                TasksListBox.Items.Add(CreateTaskCard(task));
        }

        private Border CreateTaskCard(ScheduledTask task)
        {
            var svc = App.SchedulerService;

            var border = new Border
            {
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(20, 18, 20, 18),
                Margin = new Thickness(0, 0, 0, 12)
            };

            bool windowsRegistered = task.WindowsRegistrationError == null &&
                                    WindowsTaskSchedulerBridge.TaskExists(task.Id, out _);

            if (task.IsEnabled)
            {
                border.Background = new SolidColorBrush(Color.FromArgb(15, 16, 185, 129));
                border.BorderBrush = new SolidColorBrush(Color.FromArgb(40, 16, 185, 129));
            }
            else
            {
                border.Background = new SolidColorBrush(Color.FromArgb(15, 107, 114, 128));
                border.BorderBrush = new SolidColorBrush(Color.FromArgb(40, 107, 114, 128));
            }
            border.BorderThickness = new Thickness(1);

            var mainGrid = new Grid();
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });

            // ── Ícone de status ──
            var statusBorder = new Border
            {
                Width = 44,
                Height = 44,
                CornerRadius = new CornerRadius(12),
                Margin = new Thickness(0, 0, 18, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            if (task.IsEnabled)
            {
                statusBorder.Background = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 1),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(Color.FromRgb(16, 185, 129), 0),
                        new GradientStop(Color.FromRgb(5, 150, 105), 1)
                    }
                };
            }
            else
            {
                statusBorder.Background = new SolidColorBrush(Color.FromRgb(75, 85, 99));
            }

            statusBorder.Child = new TextBlock
            {
                Text = task.IsEnabled ? "✓" : "⏸",
                FontSize = 18,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(statusBorder, 0);
            mainGrid.Children.Add(statusBorder);

            // ── Conteúdo ──
            var contentPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

            contentPanel.Children.Add(new TextBlock
            {
                Text = task.Name,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = (SolidColorBrush)Application.Current.Resources["TextPrimaryBrush"],
                Margin = new Thickness(0, 0, 0, 4)
            });

            contentPanel.Children.Add(new TextBlock
            {
                Text = GetScheduleDescription(task),
                FontSize = 13,
                Foreground = (SolidColorBrush)Application.Current.Resources["TextSecondaryBrush"],
                Margin = new Thickness(0, 0, 0, 6)
            });

            // Badges das ações
            var actionsPanel = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var action in task.Actions ?? new List<string>())
            {
                var actionBadge = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(25, 139, 92, 246)),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 4, 10, 4),
                    Margin = new Thickness(0, 0, 8, 0)
                };
                actionBadge.Child = new TextBlock
                {
                    Text = GetActionEmoji(action) + " " + GetActionLabel(action),
                    FontSize = 11,
                    FontWeight = FontWeights.Medium,
                    Foreground = new SolidColorBrush(Color.FromRgb(139, 92, 246))
                };
                actionsPanel.Children.Add(actionBadge);
            }
            contentPanel.Children.Add(actionsPanel);

            // ── Estado real: próxima execução ──
            // Prioriza o horário que o PRÓPRIO Windows programmed. Se não houver
            // registro no Windows, usa o cálculo interno e sinaliza isso.
            DateTime? nextExec = null;
            bool fromWindows = false;
            if (windowsRegistered && WindowsTaskSchedulerBridge.TryGetNextRunTime(task.Id, out var wNext))
            {
                nextExec = wNext;
                fromWindows = true;
            }
            else
            {
                nextExec = task.NextExecution;
            }

            if (task.IsEnabled)
            {
                if (nextExec.HasValue)
                {
                    var nextExecPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
                    nextExecPanel.Children.Add(new TextBlock
                    {
                        Text = LocalizationService.Instance["Scheduler_NextExec"],
                        FontSize = 11,
                        Foreground = (SolidColorBrush)Application.Current.Resources["TextMutedBrush"]
                    });
                    nextExecPanel.Children.Add(new TextBlock
                    {
                        Text = nextExec.Value.ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture),
                        FontSize = 11,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(Color.FromRgb(16, 185, 129))
                    });
                    contentPanel.Children.Add(nextExecPanel);
                }
                else
                {
                    string noNextText = task.ScheduleType switch
                    {
                        ScheduleType.OnStartup or ScheduleType.OnLogon => GetScheduleDescription(task),
                        ScheduleType.Once => LocalizationService.Instance["Scheduler_ResultNeverRun"],
                        _ => "-"
                    };

                    contentPanel.Children.Add(new TextBlock
                    {
                        Text = noNextText,
                        FontSize = 11,
                        Margin = new Thickness(0, 8, 0, 0),
                        Foreground = (SolidColorBrush)Application.Current.Resources["TextMutedBrush"]
                    });
                }
            }

            // ── Registro no Windows (informação honesta de persistência) ──
            var regPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            regPanel.Children.Add(new TextBlock
            {
                Text = windowsRegistered
                    ? "🗓 " + LocalizationService.Instance["Scheduler_RegisteredInWindows"]
                    : "⚠ " + LocalizationService.Instance["Scheduler_NotRegisteredInWindows"],
                FontSize = 10,
                Foreground = windowsRegistered
                    ? new SolidColorBrush(Color.FromRgb(16, 185, 129))
                    : new SolidColorBrush(Color.FromRgb(245, 158, 11)),
                ToolTip = task.WindowsRegistrationError ?? ""
            });
            if (fromWindows)
            {
                regPanel.Children.Add(new TextBlock
                {
                    Text = "  (" + LocalizationService.Instance["Scheduler_NextExec"].Trim() + " Windows)",
                    FontSize = 10,
                    Foreground = (SolidColorBrush)Application.Current.Resources["TextMutedBrush"]
                });
            }
            contentPanel.Children.Add(regPanel);

            // ── Última execução + resultado real ──
            if (task.LastExecuted.HasValue || task.LastResult != null)
            {
                var lastPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
                lastPanel.Children.Add(new TextBlock
                {
                    Text = LocalizationService.Instance["Scheduler_LastRun"] +
                           (task.LastExecuted?.ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture) ?? "-"),
                    FontSize = 10,
                    Foreground = (SolidColorBrush)Application.Current.Resources["TextMutedBrush"]
                });

                var r = task.LastResult;
                if (r != null && (r.SucceededActions.Count > 0 || r.FailedActions.Count > 0 || r.Skipped))
                {
                    string text;
                    Color color;
                    if (r.Skipped) { text = LocalizationService.Instance["Scheduler_ResultSkipped"]; color = Color.FromRgb(245, 158, 11); }
                    else if (r.Success) { text = LocalizationService.Instance["Scheduler_ResultOk"]; color = Color.FromRgb(16, 185, 129); }
                    else if (r.PartialFailure) { text = LocalizationService.Instance["Scheduler_ResultPartial"]; color = Color.FromRgb(245, 158, 11); }
                    else { text = LocalizationService.Instance["Scheduler_ResultFailed"]; color = Color.FromRgb(239, 68, 68); }

                    var tooltip = r.Error;
                    if (r.FailedActions.Count > 0) tooltip += "\n" + string.Join("\n", r.Errors);

                    lastPanel.Children.Add(new TextBlock
                    {
                        Text = "  •  " + text,
                        FontSize = 10,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(color),
                        ToolTip = tooltip
                    });
                }
                contentPanel.Children.Add(lastPanel);
            }

            Grid.SetColumn(contentPanel, 1);
            mainGrid.Children.Add(contentPanel);

            // ── Botões ──
            var buttonPanel = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            var toggleButton = CreateCardButton(
                task.IsEnabled ? LocalizationService.Instance["Scheduler_Pause"]
                               : LocalizationService.Instance["Scheduler_Activate"], 110);
            toggleButton.Click += (s, e) =>
            {
                // UpdateTask altera no lugar. Antes usava Remove+Add, que reordenava a
                // lista e fazia duas gravações disco.
                task.IsEnabled = !task.IsEnabled;
                if (!svc!.UpdateTask(task, out var err))
                {
                    task.IsEnabled = !task.IsEnabled; // rollback do toggle visual
                    ModernMessageBox.Show(
                        string.Format(LocalizationService.Instance["Scheduler_UpdateFailed"], err),
                        LocalizationService.Instance["Loc_Error"],
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                RefreshTasks();
                new ToastService().Show(
                    task.IsEnabled ? LocalizationService.Instance["Scheduler_TaskActivated"]
                                   : LocalizationService.Instance["Scheduler_TaskPaused"],
                    string.Format(task.IsEnabled
                        ? LocalizationService.Instance["Scheduler_TaskWasActivated"]
                        : LocalizationService.Instance["Scheduler_TaskWasPaused"], task.Name));

                App.TelemetryService?.TrackEvent("SCHEDULER_TOGGLE_TASK", "Scheduler", "Toggle",
                    metadata: new { Task = task.Name, Enabled = task.IsEnabled });
            };
            buttonPanel.Children.Add(toggleButton);

            var runButton = CreateCardButton(LocalizationService.Instance["Scheduler_RunNow"], 130);
            runButton.Click += async (s, e) =>
            {
                runButton.IsEnabled = false;
                try
                {
                    new ToastService().Show(
                        LocalizationService.Instance["Scheduler_RunNowStarted"],
                        string.Format(LocalizationService.Instance["Scheduler_RunNowStarted"], task.Name));

                    var result = await svc!.RunNowAsync(task, HistoryOrigin.Manual).ConfigureAwait(true);

                    RefreshTasks();

                    // A UI reflete o desfecho REAL, nunca um sucesso presumido.
                    if (result.Skipped)
                    {
                        ModernMessageBox.Show(result.Error, task.Name,
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    else if (result.Success)
                    {
                        new ToastService().Show(
                            LocalizationService.Instance["Scheduler_ResultOk"],
                            $"{task.Name} — {result.Duration.TotalSeconds:F1}s" +
                            (result.SpaceFreed > 0 ? $" — {FormatBytes(result.SpaceFreed)}" : ""));
                    }
                    else
                    {
                        ModernMessageBox.Show(
                            string.Format(LocalizationService.Instance["Scheduler_RunFailed"], task.Name, result.Error),
                            LocalizationService.Instance["Scheduler_ResultFailed"],
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch (Exception ex)
                {
                    ModernMessageBox.Show(
                        string.Format(LocalizationService.Instance["Scheduler_RunFailed"], task.Name, ex.Message),
                        LocalizationService.Instance["Scheduler_ResultFailed"],
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    runButton.IsEnabled = true;
                }
            };
            buttonPanel.Children.Add(runButton);

            var editButton = CreateCardButton(LocalizationService.Instance["Scheduler_Edit"], 100);
            editButton.Click += (s, e) => LoadTaskIntoForm(task);
            buttonPanel.Children.Add(editButton);

            var deleteButton = CreateCardButton("🗑", 44);
            deleteButton.Click += (s, e) =>
            {
                if (ModernMessageBox.Show(
                        string.Format(LocalizationService.Instance["Scheduler_ConfirmRemovalText"], task.Name),
                        LocalizationService.Instance["Scheduler_ConfirmRemovalTitle"],
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    svc?.RemoveTask(task.Id);
                    RefreshTasks();
                    new ToastService().Show(
                        LocalizationService.Instance["Scheduler_TaskRemovedTitle"],
                        string.Format(LocalizationService.Instance["Scheduler_TaskRemovedText"], task.Name));
                    App.TelemetryService?.TrackEvent("SCHEDULER_DELETE_TASK", "Scheduler", "Delete",
                        metadata: new { Task = task.Name });
                }
            };
            buttonPanel.Children.Add(deleteButton);

            Grid.SetColumn(buttonPanel, 2);
            mainGrid.Children.Add(buttonPanel);

            border.Child = mainGrid;
            return border;
        }

        private static Button CreateCardButton(string content, double width)
        {
            var b = new Button
            {
                Content = content,
                Style = (Style)Application.Current.Resources["NeonSecondaryButtonStyle"],
                Height = 40,
                MinWidth = width,
                Margin = new Thickness(0, 0, 10, 0),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold
            };
            return b;
        }

        private static string GetActionEmoji(string action) =>
            action.Trim().ToLowerInvariant() switch
            {
                "limpeza" => "🧹",
                "desempenho" => "⚡",
                "rede" => "🌐",
                "avançado" => "🔧",
                "limpeza_profunda" => "🚿",
                "reparacao_completa" => "🛠️",
                "gamer" => "🎮",
                "stream" => "📡",
                _ => "📋"
            };

        private static string GetActionLabel(string action) =>
            action.Trim().ToLowerInvariant() switch
            {
                "limpeza" => LocalizationService.Instance["Loc_CleaningBasic"],
                "desempenho" => LocalizationService.Instance["Loc_Performance"],
                "rede" => LocalizationService.Instance["Loc_Network"],
                "avançado" => LocalizationService.Instance["Loc_Advanced"],
                "limpeza_profunda" => LocalizationService.Instance["Loc_DeepCleaning"],
                "reparacao_completa" => LocalizationService.Instance["Loc_CompleteRepair_2"],
                "gamer" => LocalizationService.Instance["Loc_GamerMode"],
                "stream" => LocalizationService.Instance["Loc_StreamHub"],
                _ => action
            };

        private string GetScheduleDescription(ScheduledTask task)
        {
            var time = task.ScheduledTime?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "08:00";

            return task.ScheduleType switch
            {
                ScheduleType.Once =>
                    $"1️⃣ {task.ScheduledTime?.ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture) ?? LocalizationService.Instance["Scheduler_PickDate"]}",
                ScheduleType.Daily => $"⏰ {LocalizationService.Instance["Scheduler_Daily"]} — {time}",
                ScheduleType.Weekly => $"📆 {LocalizationService.Instance["Scheduler_Weekly"]} — {DescribeDays(task.DaysOfWeek)} — {time}",
                ScheduleType.Monthly => $"📅 {LocalizationService.Instance["Scheduler_Monthly"]} — {DescribeMonthDays(task.DaysOfMonth)} — {time}",
                ScheduleType.OnStartup => $"🚀 {LocalizationService.Instance["Scheduler_OnBoot"]}",
                ScheduleType.OnLogon => $"🔑 {LocalizationService.Instance["Scheduler_OnLogon"]}",
                _ => "-"
            };
        }

        private string DescribeDays(WeekdaySelection days)
        {
            if (days == WeekdaySelection.None) return LocalizationService.Instance["Scheduler_PickAtLeastOneDay"];
            var parts = new List<string>();
            void Add(WeekdaySelection flag, string key)
            {
                if (days.HasFlag(flag)) parts.Add(LocalizationService.Instance[key]);
            }
            Add(WeekdaySelection.Monday, "Loc_Mon");
            Add(WeekdaySelection.Tuesday, "Loc_Tue");
            Add(WeekdaySelection.Wednesday, "Loc_Wed");
            Add(WeekdaySelection.Thursday, "Loc_Thu");
            Add(WeekdaySelection.Friday, "Loc_Fri");
            Add(WeekdaySelection.Saturday, "Loc_Sat");
            Add(WeekdaySelection.Sunday, "Loc_Sun");
            return string.Join(", ", parts);
        }

        private static string DescribeMonthDays(List<int>? days)
        {
            if (days == null || days.Count == 0) return "-";
            return string.Join(", ", days.Distinct().OrderBy(d => d));
        }

        private static string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1) { order++; len /= 1024; }
            return $"{len:0.##} {sizes[order]}";
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Criação / edição
        // ─────────────────────────────────────────────────────────────────────────

        private void AddTaskButton_Click(object sender, RoutedEventArgs e)
        {
            var svc = App.SchedulerService;
            if (svc == null)
            {
                ModernMessageBox.Show(LocalizationService.Instance["Scheduler_ServiceUnavailable"],
                    LocalizationService.Instance["Loc_Error"], MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var taskName = TaskNameTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(taskName))
            {
                ModernMessageBox.Show(LocalizationService.Instance["Scheduler_NameRequired"],
                    LocalizationService.Instance["Loc_Warning"], MessageBoxButton.OK, MessageBoxImage.Warning);
                TaskNameTextBox.Focus();
                return;
            }

            var scheduleType = SelectedScheduleType();

            // ── Dias da semana (Semanal) ──
            var days = WeekdaySelection.None;
            if (scheduleType == ScheduleType.Weekly)
            {
                if (DaySun.IsChecked == true) days |= WeekdaySelection.Sunday;
                if (DayMon.IsChecked == true) days |= WeekdaySelection.Monday;
                if (DayTue.IsChecked == true) days |= WeekdaySelection.Tuesday;
                if (DayWed.IsChecked == true) days |= WeekdaySelection.Wednesday;
                if (DayThu.IsChecked == true) days |= WeekdaySelection.Thursday;
                if (DayFri.IsChecked == true) days |= WeekdaySelection.Friday;
                if (DaySat.IsChecked == true) days |= WeekdaySelection.Saturday;

                if (days == WeekdaySelection.None)
                {
                    ModernMessageBox.Show(LocalizationService.Instance["Scheduler_PickAtLeastOneDay"],
                        LocalizationService.Instance["Loc_Warning"], MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            // ── Dias do mês (Mensal) ──
            List<int>? monthDays = null;
            if (scheduleType == ScheduleType.Monthly)
            {
                monthDays = new List<int>();
                foreach (var item in MonthDaysPanel.Items)
                {
                    if (item is not int day) continue;
                    var cb = FindCheckBoxForDay(MonthDaysPanel, day);
                    if (cb?.IsChecked == true) monthDays.Add(day);
                }

                if (monthDays.Count == 0)
                {
                    ModernMessageBox.Show(LocalizationService.Instance["Scheduler_PickAtLeastOneDayOfMonth"],
                        LocalizationService.Instance["Loc_Warning"], MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            // ── Data/hora ──
            DateTime? scheduledTime = null;
            bool needsTime = scheduleType == ScheduleType.Once || scheduleType == ScheduleType.Daily ||
                             scheduleType == ScheduleType.Weekly || scheduleType == ScheduleType.Monthly;

            if (needsTime)
            {
                int hour = HourComboBox.SelectedItem != null ? int.Parse(HourComboBox.SelectedItem.ToString()!) : 8;
                int minute = MinuteComboBox.SelectedItem != null ? int.Parse(MinuteComboBox.SelectedItem.ToString()!) : 0;

                if (scheduleType == ScheduleType.Once)
                {
                    if (DayComboBox.SelectedItem == null || MonthComboBox.SelectedItem == null || YearComboBox.SelectedItem == null)
                    {
                        ModernMessageBox.Show(LocalizationService.Instance["Scheduler_PickDate"],
                            LocalizationService.Instance["Loc_Warning"], MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    int day = int.Parse(DayComboBox.SelectedItem.ToString()!);
                    int month = MonthComboBox.SelectedIndex + 1;
                    int year = int.Parse(YearComboBox.SelectedItem.ToString()!);

                    // Dia 31 em fevereiro é inválido: ajusta para o último dia do mês.
                    int maxDay = DateTime.DaysInMonth(year, month);
                    if (day > maxDay) day = maxDay;

                    var chosen = new DateTime(year, month, day, hour, minute, 0);
                    if (chosen <= DateTime.Now)
                    {
                        ModernMessageBox.Show(LocalizationService.Instance["Scheduler_DateInPast"],
                            LocalizationService.Instance["Loc_Warning"], MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    scheduledTime = chosen;
                }
                else
                {
                    scheduledTime = DateTime.Today.AddHours(hour).AddMinutes(minute);
                }
            }

            // ── Ações ──
            var actions = new List<string>();
            if (ActionCleanup.IsChecked == true) actions.Add("limpeza");
            if (ActionDeepClean.IsChecked == true) actions.Add("limpeza_profunda");
            if (ActionPerformance.IsChecked == true) actions.Add("desempenho");
            if (ActionNetwork.IsChecked == true) actions.Add("rede");
            if (ActionAdvanced.IsChecked == true) actions.Add("avançado");
            if (ActionRepair.IsChecked == true) actions.Add("reparacao_completa");
            if (ActionGamer.IsChecked == true) actions.Add("gamer");
            if (ActionStream.IsChecked == true) actions.Add("stream");

            if (actions.Count == 0)
            {
                ModernMessageBox.Show(LocalizationService.Instance["Scheduler_SelectAnAction"],
                    LocalizationService.Instance["Loc_Warning"], MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_editingTaskId != null)
            {
                var existing = svc.GetTask(_editingTaskId);
                if (existing == null)
                {
                    _editingTaskId = null;
                    ModernMessageBox.Show(LocalizationService.Instance["Scheduler_ServiceUnavailable"],
                        LocalizationService.Instance["Loc_Error"], MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                existing.Name = taskName;
                existing.ScheduleType = scheduleType;
                existing.ScheduledTime = scheduledTime;
                existing.DaysOfWeek = days;
                existing.DaysOfMonth = monthDays;
                existing.Actions = actions;

                if (!svc.UpdateTask(existing, out var updateError))
                {
                    ModernMessageBox.Show(
                        string.Format(LocalizationService.Instance["Scheduler_UpdateFailed"], updateError),
                        LocalizationService.Instance["Loc_Error"], MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                _editingTaskId = null;
                ClearForm();
                RefreshTasks();
                new ToastService().Show(LocalizationService.Instance["Scheduler_EditTitle"], taskName);
                return;
            }

            var task = new ScheduledTask
            {
                Name = taskName,
                ScheduleType = scheduleType,
                ScheduledTime = scheduledTime,
                DaysOfWeek = days,
                DaysOfMonth = monthDays,
                Actions = actions,
                IsEnabled = true
            };

            var created = svc.AddTask(task, out var error);
            if (created == null)
            {
                var title = error.Contains("equivalente", StringComparison.OrdinalIgnoreCase)
                    ? LocalizationService.Instance["Scheduler_Duplicate"]
                    : LocalizationService.Instance["Loc_Error"];
                ModernMessageBox.Show(error, title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ClearForm();
            RefreshTasks();

            // A UI só afirma sucesso depois que o serviço confirma o registro real.
            if (!string.IsNullOrEmpty(created.WindowsRegistrationError))
            {
                ModernMessageBox.Show(
                    string.Format(LocalizationService.Instance["Scheduler_TaskCreatedButNotRegistered"],
                                  created.WindowsRegistrationError),
                    LocalizationService.Instance["Loc_Warning"],
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                new ToastService().Show(
                    LocalizationService.Instance["Scheduler_TaskCreatedTitle"],
                    string.Format(LocalizationService.Instance["Scheduler_TaskCreatedText"], taskName,
                        created.NextExecution?.ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture) ?? "-"));
            }

            App.TelemetryService?.TrackEvent("SCHEDULER_CREATE_TASK", "Scheduler", "Create",
                metadata: new { Task = taskName, Type = scheduleType.ToString(), Actions = string.Join(",", actions) });
        }

        private ScheduleType SelectedScheduleType() =>
            ScheduleTypeComboBox.SelectedIndex switch
            {
                IdxOnce => ScheduleType.Once,
                IdxWeekly => ScheduleType.Weekly,
                IdxMonthly => ScheduleType.Monthly,
                IdxOnStartup => ScheduleType.OnStartup,
                IdxOnLogon => ScheduleType.OnLogon,
                _ => ScheduleType.Daily
            };

        private static CheckBox? FindCheckBoxForDay(ItemsControl panel, int day)
        {
            foreach (var container in panel.Items)
            {
                if (container is not int item || item != day) continue;
                if (panel.ItemContainerGenerator.ContainerFromItem(container) is not CheckBox cb) continue;
                return cb;
            }
            return null;
        }

        private void LoadTaskIntoForm(ScheduledTask task)
        {
            _editingTaskId = task.Id;
            TaskNameTextBox.Text = task.Name;

            _populating = true;
            try
            {
                ScheduleTypeComboBox.SelectedIndex = task.ScheduleType switch
                {
                    ScheduleType.Once => IdxOnce,
                    ScheduleType.Weekly => IdxWeekly,
                    ScheduleType.Monthly => IdxMonthly,
                    ScheduleType.OnStartup => IdxOnStartup,
                    ScheduleType.OnLogon => IdxOnLogon,
                    _ => IdxDaily
                };

                if (task.ScheduledTime.HasValue)
                {
                    int h = task.ScheduledTime.Value.Hour;
                    int m = task.ScheduledTime.Value.Minute;

                    int hourIdx = HourComboBox.Items.IndexOf(h.ToString("D2"));
                    HourComboBox.SelectedIndex = hourIdx >= 0 ? hourIdx : Math.Max(0, Math.Min(23, h));

                    int minuteIdx = MinuteComboBox.Items.IndexOf(m.ToString("D2"));
                    MinuteComboBox.SelectedIndex = minuteIdx >= 0 ? minuteIdx : 0;

                    int dayIdx = DayComboBox.Items.IndexOf(task.ScheduledTime.Value.Day.ToString("D2"));
                    DayComboBox.SelectedIndex = dayIdx >= 0 ? dayIdx : 0;
                    MonthComboBox.SelectedIndex = Math.Max(0, task.ScheduledTime.Value.Month - 1);

                    string yearStr = task.ScheduledTime.Value.Year.ToString(CultureInfo.InvariantCulture);
                    int yearIdx = YearComboBox.Items.IndexOf(yearStr);
                    if (yearIdx >= 0) YearComboBox.SelectedIndex = yearIdx;
                }

                DaySun.IsChecked = task.DaysOfWeek.HasFlag(WeekdaySelection.Sunday);
                DayMon.IsChecked = task.DaysOfWeek.HasFlag(WeekdaySelection.Monday);
                DayTue.IsChecked = task.DaysOfWeek.HasFlag(WeekdaySelection.Tuesday);
                DayWed.IsChecked = task.DaysOfWeek.HasFlag(WeekdaySelection.Wednesday);
                DayThu.IsChecked = task.DaysOfWeek.HasFlag(WeekdaySelection.Thursday);
                DayFri.IsChecked = task.DaysOfWeek.HasFlag(WeekdaySelection.Friday);
                DaySat.IsChecked = task.DaysOfWeek.HasFlag(WeekdaySelection.Saturday);
            }
            finally
            {
                _populating = false;
            }

            // Meses: o painel usa ItemContainerGenerator, então é preciso materializar
            // os containers antes de marcar os dias.
            MonthDaysPanel.UpdateLayout();
            foreach (var d in task.DaysOfMonth ?? new List<int>())
            {
                var cb = FindCheckBoxForDay(MonthDaysPanel, d);
                if (cb != null) cb.IsChecked = true;
            }

            ActionCleanup.IsChecked = task.Actions.Contains("limpeza", StringComparer.OrdinalIgnoreCase);
            ActionDeepClean.IsChecked = task.Actions.Contains("limpeza_profunda", StringComparer.OrdinalIgnoreCase);
            ActionPerformance.IsChecked = task.Actions.Contains("desempenho", StringComparer.OrdinalIgnoreCase);
            ActionNetwork.IsChecked = task.Actions.Contains("rede", StringComparer.OrdinalIgnoreCase);
            ActionAdvanced.IsChecked = task.Actions.Contains("avançado", StringComparer.OrdinalIgnoreCase);
            ActionRepair.IsChecked = task.Actions.Contains("reparacao_completa", StringComparer.OrdinalIgnoreCase);
            ActionGamer.IsChecked = task.Actions.Contains("gamer", StringComparer.OrdinalIgnoreCase);
            ActionStream.IsChecked = task.Actions.Contains("stream", StringComparer.OrdinalIgnoreCase);

            UpdateTimePickerVisibility();
            TaskNameTextBox.Focus();
        }

        private void ClearForm()
        {
            _editingTaskId = null;
            TaskNameTextBox.Clear();
            _populating = true;
            try
            {
                ScheduleTypeComboBox.SelectedIndex = IdxDaily;
                HourComboBox.SelectedIndex = 8;
                MinuteComboBox.SelectedIndex = 0;
                DayComboBox.SelectedIndex = 0;
                MonthComboBox.SelectedIndex = DateTime.Now.Month - 1;
                YearComboBox.SelectedIndex = 0;
                DaySun.IsChecked = false;
                DayMon.IsChecked = false;
                DayTue.IsChecked = false;
                DayWed.IsChecked = false;
                DayThu.IsChecked = false;
                DayFri.IsChecked = false;
                DaySat.IsChecked = false;
            }
            finally
            {
                _populating = false;
            }

            MonthDaysPanel.UpdateLayout();
            foreach (var item in MonthDaysPanel.Items)
            {
                if (item is not int day) continue;
                var cb = FindCheckBoxForDay(MonthDaysPanel, day);
                if (cb != null) cb.IsChecked = false;
            }

            ActionCleanup.IsChecked = false;
            ActionDeepClean.IsChecked = false;
            ActionPerformance.IsChecked = false;
            ActionNetwork.IsChecked = false;
            ActionAdvanced.IsChecked = false;
            ActionRepair.IsChecked = false;
            ActionGamer.IsChecked = false;
            ActionStream.IsChecked = false;

            UpdateTimePickerVisibility();
        }

        private void RefreshTasks_Click(object sender, RoutedEventArgs e) => RefreshTasks();

        private void CancelEditButton_Click(object sender, RoutedEventArgs e)
        {
            ClearForm();
            RefreshTasks();
        }

        // Handlers dos cards de ação (toggle do checkbox ao clicar no card)
        private void ActionCleanup_Click(object sender, MouseButtonEventArgs e) => ActionCleanup.IsChecked = !ActionCleanup.IsChecked;
        private void ActionPerformance_Click(object sender, MouseButtonEventArgs e) => ActionPerformance.IsChecked = !ActionPerformance.IsChecked;
        private void ActionNetwork_Click(object sender, MouseButtonEventArgs e) => ActionNetwork.IsChecked = !ActionNetwork.IsChecked;
        private void ActionAdvanced_Click(object sender, MouseButtonEventArgs e) => ActionAdvanced.IsChecked = !ActionAdvanced.IsChecked;
        private void ActionDeepClean_Click(object sender, MouseButtonEventArgs e) => ActionDeepClean.IsChecked = !ActionDeepClean.IsChecked;
        private void ActionRepair_Click(object sender, MouseButtonEventArgs e) => ActionRepair.IsChecked = !ActionRepair.IsChecked;
        private void ActionGamer_Click(object sender, MouseButtonEventArgs e) => ActionGamer.IsChecked = !ActionGamer.IsChecked;
        private void ActionStream_Click(object sender, MouseButtonEventArgs e) => ActionStream.IsChecked = !ActionStream.IsChecked;
    }
}
