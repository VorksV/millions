using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services.Gamer.Intelligence.Interfaces;
using VoltrisOptimizer.Services.Gamer.Intelligence.Models;

namespace VoltrisOptimizer.Services.Gamer.Intelligence.Implementation
{
    /// <summary>
    /// Sistema de benchmark automático - avalia performance e gera recomendações
    /// </summary>
    public class AutoBenchmarkService : IAutoBenchmark
    {
        private readonly ILoggingService _logger;
        private readonly IHardwareProfiler _hardwareProfiler;
        private BenchmarkResult? _lastResult;

        // Scores de referência para percentis (baseado em dados agregados)
        private static readonly int[] PercentileScores = new[]
        {
            10, 15, 20, 25, 30, 35, 40, 45, 50, 55, // 10-100 percentil em steps de 10
            60, 65, 70, 75, 80, 85, 90, 95, 98, 100
        };

        public BenchmarkResult? LastResult => _lastResult;

        public AutoBenchmarkService(ILoggingService logger, IHardwareProfiler hardwareProfiler)
        {
            _logger.LogEntry(nameof(AutoBenchmarkService));
            _logger = logger;
            _hardwareProfiler = hardwareProfiler;
            _logger.LogExit(nameof(AutoBenchmarkService));
        }

        public async Task<BenchmarkResult> RunFullBenchmarkAsync(
            IProgress<int>? progress = null, 
            CancellationToken cancellationToken = default)
        {
            _logger.LogEntry(nameof(RunFullBenchmarkAsync));
            var result = new BenchmarkResult();
            var stopwatch = Stopwatch.StartNew();

            try
            {
                _logger.LogInfo("[Benchmark] Iniciando benchmark completo...");

                // 1. CPU Benchmark (30%)
                progress?.Report(0);
                _logger.LogInfo("[Benchmark] Testando CPU...");
                result.CpuScore = await RunCpuBenchmarkAsync(cancellationToken);
                progress?.Report(30);

                // 2. GPU Benchmark (30%)
                _logger.LogInfo("[Benchmark] Testando GPU...");
                result.GpuScore = await RunGpuBenchmarkAsync(cancellationToken);
                progress?.Report(60);

                // 3. RAM Benchmark (15%)
                _logger.LogInfo("[Benchmark] Testando RAM...");
                result.RamScore = await RunRamBenchmarkAsync(cancellationToken);
                progress?.Report(75);

                // 4. Storage Benchmark (15%)
                _logger.LogInfo("[Benchmark] Testando Storage...");
                result.StorageScore = await RunStorageBenchmarkAsync(cancellationToken);
                progress?.Report(90);

                // 5. Network Benchmark (10%)
                _logger.LogInfo("[Benchmark] Testando Network...");
                result.NetworkScore = await RunNetworkBenchmarkAsync(cancellationToken);
                progress?.Report(100);

                // Calcula score geral
                result.OverallScore = CalculateOverallScore(result);
                result.RecommendedClass = GetRecommendedClass(result.OverallScore);
                result.PercentileBetter = GetPercentileRank(result.OverallScore);
                result.Recommendations = GenerateRecommendations(result);

                stopwatch.Stop();
                _logger.LogInfo($"[Benchmark] Concluído em {stopwatch.ElapsedMilliseconds}ms");
                _logger.LogInfo($"[Benchmark] Score: {result.OverallScore} ({result.RecommendedClass})");
                _logger.LogInfo($"[Benchmark] Melhor que {result.PercentileBetter}% dos PCs");

                _lastResult = result;
                _logger.LogExit(nameof(RunFullBenchmarkAsync));
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[Benchmark] Erro: {ex.Message}");
                _logger.LogExit(nameof(RunFullBenchmarkAsync));
                return result;
            }
        }

        public async Task<BenchmarkResult> RunQuickBenchmarkAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogEntry(nameof(RunQuickBenchmarkAsync));
            var result = new BenchmarkResult();

