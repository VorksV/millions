using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.Gamer.Implementation
{
    /// <summary>
    /// Memória centralizada de estado do Modo Gamer.
    /// Registra o estado original de cada configuração antes de qualquer modificação,
    /// garantindo que a restauração seja fiel ao estado pré-ativação.
    /// Política: first-write-wins — o primeiro registro de uma chave não é sobrescrito.
    /// Suporta persistência em disco para recuperação após crash.
    /// </summary>
    public class GamerStateMemory
    {
        private readonly Dictionary<string, object?> _originalStates = new();
        private static readonly string _persistPath = AppDataPaths.GamerStateMemory;

        /// <summary>
        /// Registra o estado original de uma configuração.
        /// Se a chave já existir, o valor não é sobrescrito (first-write-wins).
        /// </summary>
        public void Register(string key, object? originalValue)
        {
            VoltrisOptimizer.App.LoggingService.LogEntry(nameof(Register));
            if (!_originalStates.ContainsKey(key))
                _originalStates[key] = originalValue;
            VoltrisOptimizer.App.LoggingService.LogExit(nameof(Register));
        }

        /// <summary>
        /// Recupera o estado original registrado para uma chave.
        /// Retorna default(T) se a chave não foi registrada.
        /// </summary>
        public T? GetOriginal<T>(string key)
        {
            VoltrisOptimizer.App.LoggingService.LogEntry(nameof(GetOriginal));
            if (_originalStates.TryGetValue(key, out var value) && value is T typed)
            {
                VoltrisOptimizer.App.LoggingService.LogExit(nameof(GetOriginal));
                return typed;
            }

            // Suporte a deserialização JSON: JsonElement → tipo primitivo
            if (_originalStates.TryGetValue(key, out var raw) && raw is JsonElement element)
            {
                try
                {
                    var converted = JsonSerializer.Deserialize<T>(element.GetRawText());
                    if (converted != null)
                    {
                        VoltrisOptimizer.App.LoggingService.LogExit(nameof(GetOriginal));
                        return converted;
                    }
                }
                catch { }
            }

            VoltrisOptimizer.App.LoggingService.LogExit(nameof(GetOriginal));
            return default;
        }

        /// <summary>
        /// Retorna true se a chave foi registrada (independente do valor).
        /// </summary>
        public bool WasModifiedByGamerMode(string key)
        {
            VoltrisOptimizer.App.LoggingService.LogEntry(nameof(WasModifiedByGamerMode));
            var result = _originalStates.ContainsKey(key);
            VoltrisOptimizer.App.LoggingService.LogExit(nameof(WasModifiedByGamerMode));
            return result;
        }

        /// <summary>
        /// Retorna true se há estados registrados aguardando restauração.
        /// </summary>
        public bool HasPendingRestoration()
        {
            VoltrisOptimizer.App.LoggingService.LogEntry(nameof(HasPendingRestoration));
            var result = _originalStates.Count > 0;
            VoltrisOptimizer.App.LoggingService.LogExit(nameof(HasPendingRestoration));
            return result;
        }

        /// <summary>
        /// Limpa todos os estados registrados. Deve ser chamado no início de cada sessão.
        /// </summary>
        public void Clear()
        {
            VoltrisOptimizer.App.LoggingService.LogEntry(nameof(Clear));
            _originalStates.Clear();
            VoltrisOptimizer.App.LoggingService.LogExit(nameof(Clear));
        }

        /// <summary>
        /// Exporta o dicionário interno como snapshot serializável para persistência.
        /// </summary>
        public Dictionary<string, object?> ExportSnapshot()
        {
            VoltrisOptimizer.App.LoggingService.LogEntry(nameof(ExportSnapshot));
            var result = new Dictionary<string, object?>(_originalStates);
            VoltrisOptimizer.App.LoggingService.LogExit(nameof(ExportSnapshot));
            return result;
        }

        /// <summary>
        /// Importa um snapshot previamente exportado, restaurando o estado em memória.
        /// Usado na recuperação após crash.
        /// </summary>
        public void ImportSnapshot(Dictionary<string, object?>? snapshot)
        {
            VoltrisOptimizer.App.LoggingService.LogEntry(nameof(ImportSnapshot));
            _originalStates.Clear();
            if (snapshot != null)
            {
                foreach (var kvp in snapshot)
                    _originalStates[kvp.Key] = kvp.Value;
            }
            VoltrisOptimizer.App.LoggingService.LogExit(nameof(ImportSnapshot));
        }

        /// <summary>
        /// Persiste o estado atual em disco (JSON) para recuperação após crash.
        /// </summary>
        public async Task SaveToDiskAsync()
        {
            VoltrisOptimizer.App.LoggingService.LogEntry(nameof(SaveToDiskAsync));
            try
            {
                if (_originalStates.Count == 0)
                {
                    VoltrisOptimizer.App.LoggingService.LogExit(nameof(SaveToDiskAsync));
                    return;
                }

                AppDataPaths.EnsureDirectory(Path.GetDirectoryName(_persistPath));
                var json = JsonSerializer.Serialize(_originalStates, new JsonSerializerOptions { WriteIndented = false,
                    ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles
                });
                await File.WriteAllTextAsync(_persistPath, json);
            }
            catch
            {
                // Falha silenciosa — não deve impedir o fluxo principal
            }
            VoltrisOptimizer.App.LoggingService.LogExit(nameof(SaveToDiskAsync));
        }

        /// <summary>
        /// Carrega o estado persistido do disco. Retorna true se havia dados salvos.
        /// </summary>
        public async Task<bool> LoadFromDiskAsync()
        {
            VoltrisOptimizer.App.LoggingService.LogEntry(nameof(LoadFromDiskAsync));
            try
            {
                if (!File.Exists(_persistPath))
                {
                    VoltrisOptimizer.App.LoggingService.LogExit(nameof(LoadFromDiskAsync));
                    return false;
                }

                var json = await File.ReadAllTextAsync(_persistPath);
                var snapshot = JsonSerializer.Deserialize<Dictionary<string, object?>>(json);
                if (snapshot != null && snapshot.Count > 0)
                {
                    ImportSnapshot(snapshot);
                    VoltrisOptimizer.App.LoggingService.LogExit(nameof(LoadFromDiskAsync));
                    return true;
                }
                VoltrisOptimizer.App.LoggingService.LogExit(nameof(LoadFromDiskAsync));
                return false;
            }
            catch
            {
                VoltrisOptimizer.App.LoggingService.LogExit(nameof(LoadFromDiskAsync));
                return false;
            }
        }

        /// <summary>
        /// Remove o arquivo de persistência do disco (chamado após restauração bem-sucedida).
        /// </summary>
        public void ClearDiskState()
        {
            VoltrisOptimizer.App.LoggingService.LogEntry(nameof(ClearDiskState));
            try
            {
                if (File.Exists(_persistPath))
                    File.Delete(_persistPath);
            }
            catch { }
            VoltrisOptimizer.App.LoggingService.LogExit(nameof(ClearDiskState));
        }

        /// <summary>
        /// Obtém estatísticas de uma sessão de jogo para reporte de reward ao Brain.
        /// </summary>
        public GamingSessionStats GetSessionStats(string gameName)
        {
            VoltrisOptimizer.App.LoggingService.LogEntry(nameof(GetSessionStats));
            var duration = TimeSpan.Zero;
            var avgFps = 0.0;
            var fpsVariance = 0.0;
            var avgCpuUsage = 0.0;
            var stutterCount = 0;
            var maxCpuTemp = 0.0;

            if (_originalStates.TryGetValue($"session:{gameName}:duration", out var dur) && dur is double durVal)
                duration = TimeSpan.FromMinutes(durVal);
            if (_originalStates.TryGetValue($"session:{gameName}:avgFps", out var fps) && fps is double fpsVal)
                avgFps = fpsVal;
            if (_originalStates.TryGetValue($"session:{gameName}:fpsVariance", out var var_) && var_ is double varVal)
                fpsVariance = varVal;
            if (_originalStates.TryGetValue($"session:{gameName}:avgCpu", out var cpu) && cpu is double cpuVal)
                avgCpuUsage = cpuVal;
            if (_originalStates.TryGetValue($"session:{gameName}:stutterCount", out var st) && st is int stVal)
                stutterCount = stVal;
            if (_originalStates.TryGetValue($"session:{gameName}:maxTemp", out var temp) && temp is double tempVal)
                maxCpuTemp = tempVal;

            VoltrisOptimizer.App.LoggingService.LogExit(nameof(GetSessionStats));
            return new GamingSessionStats
            {
                Duration = duration,
                AvgFps = avgFps,
                FpsVariance = fpsVariance,
                AvgCpuUsage = avgCpuUsage,
                StutterCount = stutterCount,
                MaxCpuTemp = maxCpuTemp
            };
        }
    }

    /// <summary>
    /// Estatísticas de uma sessão de jogo para reporte de reward ao Brain.
    /// </summary>
    public class GamingSessionStats
    {
        public TimeSpan Duration { get; init; }
        public double AvgFps { get; init; }
        public double FpsVariance { get; init; }
        public double AvgCpuUsage { get; init; }
        public int StutterCount { get; init; }
        public double MaxCpuTemp { get; init; }
    }
}
