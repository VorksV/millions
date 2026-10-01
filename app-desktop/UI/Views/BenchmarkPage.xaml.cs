using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VoltrisOptimizer.Core.Benchmark;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Performance;
using Microsoft.Extensions.DependencyInjection;

namespace VoltrisOptimizer.UI.Views
{
    public partial class BenchmarkPage : UserControl
    {
        private readonly BenchmarkService _benchmarkService = new();
        private readonly BenchmarkComparer _comparer = new();
        private BenchmarkFullResult? _initialResult;
        private BenchmarkFullResult? _finalResult;
        private CancellationTokenSource? _cts;
        private bool _isRunning;

        public bool IsRunning
        {
            get => _isRunning;
            set
            {
                _isRunning = value;
Dispatcher.InvokeAsync(() =>
                    {
                        BtnInitial.IsEnabled = !value;
                        BtnFinal.IsEnabled = !value;
                        BtnComplete.IsEnabled = !value;
                        BtnCompare.IsEnabled = !value && _initialResult != null && _finalResult != null;
                    });
            }
        }

        public BenchmarkPage()
        {
            InitializeComponent();
            DataContext = this;
        }

        public bool IsNotRunning => !_isRunning;
        public bool CanCompare => !_isRunning && _initialResult != null && _finalResult != null;

        private async void BtnInitial_Click(object sender, RoutedEventArgs e)
        {
            await RunBenchmarkAsync("Benchmark Inicial", r => _initialResult = r);
        }

        private async void BtnFinal_Click(object sender, RoutedEventArgs e)
        {
            await RunBenchmarkAsync("Benchmark Final", r => _finalResult = r);
        }

        private void BtnCompare_Click(object sender, RoutedEventArgs e)
        {
            if (_initialResult == null || _finalResult == null) return;
            ShowComparison();
        }

        private async void BtnComplete_Click(object sender, RoutedEventArgs e)
        {
            await RunCompleteBenchmarkAsync();
        }

