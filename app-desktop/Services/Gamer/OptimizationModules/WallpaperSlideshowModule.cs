using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Services.Gamer.Implementation;

namespace VoltrisOptimizer.Services.Gamer.OptimizationModules
{
    /// <summary>
    /// Muda o wallpaper para cor sólida durante o Modo Gamer.
    /// Detecta o tipo atual (imagem, slideshow ou cor sólida), salva o estado completo,
    /// aplica cor sólida preta, e restaura fielmente ao desativar.
    /// Compatível com Windows 10 e 11.
    /// </summary>
    public class WallpaperSlideshowModule : IGamerOptimizationModule
    {
        // --- Win32 API para wallpaper ---
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int SystemParametersInfo(int uAction, int uParam, string? lpvParam, int fuWinIni);

        private const int SPI_SETDESKWALLPAPER = 0x0014;
        private const int SPIF_UPDATEINIFILE = 0x01;
        private const int SPIF_SENDCHANGE = 0x02;

        // --- Registry paths ---
        private const string DESKTOP_KEY = @"Control Panel\Desktop";
        private const string SLIDESHOW_KEY = @"Control Panel\Desktop\Slideshow";
        private const string COLORS_KEY = @"Control Panel\Colors";

        // --- State memory keys ---
        private const string STATE_WALLPAPER_TYPE = "wallpaper.type";         // "image", "slideshow", "solid"
        private const string STATE_WALLPAPER_PATH = "wallpaper.path";         // caminho da imagem
        private const string STATE_WALLPAPER_STYLE = "wallpaper.style";       // WallpaperStyle (0,2,6,10,22)
        private const string STATE_TILE_WALLPAPER = "wallpaper.tile";         // TileWallpaper (0,1)
        private const string STATE_SLIDESHOW_ENABLED = "wallpaper.slideshowEnabled";
        private const string STATE_SLIDESHOW_DIR = "wallpaper.slideshowDir";
        private const string STATE_SLIDESHOW_INTERVAL = "wallpaper.slideshowInterval";
        private const string STATE_SLIDESHOW_SHUFFLE = "wallpaper.slideshowShuffle";
        private const string STATE_BG_COLOR = "wallpaper.bgColor";           // "R G B" original

        private readonly GamerStateMemory _stateMemory;

        public string Name => "Wallpaper → Cor Sólida";
        public string Description => "Muda wallpaper para cor sólida preta durante o Modo Gamer (elimina GPU/CPU de renderização de wallpaper)";

        public WallpaperSlideshowModule(GamerStateMemory stateMemory)
        {
            _stateMemory = stateMemory;
        }

        public Task<ModuleApplyResult> ApplyAsync(GamerSessionContext ctx, CancellationToken ct = default)
        {
            var result = new ModuleApplyResult();
            try
            {
                ctx.Logger.LogInfo("[Wallpaper] Detectando tipo de wallpaper atual...");

                // 1. Detectar tipo atual e salvar estado completo
                var wallpaperType = DetectCurrentType(ctx);
                ctx.Logger.LogInfo($"[Wallpaper] Tipo detectado: {wallpaperType}");

                if (wallpaperType == "solid")
                {
                    ctx.Logger.LogInfo("[Wallpaper] Já é cor sólida, nenhuma alteração necessária.");
                    result.Success = true;
                    result.AppliedChanges = Array.Empty<string>();
                    return Task.FromResult(result);
                }

                // 2. Desativar slideshow se ativo
                if (wallpaperType == "slideshow")
                {
                    DisableSlideshow(ctx);
                }

                // 3. Aplicar cor sólida preta
                ApplySolidColor(ctx);

                ctx.Logger.LogSuccess($"[Wallpaper] ✅ Wallpaper mudado de '{wallpaperType}' para cor sólida preta.");
                result.Success = true;
                result.ChangesApplied = 1;
                result.AppliedChanges = new[] { $"Wallpaper: {wallpaperType} → cor sólida" };
            }
            catch (Exception ex)
            {
                ctx.Logger.LogError($"[Wallpaper] ❌ Erro ao aplicar cor sólida: {ex.Message}", ex);
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }
            return Task.FromResult(result);
        }

