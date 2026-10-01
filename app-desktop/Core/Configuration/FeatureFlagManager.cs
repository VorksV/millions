using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Core.Configuration
{
    /// <summary>
    /// GERENCIADOR DE FEATURE FLAGS (ENTERPRISE CONFIGURATION)
    /// 
    /// Permite ativar/desativar funcionalidades remotamente sem precisar de updates de código.
    /// Essencial para deploys seguros ("Kill Switch" para features bugadas) e Strangler Pattern.
    /// Lock-free reads após o load para performance máxima (Low Overhead).
    /// </summary>
    public class FeatureFlagManager : IVoltrisFeatureFlagManager
    {
        private const string REMOTE_FLAGS_URL = "https://api.voltris.com.br/flags.json"; // URL simulada
        private readonly string _localFlagsPath;
        private readonly ILoggingService _logger;
        
        // Dicionário readonly para reads lock-free na runtime
        private volatile Dictionary<string, bool> _flags;

        // Flags padrão (Hardcoded safe defaults)
        private readonly Dictionary<string, bool> _defaults = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            { "UseNewCleanerEngine", true },
            { "AllowGodModeValues", true },
            { "EnableContextAwareness", true },
            { "EnableTelemetryLocal", true },
            { "ShowBetaFeatures", false },
            { "UseSafeRegistryManager", true }, // Strangler Pattern defaults
            { "UseSafePowerManager", true },
            { "UseSafeProcessManager", true },
            { "UseSafeAffinityManager", true },
            { "UseAtomicRollbackEngine", true },
            { "UseEnterpriseScheduler", true }
        };

        public FeatureFlagManager(ILoggingService logger)
        {
            _logger = logger;
            _localFlagsPath = AppDataPaths.FeatureFlagsCache;
            _flags = new Dictionary<string, bool>(_defaults, StringComparer.OrdinalIgnoreCase);
            
            AppDataPaths.EnsureDirectory(Path.GetDirectoryName(_localFlagsPath));
            Initialize();
        }

        private void Initialize()
        {
            LoadLocalCache();
            // Fire-and-forget
            _ = Task.Run(RefreshFlagsAsync);
        }

        public bool IsEnabled(string featureKey)
        {
            var currentFlags = _flags; // thread-safe reference copy
            if (currentFlags.TryGetValue(featureKey, out bool value))
            {
                return value;
            }
            return false;
        }

        // Fast properties para Strangler Pattern (evitando alocações de string)
        public bool UseSafeRegistryManager => IsEnabled("UseSafeRegistryManager");
        public bool UseSafePowerManager => IsEnabled("UseSafePowerManager");
        public bool UseSafeProcessManager => IsEnabled("UseSafeProcessManager");
        public bool UseSafeAffinityManager => IsEnabled("UseSafeAffinityManager");
        public bool UseAtomicRollbackEngine => IsEnabled("UseAtomicRollbackEngine");
        public bool UseEnterpriseScheduler => IsEnabled("UseEnterpriseScheduler");

        private void LoadLocalCache()
        {
            try
            {
                if (File.Exists(_localFlagsPath))
                {
                    var json = File.ReadAllText(_localFlagsPath);
                    var localFlags = JsonSerializer.Deserialize<Dictionary<string, bool>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                    
                    if (localFlags != null)
                    {
                        var newFlags = new Dictionary<string, bool>(_defaults, StringComparer.OrdinalIgnoreCase);
                        foreach (var kvp in localFlags)
                        {
                            newFlags[kvp.Key] = kvp.Value;
                        }
                        _flags = newFlags; // atomic swap
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Falha ao carregar flags locais", ex);
            }
        }

        private async Task RefreshFlagsAsync()
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(5);

                var json = await client.GetStringAsync(REMOTE_FLAGS_URL).ConfigureAwait(false);
                var remoteFlags = JsonSerializer.Deserialize<Dictionary<string, bool>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

                if (remoteFlags != null)
                {
                    var newFlags = new Dictionary<string, bool>(_flags, StringComparer.OrdinalIgnoreCase);
                    foreach (var kvp in remoteFlags)
                    {
                        newFlags[kvp.Key] = kvp.Value;
                    }
                    
                    _flags = newFlags; // atomic swap
                    SaveLocalCache();
                    _logger.LogInfo("[FLAGS] Feature Flags atualizadas remotamente.");
                }
            }
            catch (HttpRequestException)
            {
                _logger.LogInfo("[FLAGS] Servidor de flags indisponível — usando defaults/cache locais.");
            }
            catch (TaskCanceledException)
            {
                _logger.LogInfo("[FLAGS] Timeout ao buscar flags remotas — usando defaults/cache locais.");
            }
            catch (Exception ex)
            {
                _logger.LogError("[FLAGS] Erro inesperado ao buscar flags remotas", ex);
            }
        }

        private void SaveLocalCache()
        {
            try
            {
                var dir = Path.GetDirectoryName(_localFlagsPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);

                var json = JsonSerializer.Serialize(_flags, new JsonSerializerOptions { WriteIndented = true, ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                File.WriteAllText(_localFlagsPath, json);
            }
            catch { }
        }
    }
}