        private void BtnTutorial_Click(object sender, RoutedEventArgs e)
        {
            PanelTutorial.Visibility = PanelTutorial.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void BtnCloseTutorial_Click(object sender, RoutedEventArgs e)
        {
            PanelTutorial.Visibility = Visibility.Collapsed;
        }

        private async Task RunBenchmarkAsync(string label, Action<BenchmarkFullResult> onComplete)
        {
            if (_isRunning) return;
            IsRunning = true;
            _cts = new CancellationTokenSource();

            App.LoggingService?.LogInfo($"[Benchmark] Iniciando benchmark: {label}");
            GlobalProgressService.Instance.StartOperation($"Benchmark: {label}", isPriority: true);

            try
            {
                var progress = new Progress<(string Message, double Percent)>(p =>
                {
                    Dispatcher.InvokeAsync(() =>
                    {
                        TxtStatus.Text = p.Message;
                        TxtPercent.Text = $"{p.Percent:F0}%";
                        ProgressBenchmark.Value = p.Percent;
                    });
                    GlobalProgressService.Instance.UpdateProgress((int)p.Percent, $"Benchmark: {p.Message}");
                });

                var result = await _benchmarkService.RunFullBenchmarkAsync(label, progress, _cts.Token);
                onComplete(result);

                App.LoggingService?.LogInfo($"[Benchmark] {label} concluído — Score: {result.OverallScore:F2} (CPU:{result.CpuScore:F1} MEM:{result.MemoryScore:F1} DISK:{result.DiskScore:F1} SCHED:{result.SchedulerScore:F1} UI:{result.UiScore:F1})");

                Dispatcher.InvokeAsync(() => DisplayResult(result));
            }
            catch (OperationCanceledException)
            {
                App.LoggingService?.LogWarning($"[Benchmark] {label} cancelado pelo usuário.");
                Dispatcher.InvokeAsync(() => TxtStatus.Text = LocalizationService.Instance.GetString("BenchmarkCancelledLabel"));
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[Benchmark] Erro durante {label}: {ex.Message}", ex);
                Dispatcher.InvokeAsync(() => TxtStatus.Text = string.Format(LocalizationService.Instance.GetString("BenchmarkErrorFormat"), ex.Message));
            }
            finally
            {
                IsRunning = false;
                GlobalProgressService.Instance.CompleteOperation($"Benchmark {label} concluído");
                _cts?.Dispose();
                _cts = null;
            }
        }

        private async Task RunCompleteBenchmarkAsync()
        {
            if (_isRunning) return;
            IsRunning = true;
            _cts = new CancellationTokenSource();

            App.LoggingService?.LogInfo("[Benchmark] Iniciando Benchmark Completo (fluxo automático 4 fases).");
            GlobalProgressService.Instance.StartOperation(VoltrisOptimizer.Services.LocalizationService.Instance.GetString("BenchmarkOpStart"), isPriority: true);

            try
            {
                // Fase 1 progress: 0-25%
                var progress1 = new Progress<(string Message, double Percent)>(p =>
                {
                    Dispatcher.InvokeAsync(() =>
                    {
                        TxtStatus.Text = p.Message;
                        TxtPercent.Text = $"{p.Percent:F0}%";
                        ProgressBenchmark.Value = p.Percent;
                    });
                    int globalPct = (int)(p.Percent * 0.25);
                    GlobalProgressService.Instance.UpdateProgress(globalPct, $"Fase 1/4: {p.Message}");
                });

                // 1. Benchmark Inicial
                App.LoggingService?.LogInfo("[Benchmark] Fase 1/4: Benchmark Inicial...");
                Dispatcher.InvokeAsync(() => TxtStatus.Text = LocalizationService.Instance.GetString("BenchmarkPhase1Start"));
                _initialResult = await _benchmarkService.RunFullBenchmarkAsync("Benchmark Inicial (Auto)", progress1, _cts.Token);
                App.LoggingService?.LogInfo($"[Benchmark] Fase 1/4 concluída — Score Inicial: {_initialResult.OverallScore:F2}");
                Dispatcher.InvokeAsync(() => DisplayResult(_initialResult));

                // 2. Aplicar Otimizações (25-50%)
                App.LoggingService?.LogInfo("[Benchmark] Fase 2/4: Aplicando otimizações...");
                GlobalProgressService.Instance.UpdateProgress(30, "Fase 2/4: Aplicando otimizações...");
                Dispatcher.InvokeAsync(() =>
                {
                    TxtStatus.Text = LocalizationService.Instance["Loc_Fase24AplicandoOtimizacoes"];
                    ProgressBenchmark.Value = 0;
                });

                await ApplyOptimizationsAsync();
                GlobalProgressService.Instance.UpdateProgress(50, "Fase 2/4: Otimizações aplicadas");
                App.LoggingService?.LogInfo("[Benchmark] Fase 2/4 concluída — Otimizações aplicadas.");

                // 3. Aguardar estabilização (50-75%)
                App.LoggingService?.LogInfo("[Benchmark] Fase 3/4: Aguardando estabilização (15s)...");
                Dispatcher.InvokeAsync(() => TxtStatus.Text = LocalizationService.Instance["Loc_AguardandoEstabilizacaoDoSistema15s"]);
                for (int i = 0; i < 15; i++)
                {
                    await Task.Delay(1000, _cts.Token);
                    int pct = 50 + (int)((i + 1) / 15.0 * 25);
                    GlobalProgressService.Instance.UpdateProgress(pct, $"Fase 3/4: Estabilizando... ({15 - i - 1}s)");
                }

                // 4. Benchmark Final (75-100%)
                var progress4 = new Progress<(string Message, double Percent)>(p =>
                {
                    Dispatcher.InvokeAsync(() =>
                    {
                        TxtStatus.Text = p.Message;
                        TxtPercent.Text = $"{p.Percent:F0}%";
                        ProgressBenchmark.Value = p.Percent;
                    });
                    int globalPct = 75 + (int)(p.Percent * 0.25);
                    GlobalProgressService.Instance.UpdateProgress(globalPct, $"Fase 4/4: {p.Message}");
                });

                App.LoggingService?.LogInfo("[Benchmark] Fase 4/4: Benchmark Final...");
                Dispatcher.InvokeAsync(() =>
                {
                    TxtStatus.Text = LocalizationService.Instance.GetString("BenchmarkPhase4Start");
                    ProgressBenchmark.Value = 0;
                });
                _finalResult = await _benchmarkService.RunFullBenchmarkAsync("Benchmark Final (Auto)", progress4, _cts.Token);
                App.LoggingService?.LogInfo($"[Benchmark] Fase 4/4 concluída — Score Final: {_finalResult.OverallScore:F2}");
                Dispatcher.InvokeAsync(() => DisplayResult(_finalResult));

                // 5. Comparação
                Dispatcher.InvokeAsync(() =>
                {
                    TxtStatus.Text = LocalizationService.Instance["Loc_GerandoComparacao"];
                    ShowComparison();
                    TxtStatus.Text = LocalizationService.Instance.GetString("BenchmarkFinished");
                });

                var gain = _finalResult.OverallScore - _initialResult.OverallScore;
                App.LoggingService?.LogInfo($"[Benchmark] Benchmark Completo finalizado — Inicial: {_initialResult.OverallScore:F2} → Final: {_finalResult.OverallScore:F2} (Δ {gain:+0.00;-0.00})");
            }
            catch (OperationCanceledException)
            {
                App.LoggingService?.LogWarning("[Benchmark] Benchmark Completo cancelado pelo usuário.");
                Dispatcher.InvokeAsync(() => TxtStatus.Text = LocalizationService.Instance.GetString("BenchmarkCancelledLabel"));
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[Benchmark] Erro no Benchmark Completo: {ex.Message}", ex);
                Dispatcher.InvokeAsync(() => TxtStatus.Text = string.Format(LocalizationService.Instance.GetString("BenchmarkErrorFormat"), ex.Message));
            }
            finally
            {
                IsRunning = false;
                GlobalProgressService.Instance.CompleteOperation(VoltrisOptimizer.Services.LocalizationService.Instance.GetString("BenchmarkOpEnd"));
                _cts?.Dispose();
                _cts = null;
            }
        }

        private async Task ApplyOptimizationsAsync()
        {
            try
            {
                var perfService = App.Services?.GetService<IPerformanceOptimizationService>();
                if (perfService != null)
                {
                    await perfService.ApplyRecommendedOptimizationsAsync();
                }
                else
                {
                    App.LoggingService?.LogWarning("[BenchmarkPage] Serviço de otimização não disponível.");
                }
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError("[BenchmarkPage] Erro ao aplicar otimizações", ex);
            }
        }

        private void DisplayResult(BenchmarkFullResult result)
        {
            PanelScore.Visibility = Visibility.Visible;

            TxtOverallScore.Text = $"{result.OverallScore:F2}";
            TxtScoreLabel.Text = result.Label;

            // Normalizar scores para barras (cap em 100 para visualização)
            double Norm(double v) => Math.Min(100, Math.Max(0, v));

            BarCpu.Value = Norm(result.CpuScore);
            TxtCpuScore.Text = $"{result.CpuScore:F1}";

            BarMemory.Value = Norm(result.MemoryScore);
            TxtMemoryScore.Text = $"{result.MemoryScore:F1}";

            BarDisk.Value = Norm(result.DiskScore);
            TxtDiskScore.Text = $"{result.DiskScore:F1}";

            BarScheduler.Value = Norm(result.SchedulerScore);
            TxtSchedulerScore.Text = $"{result.SchedulerScore:F1}";

            BarUi.Value = Norm(result.UiScore);
            TxtUiScore.Text = $"{result.UiScore:F1}";

            // Throttling
            if (result.ThrottlingDetected)
            {
                PanelThrottling.Visibility = Visibility.Visible;
                TxtThrottlingInfo.Text = string.Format(LocalizationService.Instance.GetString("BenchmarkThrottlingDetail"), $"{result.MinCpuFreqPercent:F0}", $"{result.MaxCpuFreqPercent:F0}");
            }
            else
            {
                PanelThrottling.Visibility = Visibility.Collapsed;
            }
        }

        private void ShowComparison()
        {
            if (_initialResult == null || _finalResult == null) return;

            var cmp = _comparer.Compare(_initialResult, _finalResult);
            PanelComparison.Visibility = Visibility.Visible;

            SetCompRow(CmpCpuBefore, CmpCpuAfter, CmpCpuGain, _initialResult.CpuScore, _finalResult.CpuScore, cmp.CpuImprovement);
            SetCompRow(CmpMemBefore, CmpMemAfter, CmpMemGain, _initialResult.MemoryScore, _finalResult.MemoryScore, cmp.MemoryImprovement);
            SetCompRow(CmpDiskBefore, CmpDiskAfter, CmpDiskGain, _initialResult.DiskScore, _finalResult.DiskScore, cmp.DiskImprovement);
            SetCompRow(CmpSchedBefore, CmpSchedAfter, CmpSchedGain, _initialResult.SchedulerScore, _finalResult.SchedulerScore, cmp.SchedulerImprovement);
            SetCompRow(CmpOverallBefore, CmpOverallAfter, CmpOverallGain, _initialResult.OverallScore, _finalResult.OverallScore, cmp.OverallImprovement);
        }

        private void SetCompRow(System.Windows.Controls.TextBlock before, System.Windows.Controls.TextBlock after,
            System.Windows.Controls.TextBlock gain, double bVal, double aVal, double improvement)
        {
            before.Text = $"{bVal:F1}";
            after.Text = $"{aVal:F1}";
            gain.Text = $"{improvement:+0.0;-0.0;0.0}%";
            gain.Foreground = improvement >= 0
                ? new SolidColorBrush(Color.FromRgb(16, 185, 129))  // Green
                : new SolidColorBrush(Color.FromRgb(239, 68, 68));  // Red
        }
    }
}