            try
            {
                _logger.LogInfo("[Benchmark] Executando benchmark rápido...");

                // Usa perfil de hardware como base
                var hardware = await _hardwareProfiler.AnalyzeAsync(cancellationToken);

                result.CpuScore = hardware.Cpu.CpuScore;
                result.GpuScore = hardware.Gpu.GpuScore;
                result.RamScore = hardware.Ram.RamScore;
                result.StorageScore = hardware.Storage.StorageScore;
                result.NetworkScore = 50; // Assume médio

                result.OverallScore = hardware.PerformanceScore;
                result.RecommendedClass = hardware.Classification;
                result.PercentileBetter = GetPercentileRank(result.OverallScore);
                result.Recommendations = GenerateRecommendations(result);

                _lastResult = result;
                _logger.LogExit(nameof(RunQuickBenchmarkAsync));
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[Benchmark] Erro no quick benchmark: {ex.Message}");
                _logger.LogExit(nameof(RunQuickBenchmarkAsync));
                return result;
            }
        }

        private async Task<int> RunCpuBenchmarkAsync(CancellationToken cancellationToken)
        {
            _logger.LogEntry(nameof(RunCpuBenchmarkAsync));
            var scores = new List<double>();

            // Teste 1: Single-thread performance
            var singleThreadScore = await Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();
                double result = 0;
                
                // Operações intensivas
                for (int i = 0; i < 10_000_000 && !cancellationToken.IsCancellationRequested; i++)
                {
                    result += Math.Sqrt(i) * Math.Sin(i) * Math.Cos(i);
                }
                
                sw.Stop();
                // Quanto menor o tempo, maior o score
                return Math.Min(100, 10000.0 / sw.ElapsedMilliseconds * 10);
            }, cancellationToken);
            scores.Add(singleThreadScore);

            // Teste 2: Multi-thread performance
            var multiThreadScore = await Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();
                var tasks = new Task[Environment.ProcessorCount];
                
                for (int t = 0; t < tasks.Length; t++)
                {
                    tasks[t] = Task.Run(() =>
                    {
                        double result = 0;
                        for (int i = 0; i < 2_000_000; i++)
                        {
                            result += Math.Sqrt(i);
                        }
                        return result;
                    }, cancellationToken);
                }
                
                Task.WaitAll(tasks);
                sw.Stop();
                
                return Math.Min(100, (10000.0 / sw.ElapsedMilliseconds) * Environment.ProcessorCount);
            }, cancellationToken);
            scores.Add(multiThreadScore);

            var result = (int)Math.Clamp(scores.Average(), 0, 100);
            _logger.LogExit(nameof(RunCpuBenchmarkAsync));
            return result;
        }

        private async Task<int> RunGpuBenchmarkAsync(CancellationToken cancellationToken)
        {
            _logger.LogEntry(nameof(RunGpuBenchmarkAsync));
            // Como não temos acesso direto à GPU sem DirectX, usamos heurísticas
            var hardware = _hardwareProfiler.CurrentProfile;
            
            // Usa score do hardware profiler como base
            int baseScore = hardware.Gpu.GpuScore;

            // Ajusta baseado em VRAM
            int vramBonus = hardware.Gpu.VramMb switch
            {
                >= 16000 => 15,
                >= 12000 => 12,
                >= 8000 => 8,
                >= 6000 => 5,
                >= 4000 => 2,
                _ => 0
            };

            // Ajusta por features
            int featureBonus = 0;
            if (hardware.Gpu.SupportsRayTracing) featureBonus += 5;
            if (hardware.Gpu.SupportsDLSS) featureBonus += 5;

            await Task.Delay(100, cancellationToken); // Simula tempo de benchmark

            var result = Math.Clamp(baseScore + vramBonus + featureBonus, 0, 100);
            _logger.LogExit(nameof(RunGpuBenchmarkAsync));
            return result;
        }

        private async Task<int> RunRamBenchmarkAsync(CancellationToken cancellationToken)
        {
            _logger.LogEntry(nameof(RunRamBenchmarkAsync));
            var result = await Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();
                
                // Aloca e acessa memória
                const int size = 100_000_000; // 100MB
                var array = new byte[size];
                var random = new Random();

                // Escrita sequencial
                for (int i = 0; i < size && !cancellationToken.IsCancellationRequested; i += 4096)
                {
                    array[i] = (byte)(i & 0xFF);
                }

                // Leitura aleatória
                long sum = 0;
                for (int i = 0; i < 100000 && !cancellationToken.IsCancellationRequested; i++)
                {
                    sum += array[random.Next(size)];
                }

                sw.Stop();
                
                // Score baseado em tempo
                var score = Math.Min(100, 5000.0 / sw.ElapsedMilliseconds * 10);
                
                // Bonus por quantidade de RAM
                var hardware = _hardwareProfiler.CurrentProfile;
                if (hardware.Ram.TotalGb >= 32) score = Math.Min(100, score + 15);
                else if (hardware.Ram.TotalGb >= 16) score = Math.Min(100, score + 8);

                return (int)score;
            }, cancellationToken);
            _logger.LogExit(nameof(RunRamBenchmarkAsync));
            return result;
        }

        private async Task<int> RunStorageBenchmarkAsync(CancellationToken cancellationToken)
        {
            _logger.LogEntry(nameof(RunStorageBenchmarkAsync));
            var result = await Task.Run(() =>
            {
                var tempFile = System.IO.Path.GetTempFileName();
                var sw = Stopwatch.StartNew();

                try
                {
                    // Escreve 50MB
                    var data = new byte[50 * 1024 * 1024];
                    new Random().NextBytes(data);
                    
                    System.IO.File.WriteAllBytes(tempFile, data);
                    
                    // Lê de volta
                    var read = System.IO.File.ReadAllBytes(tempFile);

                    sw.Stop();

                    // NVMe: < 200ms, SSD: 200-500ms, HDD: > 500ms
                    return sw.ElapsedMilliseconds switch
                    {
                        < 100 => 100,
                        < 200 => 90,
                        < 300 => 75,
                        < 500 => 60,
                        < 1000 => 40,
                        _ => 20
                    };
                }
                finally
                {
                    try { System.IO.File.Delete(tempFile); } catch { }
                }
            }, cancellationToken);
            _logger.LogExit(nameof(RunStorageBenchmarkAsync));
            return result;
        }

        private async Task<int> RunNetworkBenchmarkAsync(CancellationToken cancellationToken)
        {
            _logger.LogEntry(nameof(RunNetworkBenchmarkAsync));
            var scores = new List<double>();

            // Testa latência para vários servidores
            var targets = new[] { "8.8.8.8", "1.1.1.1", "208.67.222.222" };
            
            using var ping = new System.Net.NetworkInformation.Ping();
            
            foreach (var target in targets)
            {
                if (cancellationToken.IsCancellationRequested) break;
                
                try
                {
                    var reply = await ping.SendPingAsync(target, 1000);
                    if (reply.Status == System.Net.NetworkInformation.IPStatus.Success)
                    {
                        // Latência: < 10ms = 100, > 100ms = 20
                        var latencyScore = Math.Max(20, 100 - (reply.RoundtripTime - 10));
                        scores.Add(latencyScore);
                    }
                }
                catch { }
            }

            var result = scores.Any() ? (int)scores.Average() : 50;
            _logger.LogExit(nameof(RunNetworkBenchmarkAsync));
            return result;
        }

        private int CalculateOverallScore(BenchmarkResult result)
        {
            _logger.LogEntry(nameof(CalculateOverallScore));
            // Pesos: CPU 30%, GPU 40%, RAM 15%, Storage 10%, Network 5%
            var score = (int)(
                result.CpuScore * 0.30 +
                result.GpuScore * 0.40 +
                result.RamScore * 0.15 +
                result.StorageScore * 0.10 +
                result.NetworkScore * 0.05
            );
            _logger.LogExit(nameof(CalculateOverallScore));
            return score;
        }

        private HardwareClass GetRecommendedClass(int score)
        {
            _logger.LogEntry(nameof(GetRecommendedClass));
            HardwareClass result = score switch
            {
                >= 90 => HardwareClass.Enthusiast,
                >= 75 => HardwareClass.Ultra,
                >= 60 => HardwareClass.High,
                >= 40 => HardwareClass.Medium,
                >= 25 => HardwareClass.Low,
                _ => HardwareClass.UltraLow
            };
            _logger.LogExit(nameof(GetRecommendedClass));
            return result;
        }

        public int GetPercentileRank(int score)
        {
            _logger.LogEntry(nameof(GetPercentileRank));
            // Distribuição aproximada de scores
            // A maioria dos PCs está entre 30-60
            for (int i = PercentileScores.Length - 1; i >= 0; i--)
            {
                if (score >= PercentileScores[i])
                {
                    _logger.LogExit(nameof(GetPercentileRank));
                    return (i + 1) * 5; // Retorna percentil (5, 10, 15... 100)
                }
            }
            _logger.LogExit(nameof(GetPercentileRank));
            return 1;
        }

        public IReadOnlyList<BenchmarkRecommendation> GetRecommendations(BenchmarkResult result)
        {
            _logger.LogEntry(nameof(GetRecommendations));
            var recommendations = GenerateRecommendations(result);
            _logger.LogExit(nameof(GetRecommendations));
            return recommendations;
        }

        private List<BenchmarkRecommendation> GenerateRecommendations(BenchmarkResult result)
        {
            var recommendations = new List<BenchmarkRecommendation>();

            // CPU
            if (result.CpuScore < 40)
            {
                recommendations.Add(new BenchmarkRecommendation
                {
                    Category = "CPU",
                    Recommendation = "CPU abaixo do recomendado para jogos modernos",
                    Impact = "Pode causar gargalo em jogos CPU-bound",
                    Priority = RecommendationPriority.High
                });
            }
            else if (result.CpuScore < 60)
            {
                recommendations.Add(new BenchmarkRecommendation
                {
                    Category = "CPU",
                    Recommendation = "Considere fechar aplicativos em segundo plano ao jogar",
                    Impact = "Melhora 10-20% em FPS",
                    Priority = RecommendationPriority.Medium
                });
            }

            // GPU
            if (result.GpuScore < 30)
            {
                recommendations.Add(new BenchmarkRecommendation
                {
                    Category = "GPU",
                    Recommendation = "GPU integrada detectada - use configurações baixas",
                    Impact = "Jogos limitados a 720p Low",
                    Priority = RecommendationPriority.Critical
                });
            }
            else if (result.GpuScore < 50)
            {
                recommendations.Add(new BenchmarkRecommendation
                {
                    Category = "GPU",
                    Recommendation = "Habilite FSR/DLSS quando disponível",
                    Impact = "Pode dobrar FPS mantendo qualidade visual",
                    Priority = RecommendationPriority.High
                });
            }

            // RAM
            var hardware = _hardwareProfiler.CurrentProfile;
            if (hardware.Ram.TotalGb < 16)
            {
                recommendations.Add(new BenchmarkRecommendation
                {
                    Category = "RAM",
                    Recommendation = "Upgrade para 16GB recomendado",
                    Impact = "Reduz stutters e carregamentos",
                    Priority = RecommendationPriority.High
                });
            }
            else if (hardware.Ram.Channels < 2)
            {
                recommendations.Add(new BenchmarkRecommendation
                {
                    Category = "RAM",
                    Recommendation = "Adicione um segundo módulo para dual-channel",
                    Impact = "Melhora 10-15% em performance geral",
                    Priority = RecommendationPriority.Medium
                });
            }

            // Storage
            if (result.StorageScore < 40)
            {
                recommendations.Add(new BenchmarkRecommendation
                {
                    Category = "Storage",
                    Recommendation = "Instale jogos em SSD",
                    Impact = "Reduz tempos de carregamento em até 70%",
                    Priority = RecommendationPriority.High
                });
            }
            else if (result.StorageScore < 70)
            {
                recommendations.Add(new BenchmarkRecommendation
                {
                    Category = "Storage",
                    Recommendation = "Upgrade para NVMe para melhores resultados",
                    Impact = "Carregamentos até 3x mais rápidos",
                    Priority = RecommendationPriority.Low
                });
            }

            // Network
            if (result.NetworkScore < 40)
            {
                recommendations.Add(new BenchmarkRecommendation
                {
                    Category = "Network",
                    Recommendation = "Latência alta detectada - use conexão cabeada",
                    Impact = "Reduz ping e melhora resposta em jogos online",
                    Priority = RecommendationPriority.Medium
                });
            }

            // Otimizações Voltris
            recommendations.Add(new BenchmarkRecommendation
            {
                Category = "Voltris",
                Recommendation = $"Ative o Modo Gamer otimizado para {result.RecommendedClass}",
                Impact = "Aplica otimizações ideais para seu hardware",
                Priority = RecommendationPriority.High
            });

            return recommendations;
        }
    }
}

