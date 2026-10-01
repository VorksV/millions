using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.Optimization.ZeroTrustCleaning
{
    public class AppSafetyLayer : IAppSafetyLayer
    {
        private readonly ILoggingService _logger;
        private readonly HashSet<string> _sacredGameConfigPaths = new(StringComparer.OrdinalIgnoreCase)
        {
            @"\Saved\",       // Unreal Engine
            @"\SaveGames\",   // Steam Cloud
            @"\UserData\",    // Chrome/Edge/Ea
            @"\Config\",      // Unreal Engine
            @"\My Games\",    // Documentos (Skyrim, Fallout)
            @"\Mods\",        // Modding geral
            @"\Profiles\"     // Assinaturas de perfil locale
        };

        public AppSafetyLayer(ILoggingService logger)
        {
            _logger = logger;
        }

        public bool IsLockedByActiveProcess(MutableNode node)
        {
            if (node.Type != NodeType.File) return false;

            try
            {
                // Tenta abrir o arquivo com SHARE=NONE exclusivo
                using var fs = new FileStream(node.Path, FileMode.Open, FileAccess.Write, FileShare.None);
                return false;
            }
            catch (IOException ex) when (IsFileLocked(ex))
            {
                _logger.LogWarning($"[AppSafetyLayer] PROTEÇÃO DE PROCESSO: Arquivo travado em runtime por aplicativo/jogo vivo. ({node.Path})");
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                _logger.LogWarning($"[AppSafetyLayer] PROTEÇÃO DE ACESSO: Sem privilégios para mexer em {node.Path}. Risco alto detetato.");
                return true;
            }
            catch { return false; }
        }

        private static int ERROR_SHARING_VIOLATION = 32;
        private static int ERROR_LOCK_VIOLATION = 33;

        private static bool IsFileLocked(Exception exception)
        {
            int errorCode = System.Runtime.InteropServices.Marshal.GetHRForException(exception) & ((1 << 16) - 1);
            return errorCode == ERROR_SHARING_VIOLATION || errorCode == ERROR_LOCK_VIOLATION;
        }

        public bool IsProtectedGameAsset(MutableNode node, IEnumerable<string> knownGameDirs)
        {
            if (string.IsNullOrEmpty(node.Path)) return false;

            // REGRA 1: Caminhos "Sagrados" internos típicos de Mod/Saves
            foreach (var sacredPattern in _sacredGameConfigPaths)
            {
                if (node.Path.IndexOf(sacredPattern, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _logger.LogWarning($"[AppSafetyLayer] PROTEÇÃO DE JOGO: Padrão conhecido de 'Save/Config/Mod' detectado. ({node.Path})");
                    return true; // Intocável
                }
            }

            // REGRA 2: Verificação do banco de dados conhecido do usuário (known_games_database.json)
            if (knownGameDirs != null)
            {
                foreach (var gameDir in knownGameDirs)
                {
                    if (string.IsNullOrWhiteSpace(gameDir)) continue;

                    // Se a excluso está contida DENTRO ou É O PRÓPRIO diretório
                    if (node.Path.StartsWith(gameDir, StringComparison.OrdinalIgnoreCase))
                    {
                        // EXCEÇÃO: Shaders Caches DE FATO gerados dinamicamente em pastas vizinhas. (Série DXCache ou GLCache)
                        if (node.Path.IndexOf("D3DSCache", StringComparison.OrdinalIgnoreCase) >= 0 || 
                            node.Path.IndexOf("GLCache", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return false; // É Game Asset, mas é cache refazível (Stutter Fix)
                        }

                        _logger.LogWarning($"[AppSafetyLayer] PROTEÇÃO DE JOGO: Módulo tentou ler dentro da instalação raíz de um jogo conhecido. Risco de corrupção evitado. ({gameDir})");
                        return true; 
                    }
                }
            }

            return false;
        }

        public bool WasAccessedInLastHours(MutableNode node, int hours = 24)
        {
            if (node.LastAccessTime == DateTime.MinValue) return false; // Default de creation fallback

            var timeSinceAccess = DateTime.Now - node.LastAccessTime;
            
            if (timeSinceAccess.TotalHours <= hours)
            {
                 _logger.LogWarning($"[AppSafetyLayer] PROTEÇÃO DE TELEMETRIA: Arquivo usado/acessado muito recentemente (< {hours}h). Cache quente, não deve ser expurgado. ({node.Path})");
                 return true;
            }

            return false;
        }
    }
}
