using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using System.Windows.Media;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.DPC;

namespace VoltrisOptimizer.UI.Views
{
    public partial class DpcHealthView : UserControl
    {
        private readonly IDpcAnalyzerService _svc;
        private readonly CancellationTokenSource _cts = new();
        
        public DpcHealthView()
        {
            InitializeComponent();
            _svc = App.Services?.GetService(typeof(IDpcAnalyzerService)) as IDpcAnalyzerService ?? new DpcAnalyzerService(App.LoggingService!);
            _svc.SampleReceived += OnSample;
            _svc.Start();
            Unloaded += (_, _) => { try { _cts.Cancel(); } catch { } };
            _ = RefreshUiLoop();
        }
        private void OnSupportPack(object sender, RoutedEventArgs e)
        {
            _ = CreatePack();
        }
        private async Task CreatePack()
        {
            var path = await _svc.CreateSupportPackAsync();
            App.LoggingService?.LogSuccess("[DPC] Support pack gerado: " + path);
        }
        private void OnSample(DpcSample s)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (Chart.Items.Count > 300) Chart.Items.RemoveAt(0);
                var rect = new Rectangle { Width = 2, Height = Math.Min(100, s.DpcPercent * 2), Fill = Brushes.Lime, Margin = new Thickness(1,0,1,0), VerticalAlignment = VerticalAlignment.Bottom };
                Chart.Items.Add(rect);
            });
        }
        private async Task RefreshUiLoop()
        {
            var token = _cts.Token;
            while (!token.IsCancellationRequested)
            {
                try { await Task.Delay(1000, token); }
                catch (OperationCanceledException) { break; }
                
                try
                {
                    var r = _svc.GetLatestAnalysis();
                    StatsText.Text = string.Format(LocalizationService.Instance.GetString("DpcStatsFormat"), $"{r.Stats.Avg:F1}", $"{r.Stats.P95:F1}", $"{r.Stats.P99:F1}", r.Stats.SpikeCount);
                    RecText.Text = r.Recommendation;
                    SpikesList.ItemsSource = r.Spikes.Select(s => new { Time = s.Timestamp.ToLocalTime().ToString("HH:mm:ss"), Value = s.Value.ToString("F1") + "%", Driver = s.Driver ?? "-", Process = s.ProcessName ?? "-" }).ToList();
                }
                catch (OperationCanceledException) { break; }
                catch { }
            }
        }
    }
}