        public Task<ModuleRevertResult> RevertAsync(GamerSessionContext ctx, CancellationToken ct = default)
        {
            var result = new ModuleRevertResult();
            try
            {
                if (!_stateMemory.WasModifiedByGamerMode(STATE_WALLPAPER_TYPE))
                {
                    ctx.Logger.LogInfo("[Wallpaper] Nenhum estado registrado para restaurar.");
                    result.Success = true;
                    return Task.FromResult(result);
                }

                var originalType = _stateMemory.GetOriginal<string>(STATE_WALLPAPER_TYPE) ?? "image";
                ctx.Logger.LogInfo($"[Wallpaper] Restaurando wallpaper original (tipo: {originalType})...");

                if (originalType == "slideshow")
                {
                    RestoreSlideshow(ctx);
                }
                else if (originalType == "image")
                {
                    RestoreImage(ctx);
                }
                // Se era "solid", não precisamos fazer nada (já está sólido)

                ctx.Logger.LogSuccess($"[Wallpaper] ✅ Wallpaper restaurado para: {originalType}");
                result.Success = true;
                result.ChangesReverted = 1;
                result.RevertedChanges = new[] { $"Wallpaper restaurado: {originalType}" };
            }
            catch (Exception ex)
            {
                ctx.Logger.LogError($"[Wallpaper] ❌ Erro ao restaurar wallpaper: {ex.Message}", ex);
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }
            return Task.FromResult(result);
        }

        /// <summary>
        /// Detecta o tipo de wallpaper atual e salva o estado completo no StateMemory.
        /// </summary>
        private string DetectCurrentType(GamerSessionContext ctx)
        {
            string wallpaperType;

            // Ler wallpaper path atual
            using (var desktopKey = Registry.CurrentUser.OpenSubKey(DESKTOP_KEY))
            {
                var wallpaperPath = desktopKey?.GetValue("Wallpaper") as string ?? "";
                var wallpaperStyle = desktopKey?.GetValue("WallpaperStyle") as string ?? "10";
                var tileWallpaper = desktopKey?.GetValue("TileWallpaper") as string ?? "0";

                _stateMemory.Register(STATE_WALLPAPER_PATH, wallpaperPath);
                _stateMemory.Register(STATE_WALLPAPER_STYLE, wallpaperStyle);
                _stateMemory.Register(STATE_TILE_WALLPAPER, tileWallpaper);

                ctx.Logger.LogInfo($"[Wallpaper] Path: '{wallpaperPath}', Style: {wallpaperStyle}, Tile: {tileWallpaper}");

                // Verificar se é slideshow
                bool isSlideshowActive = false;
                using (var slideKey = Registry.CurrentUser.OpenSubKey(SLIDESHOW_KEY))
                {
                    if (slideKey != null)
                    {
                        var slideshowEnabled = slideKey.GetValue("Enabled");
                        int enabledVal = slideshowEnabled is int sv ? sv : 0;

                        // Windows 11 usa "Enabled", Windows 10 usa "SlideshowEnabled" ou verifica ImagesRootPath
                        if (enabledVal == 0)
                        {
                            // Fallback: checar se há diretório configurado (Win10 pode não ter "Enabled")
                            var rootPath = slideKey.GetValue("ImagesRootPath") as string;
                            var interval = slideKey.GetValue("Interval") as string;
                            if (!string.IsNullOrEmpty(rootPath) && !string.IsNullOrEmpty(interval))
                            {
                                // Tem config de slideshow, verificar se está realmente ativo
                                // No Win10, se Wallpaper está vazio e há ImagesRootPath, é slideshow
                                isSlideshowActive = string.IsNullOrEmpty(wallpaperPath) || enabledVal == 1;
                            }
                        }
                        else
                        {
                            isSlideshowActive = true;
                        }

                        // Salvar estado do slideshow independente de estar ativo
                        _stateMemory.Register(STATE_SLIDESHOW_ENABLED, enabledVal);
                        _stateMemory.Register(STATE_SLIDESHOW_DIR, slideKey.GetValue("ImagesRootPath") as string ?? "");
                        _stateMemory.Register(STATE_SLIDESHOW_INTERVAL, slideKey.GetValue("Interval") as string ?? "");
                        _stateMemory.Register(STATE_SLIDESHOW_SHUFFLE, slideKey.GetValue("Shuffle") is int sh ? sh : 0);
                    }
                }

                if (isSlideshowActive)
                {
                    wallpaperType = "slideshow";
                }
                else if (string.IsNullOrEmpty(wallpaperPath))
                {
                    wallpaperType = "solid";
                }
                else
                {
                    wallpaperType = "image";
                }
            }

            // Salvar cor de fundo atual
            using (var colorsKey = Registry.CurrentUser.OpenSubKey(COLORS_KEY))
            {
                var bgColor = colorsKey?.GetValue("Background") as string ?? "0 0 0";
                _stateMemory.Register(STATE_BG_COLOR, bgColor);
            }

            _stateMemory.Register(STATE_WALLPAPER_TYPE, wallpaperType);
            return wallpaperType;
        }

