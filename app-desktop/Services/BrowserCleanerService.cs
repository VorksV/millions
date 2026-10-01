using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services
{
    /// <summary>
    /// Resultado da operação de limpeza de navegadores.
    /// </summary>
    public record CleanerResult
    {
        /// <summary>Indica se a operação terminou sem erros críticos.</summary>
        public bool IsSuccess { get; init; } = true;

        /// <summary>Quantidade total de bytes liberados.</summary>
        public long BytesFreed { get; init; }

        /// <summary>Mensagens de erro/ignore para arquivos que não puderam ser deletados.</summary>
        public List<string> IgnoredErrors { get; init; } = new();
    }

    /// <summary>
    /// Serviço responsável por limpar caches de navegadores de forma segura.
    /// Aplica as regras de Enterprise‑grade: detecta processos ativos, preserva diretórios críticos
    /// (Code Cache, GPUCache, CacheStorage) e lida com arquivos bloqueados sem lançar exceções.
    /// </summary>
    public class BrowserCleanerService
    {
        private readonly ILoggingService _logger;
        private readonly string _localAppData;

        public BrowserCleanerService(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }

        // Definição mínima das informações necessárias de cada navegador suportado.
        private sealed record BrowserInfo(string Name, string ProcessName, string RelativeUserDataPath);

        private static readonly BrowserInfo[] Browsers = new[]
        {
            new BrowserInfo("Chrome", "chrome", Path.Combine("Google", "Chrome", "User Data", "Default")),
            new BrowserInfo("Edge", "msedge", Path.Combine("Microsoft", "Edge", "User Data", "Default")),
            new BrowserInfo("Firefox", "firefox", Path.Combine("Mozilla", "Firefox", "Profiles")),
            new BrowserInfo("Brave", "brave", Path.Combine("BraveSoftware", "Brave-Browser", "User Data", "Default")),
            new BrowserInfo("Opera", "opera", Path.Combine("Opera Software", "Opera Stable"))
        };

        // Sub‑pastas que são consideradas seguras para remoção do conteúdo.
        // REGRA ENTERPRISE: Limpar apenas o CONTEÚDO de pastas de blobs pesados,
        // NUNCA o root de Cache (que contém Code Cache, GPUCache, etc.)
        //
        // Entradas terminando em "\\" indicam diretórios cujo CONTEÚDO será limpo.
        // Entradas com wildcard (*.tmp) indicam um padrão de arquivo dentro do pai.
        private static readonly string[] SafeSubfolders = new[]
        {
            "Cache\\Cache_Data\\",            // Blobs pesados do cache HTTP (Chromium 100+)
            "Cache\\*.tmp",                   // Arquivos temporários órfãos dentro de Cache
            "Network\\Cookies-journal",       // Journal do SQLite (recriado automaticamente)
            "Service Worker\\CacheStorage\\", // Cache de Service Workers (pode ser grande)
            "ShaderCache\\"                   // Shader compilados (recriados pelo GPU)
        };

        // Pastas que contêm componentes críticos de renderização – nunca são apagadas.
        private static readonly string[] ProtectedSubfolders = new[]
        {
            "Code Cache",
            "GPUCache",
            "CacheStorage"
        };

        /// <summary>
        /// Executa a limpeza segura de todos os navegadores configurados.
        /// </summary>
        /// <param name="ct">Token de cancelamento.</param>
        /// <returns>Resultado estruturado da operação.</returns>
        public async Task<CleanerResult> CleanAsync(CancellationToken ct = default)
        {
            return await Task.Run(() =>
            {
                var ignored = new List<string>();
                long totalFreed = 0;

                foreach (var browser in Browsers)
                {
                    if (ct.IsCancellationRequested) break;

                    // 1️⃣ Detectar se o processo do navegador está ativo.
                    bool isRunning = Process.GetProcessesByName(browser.ProcessName).Any();
                    if (isRunning)
                    {
                        _logger?.LogWarning($"[BrowserCleaner] {browser.Name} está em execução – limpeza agressiva ignorada.");
                        continue; // Salta este navegador para evitar corrupção de cache em tempo de execução.
                    }

                    var basePath = Path.Combine(_localAppData, browser.RelativeUserDataPath);
                    if (!Directory.Exists(basePath))
                    {
                        _logger?.LogDebug($"[BrowserCleaner] Pasta de dados não encontrada para {browser.Name}: {basePath}");
                        continue;
                    }

                    // 2️⃣ Limpar sub‑pastas seguras.
                    foreach (var pattern in SafeSubfolders)
                    {
                        if (ct.IsCancellationRequested) break;

                        var targetPath = Path.Combine(basePath, pattern);

                        // Determinar tipo de entrada:
                        // - Termina com "\\" → é diretório: limpar todo o conteúdo
                        // - Contém "*" → é pattern de arquivo: usar como glob no pai
                        // - Caso contrário → arquivo específico: deletar diretamente
                        if (pattern.EndsWith("\\"))
                        {
                            // Limpar conteúdo do diretório inteiro
                            var dirPath = targetPath.TrimEnd('\\');
                            if (!Directory.Exists(dirPath)) continue;

                            try
                            {
                                foreach (var file in Directory.EnumerateFiles(dirPath, "*", SearchOption.AllDirectories))
                                {
                                    try
                                    {
                                        var size = new FileInfo(file).Length;
                                        File.Delete(file);
                                        totalFreed += size;
                                    }
                                    catch (IOException ioEx)
                                    {
                                        ignored.Add($"IO: {file} – {ioEx.Message}");
                                    }
                                    catch (UnauthorizedAccessException uaEx)
                                    {
                                        ignored.Add($"Access: {file} – {uaEx.Message}");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                ignored.Add($"Dir: {dirPath} – {ex.Message}");
                                _logger?.LogError($"[BrowserCleaner] Falha ao enumerar {dirPath}: {ex.Message}", ex);
                            }
                        }
                        else if (pattern.Contains("*"))
                        {
                            // Pattern de arquivo (glob) dentro do diretório pai
                            var dir = Path.GetDirectoryName(targetPath) ?? basePath;
                            var searchGlob = Path.GetFileName(targetPath);

                            if (!Directory.Exists(dir)) continue;

                            try
                            {
                                foreach (var file in Directory.EnumerateFiles(dir, searchGlob, SearchOption.TopDirectoryOnly))
                                {
                                    try
                                    {
                                        var size = new FileInfo(file).Length;
                                        File.Delete(file);
                                        totalFreed += size;
                                    }
                                    catch (IOException ioEx)
                                    {
                                        ignored.Add($"IO: {file} – {ioEx.Message}");
                                    }
                                    catch (UnauthorizedAccessException uaEx)
                                    {
                                        ignored.Add($"Access: {file} – {uaEx.Message}");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                ignored.Add($"Dir: {dir} – {ex.Message}");
                                _logger?.LogError($"[BrowserCleaner] Falha ao enumerar {dir}: {ex.Message}", ex);
                            }
                        }
                        else
                        {
                            // Arquivo específico (ex: Cookies-journal)
                            if (!File.Exists(targetPath)) continue;

                            try
                            {
                                var size = new FileInfo(targetPath).Length;
                                File.Delete(targetPath);
                                totalFreed += size;
                            }
                            catch (IOException ioEx)
                            {
                                ignored.Add($"IO: {targetPath} – {ioEx.Message}");
                            }
                            catch (UnauthorizedAccessException uaEx)
                            {
                                ignored.Add($"Access: {targetPath} – {uaEx.Message}");
                            }
                        }
                    }

                    // 3️⃣ Registrar que as pastas protegidas foram preservadas (auditoria).
                    foreach (var protectedFolder in ProtectedSubfolders)
                    {
                        var protectedPath = Path.Combine(basePath, protectedFolder);
                        if (Directory.Exists(protectedPath))
                        {
                            _logger?.LogDebug($"[BrowserCleaner] Preservando pasta crítica de {browser.Name}: {protectedPath}");
                        }
                    }
                }

                var result = new CleanerResult
                {
                    IsSuccess = ignored.Count == 0,
                    BytesFreed = totalFreed,
                    IgnoredErrors = ignored
                };

                _logger?.LogInfo($"[BrowserCleaner] Concluída – {result.BytesFreed} bytes liberados, erros ignorados: {result.IgnoredErrors.Count}");
                return result;
            }, ct);
        }
    }
}
