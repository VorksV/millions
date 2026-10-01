using System;
using System.IO;

namespace VoltrisOptimizer.Helpers
{
    public static class AppDataPaths
    {
        // ─────────────────────────────────────────────────────────────────────────
        // POR QUE SÃO CALCULADOS PREGUIÇOSAMENTE (E NÃO EM static readonly)
        //
        // BUG CRÍTICO CORRIGIDO AQUI:
        // A tarefa agendada é registrada no Agendador do Windows para rodar como
        // LOCAL SYSTEM (SID S-1-5-18), porque é o único modo que executa com o
        // VOLTRIS fechado, sem senha armazenada e com elevação completa.
        //
        // Só que Environment.SpecialFolder.LocalApplicationData é resolvido a partir
        // do PERFIL DO USUÁRIO DO PROCESSO. Para SYSTEM, isso dá:
        //     C:\Windows\System32\config\systemprofile\AppData\Local
        // e não:
        //     C:\Users\<usuário>\AppData\Local
        //
        // O runner headless, portanto, lia
        //     C:\Windows\System32\config\systemprofile\AppData\Local\Voltris\Scheduler\schedules.json
        // que NÃO EXISTE — e toda tarefa agendada terminava em "tarefa não encontrada"
        // com exit code 2, silenciosamente, para sempre.
        //
        // A correção passa por dois mecanismos combinados:
        //  1) <see cref="ApplyDataRootOverride"/> — o caminho do usuário é informado
        //     pelo próprio Agendador, via --data-root, e sobrescreve a resolução.
        //     Precisa poder ser aplicado ANTES de qualquer leitura de caminho, por
        //     isso a raiz é resolvida no primeiro acesso e não no inicializador.
        //  2) O runner também recebe a definição da tarefa embutida em Base64
        //     (--task-payload), então nem depende de ler o JSON para saber o que
        //     executar. O agendador do Windows é o armazenamento durável.
        // ─────────────────────────────────────────────────────────────────────────

        private static readonly object _lock = new object();
        private static string? _unifiedRootOverride;
        private static string? _unifiedRoot;
        private static string? _roamingRoot;

        /// <summary>
        /// Sobrescreve a raiz de dados do usuário atual. Usado EXCLUSIVAMENTE pelo
        /// caminho headless (<c>--run-scheduled</c>), que roda em outra conta e
        /// precisa enxergar o mesmo diretório que a interface usa.
        /// </summary>
        public static void ApplyDataRootOverride(string unifiedRoot)
        {
            if (string.IsNullOrWhiteSpace(unifiedRoot)) return;

            lock (_lock)
            {
                _unifiedRootOverride = unifiedRoot;
                EnsureDirectory(unifiedRoot);
            }
        }

        /// <summary>
        /// Raiz efetiva em uso, já com o override aplicado. Útil para diagnóstico:
        /// o log do runner registra este valor para tornar visível qualquer divergência
        /// entre o processo interativo e o headless.
        /// </summary>
        public static string EffectiveUnifiedRoot => UnifiedRoot;

        public static string UnifiedRoot
        {
            get
            {
                lock (_lock)
                {
                    if (_unifiedRoot == null)
                    {
                        _unifiedRoot = _unifiedRootOverride ?? Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "Voltris");
                        EnsureDirectory(_unifiedRoot);
                    }
                    return _unifiedRoot;
                }
            }
        }

        public static string RoamingRoot
        {
            get
            {
                lock (_lock)
                {
                    if (_roamingRoot == null)
                    {
                        _roamingRoot = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                            "Voltris");
                        EnsureDirectory(_roamingRoot);
                    }
                    return _roamingRoot;
                }
            }
        }

        // Configurações principais
        public static string Settings => Path.Combine(_unifiedRoot, "settings.json");
        
        // Profiler
        public static string Profiler => Path.Combine(_unifiedRoot, "Profiler");
        public static string ProfilerState => Path.Combine(Profiler, "state.json");
        
        // Jogos
        public static string GamesLibrary => Path.Combine(_unifiedRoot, "Games", "library.json");
        public static string GamesProfiles => Path.Combine(_unifiedRoot, "Games", "profiles.json");
        
        // Backups
        public static string Backups => Path.Combine(_unifiedRoot, "Backups");
        public static string NetworkTweaksBackup => Path.Combine(Backups, "network_tweaks.json");
        public static string GodModeSysProfileBackup => Path.Combine(Backups, "system_profile_games.json");
        public static string GodModeGameDvrBackup => Path.Combine(Backups, "game_dvr.json");
        
        // AI/ML
        public static string StreamHubSettings => Path.Combine(_unifiedRoot, "AI", "streamhub_settings.json");
        
        // Config
        public static string FeatureFlagsCache => Path.Combine(_unifiedRoot, "Config", "feature_flags_cache.json");
        
        // GamerMode (dados temporários/cache)
        public static string PowerDiagResults => Path.Combine(_unifiedRoot, "GamerMode", "power_diag_results.json");
        public static string GamerStateMemory => Path.Combine(_unifiedRoot, "GamerMode", "gamer_state_memory.json");
        public static string ShellState => Path.Combine(_unifiedRoot, "GamerMode", "shell_state.json");
        public static string RestorationState => Path.Combine(_unifiedRoot, "GamerMode", "restoration_state.json");
        public static string SystemSnapshot => Path.Combine(_unifiedRoot, "GamerMode", "system_snapshot.json");
        
        // Benchmark
        public static string BenchmarkPendingValidation => Path.Combine(_unifiedRoot, "Benchmark", "pending_validation.json");

        public static string GetPath(string subPath)
        {
            var full = Path.Combine(_unifiedRoot, subPath);
            EnsureDirectory(Path.GetDirectoryName(full));
            return full;
        }

        public static void EnsureDirectory(string path)
        {
            if (!string.IsNullOrEmpty(path) && !Directory.Exists(path))
                Directory.CreateDirectory(path);
        }
    }
}