        /// <summary>
        /// Desativa o slideshow no registro.
        /// </summary>
        private void DisableSlideshow(GamerSessionContext ctx)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(SLIDESHOW_KEY, writable: true);
                if (key != null)
                {
                    key.SetValue("Enabled", 0, RegistryValueKind.DWord);
                    ctx.Logger.LogInfo("[Wallpaper] Slideshow desativado no registro.");
                }
            }
            catch (Exception ex)
            {
                ctx.Logger.LogWarning($"[Wallpaper] Falha ao desativar slideshow: {ex.Message}");
            }
        }

        /// <summary>
        /// Aplica cor sólida preta como wallpaper.
        /// </summary>
        private void ApplySolidColor(GamerSessionContext ctx)
        {
            // 1. Definir cor de fundo preta
            using (var colorsKey = Registry.CurrentUser.OpenSubKey(COLORS_KEY, writable: true))
            {
                colorsKey?.SetValue("Background", "0 0 0");
            }

            // 2. Remover wallpaper via Win32 API (string vazia = sem imagem = cor sólida)
            int apiResult = SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, "", SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
            if (apiResult == 0)
            {
                // Fallback: tentar via registro direto
                ctx.Logger.LogWarning("[Wallpaper] Win32 API falhou, tentando via registro...");
                using var desktopKey = Registry.CurrentUser.OpenSubKey(DESKTOP_KEY, writable: true);
                desktopKey?.SetValue("Wallpaper", "", RegistryValueKind.String);
            }

            ctx.Logger.LogInfo("[Wallpaper] Cor sólida preta aplicada.");
        }

        /// <summary>
        /// Restaura o slideshow original.
        /// </summary>
        private void RestoreSlideshow(GamerSessionContext ctx)
        {
            // 1. Restaurar imagem original (se havia uma durante slideshow)
            RestoreImage(ctx);

            // 2. Reativar slideshow
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(SLIDESHOW_KEY, writable: true)
                             ?? Registry.CurrentUser.CreateSubKey(SLIDESHOW_KEY, writable: true);
                if (key != null)
                {
                    var originalEnabled = _stateMemory.GetOriginal<int>(STATE_SLIDESHOW_ENABLED);
                    var originalDir = _stateMemory.GetOriginal<string>(STATE_SLIDESHOW_DIR) ?? "";
                    var originalInterval = _stateMemory.GetOriginal<string>(STATE_SLIDESHOW_INTERVAL) ?? "";
                    var originalShuffle = _stateMemory.GetOriginal<int>(STATE_SLIDESHOW_SHUFFLE);

                    // Restaurar valores fiéis
                    key.SetValue("Enabled", originalEnabled, RegistryValueKind.DWord);

                    if (!string.IsNullOrEmpty(originalDir))
                        key.SetValue("ImagesRootPath", originalDir, RegistryValueKind.String);
                    if (!string.IsNullOrEmpty(originalInterval))
                        key.SetValue("Interval", originalInterval, RegistryValueKind.String);

                    key.SetValue("Shuffle", originalShuffle, RegistryValueKind.DWord);

                    ctx.Logger.LogInfo($"[Wallpaper] Slideshow restaurado: Enabled={originalEnabled}, Dir='{originalDir}'");

                }
            }
            catch (Exception ex)
            {
                ctx.Logger.LogWarning($"[Wallpaper] Falha ao restaurar slideshow: {ex.Message}");
            }
        }

        /// <summary>
        /// Restaura a imagem de wallpaper original.
        /// </summary>
        private void RestoreImage(GamerSessionContext ctx)
        {
            var originalPath = _stateMemory.GetOriginal<string>(STATE_WALLPAPER_PATH) ?? "";
            var originalStyle = _stateMemory.GetOriginal<string>(STATE_WALLPAPER_STYLE) ?? "10";
            var originalTile = _stateMemory.GetOriginal<string>(STATE_TILE_WALLPAPER) ?? "0";
            var originalBgColor = _stateMemory.GetOriginal<string>(STATE_BG_COLOR) ?? "0 0 0";

            // 1. Restaurar cor de fundo
            try
            {
                using var colorsKey = Registry.CurrentUser.OpenSubKey(COLORS_KEY, writable: true);
                colorsKey?.SetValue("Background", originalBgColor);
            }
            catch { }

            // 2. Restaurar estilo
            try
            {
                using var desktopKey = Registry.CurrentUser.OpenSubKey(DESKTOP_KEY, writable: true);
                if (desktopKey != null)
                {
                    desktopKey.SetValue("WallpaperStyle", originalStyle, RegistryValueKind.String);
                    desktopKey.SetValue("TileWallpaper", originalTile, RegistryValueKind.String);
                }
            }
            catch { }

            // 3. Restaurar imagem via Win32 API
            // SE for slideshow, originalPath pode ser o último slide ou vazio. 
            // Precisamos garantir que a API seja chamada para remover a cor sólida "preta".
            string pathToSet = originalPath;
            
            // Se for slideshow e o path estiver vazio, tentaremos "limpar" o estado de cor sólida
            // para que o Windows retome o slideshow.
            if (string.IsNullOrEmpty(pathToSet) && _stateMemory.GetOriginal<string>(STATE_WALLPAPER_TYPE) == "slideshow")
            {
                // No Windows, setar para uma string com espaço ou nula pode forçar a releitura do slideshow
                pathToSet = ""; 
            }

            if (true) // Sempre chamar para garantir que a cor sólida seja removida
            {
                int apiResult = SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, pathToSet, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
                if (apiResult == 0 && !string.IsNullOrEmpty(pathToSet))
                {
                    ctx.Logger.LogWarning($"[Wallpaper] Win32 API falhou ao restaurar '{pathToSet}', tentando via registro...");
                    try
                    {
                        using var desktopKey = Registry.CurrentUser.OpenSubKey(DESKTOP_KEY, writable: true);
                        desktopKey?.SetValue("Wallpaper", pathToSet, RegistryValueKind.String);
                    }
                    catch { }
                }
                ctx.Logger.LogInfo($"[Wallpaper] Comando de restauração enviado (Path='{pathToSet}', Type='{_stateMemory.GetOriginal<string>(STATE_WALLPAPER_TYPE)}')");
            }
        }
    }
}
