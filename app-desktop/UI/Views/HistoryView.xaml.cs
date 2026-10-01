using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using VoltrisOptimizer;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.UI.Views
{
    public partial class HistoryView : UserControl
    {
        public HistoryView()
        {
            InitializeComponent();
            Loaded += HistoryView_Loaded;
            Unloaded += HistoryView_Unloaded;
            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        }

        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                RefreshHistory();
            }));
        }

        private void HistoryView_Unloaded(object sender, RoutedEventArgs e)
        {
            LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        }

        private void HistoryView_Loaded(object sender, RoutedEventArgs e)
        {
            // Track page view
            App.TelemetryService?.TrackEvent(
                "PAGE_VIEW",
                "History",
                "Load",
                success: true
            );
            
            RefreshHistory();
        }

        private void RefreshHistory()
        {
            if (App.HistoryService == null)
            {
                HistoryListBox.Items.Clear();
                return;
            }

            var stats = App.HistoryService.GetStats();
            var history = App.HistoryService.GetHistory(100);

            // Atualizar estatísticas
            TotalOptimizationsText.Text = stats.TotalOptimizations.ToString();
            Last30DaysText.Text = stats.Last30DaysCount.ToString();
            TotalSpaceFreedText.Text = FormatBytes(stats.TotalSpaceFreed);
            AverageTimeText.Text = $"{stats.AverageTime:F1}s";

            // Atualizar lista
            HistoryListBox.Items.Clear();
            
            if (history.Count == 0)
            {
                // Mensagem quando não há histórico
                var emptyBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(10, 255, 255, 255)),
                    CornerRadius = new CornerRadius(14),
                    Padding = new Thickness(40, 50, 40, 50)
                };
                var emptyPanel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                emptyPanel.Children.Add(new TextBlock
                {
                    Text = "📊",
                    FontSize = 48,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Opacity = 0.5,
                    Margin = new Thickness(0, 0, 0, 16)
                });
                emptyPanel.Children.Add(new TextBlock
                {
                    Text = LocalizationService.Instance.GetString("Loc_NoRecordsYet"),
                    FontSize = 16,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (SolidColorBrush)Application.Current.Resources["TextSecondaryBrush"],
                    HorizontalAlignment = HorizontalAlignment.Center
                });
                emptyPanel.Children.Add(new TextBlock
                {
                    Text = LocalizationService.Instance.GetString("Loc_RunOptimizationsToSeeHistory"),
                    FontSize = 13,
                    Foreground = (SolidColorBrush)Application.Current.Resources["TextMutedBrush"],
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 6, 0, 0)
                });
                emptyBorder.Child = emptyPanel;
                HistoryListBox.Items.Add(emptyBorder);
                return;
            }
            
            foreach (var entry in history)
            {
                var border = new Border
                {
                    CornerRadius = new CornerRadius(14),
                    Padding = new Thickness(20, 18, 20, 18),
                    Margin = new Thickness(0, 0, 0, 12)
                };
                
                // Fundo baseado no status
                if (entry.Success)
                {
                    border.Background = new SolidColorBrush(Color.FromArgb(12, 16, 185, 129));
                    border.BorderBrush = new SolidColorBrush(Color.FromArgb(35, 16, 185, 129));
                }
                else
                {
                    border.Background = new SolidColorBrush(Color.FromArgb(12, 239, 68, 68));
                    border.BorderBrush = new SolidColorBrush(Color.FromArgb(35, 239, 68, 68));
                }
                border.BorderThickness = new Thickness(1);

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20, GridUnitType.Pixel) }); // Espaçador
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20, GridUnitType.Pixel) }); // Espaçador
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });

                // Ícone com estilo melhorado
                var iconBorder = new Border
                {
                    Width = 50,
                    Height = 50,
                    CornerRadius = new CornerRadius(14),
                    VerticalAlignment = VerticalAlignment.Center
                };
                
                if (entry.Success)
                {
                    iconBorder.Background = new LinearGradientBrush
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
                    iconBorder.Background = new LinearGradientBrush
                    {
                        StartPoint = new Point(0, 0),
                        EndPoint = new Point(1, 1),
                        GradientStops = new GradientStopCollection
                        {
                            new GradientStop(Color.FromRgb(239, 68, 68), 0),
                            new GradientStop(Color.FromRgb(185, 28, 28), 1)
                        }
                    };
                }
                
                var iconText = new TextBlock
                {
                    Text = entry.Success ? "✓" : "✕",
                    FontSize = 22,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                iconBorder.Child = iconText;
                Grid.SetColumn(iconBorder, 0);
                grid.Children.Add(iconBorder);

                // Conteúdo com espaçamento adequado
                var contentPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                
                // Tipo de ação
                contentPanel.Children.Add(new TextBlock
                {
                    Text = LocalizationService.Instance.GetString(entry.ActionType),
                    FontSize = 15,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (SolidColorBrush)Application.Current.Resources["TextPrimaryBrush"],
                    Margin = new Thickness(0, 0, 0, 6)
                });
                
                // Descrição
                contentPanel.Children.Add(new TextBlock
                {
                    Text = LocalizationService.Instance.GetString(entry.Description),
                    FontSize = 13,
                    Foreground = (SolidColorBrush)Application.Current.Resources["TextSecondaryBrush"],
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 400,
                    Margin = new Thickness(0, 0, 0, 8)
                });
                
                // Data e duração em linha separada com badges
                var metaPanel = new StackPanel { Orientation = Orientation.Horizontal };
                
                // Badge de data
                var dateBadge = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(20, 139, 92, 246)),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 4, 10, 4),
                    Margin = new Thickness(0, 0, 10, 0)
                };
                dateBadge.Child = new TextBlock
                {
                    Text = $"📅 {entry.Timestamp:dd/MM/yyyy HH:mm}",
                    FontSize = 11,
                    FontWeight = FontWeights.Medium,
                    Foreground = new SolidColorBrush(Color.FromRgb(139, 92, 246))
                };
                metaPanel.Children.Add(dateBadge);
                
                // Badge de duração
                if (entry.Duration.TotalSeconds > 0)
                {
                    var durationBadge = new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(20, 245, 158, 11)),
                        CornerRadius = new CornerRadius(6),
                        Padding = new Thickness(10, 4, 10, 4)
                    };
                    durationBadge.Child = new TextBlock
                    {
                        Text = $"⏱ {entry.Duration:mm\\:ss}",
                        FontSize = 11,
                        FontWeight = FontWeights.Medium,
                        Foreground = new SolidColorBrush(Color.FromRgb(245, 158, 11))
                    };
                    metaPanel.Children.Add(durationBadge);
                }
                
                contentPanel.Children.Add(metaPanel);
                Grid.SetColumn(contentPanel, 2);
                grid.Children.Add(contentPanel);

                // Espaço liberado com estilo melhorado
                var spacePanel = new StackPanel 
                { 
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                
                if (entry.SpaceFreed > 0)
                {
                    var spaceBorder = new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(20, 16, 185, 129)),
                        CornerRadius = new CornerRadius(10),
                        Padding = new Thickness(16, 10, 16, 10)
                    };
                    var spaceStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                    spaceStack.Children.Add(new TextBlock
                    {
                        Text = FormatBytes(entry.SpaceFreed),
                        FontSize = 18,
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(Color.FromRgb(16, 185, 129)),
                        HorizontalAlignment = HorizontalAlignment.Center
                    });
                    spaceStack.Children.Add(new TextBlock
                    {
                        Text = LocalizationService.Instance.GetString("Loc_FreedLower"),
                        FontSize = 10,
                        Foreground = (SolidColorBrush)Application.Current.Resources["TextMutedBrush"],
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 2, 0, 0)
                    });
                    spaceBorder.Child = spaceStack;
                    spacePanel.Children.Add(spaceBorder);
                }
                else
                {
                    var statusBadge = new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(20, 107, 114, 128)),
                        CornerRadius = new CornerRadius(10),
                        Padding = new Thickness(16, 10, 16, 10)
                    };
                    statusBadge.Child = new TextBlock
                    {
                        Text = entry.Success ? LocalizationService.Instance.GetString("Loc_Ok") : LocalizationService.Instance.GetString("Loc_Error"),
                        FontSize = 14,
                        FontWeight = FontWeights.Bold,
                        Foreground = entry.Success 
                            ? new SolidColorBrush(Color.FromRgb(16, 185, 129))
                            : new SolidColorBrush(Color.FromRgb(239, 68, 68)),
                        HorizontalAlignment = HorizontalAlignment.Center
                    };
                    spacePanel.Children.Add(statusBadge);
                }
                
                Grid.SetColumn(spacePanel, 4);
                grid.Children.Add(spacePanel);

                border.Child = grid;
                HistoryListBox.Items.Add(border);
            }
        }

        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshHistory();
        }
        
        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            if (App.HistoryService == null)
            {
                MessageBox.Show(LocalizationService.Instance.GetString("Loc_HistoryServiceNotAvailable"), LocalizationService.Instance.GetString("Loc_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            
            var history = App.HistoryService.GetHistory();
            if (history.Count == 0)
            {
                MessageBox.Show(LocalizationService.Instance.GetString("Loc_NoHistoryToExport"), LocalizationService.Instance.GetString("Loc_Warning"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "JSON (*.json)|*.json|CSV (*.csv)|*.csv|Texto (*.txt)|*.txt",
                FileName = $"voltris_history_{DateTime.Now:yyyyMMdd_HHmmss}"
            };
            
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var extension = System.IO.Path.GetExtension(dialog.FileName).ToLower();
                    
                    switch (extension)
                    {
                        case ".json":
                            ExportToJson(dialog.FileName, history);
                            break;
                        case ".csv":
                            ExportToCsv(dialog.FileName, history);
                            break;
                        case ".txt":
                            ExportToText(dialog.FileName, history);
                            break;
                    }
                    
                    MessageBox.Show(LocalizationService.Instance["Loc_HistoricoExportadoComSucesso"] + "\n\n" + dialog.FileName, LocalizationService.Instance["Loc_ExportacaoConcluida"], MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(LocalizationService.Instance.GetString("Loc_ErrorExportingHistory") + $": {ex.Message}", LocalizationService.Instance.GetString("Loc_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        
        private void ExportToJson(string filePath, List<VoltrisOptimizer.Services.OptimizationHistory> history)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(history, new System.Text.Json.JsonSerializerOptions 
            { 
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            });
            System.IO.File.WriteAllText(filePath, json, System.Text.Encoding.UTF8);
        }
        
        private void ExportToCsv(string filePath, List<VoltrisOptimizer.Services.OptimizationHistory> history)
        {
            var csv = new System.Text.StringBuilder();
            csv.AppendLine("Data/Hora,Tipo de Ação,Descrição,Duração (s),Espaço Liberado (bytes),Sucesso");
            
            foreach (var entry in history)
            {
                csv.AppendLine($"\"{entry.Timestamp:yyyy-MM-dd HH:mm:ss}\",\"{entry.ActionType}\",\"{entry.Description.Replace("\"", "\"\"")}\",{entry.Duration.TotalSeconds},{entry.SpaceFreed},{entry.Success}");
            }
            
            System.IO.File.WriteAllText(filePath, csv.ToString(), System.Text.Encoding.UTF8);
        }
        
        private void ExportToText(string filePath, List<VoltrisOptimizer.Services.OptimizationHistory> history)
        {
            var text = new System.Text.StringBuilder();
            text.AppendLine("═══════════════════════════════════════════════════════════");
            text.AppendLine("         VOLTRIS OPTIMIZER - HISTÓRICO DE OTIMIZAÇÕES");
            text.AppendLine("═══════════════════════════════════════════════════════════");
            text.AppendLine($"Gerado em: {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
            text.AppendLine($"Total de registros: {history.Count}");
            text.AppendLine("═══════════════════════════════════════════════════════════");
            text.AppendLine();
            
            foreach (var entry in history)
            {
                text.AppendLine($"[{entry.Timestamp:dd/MM/yyyy HH:mm:ss}] {(entry.Success ? "✓" : "✗")} {entry.ActionType}");
                text.AppendLine($"  Descrição: {entry.Description}");
                if (entry.Duration.TotalSeconds > 0)
                    text.AppendLine($"  Duração: {entry.Duration:mm\\:ss}");
                if (entry.SpaceFreed > 0)
                    text.AppendLine($"  Espaço liberado: {FormatBytes(entry.SpaceFreed)}");
                text.AppendLine();
            }
            
            System.IO.File.WriteAllText(filePath, text.ToString(), System.Text.Encoding.UTF8);
        }
    }

    public class HistoryItemViewModel
    {
        public string ActionType { get; set; } = "";
        public string Description { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public TimeSpan Duration { get; set; }
        public long SpaceFreed { get; set; }
        public string SpaceFreedFormatted { get; set; } = "";
        public bool Success { get; set; }
        public string Icon { get; set; } = "";
    }
}

