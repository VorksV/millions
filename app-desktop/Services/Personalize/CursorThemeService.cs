using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.Personalize
{
    /// <summary>
    /// Representa um tema de cursor disponível na pasta Cursor\.
    /// </summary>
    public class CursorThemeInfo
    {
        public string Name        { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string FolderPath  { get; set; } = string.Empty;
        public string Emoji       { get; set; } = "🖱️";
        public string Description { get; set; } = string.Empty;
    }

    /// <summary>
    /// CursorThemeService — aplica temas de cursor customizados via Registro do Windows.
    ///
    /// ESTRATÉGIA:
    ///   1. Enumera pastas em [exe]\Cursor\
    ///   2. Para cada pasta, tenta mapear os arquivos .cur/.ani para os valores
    ///      da chave HKCU\Control Panel\Cursors usando nomes conhecidos.
    ///   3. Grava os caminhos no Registro.
    ///   4. Chama SystemParametersInfo(SPI_SETCURSORS) para aplicar em tempo real.
    ///   5. Para reverter, apaga os valores e restaura o padrão do Windows.
    ///
    /// PERSISTÊNCIA: Os valores ficam no Registro mesmo após o PC reiniciar.
    /// </summary>
    public class CursorThemeService
    {
        private const string TAG     = "[CursorTheme]";
        private const string REG_KEY = @"Control Panel\Cursors";

        private readonly ILoggingService _logger;
        private readonly string _cursorBaseDir;

        // P/Invoke para aplicar cursores em tempo real
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

        private const uint SPI_SETCURSORS = 0x0057;
        private const uint SPIF_UPDATEINIFILE = 0x01;
        private const uint SPIF_SENDCHANGE    = 0x02;

        // ── Metadados visuais por pasta ──────────────────────────────────────────
        private static readonly Dictionary<string, (string DisplayName, string Emoji, string Description)> _themeMeta
            = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Windows11Light"] = ("Windows 11 Light",  "🪟", "Visual nativo do Windows 11 — cursor branco moderno com sombra suave"),
            ["BlueSky"]        = ("Blue Sky",           "🔵", "Cursor azul vibrante com estilo flat e bordas nítidas"),
            ["GamerRGB"]       = ("Gamer RGB",          "🎮", "Cursores animados com visual gaming — bordas coloridas animadas"),
            ["LayanWhite"]     = ("Layan White",        "⬜", "Design minimalista branco — inspirado no tema Layan do Linux"),
            ["Dark"]           = ("Voltris Dark Edition", "🌑", "Cursores premium em tons escuros — visual elegante e alto contraste para setups dark"),
            ["Light"]          = ("Voltris Light Edition", "☀️", "Cursores claros e nítidos — design minimalista com visibilidade premium")};

        // ── Mapeamento: nome de arquivo (sem extensão, case-insensitive) → valor do Registro ──
        // HKCU\Control Panel\Cursors value names:
        //   Arrow, Help, AppStarting, Wait, Crosshair, IBeam, NWPen, No,
        //   SizeNS, SizeWE, SizeNWSE, SizeNESW, SizeAll, UpArrow, Hand
        private static readonly List<(string[] FilePatterns, string RegValue)> _mapping = new()
        {
            // ── Normal Select / Arrow ──────────────────────────────────────────
            (new[] { "normal select", "blue arrow", "arrow", "default", "pointer" },
                "Arrow"),

            // ── Help Select ───────────────────────────────────────────────────
            (new[] { "help select", "help", "blue help" },
                "Help"),

            // ── Working in Background / AppStarting ───────────────────────────
            (new[] { "working in background", "working", "blue working", "appstarting", "progress" },
                "AppStarting"),

            // ── Busy / Wait ───────────────────────────────────────────────────
            (new[] { "busy", "wait", "blue busy" },
                "Wait"),

            // ── Precision Select / Crosshair ──────────────────────────────────
            (new[] { "precision select", "precision", "crosshair", "blue cross" },
                "Crosshair"),

            // ── Text Select / Beam ────────────────────────────────────────────
            (new[] { "text select", "text", "beam", "ibeam", "blue text" },
                "IBeam"),

            // ── Handwriting ───────────────────────────────────────────────────
            (new[] { "handwriting", "pencil" },
                "NWPen"),

            // ── Unavailable / No ──────────────────────────────────────────────
            (new[] { "unavailable", "blue unavailable", "not-allowed", "no-drop", "dnd-no-drop", "no" },
                "No"),

            // ── Vertical Resize ───────────────────────────────────────────────
            (new[] { "verticle resize", "vertical resize", "vert", "size_ver", "bottom_side", "top_side", "blue vertical" },
                "SizeNS"),

            // ── Horizontal Resize ─────────────────────────────────────────────
            (new[] { "horizontal resize", "horz", "size_hor", "blue horizontal" },
                "SizeWE"),

            // ── Diagonal Resize 1 (NW-SE) ────────────────────────────────────
            (new[] { "diagonal resize 1", "dgn1", "size_fdiag", "bottom_right_corner", "top_left_corner", "blue nw_se" },
                "SizeNWSE"),

            // ── Diagonal Resize 2 (NE-SW) ────────────────────────────────────
            (new[] { "diagonal resize 2", "dgn2", "size_bdiag", "bottom_left_corner", "top_right_corner", "blue ne_sw" },
                "SizeNESW"),

            // ── Move / SizeAll ────────────────────────────────────────────────
            (new[] { "move", "fleur", "all-scroll" },
                "SizeAll"),

            // ── Alternate Select ──────────────────────────────────────────────
            (new[] { "alternate select", "alternate", "up-arrow" },
                "UpArrow"),

            // ── Link Select / Hand ────────────────────────────────────────────
            (new[] { "link select", "link", "blue link", "hand", "pointer" },
                "Hand"),

            // ── Person ────────────────────────────────────────────────────────
            (new[] { "person" },
                "Person"),

            // ── Pin ───────────────────────────────────────────────────────────
            (new[] { "pin" },
                "Pin")};

        public CursorThemeService(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // ESTRATÉGIA DE BUSCA PROFISSIONAL:
            // 1. Tentar na pasta do executável (Modo Produção)
            // 2. Tentar subir diretórios (Modo Debug no VS / IDE)
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string localDir = Path.Combine(exeDir, "Cursor");

            if (Directory.Exists(localDir))
            {
                _cursorBaseDir = localDir;
            }
            else
            {
                // Subir até 5 níveis (para encontrar a raiz do projeto em bin/Debug/net8.0/...)
                string? current = exeDir;
                string? found = null;
                for (int i = 0; i < 5 && current != null; i++)
                {
                    string candidate = Path.Combine(current, "Cursor");
                    if (Directory.Exists(candidate))
                    {
                        found = candidate;
                        break;
                    }
                    current = Path.GetDirectoryName(current);
                }
                _cursorBaseDir = found ?? localDir;
            }

            _logger.LogInfo($"{TAG} Serviço iniciado. Base detectada: {_cursorBaseDir} (Existe: {Directory.Exists(_cursorBaseDir)})");
        }

        // ── API pública ──────────────────────────────────────────────────────────

        /// <summary>Enumera todos os temas disponíveis na pasta Cursor\.</summary>
        public List<CursorThemeInfo> GetAvailableThemes()
        {
            var themes = new List<CursorThemeInfo>();

            if (!Directory.Exists(_cursorBaseDir))
            {
                _logger.LogWarning($"{TAG} [GetThemes] Pasta Cursor não encontrada: {_cursorBaseDir}");
                return themes;
            }

            foreach (var dir in Directory.GetDirectories(_cursorBaseDir))
            {
                string name = Path.GetFileName(dir);
                _themeMeta.TryGetValue(name, out var meta);

                themes.Add(new CursorThemeInfo
                {
                    Name        = name,
                    DisplayName = !string.IsNullOrEmpty(meta.DisplayName) ? meta.DisplayName : name,
                    FolderPath  = dir,
                    Emoji       = !string.IsNullOrEmpty(meta.Emoji) ? meta.Emoji : "🖱️",
                    Description = !string.IsNullOrEmpty(meta.Description) ? meta.Description : $"Tema de cursor customizado: {name}"});

                _logger.LogDebug($"{TAG} [GetThemes] Tema encontrado: '{name}' ({dir})", source: "CursorTheme");
            }

            _logger.LogInfo($"{TAG} [GetThemes] Total de temas: {themes.Count}");
            return themes;
        }

        /// <summary>Nome do tema atualmente aplicado (lido do Registro).</summary>
        public string GetCurrentThemeName()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(REG_KEY);
                var schemeName = key?.GetValue("") as string ?? string.Empty;
                _logger.LogDebug($"{TAG} [GetCurrent] Scheme name no registro: '{schemeName}'", source: "CursorTheme");
                return schemeName;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"{TAG} [GetCurrent] Erro: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>Aplica um tema de cursor. Retorna true em caso de sucesso.</summary>
        public async Task<bool> ApplyThemeAsync(CursorThemeInfo theme)
        {
            _logger.LogInfo($"{TAG} [Apply] ══════════════════════════════════════");
            _logger.LogInfo($"{TAG} [Apply] Tema: '{theme.Name}' | Pasta: {theme.FolderPath}");

            if (!Directory.Exists(theme.FolderPath))
            {
                _logger.LogError($"{TAG} [Apply] ❌ Pasta não existe: {theme.FolderPath}");
                return false;
            }

            return await Task.Run(() =>
            {
                try
                {
                    // Enumerar todos os arquivos .cur e .ani da pasta
                    var files = Directory.GetFiles(theme.FolderPath, "*.cur")
                        .Concat(Directory.GetFiles(theme.FolderPath, "*.ani"))
                        .ToList();

                    _logger.LogInfo($"{TAG} [Apply] Arquivos encontrados: {files.Count}");
                    foreach (var f in files)
                        _logger.LogDebug($"{TAG} [Apply]   {Path.GetFileName(f)}", source: "CursorTheme");

                    // Construir mapa: RegValue → CaminhoAbsoluto
                    var mapped = BuildFileMap(files, theme.Name);

                    if (mapped.Count == 0)
                    {
                        _logger.LogError($"{TAG} [Apply] ❌ Nenhum arquivo mapeado para cursores do Windows.");
                        return false;
                    }

                    _logger.LogInfo($"{TAG} [Apply] Arquivos mapeados ({mapped.Count}):");
                    foreach (var kv in mapped)
                        _logger.LogInfo($"{TAG} [Apply]   [{kv.Key}] → {kv.Value}");

                    // Gravar no Registro
                    using var key = Registry.CurrentUser.CreateSubKey(REG_KEY, writable: true);
                    if (key == null)
                    {
                        _logger.LogError($"{TAG} [Apply] ❌ Não foi possível abrir a chave de Registro.");
                        return false;
                    }

                    // Definir o scheme name como o nome do tema
                    key.SetValue("", theme.DisplayName, RegistryValueKind.String);
                    _logger.LogDebug($"{TAG} [Apply] Scheme name definido: '{theme.DisplayName}'", source: "CursorTheme");

                    foreach (var kv in mapped)
                    {
                        key.SetValue(kv.Key, kv.Value, RegistryValueKind.ExpandString);
                        _logger.LogDebug($"{TAG} [Apply] Reg [{kv.Key}] = {kv.Value}", source: "CursorTheme");
                    }

                    // Aplicar em tempo real via SPI_SETCURSORS
                    bool ok = SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero,
                        SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);

                    if (ok)
                        _logger.LogInfo($"{TAG} [Apply] ✅ Tema '{theme.Name}' aplicado com sucesso!");
                    else
                    {
                        int err = Marshal.GetLastWin32Error();
                        if (err == 0)
                        {
                            // Win32Error=0 com retorno false: broadcast WM_SETTINGCHANGE não confirmado por todas as janelas.
                            // Registro já foi escrito — cursor será aplicado corretamente.
                            _logger.LogInfo($"{TAG} [Apply] Tema '{theme.Name}' aplicado via registro (broadcast pendente).");
                        }
                        else
                        {
                            _logger.LogWarning($"{TAG} [Apply] SystemParametersInfo falhou. Win32Error={err}");
                        }
                        // Não é fatal — registro foi escrito, cursor será correto no próximo login
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"{TAG} [Apply] ❌ Exceção: {ex.Message}", ex);
                    return false;
                }
            });
        }

        /// <summary>Restaura o cursor padrão do Windows original (true system reset).</summary>
        public async Task<bool> RestoreDefaultAsync()
        {
            _logger.LogInfo($"{TAG} [Restore] Restaurando para o cursor padrão original do Windows (Aero)...");

            return await Task.Run(() =>
            {
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(REG_KEY, writable: true);
                    if (key == null)
                    {
                        _logger.LogError($"{TAG} [Restore] ❌ Não foi possível abrir a chave de Registro.");
                        return false;
                    }

                    // Lista de todos os valores de cursor padrão do Windows
                    var cursorValues = new[]
                    {
                        "Arrow", "Help", "AppStarting", "Wait", "Crosshair",
                        "IBeam", "NWPen", "No", "SizeNS", "SizeWE",
                        "SizeNWSE", "SizeNESW", "SizeAll", "UpArrow", "Hand",
                        "Person", "Pin"
                    };

                    // ESTRATÉGIA:
                    // 1. Limpar os caminhos manuais. Quando o Registro está vazio para um valor,
                    //    o Windows usa o cursor definido pelo Esquema (Scheme).
                    foreach (var val in cursorValues)
                    {
                        try { key.DeleteValue(val, false); } catch { }
                    }

                    // 2. Definir o esquema como "Windows Aero" (padrão do Win 10/11)
                    //    Isso evita que o Windows carregue a versão "antiga/gigante" (Standard)
                    key.SetValue("", "Windows Aero", RegistryValueKind.String);
                    
                    // 3. Resetar metadados de esquemas externos
                    try { key.SetValue("Scheme Source", 0, RegistryValueKind.DWord); } catch { }

                    // 4. Aplicar em tempo real
                    // uiParam = 0 significa recarregar do Registro
                    bool ok = SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero,
                        SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);

                    _logger.LogInfo($"{TAG} [Restore] Cursor redefinido para 'Windows Aero'. SPI_SETCURSORS={ok}");
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"{TAG} [Restore] ❌ Exceção Crítica: {ex.Message}", ex);
                    return false;
                }
            });
        }

        // ── Lógica de mapeamento ─────────────────────────────────────────────────

        private Dictionary<string, string> BuildFileMap(List<string> files, string themeName)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var usedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            _logger.LogDebug($"{TAG} [BuildMap] Iniciando mapeamento para tema: {themeName}", source: "CursorTheme");

            foreach (var (patterns, regValue) in _mapping)
            {
                // Evitar sobrescrever se já mapeado (ex: "pointer" serve tanto Arrow quanto Hand — Arrow tem prioridade)
                if (result.ContainsKey(regValue))
                    continue;

                string? matched = null;

                foreach (var pattern in patterns)
                {
                    // Busca pelo nome do arquivo (sem extensão), case-insensitive
                    matched = files.FirstOrDefault(f =>
                    {
                        string nameNoExt = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                        return nameNoExt == pattern.ToLowerInvariant();
                    });

                    if (matched != null) break;

                    // Segunda tentativa: contém o padrão
                    matched = files.FirstOrDefault(f =>
                    {
                        string nameNoExt = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                        return nameNoExt.Contains(pattern.ToLowerInvariant());
                    });

                    if (matched != null) break;
                }

                if (matched != null)
                {
                    result[regValue] = matched;
                    usedFiles.Add(matched);
                    _logger.LogDebug(
                        $"{TAG} [BuildMap]   [{regValue}] ← '{Path.GetFileName(matched)}'",
                        source: "CursorTheme");
                }
                else
                {
                    _logger.LogDebug(
                        $"{TAG} [BuildMap]   [{regValue}] ← não encontrado (patterns: {string.Join(", ", patterns)})",
                        source: "CursorTheme");
                }
            }

            // Log de arquivos não mapeados (para diagnóstico)
            var unmapped = files.Where(f => !usedFiles.Contains(f)).Select(Path.GetFileName).ToList();
            if (unmapped.Any())
                _logger.LogDebug(
                    $"{TAG} [BuildMap] Arquivos não mapeados ({unmapped.Count}): {string.Join(", ", unmapped)}",
                    source: "CursorTheme");

            return result;
        }
    }
}
