using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Gamer.GameCategorization.Models;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services.Gamer.GameCategorization
{
    public class GameCategorizerService : IGameCategorizerEngine
    {
        private readonly ILoggingService _logger;

        // Dicionário de jogos competitivos conhecidos (hard-coded heurístico inicial)
        private readonly HashSet<string> _competitiveExecutables = new(StringComparer.OrdinalIgnoreCase)
        {
            "cs2.exe", "csgo.exe", "valorant-win64-shipping.exe", "rainbowsix.exe", 
            "rainbowsix_vulkan.exe", "fortniteclient-win64-shipping.exe", "r5apex.exe", 
            "tslgame.exe", "dota2.exe", "league of legends.exe", "overwatch.exe"
        };

        // Simulação
        private readonly HashSet<string> _simulationExecutables = new(StringComparer.OrdinalIgnoreCase)
        {
            "flightsimulator.exe", "assettocorsa.exe", "acs.exe", "eurotrucks2.exe", 
            "amtrucks.exe", "x-plane.exe", "dcs.exe"
        };

        public GameCategorizerService(ILoggingService logger)
        {
            _logger = logger;
            _logger.LogEntry(nameof(GameCategorizerService));
            _logger.LogExit(nameof(GameCategorizerService));
        }

        public Task<GameProfileAnalysis> AnalyzeGameAsync(string executablePath, CancellationToken cancellationToken = default)
        {
            _logger.LogEntry(nameof(AnalyzeGameAsync));
            return Task.Run(() =>
            {
                var analysis = new GameProfileAnalysis
                {
                    ExecutablePath = executablePath,
                    GameName = Path.GetFileNameWithoutExtension(executablePath),
                    ConfidenceLevel = 0
                };

                if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
                {
                    _logger.LogWarning($"[GameCategorizer] Caminho inválido ou arquivo não existe: {executablePath}");
                    _logger.LogExit(nameof(AnalyzeGameAsync));
                    return analysis;
                }

                try
                {
                    DetectCategory(analysis);
                    DetectEngine(analysis);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[GameCategorizer] Erro analisando {executablePath}: {ex.Message}");
                }

                _logger.LogInfo($"[GameCategorizer] {analysis.GameName} -> Categoria: {analysis.Category}, Engine: {analysis.Engine} (Confiança: {analysis.ConfidenceLevel}%)");
                _logger.LogExit(nameof(AnalyzeGameAsync));
                return analysis;
            }, cancellationToken);
        }

        private void DetectCategory(GameProfileAnalysis analysis)
        {
            _logger.LogEntry(nameof(DetectCategory));
            string fileName = Path.GetFileName(analysis.ExecutablePath);

            if (_competitiveExecutables.Contains(fileName))
            {
                analysis.Category = GameCategory.Competitive;
                analysis.ConfidenceLevel += 40;
                _logger.LogExit(nameof(DetectCategory));
                return;
            }

            if (_simulationExecutables.Contains(fileName))
            {
                analysis.Category = GameCategory.Simulation;
                analysis.ConfidenceLevel += 40;
                _logger.LogExit(nameof(DetectCategory));
                return;
            }

            // Se for um executável muito pequeno (Minecraft Launcher, etc), pode ser Leve.
            try
            {
                var fileInfo = new FileInfo(analysis.ExecutablePath);
                if (fileInfo.Length < 1024 * 1024 * 2 && (fileName.Contains("minecraft") || fileName.Contains("terraria")))
                {
                    analysis.Category = GameCategory.Lightweight;
                    analysis.ConfidenceLevel += 20;
                    _logger.LogExit(nameof(DetectCategory));
                    return;
                }
            }
            catch { }

            // Por padrão, assumimos AAA para jogos modernos desconhecidos que ativam o Modo Gamer.
            analysis.Category = GameCategory.AAA;
            analysis.ConfidenceLevel += 10; // Baixa confiança, assumido por padrão
            _logger.LogExit(nameof(DetectCategory));
        }

        private void DetectEngine(GameProfileAnalysis analysis)
        {
            _logger.LogEntry(nameof(DetectEngine));
            var dir = Path.GetDirectoryName(analysis.ExecutablePath);
            if (string.IsNullOrEmpty(dir)) { _logger.LogExit(nameof(DetectEngine)); return; }

            var dirInfo = new DirectoryInfo(dir);
            var parentDir = dirInfo.Parent;
            var grandparentDir = parentDir?.Parent;

            // 1. UNITY
            // Unity geralmente tem "UnityPlayer.dll" e uma pasta "NomeDoJogo_Data"
            if (File.Exists(Path.Combine(dir, "UnityPlayer.dll")) || 
                Directory.GetDirectories(dir, "*_Data").Any())
            {
                analysis.Engine = GameEngineType.Unity;
                analysis.ConfidenceLevel += 50;
                _logger.LogExit(nameof(DetectEngine));
                return;
            }

            // 2. UNREAL ENGINE (4 & 5)
            // Unreal geralmente fica em "Jogo\Binaries\Win64"
            if (dir.Contains(@"\Binaries\Win64", StringComparison.OrdinalIgnoreCase) ||
                File.Exists(Path.Combine(dir, "CrashReportClient.exe")))
            {
                // Diferenciar UE4 de UE5: UE5 frequentemente tem pastas "UnrealGame" ou arquivos .pak estruturados de forma levemente diferente,
                // mas podemos buscar por "UnrealEngine5" strings nas DLLs. 
                // Por heurística simples, assumiremos UE4 a menos que achemos pistas de UE5.
                analysis.Engine = GameEngineType.UnrealEngine4;
                analysis.ConfidenceLevel += 40;
                
                // Se o nome do executável tiver "Win64-Shipping", é um traço forte de UE.
                if (analysis.ExecutablePath.Contains("Shipping", StringComparison.OrdinalIgnoreCase))
                    analysis.ConfidenceLevel += 10;
                
                _logger.LogExit(nameof(DetectEngine));
                return;
            }

            // 3. SOURCE / SOURCE 2
            if (File.Exists(Path.Combine(dir, "tier0.dll")) && File.Exists(Path.Combine(dir, "vstdlib.dll")))
            {
                if (File.Exists(Path.Combine(dir, "engine2.dll")))
                {
                    analysis.Engine = GameEngineType.Source2; // Ex: CS2
                }
                else
                {
                    analysis.Engine = GameEngineType.Source; // Ex: CSGO
                }
                analysis.ConfidenceLevel += 50;
                _logger.LogExit(nameof(DetectEngine));
                return;
            }

            // 4. FROSTBITE
            if (Directory.GetFiles(dir, "*.par").Any() || Directory.GetFiles(dir, "*.sb").Any() ||
                Directory.GetFiles(dir, "*.cas").Any() ||
                analysis.ExecutablePath.Contains("bf1.exe", StringComparison.OrdinalIgnoreCase) ||
                analysis.ExecutablePath.Contains("bfv.exe", StringComparison.OrdinalIgnoreCase) ||
                analysis.ExecutablePath.Contains("bf2042.exe", StringComparison.OrdinalIgnoreCase))
            {
                analysis.Engine = GameEngineType.Frostbite;
                analysis.ConfidenceLevel += 40;
                _logger.LogExit(nameof(DetectEngine));
                return;
            }

            // 5. RE ENGINE
            if (File.Exists(Path.Combine(dir, "re_chunk_000.pak")))
            {
                analysis.Engine = GameEngineType.REEngine;
                analysis.ConfidenceLevel += 50;
                _logger.LogExit(nameof(DetectEngine));
                return;
            }

            // Sem detecção clara
            analysis.Engine = GameEngineType.Custom;
            _logger.LogExit(nameof(DetectEngine));
        }
    }
}
